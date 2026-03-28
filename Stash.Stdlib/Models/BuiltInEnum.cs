namespace Stash.Stdlib.Models;

/// <summary>Describes a built-in enum type.</summary>
public record BuiltInEnum(string Name, string[] Members, string? Namespace = null)
{
    public string Detail => $"enum {Name} {{ {string.Join(", ", Members)} }}";
}
