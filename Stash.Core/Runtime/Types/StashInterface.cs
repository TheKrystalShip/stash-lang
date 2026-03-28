namespace Stash.Runtime.Types;

using System.Collections.Generic;

/// <summary>
/// Represents a required field declared in an interface.
/// </summary>
public record InterfaceField(string Name, string? TypeHint);

/// <summary>
/// Represents a required method signature declared in an interface.
/// </summary>
public record InterfaceMethod(string Name, int Arity, List<string> ParameterNames, List<string?> ParameterTypes, string? ReturnType);

/// <summary>
/// Represents an interface declaration — a named contract with required fields and method signatures.
/// </summary>
public class StashInterface
{
    public string Name { get; }
    public List<StashInterface> Extends { get; }
    public List<InterfaceField> RequiredFields { get; }
    public List<InterfaceMethod> RequiredMethods { get; }

    public StashInterface(string name, List<InterfaceField> requiredFields, List<InterfaceMethod> requiredMethods)
    {
        Name = name;
        Extends = new List<StashInterface>();
        RequiredFields = requiredFields;
        RequiredMethods = requiredMethods;
    }

    public override string ToString() => $"<interface {Name}>";
}
