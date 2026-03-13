namespace Stash.Interpreting;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// Represents a runtime instance of a struct — a dictionary of field values with a type tag.
/// </summary>
public class StashInstance
{
    public string TypeName { get; }
    private readonly Dictionary<string, object?> _fields;

    public StashInstance(string typeName, Dictionary<string, object?> fields)
    {
        TypeName = typeName;
        _fields = fields;
    }

    public object? GetField(string name, SourceSpan? span)
    {
        if (_fields.TryGetValue(name, out object? value))
        {
            return value;
        }

        throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
    }

    public void SetField(string name, object? value, SourceSpan? span)
    {
        if (!_fields.ContainsKey(name))
        {
            throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
        }

        _fields[name] = value;
    }

    /// <summary>
    /// Gets all fields of this instance for debugging/inspection.
    /// </summary>
    public IReadOnlyDictionary<string, object?> GetFields() => _fields;

    public override string ToString()
    {
        return $"<{TypeName} instance>";
    }
}
