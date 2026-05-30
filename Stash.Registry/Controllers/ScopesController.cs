using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Common;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using static Stash.Registry.Auth.TokenCeilingConverter;

namespace Stash.Registry.Controllers;

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
    private readonly AuditService _auditService;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    public ScopesController(
        IRegistryDatabase db,
        IRegistryAuthorizer authorizer,
        ScopeChallengeService scopeChallenge,
        AuditService auditService)
    {
        _db = db;
        _authorizer = authorizer;
        _scopeChallenge = scopeChallenge;
        _auditService = auditService;
    }

    // ── Helper: build Principal ───────────────────────────────────────────────

    private static Principal BuildPrincipal(System.Security.Claims.ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return new AnonymousPrincipal();

        string username = user.Identity!.Name!;
        bool isAdmin = user.IsInRole(UserRoles.Admin);
        TokenCeiling ceiling = FromClaimValue(user.FindFirst(RegistryClaims.TokenScope)?.Value);
        UserRole role = isAdmin ? UserRole.Admin : UserRole.User;
        string tokenId = user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value ?? "";
        return new UserPrincipal(username, role, ceiling, tokenId);
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
    [HttpGet("{scope}")]
    public async Task<IActionResult> GetScope(string scope)
    {
        var principal = BuildPrincipal(User);
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.ResolveScope, new ScopeResource(scope));
        if (!decision.Allowed)
        {
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        var record = await _db.GetScopeAsync(scope);
        if (record == null)
            return NotFound(new ErrorResponse { Error = $"Scope '@{scope}' not found." });

        return Ok(BuildScopeDetailResponse(record));
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
    [HttpPost]
    public async Task<IActionResult> ClaimScope()
    {
        string callerUsername = User.Identity!.Name!;
        var principal = BuildPrincipal(User);

        ClaimScopeRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ClaimScopeRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? scopeName = body?.Scope?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(scopeName))
            return BadRequest(new ErrorResponse { Error = "scope is required." });

        string? ownerType = body?.OwnerType?.Trim().ToLowerInvariant();
        if (ownerType != ScopeOwnerTypes.User && ownerType != ScopeOwnerTypes.Org)
            return BadRequest(new ErrorResponse { Error = $"owner_type must be '{ScopeOwnerTypes.User}' or '{ScopeOwnerTypes.Org}'." });

        string? owner = body?.Owner?.Trim();
        if (string.IsNullOrEmpty(owner))
            return BadRequest(new ErrorResponse { Error = "owner is required." });

        // Validate scope name grammar: 1-39 chars, starts with lowercase letter, [a-z0-9-] only
        if (!PackageManifest.IsValidScopeName(scopeName))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "Scope name must be 1-39 characters, start with a lowercase letter, and contain only [a-z0-9-]."
            });
        }

        // Run PDP for ClaimScope
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.ClaimScope, new ScopeResource(scopeName));
        if (!decision.Allowed)
        {
            int status = decision.Reason switch
            {
                AuthzDenyReason.ScopeReserved => 409,
                AuthzDenyReason.ScopeNotOwned => 409,
                AuthzDenyReason.NotAuthenticated => 401,
                _ => 403
            };
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        // Owner-type-specific validation (user: caller matches owner; org: caller is org owner)
        string ownerId;
        string ownerDisplayName;
        if (ownerType == ScopeOwnerTypes.User)
        {
            if (!string.Equals(owner, callerUsername, StringComparison.Ordinal) && !User.IsInRole(UserRoles.Admin))
                return StatusCode(403, new ErrorResponse { Error = "You may only claim a user scope for your own account." });

            ownerId = owner;
            ownerDisplayName = owner;
        }
        else // ownerType == "org"
        {
            var orgRecord = await _db.GetOrgAsync(owner);
            if (orgRecord == null)
                return NotFound(new ErrorResponse { Error = $"Organization '{owner}' not found." });

            bool isOrgOwner = await _db.IsOrgOwnerAsync(orgRecord.Id, callerUsername);
            bool isAdmin = User.IsInRole(UserRoles.Admin);
            if (!isOrgOwner && !isAdmin)
                return StatusCode(403, new ErrorResponse { Error = $"User '{callerUsername}' is not an owner of organization '{owner}'." });

            ownerId = orgRecord.Id;
            ownerDisplayName = orgRecord.Name;
        }

        // Namespace-pool collision checks (username, org-name)
        if (await _db.GetUserAsync(scopeName) is not null)
            return Conflict(new ErrorResponse { Error = $"The name '{scopeName}' is already taken by a user account." });

        if (await _db.GetOrgAsync(scopeName) is not null)
            return Conflict(new ErrorResponse { Error = $"The name '{scopeName}' is already taken by an organization." });

        // Atomic claim via insert-then-handle-unique-violation
        var (succeeded, response) = await _scopeChallenge.TryClaimAsync(
            scopeName, ownerType, ownerId, ownerDisplayName);

        if (!succeeded)
            return Conflict(new ErrorResponse { Error = $"Scope '@{scopeName}' already exists." });

        return StatusCode(201, response);
    }

    /// <summary>
    /// Submits a scope for verification (501 stub — DNS resolver deferred to Q4).
    /// </summary>
    /// <param name="scope">The bare scope name.</param>
    /// <returns><c>501 NotImplemented</c> with documented body shape.</returns>
    [Authorize]
    [HttpPost("{scope}/verify")]
    public async Task<IActionResult> VerifyScope(string scope)
    {
        var principal = BuildPrincipal(User);
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.VerifyScope, new ScopeResource(scope));
        if (!decision.Allowed)
        {
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        // Resolver is stubbed 501 this phase (Q4 deferred).
        return StatusCode(501, new ScopeVerifyResponse
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
    [HttpDelete("{scope}")]
    public async Task<IActionResult> DeleteScope(string scope)
    {
        var principal = BuildPrincipal(User);
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.DeleteScope, new ScopeResource(scope));
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("DeleteScope", up.Username, $"@{scope}", decision.Reason, ip);
            int statusCode = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(statusCode, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        var record = await _db.GetScopeAsync(scope);
        if (record == null)
            return NotFound(new ErrorResponse { Error = $"Scope '@{scope}' not found." });

        try
        {
            await _db.DeleteScopeAsync(scope);
            return StatusCode(204);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = "ScopeNotEmpty", Message = ex.Message });
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
