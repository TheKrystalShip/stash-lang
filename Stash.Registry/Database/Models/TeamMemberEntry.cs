using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing the membership of a user in a team.
/// </summary>
/// <remarks>
/// The composite primary key is (<see cref="TeamId"/>, <see cref="Username"/>).
/// Column names use <c>snake_case</c> in the database.
/// </remarks>
public sealed class TeamMemberEntry
{
    /// <summary>The team identifier (part of composite primary key, FK to <see cref="TeamRecord.Id"/>).</summary>
    public string TeamId { get; set; } = "";

    /// <summary>The username of the team member (part of composite primary key).</summary>
    public string Username { get; set; } = "";

    /// <summary>The UTC timestamp at which the user joined the team.</summary>
    public DateTime JoinedAt { get; set; }
}
