namespace Stash.Analysis.Cli;

using System.Collections.Generic;

/// <summary>
/// Maps a result-variable name (the identifier bound to the output of <c>cli.parse(schema)</c>)
/// to the <see cref="CliSchemaInfo"/> extracted from the corresponding literal schema.
/// </summary>
/// <remarks>
/// Built by <see cref="CliSchemaAnalyzer.Analyze"/> and attached to <see cref="Stash.Analysis.AnalysisResult"/>
/// for consumption by hover and completion handlers.
/// </remarks>
public sealed class CliSchemaIndex
{
    private readonly Dictionary<string, CliSchemaInfo> _map =
        new(System.StringComparer.Ordinal);

    /// <summary>
    /// Associates a parse-result variable name with its schema info.
    /// </summary>
    public void Add(string resultVar, CliSchemaInfo info)
    {
        _map[resultVar] = info;
    }

    /// <summary>
    /// Tries to retrieve the <see cref="CliSchemaInfo"/> for the given variable name.
    /// Returns <see langword="null"/> if no literal schema was found for that variable.
    /// </summary>
    public CliSchemaInfo? TryGet(string resultVar)
    {
        return _map.TryGetValue(resultVar, out var info) ? info : null;
    }

    /// <summary>Gets the number of entries in this index.</summary>
    public int Count => _map.Count;
}

/// <summary>
/// Holds the statically-extracted fields from a literal <c>cli.schema({...})</c> call.
/// </summary>
public sealed class CliSchemaInfo
{
    /// <summary>The declared fields of the schema, in declaration order.</summary>
    public IReadOnlyList<CliFieldInfo> Fields { get; }

    /// <summary>
    /// Creates a new <see cref="CliSchemaInfo"/> with the given fields.
    /// </summary>
    public CliSchemaInfo(IReadOnlyList<CliFieldInfo> fields)
    {
        Fields = fields;
    }
}

/// <summary>
/// Describes a single declared field in a literal <c>cli.schema({...})</c>.
/// </summary>
public sealed class CliFieldInfo
{
    /// <summary>The Stash-side property key (dict key) as it appears in the schema definition.</summary>
    public string Name { get; }

    /// <summary>
    /// The inferred type tag for the field (e.g. <c>"string"</c>, <c>"int"</c>, <c>"bool"</c>),
    /// or <see langword="null"/> when the type could not be statically determined.
    /// </summary>
    public string? TypeTag { get; }

    /// <summary>
    /// Creates a new <see cref="CliFieldInfo"/>.
    /// </summary>
    public CliFieldInfo(string name, string? typeTag)
    {
        Name = name;
        TypeTag = typeTag;
    }
}
