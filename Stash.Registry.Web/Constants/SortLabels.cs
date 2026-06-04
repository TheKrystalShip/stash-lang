using Stash.Registry.Contracts;

namespace Stash.Registry.Web.Constants;

/// <summary>
/// Display labels for <see cref="PackageSortOrder"/> values shown in the search sort dropdown.
/// Single source of truth — never inline sort label strings in Razor views.
/// </summary>
public static class SortLabels
{
    /// <summary>Display label for <see cref="PackageSortOrder.Relevance"/>.</summary>
    public const string Relevance = "Relevance";

    /// <summary>Display label for <see cref="PackageSortOrder.Name"/>.</summary>
    public const string Name = "Name";

    /// <summary>Display label for <see cref="PackageSortOrder.Updated"/>.</summary>
    public const string Updated = "Recently Updated";

    /// <summary>Display label for <see cref="PackageSortOrder.Published"/>.</summary>
    public const string Published = "Recently Published";

    /// <summary>
    /// Returns the display label for the given sort order.
    /// </summary>
    public static string For(PackageSortOrder order) => order switch
    {
        PackageSortOrder.Relevance => Relevance,
        PackageSortOrder.Name => Name,
        PackageSortOrder.Updated => Updated,
        PackageSortOrder.Published => Published,
        _ => order.ToString(),
    };
}
