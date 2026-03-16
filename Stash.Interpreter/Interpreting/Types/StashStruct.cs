namespace Stash.Interpreting.Types;

using System.Collections.Generic;

/// <summary>
/// Represents a struct declaration — a named template with a list of field names and methods.
/// </summary>
public class StashStruct
{
    public string Name { get; }
    public List<string> Fields { get; }
    public Dictionary<string, StashFunction> Methods { get; }

    public StashStruct(string name, List<string> fields, Dictionary<string, StashFunction> methods)
    {
        Name = name;
        Fields = fields;
        Methods = methods;
    }

    public override string ToString() => $"<struct {Name}>";
}
