namespace Stash.Runtime.Stdlib;

using System.Collections.Generic;
using Stash.Runtime.Types;

/// <summary>
/// Fluent builder for constructing a stdlib namespace with both
/// runtime implementation and optional tooling metadata.
/// Lives in Stash.Core so external languages can use it without depending on Stash.Stdlib.
/// </summary>
public sealed class StdlibNamespaceBuilder
{
    private readonly string _name;
    private readonly StashNamespace _namespace;
    private readonly List<StdlibFunctionMeta> _functions = [];
    private readonly List<StdlibConstantMeta> _constants = [];
    private StashCapabilities _requiredCapability = StashCapabilities.None;

    public StdlibNamespaceBuilder(string name)
    {
        _name = name;
        _namespace = new StashNamespace(name) { IsBuiltIn = true };
    }

    /// <summary>
    /// Register a function with the zero-alloc DirectHandler signature.
    /// Parameter metadata is optional but enables LSP completion/hover.
    /// </summary>
    public StdlibNamespaceBuilder Function(
        string name,
        int arity,
        BuiltInFunction.DirectHandler handler,
        StdlibParamMeta[]? parameters = null,
        string? returnType = null,
        bool isVariadic = false,
        string? documentation = null)
    {
        int effectiveArity = isVariadic ? -1 : arity;
        string qualifiedName = $"{_name}.{name}";
        _namespace.Define(name, new BuiltInFunction(qualifiedName, effectiveArity, handler));

        if (parameters is not null)
        {
            _functions.Add(new StdlibFunctionMeta(
                name, parameters, returnType, isVariadic, documentation));
        }

        return this;
    }

    /// <summary>Register a constant value.</summary>
    public StdlibNamespaceBuilder Constant(
        string name,
        StashValue value,
        string type,
        string displayValue,
        string? documentation = null)
    {
        _namespace.Define(name, value.ToObject());
        _constants.Add(new StdlibConstantMeta(name, type, displayValue, documentation));
        return this;
    }

    /// <summary>Declare the capability requirement for this namespace.</summary>
    public StdlibNamespaceBuilder RequiresCapability(StashCapabilities capability)
    {
        _requiredCapability = capability;
        return this;
    }

    /// <summary>Build the namespace entry. Freezes the namespace automatically.</summary>
    public StdlibNamespaceEntry Build()
    {
        _namespace.Freeze();
        return new StdlibNamespaceEntry(
            _name,
            _namespace,
            _requiredCapability,
            _functions.Count > 0 ? _functions : null,
            _constants.Count > 0 ? _constants : null);
    }
}
