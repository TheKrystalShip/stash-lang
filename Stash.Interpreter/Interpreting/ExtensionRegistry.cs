namespace Stash.Interpreting;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Stash.Runtime;

/// <summary>
/// Stores extension methods registered by <c>extend</c> blocks, keyed by type name and method name.
/// </summary>
/// <remarks>
/// Each interpreter instance has its own extension registry, populated as <c>extend</c> blocks
/// are executed during script loading. Extension methods are resolved after struct methods
/// and before UFCS namespace functions in the method resolution order.
/// </remarks>
internal class ExtensionRegistry
{
    private readonly Dictionary<string, Dictionary<string, IStashCallable>> _extensions = new();

    /// <summary>Registers an extension method for the given type.</summary>
    /// <param name="typeName">The target type name (e.g., "string", "array", "User").</param>
    /// <param name="methodName">The method name to register.</param>
    /// <param name="callable">The callable implementing the method.</param>
    public void Register(string typeName, string methodName, IStashCallable callable)
    {
        if (!_extensions.TryGetValue(typeName, out Dictionary<string, IStashCallable>? methods))
        {
            methods = new Dictionary<string, IStashCallable>();
            _extensions[typeName] = methods;
        }

        methods[methodName] = callable;
    }

    /// <summary>Attempts to retrieve an extension method for the given type and method name.</summary>
    public bool TryGetMethod(string typeName, string methodName, [NotNullWhen(true)] out IStashCallable? callable)
    {
        if (_extensions.TryGetValue(typeName, out Dictionary<string, IStashCallable>? methods) &&
            methods.TryGetValue(methodName, out callable))
        {
            return true;
        }

        callable = null;
        return false;
    }

    /// <summary>Checks whether an extension method exists for the given type and method name.</summary>
    public bool HasMethod(string typeName, string methodName)
    {
        return _extensions.TryGetValue(typeName, out Dictionary<string, IStashCallable>? methods) &&
               methods.ContainsKey(methodName);
    }

    /// <summary>Gets all registered extension methods for a type.</summary>
    public IReadOnlyDictionary<string, IStashCallable>? GetMethodsForType(string typeName)
    {
        return _extensions.TryGetValue(typeName, out Dictionary<string, IStashCallable>? methods) ? methods : null;
    }
}
