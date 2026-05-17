namespace Stash.Analysis;

using System.Collections.Generic;
using System.Collections.Immutable;
using Stash.Core.Resolution;
using Stash.Parsing.AST;

/// <summary>
/// Builds the lightweight <see cref="Stash.Core.Resolution.ModuleExports"/> record that is
/// attached to the compiled <c>Chunk</c> and used by the runtime filter at module-load time.
/// </summary>
/// <remarks>
/// This class contains only the logic needed to determine which names are exported.  Diagnostic
/// emission (SA0805–SA0808) is performed by the <see cref="ModuleExports.Build"/> static method in
/// <see cref="ModuleExports"/>, which is invoked by <see cref="SemanticValidator"/> during the
/// analysis pass.  <see cref="ModuleExportsBuilder.Build"/> is the compilation-pipeline entry
/// point: it is called by the caller that owns both the analysis result and the compiler, and the
/// resulting <see cref="Stash.Core.Resolution.ModuleExports"/> is passed to
/// <c>Compiler.Compile()</c> as an optional argument.
/// </remarks>
public static class ModuleExportsBuilder
{
    /// <summary>
    /// Walks the top-level statement list once and produces the lightweight
    /// <see cref="Stash.Core.Resolution.ModuleExports"/> record suitable for attaching to a
    /// compiled <c>Chunk</c>.
    /// </summary>
    /// <param name="topLevel">The top-level statement list of the module.</param>
    /// <param name="diagnostics">
    /// The diagnostic list to which SA0805–SA0808 violations are appended.
    /// </param>
    /// <returns>
    /// A <see cref="Stash.Core.Resolution.ModuleExports"/> instance.
    /// When no export annotation is found, <see cref="Stash.Core.Resolution.ModuleExports.HasExplicitExports"/>
    /// is <see langword="false"/> and <see cref="Stash.Core.Resolution.ModuleExports.Names"/> is empty.
    /// </returns>
    public static Stash.Core.Resolution.ModuleExports Build(
        IReadOnlyList<Stmt> topLevel,
        List<SemanticDiagnostic> diagnostics)
    {
        // Delegate to the rich Analysis builder and extract just the name set.
        var analysisExports = ModuleExports.Build(topLevel, null!, diagnostics);

        if (!analysisExports.HasExplicitExports)
        {
            return Stash.Core.Resolution.ModuleExports.Empty;
        }

        var names = ImmutableHashSet.CreateRange(analysisExports.Names.Keys);
        return Stash.Core.Resolution.ModuleExports.Create(true, names);
    }
}
