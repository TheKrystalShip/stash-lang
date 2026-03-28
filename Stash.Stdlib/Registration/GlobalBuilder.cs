namespace Stash.Stdlib.Registration;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Stdlib.Models;

/// <summary>
/// Fluent builder for creating global function definitions that pair metadata with implementations.
/// Each <see cref="Function"/> call registers BOTH the runtime callable and the LSP/Analysis metadata.
/// </summary>
public class GlobalBuilder
{
    private readonly List<(string Name, Runtime.BuiltInFunction Runtime, Models.BuiltInFunction Metadata)> _functions = [];

    /// <summary>
    /// Defines a global function with both metadata and implementation in a single call.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="parameters">Parameter metadata for LSP/Analysis.</param>
    /// <param name="body">The C# implementation delegate.</param>
    /// <param name="arity">Explicit arity. If null, derived from parameters.Length. Use -1 for variadic.</param>
    /// <param name="returnType">Optional return type string for documentation.</param>
    /// <param name="documentation">Optional documentation string with @param/@return tags.</param>
    public GlobalBuilder Function(string name, BuiltInParam[] parameters,
        Func<IInterpreterContext, List<object?>, object?> body,
        int? arity = null, string? returnType = null, string? documentation = null)
    {
        int actualArity = arity ?? parameters.Length;
        var runtime = new Runtime.BuiltInFunction(name, actualArity, body);
        var metadata = new Models.BuiltInFunction(name, parameters, returnType, documentation);
        _functions.Add((name, runtime, metadata));
        return this;
    }

    /// <summary>
    /// Builds the global functions definition.
    /// </summary>
    public GlobalDefinition Build()
    {
        return new GlobalDefinition(
            _functions.Select(f => f.Metadata).ToArray(),
            _functions.ToDictionary(f => f.Name, f => (Runtime.BuiltInFunction)f.Runtime));
    }
}
