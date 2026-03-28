namespace Stash.Interpreting.Types;

using System.Collections.Generic;

/// <summary>
/// Represents a required field declared in an interface.
/// </summary>
/// <param name="Name">The field name.</param>
/// <param name="TypeHint">The optional type hint string, or <c>null</c> if unspecified.</param>
public record InterfaceField(string Name, string? TypeHint);

/// <summary>
/// Represents a required method signature declared in an interface.
/// </summary>
/// <param name="Name">The method name.</param>
/// <param name="Arity">The required parameter count (excluding <c>self</c>).</param>
/// <param name="ParameterNames">The parameter names (for documentation; not enforced at runtime).</param>
/// <param name="ParameterTypes">The optional type hint for each parameter. Each entry is <c>null</c> if unspecified.</param>
/// <param name="ReturnType">The optional return type hint string, or <c>null</c> if unspecified.</param>
public record InterfaceMethod(string Name, int Arity, List<string> ParameterNames, List<string?> ParameterTypes, string? ReturnType);

/// <summary>
/// Represents an interface declaration — a named contract with required fields and method signatures.
/// </summary>
/// <remarks>
/// Interfaces are pure contracts with no method bodies or dispatch mechanism. At struct definition time,
/// the interpreter validates that the struct satisfies all required fields and methods. The <c>is</c>
/// operator checks interface conformance via reference equality against the struct's interface list.
/// </remarks>
public class StashInterface
{
    /// <summary>Gets the interface name.</summary>
    public string Name { get; }
    /// <summary>Gets the list of parent interfaces this interface extends. Empty in v1; reserved for future composition.</summary>
    public List<StashInterface> Extends { get; }
    /// <summary>Gets the list of required fields.</summary>
    public List<InterfaceField> RequiredFields { get; }
    /// <summary>Gets the list of required method signatures.</summary>
    public List<InterfaceMethod> RequiredMethods { get; }

    /// <summary>Initializes a new instance of <see cref="StashInterface"/>.</summary>
    public StashInterface(string name, List<InterfaceField> requiredFields, List<InterfaceMethod> requiredMethods)
    {
        Name = name;
        Extends = new List<StashInterface>();
        RequiredFields = requiredFields;
        RequiredMethods = requiredMethods;
    }

    /// <inheritdoc />
    public override string ToString() => $"<interface {Name}>";
}
