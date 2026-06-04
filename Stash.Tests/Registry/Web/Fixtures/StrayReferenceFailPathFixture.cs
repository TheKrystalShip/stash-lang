using System.Collections.Generic;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Synthetic fixture used by <see cref="WebProjectIsolationMetaTests"/> self-tests.
/// Provides two reference-name lists: one containing the forbidden assembly name
/// (the "stray reference" case) and one without it (the clean case).
/// This proves the scan helper trips when a stray reference exists and passes when clean.
/// </summary>
/// <remarks>
/// The forbidden name (<c>StashRegistry</c> at runtime) is obtained by the test
/// through <c>typeof(Stash.Registry.Configuration.RegistryConfig).Assembly.GetName().Name</c>,
/// so the fixture stays correct even if <c>&lt;AssemblyName&gt;</c> ever changes.
/// This file does NOT introduce a real <c>&lt;ProjectReference&gt;</c> to Stash.Registry —
/// it is a synthetic-input self-test feeding the scan helper directly.
/// </remarks>
internal static class StrayReferenceFailPathFixture
{
    /// <summary>
    /// A synthesised list that mimics what <c>Assembly.GetReferencedAssemblies()</c> would
    /// return for a web project that <em>does</em> have a stray reference to the core registry.
    /// Includes <c>StashRegistry</c> (the actual runtime name of <c>Stash.Registry</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> StrayReferencedNames =
    [
        "Stash.Registry.Contracts",
        "Microsoft.AspNetCore.Mvc.RazorPages",
        "System.Runtime",
        // The stray reference — identical to what typeof(RegistryConfig).Assembly.GetName().Name returns:
        typeof(Stash.Registry.Configuration.RegistryConfig).Assembly.GetName().Name!,
    ];

    /// <summary>
    /// A synthesised list for a clean web project — only Contracts and framework assemblies.
    /// The forbidden assembly name is deliberately absent.
    /// </summary>
    public static readonly IReadOnlyList<string> CleanReferencedNames =
    [
        "Stash.Registry.Contracts",
        "Microsoft.AspNetCore.Mvc.RazorPages",
        "System.Runtime",
    ];
}
