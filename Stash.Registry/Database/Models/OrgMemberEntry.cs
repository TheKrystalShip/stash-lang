using System;
using Stash.Registry.Auth;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing the membership of a user in an organization.
/// </summary>
/// <remarks>
/// The composite primary key is (<see cref="OrgId"/>, <see cref="Username"/>).
/// The <see cref="OrgRole"/> column is constrained to <c>owner</c> or <c>member</c>.
/// Column names use <c>snake_case</c> in the database.
/// </remarks>
public sealed class OrgMemberEntry
{
    /// <summary>The organization identifier (part of composite primary key, FK to <see cref="OrganizationRecord.Id"/>).</summary>
    public string OrgId { get; set; } = "";

    /// <summary>The username of the member (part of composite primary key).</summary>
    public string Username { get; set; } = "";

    /// <summary>The role of this member in the organization: <c>owner</c> or <c>member</c>.</summary>
    public string OrgRole { get; set; } = OrgRoles.Member;

    /// <summary>The UTC timestamp at which the user joined the organization.</summary>
    public DateTime JoinedAt { get; set; }
}
