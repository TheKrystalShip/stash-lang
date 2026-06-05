using Stash.Registry.Contracts;

namespace Stash.Registry.Web.Constants;

/// <summary>
/// Human-facing display labels for bounded-domain values shown in the maintainer UI.
/// Single source of truth — never inline control labels in Razor views.
/// </summary>
/// <remarks>
/// <para>
/// All visibility option labels are sourced from here; the option <em>values</em> (wire strings)
/// come from <see cref="Visibilities"/> via <c>Enum.GetValues&lt;Visibilities&gt;()</c> so
/// no <c>"public"</c> / <c>"private"</c> literal ever appears under <c>Areas/</c>.
/// </para>
/// </remarks>
public static class ViewLabels
{
    // ── Visibility labels ─────────────────────────────────────────────────────

    /// <summary>Display label for <see cref="Visibilities.Public"/>.</summary>
    public const string VisibilityPublic = "Public — accessible to anyone";

    /// <summary>Display label for <see cref="Visibilities.Private"/>.</summary>
    public const string VisibilityPrivate = "Private — accessible to authorized users only";

    /// <summary>Display label for <see cref="Visibilities.Internal"/>.</summary>
    public const string VisibilityInternal = "Internal — accessible to org members";

    /// <summary>
    /// Returns the human-facing display label for the given <see cref="Visibilities"/> value.
    /// </summary>
    public static string ForVisibility(Visibilities v) => v switch
    {
        Visibilities.Public => VisibilityPublic,
        Visibilities.Private => VisibilityPrivate,
        Visibilities.Internal => VisibilityInternal,
        _ => v.ToString(),
    };

    // ── Token scope labels ────────────────────────────────────────────────────

    /// <summary>Display label for <see cref="TokenScopes.Read"/>.</summary>
    public const string TokenScopeRead = "Read — download and browse packages";

    /// <summary>Display label for <see cref="TokenScopes.Publish"/>.</summary>
    public const string TokenScopePublish = "Publish — create and update packages";

    /// <summary>
    /// Returns the human-facing display label for the given <see cref="TokenScopes"/> value.
    /// Only <see cref="TokenScopes.Read"/> and <see cref="TokenScopes.Publish"/> are surfaced in v1;
    /// Admin is out of scope.
    /// </summary>
    public static string ForTokenScope(TokenScopes s) => s switch
    {
        TokenScopes.Read => TokenScopeRead,
        TokenScopes.Publish => TokenScopePublish,
        _ => s.ToString(),
    };

    // ── Token list / settings page labels ─────────────────────────────────────

    /// <summary>Heading for the active-tokens list section.</summary>
    public const string TokenListSectionTitle = "Active API Tokens";

    /// <summary>Heading for the create-token form section.</summary>
    public const string CreateTokenSectionTitle = "Create New Token";

    /// <summary>CSS class applied to the "current session" badge on the token row.</summary>
    public const string CurrentSessionBadgeClass = "badge-current-session";

    /// <summary>Human-facing label for the "current session" badge on the token row.</summary>
    public const string CurrentSessionBadgeLabel = "current session";

    /// <summary>Heading for the just-created token reveal banner.</summary>
    public const string TokenJustCreatedTitle = "Token created — copy it now";

    /// <summary>Warning text shown alongside the just-created token value (once-only reveal).</summary>
    public const string TokenJustCreatedWarning =
        "This is the only time the token value will be shown. Copy it now — it cannot be retrieved again.";

    // ── Manage page section labels ────────────────────────────────────────────

    /// <summary>Heading for the package-level deprecation section.</summary>
    public const string DeprecatePackageSectionTitle = "Deprecate Package";

    /// <summary>Heading for the un-deprecation action.</summary>
    public const string UndeprecatePackageSectionTitle = "Remove Deprecation";

    /// <summary>Heading for the visibility control section.</summary>
    public const string VisibilitySectionTitle = "Package Visibility";

    /// <summary>Heading for the versions management table.</summary>
    public const string VersionsSectionTitle = "Version Deprecation";
}
