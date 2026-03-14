namespace Stash.Interpreting.Types;

using System.Collections.Generic;

/// <summary>
/// Represents a struct declaration — a named template with a list of field names.
/// </summary>
public class StashStruct
{
    public string Name { get; }
    public List<string> Fields { get; }

    public StashStruct(string name, List<string> fields)
    {
        Name = name;
        Fields = fields;
    }

    public override string ToString() => $"<struct {Name}>";
}
