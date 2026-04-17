namespace Stash.Runtime.Stdlib;

using System.Collections.Generic;
using Stash.Runtime.Types;

/// <summary>A namespace contributed by an IStdlibProvider.</summary>
public sealed record StdlibNamespaceEntry(
    string Name,
    StashNamespace Namespace,
    StashCapabilities RequiredCapability = StashCapabilities.None,
    IReadOnlyList<StdlibFunctionMeta>? Functions = null,
    IReadOnlyList<StdlibConstantMeta>? Constants = null
);

/// <summary>A standalone global (function or value) contributed by an IStdlibProvider.</summary>
public sealed record StdlibGlobalEntry(
    string Name,
    StashValue Value,
    StashCapabilities RequiredCapability = StashCapabilities.None,
    StdlibFunctionMeta? FunctionMeta = null
);

/// <summary>Tooling metadata for a single function in a namespace.</summary>
public sealed record StdlibFunctionMeta(
    string Name,
    StdlibParamMeta[] Parameters,
    string? ReturnType = null,
    bool IsVariadic = false,
    string? Documentation = null
);

/// <summary>Tooling metadata for a function parameter.</summary>
public sealed record StdlibParamMeta(
    string Name,
    string? Type = null
);

/// <summary>Tooling metadata for a namespace constant.</summary>
public sealed record StdlibConstantMeta(
    string Name,
    string Type,
    string DisplayValue,
    string? Documentation = null
);
