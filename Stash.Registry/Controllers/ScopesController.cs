using System;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Stash.Common;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;

namespace Stash.Registry.Controllers;

// ── Custom typed result helper for 501 with body ──────────────────────────────
//
// TypedResults has no typed-501-with-body variant.  To preserve the ScopeVerifyResponse
// wire body on this status code while still advertising the schema to ApiExplorer (so the
// coverage meta-test sees a $ref for the 501 response), we define an internal
// IResult + IEndpointMetadataProvider implementation here, mirroring the JsonUnauthorized<T>
// / JsonForbidden<T> helpers in AuthController.cs.

/// <summary>
/// Returns HTTP 501 with a JSON-serialised body, advertising the body type to ApiExplorer.
/// </summary>
public sealed class JsonNotImplemented<T> : IResult, IEndpointMetadataProvider
{
    private readonly T _body;
    public JsonNotImplemented(T body) => _body = body;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status501NotImplemented;
        httpContext.Response.ContentType = "application/json";
        return httpContext.Response.WriteAsJsonAsync(_body);
    }

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status501NotImplemented, typeof(T), ["application/json"]));
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// REST API controller for scope operations.
/// </summary>
/// <remarks>
/// <para>
/// Scopes form the top-level namespace for package names (<c>@scope/name</c>). Each scope
/// has a polymorphic owner: a user, an organization, or the system. The <c>GET /scopes/{scope}</c>
/// endpoint is public; claiming a new scope requires a JWT with the <c>publish</c> or <c>admin</c> scope.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/scopes")]
public class ScopesController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IRegistryAuthorizer _authorizer;
    private readonly ScopeChallengeService _scopeChallenge;
    private readonly IRegistryAuthzPrincipalFactory _principalFactory;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    public ScopesController(
        IRegistryDatabase db,
        IRegistryAuthorizer authorizer,
        ScopeChallengeService scopeChallenge,
        IRegistryAuthzPrincipalFactory principalFactory)
    {
        _db = db;
        _authorizer = authorizer;
        _scopeChallenge = scopeChallenge;
        _principalFactory = principalFactory;
    }

    /// <summary>
    /// Resolves a scope to its owner shape.
    /// </summary>
    /// <param name="scope">The bare scope name (without the leading <c>@</c>).</param>
    /// <returns>
    /// <c>200</c> with a <see cref="ScopeDetailResponse"/> containing the scope name,
    /// owner type, and owner identifier, or <c>404</c> if the scope does not exist.
    /// </returns>
    [PublicEndpoint("scope ownership metadata is public — used to discover who owns @scope before publishing")]
    [RegistryAuthorize(RegistryAction.ResolveScope)]
    [HttpGet("{scope}")]
    public async Task<Results<Ok<ScopeDetailResponse>, NotFound<ErrorResponse>>> GetScope(string scope)
    {
        var record = await _db.GetScopeAsync(scope);
        if (record == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Scope '@{scope}' not found." });

        return TypedResults.Ok(BuildScopeDetailResponse(record));
    }

    /// <summary>
    /// Claims a new scope for a user or an organization owned by the caller.
    /// Under <c>ScopeOwnershipPolicy=Verified</c>, returns a DNS-TXT challenge body.
    /// </summary>
    /// <remarks>
    /// Rejects collisions with existing scopes, usernames, org names, and reserved system scopes
    /// (<c>@stash</c>, <c>@admin</c>). Requires a JWT with the <c>publish</c> or <c>admin</c> scope.
    /// The insert is atomic via insert-then-handle-unique-violation (UNIQUE constraint on scopes.name).
    /// </remarks>
    /// <returns><c>201</c> with <see cref="ScopeDetailResponse"/> on success, <c>409</c> on collision.</returns>
    [Authorize]
    [ImperativeAuthz("scope/owner/ownerType fields come from the JSON request body (not route values), so the shared filter's pure-route resolver cannot build a ScopeResource before the PDP call; the bespoke 409 status mapping (ScopeReserved/ScopeNotOwned → 409 instead of 403) also requires inline coordination. Folding requires the body-resolver refactor tracked in .kanban/0-backlog/registry/Body-resolver authz filter.md")]
    [HttpPost]
    public async Task<Results<Created<ScopeDetailResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>, Conflict<ErrorResponse>, JsonUnauthorized<ErrorResponse>, JsonForbidden<ErrorResponse>>> ClaimScope()
    {
        string callerUsername = User.Identity!.Name!;
        var principal = _principalFactory.Build(User);

        ClaimScopeRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ClaimScopeRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? scopeName = body?.Scope?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(scopeName))
            return TypedResults.BadRequest(new ErrorResponse { Error = "scope is required." });

        string? ownerType = body?.OwnerType?.Trim().ToLowerInvariant();
        if (ownerType != ScopeOwnerTypes.User && ownerType != ScopeOwnerTypes.Org)
            return TypedResults.BadRequest(new ErrorResponse { Error = $"owner_type must be '{ScopeOwnerTypes.User}' or '{ScopeOwnerTypes.Org}'." });

        string? owner = body?.Owner?.Trim();
        if (string.IsNullOrEmpty(owner))
            return TypedResults.BadRequest(new ErrorResponse { Error = "owner is required." });

        // Validate scope name grammar: 1-39 chars, starts with lowercase letter, [a-z0-9-] only
        if (!PackageManifest.IsValidScopeName(scopeName))
        {
            return TypedResults.BadRequest(new ErrorResponse
            {
                Error = "Scope name must be 1-39 characters, start with a lowercase letter, and contain only [a-z0-9-]."
            });
        }

        // Run PDP for ClaimScope
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.ClaimScope, new ScopeResource(scopeName));
        if (!decision.Allowed)
        {
            if (decision.Reason == AuthzDenyReason.ScopeReserved || decision.Reason == AuthzDenyReason.ScopeNotOwned)
                return TypedResults.Conflict(new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
            if (decision.Reason == AuthzDenyReason.NotAuthenticated)
                return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
            return new JsonForbidden<ErrorResponse>(new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        // Owner-type-specific validation (user: caller matches owner; org: caller is org owner)
        string ownerId;
        string ownerDisplayName;
        if (ownerType == ScopeOwnerTypes.User)
        {
            if (!string.Equals(owner, callerUsername, StringComparison.Ordinal) && !User.IsInRole(UserRoles.Admin))
                return new JsonForbidden<ErrorResponse>(new ErrorResponse { Error = "You may only claim a user scope for your own account." });

            ownerId = owner;
            ownerDisplayName = owner;
        }
        else // ownerType == "org"
        {
            var orgRecord = await _db.GetOrgAsync(owner);
            if (orgRecord == null)
                return TypedResults.NotFound(new ErrorResponse { Error = $"Organization '{owner}' not found." });

            bool isOrgOwner = await _db.IsOrgOwnerAsync(orgRecord.Id, callerUsername);
            bool isAdmin = User.IsInRole(UserRoles.Admin);
            if (!isOrgOwner && !isAdmin)
                return new JsonForbidden<ErrorResponse>(new ErrorResponse { Error = $"User '{callerUsername}' is not an owner of organization '{owner}'." });

            ownerId = orgRecord.Id;
            ownerDisplayName = orgRecord.Name;
        }

        // Namespace-pool collision checks (username, org-name)
        if (await _db.GetUserAsync(scopeName) is not null)
            return TypedResults.Conflict(new ErrorResponse { Error = $"The name '{scopeName}' is already taken by a user account." });

        if (await _db.GetOrgAsync(scopeName) is not null)
            return TypedResults.Conflict(new ErrorResponse { Error = $"The name '{scopeName}' is already taken by an organization." });

        // Atomic claim via insert-then-handle-unique-violation
        var (succeeded, response) = await _scopeChallenge.TryClaimAsync(
            scopeName, ownerType, ownerId, ownerDisplayName);

        if (!succeeded)
            return TypedResults.Conflict(new ErrorResponse { Error = $"Scope '@{scopeName}' already exists." });

        return TypedResults.Created((string?)null, response!);
    }

    /// <summary>
    /// Submits a scope for verification (501 stub — DNS resolver deferred to Q4).
    /// </summary>
    /// <param name="scope">The bare scope name.</param>
    /// <returns><c>501 NotImplemented</c> with documented body shape.</returns>
    [Authorize]
    [RegistryAuthorize(RegistryAction.VerifyScope)]
    [HttpPost("{scope}/verify")]
    public async Task<JsonNotImplemented<ScopeVerifyResponse>> VerifyScope(string scope)
    {
        // Resolver is stubbed 501 this phase (Q4 deferred).
        await Task.CompletedTask;
        return new JsonNotImplemented<ScopeVerifyResponse>(new ScopeVerifyResponse
        {
            Scope = scope,
            Method = "dns-txt",
            Message = "DNS-TXT verification resolver is not yet implemented. Check back in a future release."
        });
    }

    /// <summary>
    /// Deletes a scope. Returns 409 if any packages still belong to the scope.
    /// </summary>
    /// <param name="scope">The bare scope name.</param>
    /// <returns><c>204</c> on success, <c>409</c> if the scope still owns packages, <c>404</c> if not found.</returns>
    [Authorize]
    [RegistryAuthorize(RegistryAction.DeleteScope)]
    [HttpDelete("{scope}")]
    public async Task<Results<NoContent, NotFound<ErrorResponse>, Conflict<ErrorResponse>>> DeleteScope(string scope)
    {
        var record = await _db.GetScopeAsync(scope);
        if (record == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Scope '@{scope}' not found." });

        try
        {
            await _db.DeleteScopeAsync(scope);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(new ErrorResponse { Error = "ScopeNotEmpty", Message = ex.Message });
        }
    }

    private static ScopeDetailResponse BuildScopeDetailResponse(ScopeRecord record)
    {
        string? owner = record.OwnerType switch
        {
            ScopeOwnerTypes.User => record.OwnerUsername,
            ScopeOwnerTypes.Org => record.OwnerOrgId,
            _ => null
        };
        return new ScopeDetailResponse
        {
            Scope = record.Name,
            OwnerType = record.OwnerType,
            Owner = owner,
            State = record.State == ScopeStates.Pending ? ScopeStates.Pending : null
        };
    }
}
