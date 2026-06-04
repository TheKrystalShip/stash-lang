namespace Stash.Tests.Registry.Web.Fixtures;

/// <summary>
/// Pinned set of every Phase-2 file that must remain byte-unchanged on disk.
/// Used by <see cref="Stash.Tests.Registry.Web.PhaseTwoFilesByteUnchangedMetaTests"/>.
/// </summary>
/// <remarks>
/// This is the single source of truth for the Phase-2 file set. Removing a path from
/// <see cref="Paths"/> is a deliberate action that must be caught at review — the array
/// is the binding floor for the byte-unchanged guard.
/// </remarks>
public static class PhaseTwoFileSet
{
    /// <summary>
    /// The canonical Phase-2 file list (relative to the <c>Stash.Registry.Web/</c> project root).
    /// Every file in this list must be byte-unchanged between the Phase-2 merge-base and HEAD.
    /// </summary>
    public static readonly string[] Paths =
    [
        "Pages/Index.cshtml",
        "Pages/Index.cshtml.cs",
        "Pages/Search.cshtml",
        "Pages/Search.cshtml.cs",
        "Pages/Package.cshtml",
        "Pages/Package.cshtml.cs",
        "Pages/Version.cshtml",
        "Pages/Version.cshtml.cs",
        "Pages/Health.cshtml",
        "Pages/Health.cshtml.cs",
        "Pages/Shared/_Layout.cshtml",
        "Pages/Shared/_ViewImports.cshtml",
        "Pages/_ViewStart.cshtml",
        "Services/IRegistryClient.cs",
        "Services/HttpRegistryClient.cs",
        "Services/RegistryClientException.cs",
        "Configuration/RegistryClientConfig.cs",
        "Rendering/IReadmeRenderer.cs",
        "Rendering/ReadmeRenderer.cs",
        "Rendering/SafeUrl.cs",
    ];
}
