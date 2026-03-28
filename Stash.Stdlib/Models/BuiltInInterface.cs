namespace Stash.Stdlib.Models;

using System.Linq;

/// <summary>Describes a built-in interface type.</summary>
public record BuiltInInterface(string Name, BuiltInField[] Fields, string[] Methods)
{
    public string Detail => $"interface {Name} {{ {string.Join(", ", Fields.Select(f => f.Type != null ? $"{f.Name}: {f.Type}" : f.Name).Concat(Methods.Select(m => m + "()")))} }}";
}
