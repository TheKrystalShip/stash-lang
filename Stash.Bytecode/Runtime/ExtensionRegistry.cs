namespace Stash.Bytecode;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Stash.Runtime;

/// <summary>
/// Registry for extension methods defined via extend blocks on built-in types.
/// </summary>
internal sealed class ExtensionRegistry
{
    private readonly ConcurrentDictionary<string, Dictionary<string, IStashCallable>> _methods = new();

    public void Register(string typeName, string methodName, IStashCallable callable)
    {
        var methods = _methods.GetOrAdd(typeName, _ => new Dictionary<string, IStashCallable>());
        methods[methodName] = callable;
    }

    public bool TryGetMethod(string typeName, string methodName, [NotNullWhen(true)] out IStashCallable? callable)
    {
        if (_methods.TryGetValue(typeName, out Dictionary<string, IStashCallable>? methods) &&
            methods.TryGetValue(methodName, out callable))
        {
            return true;
        }

        callable = null;
        return false;
    }
}
