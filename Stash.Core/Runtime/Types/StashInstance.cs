namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using System.Text;
using Stash.Common;
using Stash.Runtime;

/// <summary>
/// Represents a runtime instance of a struct — a dictionary of field values with a type tag.
/// </summary>
public class StashInstance
{
    public string TypeName { get; }
    public StashStruct? Struct { get; }
    private readonly Dictionary<string, StashValue> _fields;

    /// <summary>
    /// When set, ToString() returns the stringified value of this field instead of the full struct representation.
    /// </summary>
    public string? StringifyField { get; init; }

    public StashInstance(string typeName, Dictionary<string, StashValue> fields)
    {
        TypeName = typeName;
        Struct = null;
        _fields = fields;
    }

    public StashInstance(string typeName, StashStruct structDef, Dictionary<string, StashValue> fields)
    {
        TypeName = typeName;
        Struct = structDef;
        _fields = fields;
    }

    public StashInstance(string typeName, Dictionary<string, StashValue> fields, StashStruct? structDef)
    {
        TypeName = typeName;
        Struct = structDef;
        _fields = fields;
    }

    public StashValue GetField(string name, SourceSpan? span)
    {
        if (_fields.TryGetValue(name, out StashValue value))
        {
            return value;
        }

        if (Struct != null && Struct.Methods.TryGetValue(name, out IStashCallable? method))
        {
            return StashValue.FromObj(new StashBoundMethod(this, method));
        }

        throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
    }

    public void SetField(string name, StashValue value, SourceSpan? span)
    {
        if (!_fields.ContainsKey(name))
        {
            throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
        }

        _fields[name] = value;
    }

    public IReadOnlyDictionary<string, StashValue> GetFields() => _fields;

    public IEnumerable<KeyValuePair<string, StashValue>> GetAllFields()
    {
        return _fields;
    }

    [ThreadStatic]
    private static HashSet<StashInstance>? _toStringGuard;

    public override string ToString()
    {
        _toStringGuard ??= new HashSet<StashInstance>(ReferenceEqualityComparer.Instance);

        if (!_toStringGuard.Add(this))
        {
            return $"{TypeName} {{ ... }}";
        }

        try
        {
            if (StringifyField is not null && _fields.TryGetValue(StringifyField, out StashValue sfValue))
            {
                return RuntimeValues.Stringify(sfValue.ToObject());
            }

            var sb = new StringBuilder(TypeName);
            sb.Append(" { ");
            bool first = true;
            foreach (var kvp in _fields)
            {
                if (!first)
                {
                    sb.Append(", ");
                }
                first = false;
                sb.Append(kvp.Key);
                sb.Append(": ");
                sb.Append(RuntimeValues.Stringify(kvp.Value.ToObject()));
            }
            sb.Append(" }");
            return sb.ToString();
        }
        finally
        {
            _toStringGuard.Remove(this);
        }
    }
}
