namespace Stash.Registry.Database.Models;

/// <summary>
/// Lifecycle states for a <see cref="ScopeRecord"/> when <c>ScopeOwnershipPolicy=Verified</c>.
/// </summary>
public static class ScopeStates
{
    /// <summary>Scope has been claimed and (in Verified mode) verified. Default state.</summary>
    public const string Claimed = "claimed";

    /// <summary>Scope has been claimed but not yet verified. Only reachable under Verified policy.</summary>
    public const string Pending = "pending";
}

/// <summary>
/// Database entity representing a package scope in the registry.
/// </summary>
/// <remarks>
/// <para>
/// The scope name serves as the primary key (without the leading <c>@</c>).
/// The <see cref="OwnerType"/> column is constrained to <c>user</c>, <c>org</c>, or <c>system</c>.
/// </para>
/// <para>
/// For <c>user</c>-owned scopes exactly <see cref="OwnerUsername"/> is set and
/// <see cref="OwnerOrgId"/> is null. For <c>org</c>-owned scopes exactly
/// <see cref="OwnerOrgId"/> is set and <see cref="OwnerUsername"/> is null. For
/// <c>system</c> scopes both owner columns are null.
/// </para>
/// <para>
/// A CHECK constraint in the database enforces the single-owner invariant.
/// Column names use <c>snake_case</c>.
/// </para>
/// </remarks>
public sealed class ScopeRecord
{
    /// <summary>The scope name without the leading <c>@</c> (primary key).</summary>
    public string Name { get; set; } = "";

    /// <summary>The type of owner: <c>user</c>, <c>org</c>, or <c>system</c>.</summary>
    public string OwnerType { get; set; } = "";

    /// <summary>
    /// The username of the owning user when <see cref="OwnerType"/> is <c>user</c>;
    /// null otherwise.
    /// </summary>
    public string? OwnerUsername { get; set; }

    /// <summary>
    /// The identifier of the owning organization when <see cref="OwnerType"/> is <c>org</c>;
    /// null otherwise.
    /// </summary>
    public string? OwnerOrgId { get; set; }

    /// <summary>
    /// Lifecycle state: <c>claimed</c> (default) or <c>pending</c> (awaiting verification under Verified policy).
    /// See <see cref="ScopeStates"/>.
    /// </summary>
    public string State { get; set; } = ScopeStates.Claimed;
}
