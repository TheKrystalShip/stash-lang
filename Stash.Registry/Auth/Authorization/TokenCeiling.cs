namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Coarse token capability ceiling — the only token-side dimension in this phase.
/// Numeric values are intentional: higher ordinal = broader capability, so
/// <c>ceiling &gt;= required</c> is a valid comparator.
/// </summary>
/// <remarks>
/// Fine-grained per-package / per-action capability rules are deliberately absent
/// this phase. They are deferred to a follow-up feature.
/// </remarks>
public enum TokenCeiling
{
    /// <summary>The token may only perform read-class actions.</summary>
    Read = 0,

    /// <summary>The token may perform read- and publish-class actions.</summary>
    Publish = 1,

    /// <summary>The token may perform read, publish, and admin-class actions.</summary>
    Admin = 2
}
