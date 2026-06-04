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
