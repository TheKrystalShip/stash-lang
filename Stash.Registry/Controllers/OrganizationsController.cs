using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Stash.Common;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Contracts;
using Stash.Registry.Database;

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

    /// <summary>
    /// Initialises the controller with the registry database.
    /// </summary>
    public OrganizationsController(IRegistryDatabase db)
    {
        _db = db;
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
    [RegistryAuthorize(RegistryAction.CreateOrg)]
    [HttpPost]
    public async Task<Results<Created<CreateOrgResponse>, BadRequest<ErrorResponse>, Conflict<ErrorResponse>>> CreateOrg()
    {
        string username = User.Identity!.Name!;

        CreateOrgRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CreateOrgRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? name = body?.Name?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name))
            return TypedResults.BadRequest(new ErrorResponse { Error = "Organization name is required." });

        // Validate org name: 1-39 chars, starts with lowercase letter, [a-z0-9-] only
        if (!PackageManifest.IsValidScopeName(name))
        {
            return TypedResults.BadRequest(new ErrorResponse
            {
                Error = "Organization name must be 1-39 characters, start with a lowercase letter, and contain only [a-z0-9-]."
            });
        }

        // Reserved system scopes may not be claimed as org names
        if (ReservedScopes.IsReserved(name))
            return TypedResults.Conflict(new ErrorResponse { Error = $"The name '{name}' is reserved and cannot be used as an organization name." });

        try
        {
            var org = await _db.CreateOrgAsync(name, body?.DisplayName, username);
            return TypedResults.Created((string?)null, new CreateOrgResponse
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
            return TypedResults.Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Returns public metadata for an organization.
    /// </summary>
    /// <param name="org">The org name (without the leading <c>@</c>).</param>
    /// <returns><c>200</c> with <see cref="OrgDetailResponse"/>, or <c>404</c> if not found.</returns>
    [PublicEndpoint("org public metadata is available without authentication")]
    [RegistryAuthorize(RegistryAction.ReadOrg)]
    [HttpGet("{org}")]
    public async Task<Results<Ok<OrgDetailResponse>, NotFound<ErrorResponse>>> GetOrg(string org)
    {
        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        return TypedResults.Ok(new OrgDetailResponse
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
    [RegistryAuthorize(RegistryAction.DeleteOrg)]
    [HttpDelete("{org}")]
    public async Task<Results<NoContent, NotFound<ErrorResponse>, Conflict<ErrorResponse>>> DeleteOrg(string org)
    {
        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        try
        {
            await _db.DeleteOrgAsync(org);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(new ErrorResponse { Error = "OrgNotEmpty", Message = ex.Message });
        }
    }

    /// <summary>
    /// Adds a member to an organization. Only org owners may invoke this endpoint.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <returns><c>200</c> on success, <c>403</c> if not an owner, <c>404</c> if org not found.</returns>
    [Authorize]
    [RegistryAuthorize(RegistryAction.AddOrgMember)]
    [HttpPost("{org}/members")]
    public async Task<Results<Ok<SuccessResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>, Conflict<ErrorResponse>>> AddMember(string org, [FromBody] AddOrgMemberRequest? body)
    {
        string username = User.Identity!.Name!;

        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        // With [FromBody], validation at deserialization boundary (JsonStringEnumConverter) returns 400 automatically.
        string? newMember = body?.Username?.Trim();
        if (string.IsNullOrEmpty(newMember))
            return TypedResults.BadRequest(new ErrorResponse { Error = "Username is required." });

        OrgRoles orgRole = body?.OrgRole ?? OrgRoles.Member;
        // Validation is handled by JsonStringEnumConverter — invalid values return 400 before reaching here.
        // orgRole is already valid since deserialization succeeded.

        // Verify the target user exists
        var targetUser = await _db.GetUserAsync(newMember);
        if (targetUser == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"User '{newMember}' not found." });

        try
        {
            await _db.AddOrgMemberAsync(orgRecord.Id, newMember, orgRole.ToWire());
            return TypedResults.Ok(new SuccessResponse());
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes a member from an organization. Only org owners may invoke this endpoint.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <param name="username">The username to remove.</param>
    /// <returns><c>200</c> on success, <c>403</c> if not an owner, <c>404</c> if org or user not found.</returns>
    [Authorize]
    [RegistryAuthorize(RegistryAction.RemoveOrgMember)]
    [HttpDelete("{org}/members/{username}")]
    public async Task<Results<Ok<SuccessResponse>, NotFound<ErrorResponse>>> RemoveMember(string org, string username)
    {
        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        await _db.RemoveOrgMemberAsync(orgRecord.Id, username);
        return TypedResults.Ok(new SuccessResponse());
    }

    /// <summary>
    /// Creates a new team within an organization. Only org owners may invoke this endpoint.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <returns><c>201</c> with <see cref="CreateTeamResponse"/> on success.</returns>
    [Authorize]
    [RegistryAuthorize(RegistryAction.CreateTeam)]
    [HttpPost("{org}/teams")]
    public async Task<Results<Created<CreateTeamResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>, Conflict<ErrorResponse>>> CreateTeam(string org)
    {
        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        CreateTeamRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CreateTeamRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? teamName = body?.Name?.Trim();
        if (string.IsNullOrEmpty(teamName))
            return TypedResults.BadRequest(new ErrorResponse { Error = "Team name is required." });

        try
        {
            var team = await _db.CreateTeamAsync(orgRecord.Id, teamName);
            return TypedResults.Created((string?)null, new CreateTeamResponse
            {
                Id = team.Id,
                Name = team.Name,
                OrgId = team.OrgId,
                CreatedAt = team.CreatedAt.ToString("o")
            });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Adds a member to a team. Only org owners may invoke this endpoint.
    /// </summary>
    /// <param name="org">The org name.</param>
    /// <param name="team">The team name.</param>
    /// <returns><c>200</c> on success, <c>403</c> if not an owner, <c>404</c> if org/team not found.</returns>
    [Authorize]
    [RegistryAuthorize(RegistryAction.AddTeamMember)]
    [HttpPost("{org}/teams/{team}/members")]
    public async Task<Results<Ok<SuccessResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>>> AddTeamMember(string org, string team)
    {
        var orgRecord = await _db.GetOrgAsync(org);
        if (orgRecord == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Organization '{org}' not found." });

        var teamRecord = await _db.GetTeamByNameAsync(orgRecord.Id, team);
        if (teamRecord == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Team '{team}' not found in organization '{org}'." });

        AddTeamMemberRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AddTeamMemberRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? newMember = body?.Username?.Trim();
        if (string.IsNullOrEmpty(newMember))
            return TypedResults.BadRequest(new ErrorResponse { Error = "Username is required." });

        await _db.AddTeamMemberAsync(teamRecord.Id, newMember);
        return TypedResults.Ok(new SuccessResponse());
    }
}
