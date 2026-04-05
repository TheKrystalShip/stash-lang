namespace Stash.Tests;

using Stash.Bytecode;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;

/// <summary>Shared test utilities for creating a VirtualMachine with stdlib globals.</summary>
internal static class TestVM
{
    internal static Dictionary<string, object?> CreateGlobals(StashCapabilities capabilities = StashCapabilities.All)
    {
        var globals = new Dictionary<string, object?>();
        var globalDef = StdlibDefinitions.GetGlobals(capabilities);
        foreach (var (name, fn) in globalDef.RuntimeFunctions)
            globals[name] = fn;
        foreach (var nsDef in StdlibDefinitions.Namespaces)
        {
            if (nsDef.RequiredCapability != StashCapabilities.None &&
                !capabilities.HasFlag(nsDef.RequiredCapability))
                continue;
            globals[nsDef.Name] = nsDef.Namespace;
        }
        globals["Backoff"] = new StashEnum("Backoff", new List<string> { "Fixed", "Linear", "Exponential" });
        globals["RetryOptions"] = new StashStruct("RetryOptions",
            new List<string> { "delay", "backoff", "maxDelay", "jitter", "timeout", "on" },
            new Dictionary<string, IStashCallable>());
        return globals;
    }
}
