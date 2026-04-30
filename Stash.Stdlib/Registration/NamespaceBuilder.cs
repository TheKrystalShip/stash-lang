namespace Stash.Stdlib.Registration;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;

/// <summary>
/// Fluent builder for creating namespace definitions that pair metadata with implementations.
/// Each <see cref="Function"/> call registers BOTH the runtime callable and the LSP/Analysis metadata,
/// making it impossible to add one without the other.
/// </summary>
public class NamespaceBuilder
{
    private readonly string _name;
    private readonly StashNamespace _namespace;
    private readonly List<NamespaceFunction> _functions = [];
    private readonly List<NamespaceConstant> _constants = [];
    private readonly List<BuiltInStruct> _structs = [];
    private readonly List<BuiltInEnum> _enums = [];
    private StashCapabilities _requiredCapability = StashCapabilities.None;

    public NamespaceBuilder(string name)
    {
        _name = name;
        _namespace = new StashNamespace(name) { IsBuiltIn = true };
    }

    /// <summary>
    /// Sets the capability required for this namespace to be registered.
    /// Namespaces without a required capability are always available.
    /// </summary>
    public NamespaceBuilder RequiresCapability(StashCapabilities capability)
    {
        _requiredCapability = capability;
        return this;
    }

    /// <summary>
    /// Defines a function using the zero-allocation StashValue-native call path.
    /// </summary>
    public NamespaceBuilder Function(string name, BuiltInParam[] parameters,
        Runtime.BuiltInFunction.DirectHandler body,
        string? returnType = null, bool isVariadic = false, string? documentation = null,
        DeprecationInfo? deprecation = null)
    {
        int arity = isVariadic ? -1 : parameters.Length;
        string qualifiedName = string.IsNullOrEmpty(_name) ? name : $"{_name}.{name}";
        _namespace.Define(name, new Runtime.BuiltInFunction(qualifiedName, arity, body));
        _functions.Add(new NamespaceFunction(_name, name, parameters, returnType, isVariadic, documentation, deprecation));
        return this;
    }

    /// <summary>
    /// Defines a constant with both runtime value and metadata in a single call.
    /// </summary>
    /// <param name="name">The constant name (without namespace prefix).</param>
    /// <param name="runtimeValue">The actual runtime value (e.g., Math.PI as double).</param>
    /// <param name="type">The type string for documentation (e.g., "float").</param>
    /// <param name="displayValue">The display string for documentation (e.g., "3.141592653589793").</param>
    /// <param name="documentation">Optional documentation string.</param>
    /// <param name="deprecation">Optional deprecation metadata for SA0830.</param>
    public NamespaceBuilder Constant(string name, object? runtimeValue,
        string type, string displayValue, string? documentation = null,
        DeprecationInfo? deprecation = null)
    {
        _namespace.Define(name, runtimeValue);
        _constants.Add(new NamespaceConstant(_name, name, type, displayValue, documentation, deprecation));
        return this;
    }

    /// <summary>
    /// Registers a built-in struct type produced by this namespace.
    /// The struct is both added to the LSP/Analysis metadata list and defined as a constructible
    /// type in the runtime namespace so that <c>ns.StructName { field: value }</c> works in Stash.
    /// </summary>
    public NamespaceBuilder Struct(string name, BuiltInField[] fields)
    {
        if (_structs.Any(s => s.Name == name))
            throw new ArgumentException($"Struct '{name}' is already registered in namespace '{_name}'.", nameof(name));
        _structs.Add(new BuiltInStruct(name, fields));
        var fieldNames = fields.Select(f => f.Name).ToList();
        _namespace.Define(name, new StashStruct(name, fieldNames, new Dictionary<string, IStashCallable>()) { IsBuiltIn = true });
        return this;
    }

    /// <summary>
    /// Registers a built-in enum type produced by this namespace.
    /// The enum is both added to the LSP/Analysis metadata list and defined as a runtime
    /// value in the namespace so that <c>EnumName.Member</c> works in Stash.
    /// </summary>
    public NamespaceBuilder Enum(string name, string[] members)
    {
        if (_enums.Any(e => e.Name == name))
            throw new ArgumentException($"Enum '{name}' is already registered in namespace '{_name}'.", nameof(name));
        _enums.Add(new BuiltInEnum(name, members, _name));
        _namespace.Define(name, new StashEnum(name, members.ToList()) { IsBuiltIn = true });
        return this;
    }

    /// <summary>
    /// Builds the namespace definition. The runtime namespace is frozen lazily on first member access.
    /// </summary>
    public NamespaceDefinition Build()
    {
        return new NamespaceDefinition(_name, _namespace, _functions, _constants, _structs, _enums, _requiredCapability);
    }
}
