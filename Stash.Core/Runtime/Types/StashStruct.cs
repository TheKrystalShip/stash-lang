namespace Stash.Runtime.Types;

using System.Collections.Generic;

/// <summary>
/// Represents a struct declaration — a named template with a list of field names and methods.
/// </summary>
public class StashStruct
{
    public string Name { get; }
    public List<string> Fields { get; }
    public Dictionary<string, int> FieldIndices { get; }
    public Dictionary<string, IStashCallable> Methods { get; }
    public List<StashInterface> Interfaces { get; }
    public HashSet<string> OriginalMethodNames { get; }

    public StashStruct(string name, List<string> fields, Dictionary<string, IStashCallable> methods)
    {
        Name = name;
        Fields = fields;
        Methods = methods;
        Interfaces = new List<StashInterface>();
        OriginalMethodNames = new HashSet<string>(methods.Keys);

        // Build field index mapping for slot-based instance storage
        var indices = new Dictionary<string, int>(fields.Count);
        for (int i = 0; i < fields.Count; i++)
            indices[fields[i]] = i;
        FieldIndices = indices;
    }

    public override string ToString() => $"<struct {Name}>";
}
