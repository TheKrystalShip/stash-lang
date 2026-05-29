using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a Stash organization.
/// </summary>
/// <remarks>
/// The organization name serves as a unique identifier (lower-case, same grammar as scope).
/// Members and teams are stored in <see cref="OrgMemberEntry"/> and <see cref="TeamRecord"/>
/// respectively. Column names use <c>snake_case</c> in the database.
/// </remarks>
public sealed class OrganizationRecord
{
    /// <summary>The unique organization identifier (primary key, UUID).</summary>
    public string Id { get; set; } = "";

    /// <summary>The unique lower-case name of the organization (same grammar as scope).</summary>
    public string Name { get; set; } = "";

    /// <summary>A human-readable display name for the organization, or <c>null</c> if not set.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The UTC timestamp at which the organization was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The username of the user who created the organization.</summary>
    public string CreatedBy { get; set; } = "";
}
