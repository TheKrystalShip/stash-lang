using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Request body for the <c>POST /api/v1/orgs</c> endpoint.
/// </summary>
public sealed class CreateOrgRequest
{
    /// <summary>The unique lower-case organization name (same grammar as a scope).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>An optional human-readable display name for the organization.</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}

/// <summary>
/// Response body returned by the <c>POST /api/v1/orgs</c> endpoint.
/// </summary>
public sealed class CreateOrgResponse
{
    /// <summary>The unique organization identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>The organization name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>The optional display name.</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>The ISO 8601 timestamp when the org was created.</summary>
    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; set; }

    /// <summary>The username of the creator, who is automatically the org owner.</summary>
    [JsonPropertyName("created_by")]
    public required string CreatedBy { get; set; }
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/orgs/{org}</c> endpoint.
/// </summary>
public sealed class OrgDetailResponse
{
    /// <summary>The unique organization identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>The organization name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>The optional display name.</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>The ISO 8601 timestamp when the org was created.</summary>
    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; set; }

    /// <summary>The username of the creator.</summary>
    [JsonPropertyName("created_by")]
    public required string CreatedBy { get; set; }
}

/// <summary>
/// Request body for the <c>POST /api/v1/orgs/{org}/members</c> endpoint.
/// </summary>
public sealed class AddOrgMemberRequest
{
    /// <summary>The username of the user to add.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>The role to assign: <c>owner</c> or <c>member</c>. Defaults to <c>member</c>.</summary>
    [JsonPropertyName("org_role")]
    public OrgRoles? OrgRole { get; set; }
}

/// <summary>
/// Request body for the <c>POST /api/v1/orgs/{org}/teams</c> endpoint.
/// </summary>
public sealed class CreateTeamRequest
{
    /// <summary>The team name (unique within the organization).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Response body for team creation.
/// </summary>
public sealed class CreateTeamResponse
{
    /// <summary>The unique team identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>The team name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>The organization identifier that owns this team.</summary>
    [JsonPropertyName("org_id")]
    public required string OrgId { get; set; }

    /// <summary>The ISO 8601 timestamp when the team was created.</summary>
    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; set; }
}

/// <summary>
/// Request body for the <c>POST /api/v1/orgs/{org}/teams/{team}/members</c> endpoint.
/// </summary>
public sealed class AddTeamMemberRequest
{
    /// <summary>The username to add to the team.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }
}
