namespace Stash.Core.Resolution;

using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
/// Represents the explicit export set of a Stash module as stored on a compiled <see cref="Stash.Bytecode.Chunk"/>.
/// </summary>
/// <remarks>
/// This is the lightweight, Bytecode-layer view of a module's export set.  It carries only the
/// exported names.  The richer analysis-layer model (with per-name <c>SymbolKind</c>, declaration
/// spans, and diagnostic helpers) lives in <c>Stash.Analysis.ModuleExportsBuilder</c>.
///
/// <see cref="Names"/> is the single source of truth.  An empty <see cref="Names"/> set means
/// the module exports nothing (private-by-default semantics).  Only names present in
/// <see cref="Names"/> are visible to importers.
/// </remarks>
public sealed class ModuleExports
{
    /// <summary>
    /// Gets the set of exported names.
    /// Empty when the module has no <c>export</c> annotations (the module exports nothing).
    /// </summary>
    public IReadOnlySet<string> Names { get; }

    /// <summary>
    /// Initializes a new <see cref="ModuleExports"/>.
    /// Use <see cref="ModuleExportsBuilder"/> in <c>Stash.Analysis</c> or
    /// <see cref="Create"/> to construct instances.
    /// </summary>
    internal ModuleExports(IReadOnlySet<string> names)
    {
        Names = names;
    }

    /// <summary>
    /// Creates a new <see cref="ModuleExports"/> instance from a set of exported names.
    /// Intended for use by the bytecode deserializer (<c>Stash.Bytecode</c>) when reading
    /// a .stashc file.
    /// </summary>
    /// <param name="names">The set of exported names.</param>
    public static ModuleExports Create(IReadOnlySet<string> names)
        => new(names);

    /// <summary>
    /// A singleton <see cref="ModuleExports"/> representing a module with no export annotations.
    /// <see cref="Names"/> is empty, meaning the module exports nothing to importers.
    /// </summary>
    public static readonly ModuleExports Empty =
        new(ImmutableHashSet<string>.Empty);
}
