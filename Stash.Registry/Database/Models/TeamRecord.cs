using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a team within an organization.
/// </summary>
/// <remarks>
/// Teams are uniquely named within their owning organization. Members are stored in
/// <see cref="TeamMemberEntry"/>. Column names use <c>snake_case</c> in the database.
/// </remarks>
public sealed class TeamRecord
{
    /// <summary>The unique team identifier (primary key, UUID).</summary>
    public string Id { get; set; } = "";

    /// <summary>The identifier of the owning organization (FK to <see cref="OrganizationRecord.Id"/>).</summary>
    public string OrgId { get; set; } = "";

    /// <summary>The team name, unique within the organization.</summary>
    public string Name { get; set; } = "";

    /// <summary>The UTC timestamp at which the team was created.</summary>
    public DateTime CreatedAt { get; set; }
}
