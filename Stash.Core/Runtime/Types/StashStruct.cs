namespace Stash.Runtime.Types;

using System.Collections.Generic;

/// <summary>
/// Represents a struct declaration — a named template with a list of field names and methods.
/// </summary>
public class StashStruct
{
    public string Name { get; }
    public List<string> Fields { get; }
    public Dictionary<string, IStashCallable> Methods { get; }
    public List<StashInterface> Interfaces { get; }

    public StashStruct(string name, List<string> fields, Dictionary<string, IStashCallable> methods)
    {
        Name = name;
        Fields = fields;
        Methods = methods;
        Interfaces = new List<StashInterface>();
    }

    public override string ToString() => $"<struct {Name}>";
}
