namespace Stash.Stdlib.Models;

using System.Linq;

/// <summary>Describes a built-in struct type (CommandResult, HttpResponse, etc.).</summary>
public record BuiltInStruct(string Name, BuiltInField[] Fields)
{
    public string Detail
    {
        get
        {
            var fieldParts = Fields.Select(f => f.Type != null ? $"{f.Name}: {f.Type}" : f.Name);
            return $"struct {Name} {{ {string.Join(", ", fieldParts)} }}";
        }
    }
}
