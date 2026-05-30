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
using Stash.Registry.Services;
using static Stash.Registry.Auth.TokenCeilingConverter;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for organization operations.
/// </summary>
/// <remarks>
/// <para>
/// Creating an organization requires a JWT with the <c>publish</c> or <c>admin</c> scope.
/// The creator is automatically added as the org owner and the org's scope is provisioned.
/// Member and team management operations require the caller to be an org owner, enforced via
/// the PDP (<see cref="IRegistryAuthorizer"/>).
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/orgs")]
public class OrganizationsController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IRegistryAuthorizer _authorizer;
    private readonly AuditService _auditService;

    /// <summary>
    /// Initialises the controller with the registry database, authorizer, and audit service.
    /// </summary>
    public OrganizationsController(IRegistryDatabase db, IRegistryAuthorizer authorizer, AuditService auditService)
    {
        _db = db;
        _authorizer = authorizer;
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
    /// Creates a new organization. The caller becomes the org owner and the org's scope is provisioned.
    /// </summary>
    /// <remarks>
    /// Requires a JWT with the <c>publish</c> or <c>admin</c> scope. Body: <see cref="CreateOrgRequest"/>.
    /// Returns <c>409</c> if an org or scope with the same name already exists.
    /// </remarks>
    /// <returns><c>201</c> with <see cref="CreateOrgResponse"/> on success.</returns>
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateOrg()
    {
        string username = User.Identity!.Name!;
        var principal = BuildPrincipal(User);

        CreateOrgRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CreateOrgRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? name = body?.Name?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new ErrorResponse { Error = "Organization name is required." });

        // Validate org name: 1-39 chars, starts with lowercase letter, [a-z0-9-] only
        if (!PackageManifest.IsValidScopeName(name))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "Organization name must be 1-39 characters, start with a lowercase letter, and contain only [a-z0-9-]."
            });
        }

        // Reserved system scopes may not be claimed as org names
        if (ReservedScopes.IsReserved(name))
            return Conflict(new ErrorResponse { Error = $"The name '{name}' is reserved and cannot be used as an organization name." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.CreateOrg, new OrgResource(name));
        if (!decision.Allowed)
        {
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        try
        {
            var org = await _db.CreateOrgAsync(name, body?.DisplayName, username);
            return StatusCode(201, new CreateOrgResponse
            {
                Id = org.Id,
                Name = org.Name,
                DisplayName = org.DisplayName,
                CreatedAt = org.CreatedAt.ToString("o"),
                CreatedBy = org.CreatedBy
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Returns public metadata for an organization.
    /// </summary>
    /// <param name="org">The org name (without the leading <c>@</c>).</param>
    /// <returns><c>200</c> with <see cref="OrgDetailResponse"/>, or <c>404</c> if not found.</returns>
    [PublicEndpoint("org public metadata is available without authentication")]
    [HttpGet("{org}")]
    public async Task<IActionResult> GetOrg(string org)
    {
        var principal = BuildPrincipal(User);
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.ReadOrg, new OrgResource(org));
        if (!decision.Allowed)
        {
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        return Ok(new OrgDetailResponse
        {
            Id = orgRecord.Id,
            Name = orgRecord.Name,
            DisplayName = orgRecord.DisplayName,
            CreatedAt = orgRecord.CreatedAt.ToString("o"),
            CreatedBy = orgRecord.CreatedBy
        });
    }

    /// <summary>
    /// Deletes an organization. Returns 409 if the org still owns scopes or packages.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <returns><c>204</c> on success, <c>409</c> if the org still owns resources, <c>404</c> if not found.</returns>
    [Authorize]
    [HttpDelete("{org}")]
    public async Task<IActionResult> DeleteOrg(string org)
    {
        var principal = BuildPrincipal(User);
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.DeleteOrg, new OrgResource(org));
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("DeleteOrg", up.Username, org, decision.Reason, ip);
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        try
        {
            await _db.DeleteOrgAsync(org);
            return StatusCode(204);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = "OrgNotEmpty", Message = ex.Message });
        }
    }

    /// <summary>
    /// Adds a member to an organization. Only org owners may invoke this endpoint.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <returns><c>200</c> on success, <c>403</c> if not an owner, <c>404</c> if org not found.</returns>
    [Authorize]
    [HttpPost("{org}/members")]
    public async Task<IActionResult> AddMember(string org)
    {
        string username = User.Identity!.Name!;
        var principal = BuildPrincipal(User);

        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.AddOrgMember, new OrgResource(org));
        if (!decision.Allowed)
        {
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        AddOrgMemberRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AddOrgMemberRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? newMember = body?.Username?.Trim();
        if (string.IsNullOrEmpty(newMember))
            return BadRequest(new ErrorResponse { Error = "Username is required." });

        string orgRole = string.IsNullOrEmpty(body?.OrgRole) ? OrgRoles.Member : body.OrgRole;
        if (orgRole != OrgRoles.Owner && orgRole != OrgRoles.Member)
            return BadRequest(new ErrorResponse { Error = $"org_role must be '{OrgRoles.Owner}' or '{OrgRoles.Member}'." });

        // Verify the target user exists
        var targetUser = await _db.GetUserAsync(newMember);
        if (targetUser == null)
            return NotFound(new ErrorResponse { Error = $"User '{newMember}' not found." });

        try
        {
            await _db.AddOrgMemberAsync(orgRecord.Id, newMember, orgRole);
            return Ok(new SuccessResponse());
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes a member from an organization. Only org owners may invoke this endpoint.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <param name="username">The username to remove.</param>
    /// <returns><c>200</c> on success, <c>403</c> if not an owner, <c>404</c> if org or user not found.</returns>
    [Authorize]
    [HttpDelete("{org}/members/{username}")]
    public async Task<IActionResult> RemoveMember(string org, string username)
    {
        string callerUsername = User.Identity!.Name!;
        var principal = BuildPrincipal(User);

        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.RemoveOrgMember, new OrgResource(org));
        if (!decision.Allowed)
        {
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        await _db.RemoveOrgMemberAsync(orgRecord.Id, username);
        return Ok(new SuccessResponse());
    }

    /// <summary>
    /// Creates a new team within an organization. Only org owners may invoke this endpoint.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <returns><c>201</c> with <see cref="CreateTeamResponse"/> on success.</returns>
    [Authorize]
    [HttpPost("{org}/teams")]
    public async Task<IActionResult> CreateTeam(string org)
    {
        string username = User.Identity!.Name!;
        var principal = BuildPrincipal(User);

        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.CreateTeam, new OrgResource(org));
        if (!decision.Allowed)
        {
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        CreateTeamRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CreateTeamRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? teamName = body?.Name?.Trim();
        if (string.IsNullOrEmpty(teamName))
            return BadRequest(new ErrorResponse { Error = "Team name is required." });

        try
        {
            var team = await _db.CreateTeamAsync(orgRecord.Id, teamName);
            return StatusCode(201, new CreateTeamResponse
            {
                Id = team.Id,
                Name = team.Name,
                OrgId = team.OrgId,
                CreatedAt = team.CreatedAt.ToString("o")
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Adds a member to a team. Only org owners may invoke this endpoint.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <param name="team">The team name.</param>
    /// <returns><c>200</c> on success, <c>403</c> if not an owner, <c>404</c> if org/team not found.</returns>
    [Authorize]
    [HttpPost("{org}/teams/{team}/members")]
    public async Task<IActionResult> AddTeamMember(string org, string team)
    {
        string username = User.Identity!.Name!;
        var principal = BuildPrincipal(User);

        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.AddTeamMember, new OrgResource(org));
        if (!decision.Allowed)
        {
            int status = decision.Reason == AuthzDenyReason.NotAuthenticated ? 401 : 403;
            return StatusCode(status, new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        var teamRecord = await _db.GetTeamByNameAsync(orgRecord.Id, team);
        if (teamRecord == null)
            return NotFound(new ErrorResponse { Error = $"Team '{team}' not found in organization '{org}'." });

        AddTeamMemberRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AddTeamMemberRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? newMember = body?.Username?.Trim();
        if (string.IsNullOrEmpty(newMember))
            return BadRequest(new ErrorResponse { Error = "Username is required." });

        await _db.AddTeamMemberAsync(teamRecord.Id, newMember);
        return Ok(new SuccessResponse());
    }
}
