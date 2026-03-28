namespace Stash.Stdlib.Models;

using System.Linq;

/// <summary>Describes a global built-in function (typeof, nameof, len, etc.).</summary>
public record BuiltInFunction(
    string Name,
    BuiltInParam[] Parameters,
    string? ReturnType = null,
    string? Documentation = null)
{
    public string Detail
    {
        get
        {
            var paramParts = Parameters.Select(p => p.Type != null ? $"{p.Name}: {p.Type}" : p.Name);
            string sig = $"fn {Name}({string.Join(", ", paramParts)})";
            return ReturnType != null ? $"{sig} -> {ReturnType}" : sig;
        }
    }

    public string[] ParamNames => Parameters.Select(p => p.Name).ToArray();
}
