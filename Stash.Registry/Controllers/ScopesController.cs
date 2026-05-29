using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

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

    /// <summary>
    /// Initialises the controller with the registry database.
    /// </summary>
    /// <param name="db">Registry database for scope queries.</param>
    public ScopesController(IRegistryDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Resolves a scope to its owner shape.
    /// </summary>
    /// <param name="scope">The bare scope name (without the leading <c>@</c>).</param>
    /// <returns>
    /// <c>200</c> with a <see cref="ScopeDetailResponse"/> containing the scope name,
    /// owner type, and owner identifier, or <c>404</c> if the scope does not exist.
    /// </returns>
    [AllowAnonymous]
    [HttpGet("{scope}")]
    public async Task<IActionResult> GetScope(string scope)
    {
        var record = await _db.GetScopeAsync(scope);
        if (record == null)
            return NotFound(new ErrorResponse { Error = $"Scope '@{scope}' not found." });

        return Ok(BuildScopeDetailResponse(record));
    }

    /// <summary>
    /// Claims a new scope for a user or an organization owned by the caller.
    /// </summary>
    /// <remarks>
    /// Rejects collisions with existing scopes, usernames, org names, and reserved system scopes
    /// (<c>@stash</c>, <c>@admin</c>). Requires a JWT with the <c>publish</c> or <c>admin</c> scope.
    /// </remarks>
    /// <returns><c>201</c> with <see cref="ScopeDetailResponse"/> on success, <c>409</c> on collision.</returns>
    [Authorize(Policy = "RequirePublishScope")]
    [HttpPost]
    public async Task<IActionResult> ClaimScope()
    {
        string callerUsername = User.Identity!.Name!;

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
        if (ownerType != "user" && ownerType != "org")
            return BadRequest(new ErrorResponse { Error = "owner_type must be 'user' or 'org'." });

        string? owner = body?.Owner?.Trim();
        if (string.IsNullOrEmpty(owner))
            return BadRequest(new ErrorResponse { Error = "owner is required." });

        // Validate scope name grammar: 1-39 chars, starts with lowercase letter, [a-z0-9-] only
        if (!System.Text.RegularExpressions.Regex.IsMatch(scopeName, @"^[a-z][a-z0-9-]{0,38}$"))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "Scope name must be 1-39 characters, start with a lowercase letter, and contain only [a-z0-9-]."
            });
        }

        // Reserved system scopes cannot be claimed
        string[] reservedScopes = ["stash", "admin"];
        if (Array.Exists(reservedScopes, s => s == scopeName))
            return Conflict(new ErrorResponse { Error = $"The scope '@{scopeName}' is a reserved system scope and cannot be claimed." });

        // Check for existing scope collision
        if (await _db.ScopeExistsAsync(scopeName))
            return Conflict(new ErrorResponse { Error = $"Scope '@{scopeName}' already exists." });

        ScopeRecord newScope;
        if (ownerType == "user")
        {
            // The caller can only claim a scope for themselves (not for another user)
            if (!string.Equals(owner, callerUsername, StringComparison.Ordinal) && !User.IsInRole("admin"))
                return StatusCode(403, new ErrorResponse { Error = "You may only claim a user scope for your own account." });

            newScope = new ScopeRecord
            {
                Name = scopeName,
                OwnerType = "user",
                OwnerUsername = owner,
                OwnerOrgId = null
            };
        }
        else // ownerType == "org"
        {
            // Verify the org exists and the caller is an owner of it
            var orgRecord = await _db.GetOrgAsync(owner);
            if (orgRecord == null)
                return NotFound(new ErrorResponse { Error = $"Organization '{owner}' not found." });

            bool isOrgOwner = await _db.IsOrgOwnerAsync(orgRecord.Id, callerUsername);
            bool isAdmin = User.IsInRole("admin");
            if (!isOrgOwner && !isAdmin)
                return StatusCode(403, new ErrorResponse { Error = $"User '{callerUsername}' is not an owner of organization '{owner}'." });

            newScope = new ScopeRecord
            {
                Name = scopeName,
                OwnerType = "org",
                OwnerOrgId = orgRecord.Id,
                OwnerUsername = null
            };
        }

        try
        {
            await _db.CreateScopeAsync(newScope);
            return StatusCode(201, BuildScopeDetailResponse(newScope));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    private static ScopeDetailResponse BuildScopeDetailResponse(ScopeRecord record)
    {
        string? owner = record.OwnerType switch
        {
            "user" => record.OwnerUsername,
            "org" => record.OwnerOrgId,
            _ => null
        };
        return new ScopeDetailResponse
        {
            Scope = record.Name,
            OwnerType = record.OwnerType,
            Owner = owner
        };
    }
}
