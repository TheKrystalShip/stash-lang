namespace Stash.Stdlib.Models;

/// <summary>Describes a constant within a built-in namespace (e.g., math.PI).</summary>
public record NamespaceConstant(
    string Namespace,
    string Name,
    string Type,
    string Value,
    string? Documentation = null,
    DeprecationInfo? Deprecation = null)
{
    public string QualifiedName => $"{Namespace}.{Name}";
    public string Detail => $"const {Namespace}.{Name}: {Type} = {Value}";
}
