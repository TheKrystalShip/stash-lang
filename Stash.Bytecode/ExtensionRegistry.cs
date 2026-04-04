namespace Stash.Bytecode;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Stash.Runtime;

/// <summary>
/// Registry for extension methods defined via extend blocks on built-in types.
/// </summary>
internal sealed class ExtensionRegistry
{
    private readonly Dictionary<string, Dictionary<string, IStashCallable>> _extensions = new();

    public void Register(string typeName, string methodName, IStashCallable callable)
    {
        if (!_extensions.TryGetValue(typeName, out Dictionary<string, IStashCallable>? methods))
        {
            methods = new Dictionary<string, IStashCallable>();
            _extensions[typeName] = methods;
        }
        methods[methodName] = callable;
    }

    public bool TryGetMethod(string typeName, string methodName,
                             [NotNullWhen(true)] out IStashCallable? callable)
    {
        if (_extensions.TryGetValue(typeName, out Dictionary<string, IStashCallable>? methods) &&
            methods.TryGetValue(methodName, out callable))
            return true;
        callable = null;
        return false;
    }
}
