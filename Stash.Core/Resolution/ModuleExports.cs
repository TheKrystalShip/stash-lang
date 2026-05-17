namespace Stash.Core.Resolution;

using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
/// Represents the explicit export set of a Stash module as stored on a compiled <see cref="Stash.Bytecode.Chunk"/>.
/// </summary>
/// <remarks>
/// This is the lightweight, Bytecode-layer view of a module's export set.  It carries only the
/// exported names and the <see cref="HasExplicitExports"/> flag needed by the runtime filter.
/// The richer analysis-layer model (with per-name <c>SymbolKind</c>, declaration spans, and
/// diagnostic helpers) lives in <c>Stash.Analysis.ModuleExportsBuilder</c>.
///
/// When <see cref="HasExplicitExports"/> is <see langword="false"/>, the module uses the
/// legacy "export everything" semantics and <see cref="Names"/> is empty.
/// When <see langword="true"/>, only names in <see cref="Names"/> are visible to importers.
/// </remarks>
public sealed class ModuleExports
{
    /// <summary>
    /// Gets whether the module contains at least one <c>export</c> annotation.
    /// When <see langword="false"/>, all top-level bindings are exported (legacy semantics).
    /// </summary>
    public bool HasExplicitExports { get; }

    /// <summary>
    /// Gets the set of exported names.
    /// Empty when <see cref="HasExplicitExports"/> is <see langword="false"/>.
    /// </summary>
    public IReadOnlySet<string> Names { get; }

    /// <summary>
    /// Initializes a new <see cref="ModuleExports"/>.
    /// Use <see cref="ModuleExportsBuilder"/> in <c>Stash.Analysis</c> or
    /// <see cref="Create"/> to construct instances.
    /// </summary>
    internal ModuleExports(bool hasExplicitExports, IReadOnlySet<string> names)
    {
        HasExplicitExports = hasExplicitExports;
        Names = names;
    }

    /// <summary>
    /// Creates a new <see cref="ModuleExports"/> instance from a set of exported names.
    /// Intended for use by the bytecode deserializer (<c>Stash.Bytecode</c>) when reading
    /// a .stashc file.
    /// </summary>
    /// <param name="hasExplicitExports">
    /// <see langword="true"/> when the module has at least one <c>export</c> annotation.
    /// </param>
    /// <param name="names">The set of exported names.</param>
    public static ModuleExports Create(bool hasExplicitExports, IReadOnlySet<string> names)
        => new(hasExplicitExports, names);

    /// <summary>
    /// A singleton <see cref="ModuleExports"/> representing a legacy module with no explicit
    /// export annotations.  <see cref="HasExplicitExports"/> is <see langword="false"/> and
    /// <see cref="Names"/> is empty.
    /// </summary>
    public static readonly ModuleExports Empty =
        new(false, ImmutableHashSet<string>.Empty);
}
