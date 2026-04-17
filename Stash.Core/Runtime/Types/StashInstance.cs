namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using System.Text;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a runtime instance of a struct — field values with a type tag.
/// Struct-backed instances use slot-based array storage; anonymous instances use dictionary storage.
/// </summary>
public class StashInstance : IVMTyped, IVMFieldAccessible, IVMFieldMutable, IVMStringifiable, IVMEquatable
{
    public string TypeName { get; }
    public StashStruct? Struct { get; }

    /// <summary>
    /// Slot-based field storage for struct-backed instances. Null for anonymous instances.
    /// When non-null, fields are indexed via <see cref="StashStruct.FieldIndices"/>.
    /// </summary>
    public readonly StashValue[]? FieldSlots;

    /// <summary>Dictionary-based field storage for anonymous instances (no StashStruct).</summary>
    private readonly Dictionary<string, StashValue>? _fields;

    /// <summary>
    /// When set, ToString() returns the stringified value of this field instead of the full struct representation.
    /// </summary>
    public string? StringifyField { get; init; }

    // --- Constructors for anonymous instances (dictionary storage) ---

    public StashInstance(string typeName, Dictionary<string, StashValue> fields)
    {
        TypeName = typeName;
        Struct = null;
        _fields = fields;
    }

    public StashInstance(string typeName, Dictionary<string, StashValue> fields, StashStruct? structDef)
    {
        TypeName = typeName;
        Struct = structDef;
        _fields = fields;
    }

    // --- Constructor for struct-backed instances (slot storage) ---

    public StashInstance(string typeName, StashStruct structDef, StashValue[] fieldSlots)
    {
        TypeName = typeName;
        Struct = structDef;
        FieldSlots = fieldSlots;
    }

    public StashValue GetField(string name, SourceSpan? span)
    {
        if (FieldSlots is not null)
        {
            if (Struct!.FieldIndices.TryGetValue(name, out int idx))
                return FieldSlots[idx];

            if (Struct.Methods.TryGetValue(name, out IStashCallable? method))
                return StashValue.FromObj(new StashBoundMethod(this, method));

            throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
        }

        if (_fields!.TryGetValue(name, out StashValue value))
            return value;

        if (Struct != null && Struct.Methods.TryGetValue(name, out IStashCallable? method2))
            return StashValue.FromObj(new StashBoundMethod(this, method2));

        throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
    }

    public void SetField(string name, StashValue value, SourceSpan? span)
    {
        if (FieldSlots is not null)
        {
            if (Struct!.FieldIndices.TryGetValue(name, out int idx))
            {
                FieldSlots[idx] = value;
                return;
            }

            throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
        }

        if (!_fields!.ContainsKey(name))
            throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);

        _fields[name] = value;
    }

    public IReadOnlyDictionary<string, StashValue> GetFields()
    {
        if (FieldSlots is not null)
        {
            var dict = new Dictionary<string, StashValue>(Struct!.Fields.Count);
            for (int i = 0; i < Struct.Fields.Count; i++)
                dict[Struct.Fields[i]] = FieldSlots[i];
            return dict;
        }

        return _fields!;
    }

    public IEnumerable<KeyValuePair<string, StashValue>> GetAllFields()
    {
        if (FieldSlots is not null)
        {
            for (int i = 0; i < Struct!.Fields.Count; i++)
                yield return new KeyValuePair<string, StashValue>(Struct.Fields[i], FieldSlots[i]);
            yield break;
        }

        foreach (var kvp in _fields!)
            yield return kvp;
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
            if (StringifyField is not null)
            {
                if (FieldSlots is not null)
                {
                    if (Struct!.FieldIndices.TryGetValue(StringifyField, out int sfIdx))
                        return RuntimeValues.Stringify(FieldSlots[sfIdx].ToObject());
                }
                else if (_fields!.TryGetValue(StringifyField, out StashValue sfValue))
                {
                    return RuntimeValues.Stringify(sfValue.ToObject());
                }
            }

            var sb = new StringBuilder(TypeName);
            sb.Append(" { ");
            bool first = true;

            if (FieldSlots is not null)
            {
                for (int i = 0; i < Struct!.Fields.Count; i++)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(Struct.Fields[i]);
                    sb.Append(": ");
                    sb.Append(RuntimeValues.Stringify(FieldSlots[i].ToObject()));
                }
            }
            else
            {
                foreach (var kvp in _fields!)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(kvp.Key);
                    sb.Append(": ");
                    sb.Append(RuntimeValues.Stringify(kvp.Value.ToObject()));
                }
            }

            sb.Append(" }");
            return sb.ToString();
        }
        finally
        {
            _toStringGuard.Remove(this);
        }
    }

    // --- Protocol implementations ---

    public string VMTypeName => TypeName;

    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        if (FieldSlots is not null)
        {
            if (Struct!.FieldIndices.TryGetValue(name, out int idx))
            {
                value = FieldSlots[idx];
                return true;
            }

            if (Struct.Methods.TryGetValue(name, out IStashCallable? method))
            {
                value = StashValue.FromObj(new StashBoundMethod(this, method));
                return true;
            }

            value = default;
            return false;
        }

        if (_fields!.TryGetValue(name, out StashValue fieldValue))
        {
            value = fieldValue;
            return true;
        }

        if (Struct != null && Struct.Methods.TryGetValue(name, out IStashCallable? method2))
        {
            value = StashValue.FromObj(new StashBoundMethod(this, method2));
            return true;
        }

        value = default;
        return false;
    }

    public void VMSetField(string name, StashValue value, SourceSpan? span)
    {
        SetField(name, value, span);
    }

    public string VMToString() => ToString();

    public bool VMEquals(StashValue other) => other.IsObj && ReferenceEquals(this, other.AsObj);
}

