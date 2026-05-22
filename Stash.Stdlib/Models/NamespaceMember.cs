namespace Stash.Stdlib.Models;

using Stash.Stdlib.Abstractions;

/// <summary>
/// Describes a read-only data member within a built-in namespace, registered via <c>[StashMember]</c>.
/// </summary>
public record NamespaceMember(
    string Namespace,
    string Name,
    string? ReturnType = null,
    Stability Stability = Stability.Cached,
    string? Documentation = null,
    DeprecationInfo? Deprecation = null,
    ThrowsEntry[]? Throws = null)
{
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

    public string Detail
    {
        get
        {
            string prefix = string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
            string stab = Stability == Stability.Live ? " [live]" : string.Empty;
            return ReturnType != null
                ? $"member{stab} {prefix}: {ReturnType}"
                : $"member{stab} {prefix}";
        }
    }
}
