namespace Stash.Stdlib.Models;

using System.Linq;

/// <summary>Describes a function within a built-in namespace (e.g., arr.push, str.upper).</summary>
public record NamespaceFunction(
    string Namespace,
    string Name,
    BuiltInParam[] Parameters,
    string? ReturnType = null,
    bool IsVariadic = false,
    string? Documentation = null,
    DeprecationInfo? Deprecation = null)
{
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

    public string Detail
    {
        get
        {
            var paramParts = Parameters.Select(p => p.Type != null ? $"{p.Name}: {p.Type}" : p.Name);
            string prefix = string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
            string sig = $"fn {prefix}({string.Join(", ", paramParts)})";
            return ReturnType != null ? $"{sig} -> {ReturnType}" : sig;
        }
    }

    public string[] ParamNames => Parameters.Select(p => p.Name).ToArray();
}
