using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;

namespace Stash.Tests.Stdlib;

using RuntimeBuiltInFunction = Stash.Runtime.BuiltInFunction;

public class StdlibConsistencyTests
{
    private static Dictionary<string, object?> CreateGlobals(StashCapabilities caps = StashCapabilities.All)
        => StdlibDefinitions.CreateVMGlobals(caps).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToObject());

    [Fact]
    public void GlobalFunctions_RegistryEntries_HaveRuntimeImplementation()
    {
        var globals = CreateGlobals();
        var runtimeNames = globals
            .Where(b => b.Value is RuntimeBuiltInFunction)
            .Select(b => b.Key)
            .ToHashSet();

        foreach (var fn in StdlibRegistry.Functions)
        {
            Assert.True(runtimeNames.Contains(fn.Name),
                $"StdlibRegistry.Functions has '{fn.Name}' but no runtime implementation exists");
        }
    }

    [Fact]
    public void GlobalFunctions_RuntimeEntries_HaveRegistryMetadata()
    {
        var globals = CreateGlobals();

        foreach (var binding in globals)
        {
            if (binding.Value is not RuntimeBuiltInFunction)
            {
                continue;
            }

            Assert.True(StdlibRegistry.IsBuiltInFunction(binding.Key),
                $"Runtime has global built-in '{binding.Key}' but StdlibRegistry has no metadata for it");
        }
    }

    [Fact]
    public void GlobalFunctions_Arity_MatchesRegistryParameterCount()
    {
        var globals = CreateGlobals();

        foreach (var binding in globals)
        {
            if (binding.Value is not RuntimeBuiltInFunction runtimeFn || runtimeFn.Arity < 0)
            {
                continue;
            }

            if (!StdlibRegistry.TryGetFunction(binding.Key, out var meta))
            {
                continue;
            }

            Assert.True(meta.Parameters.Length == runtimeFn.Arity,
                $"Global function '{binding.Key}': metadata has {meta.Parameters.Length} parameter(s) but runtime arity is {runtimeFn.Arity}");
        }
    }

    [Fact]
    public void Namespaces_RegistryEntries_HaveRuntimeNamespace()
    {
        var globals = CreateGlobals();
        var runtimeNamespaceNames = globals
            .Where(b => b.Value is StashNamespace)
            .Select(b => b.Key)
            .ToHashSet();

        foreach (var nsName in StdlibRegistry.NamespaceNames)
        {
            Assert.True(runtimeNamespaceNames.Contains(nsName),
                $"StdlibRegistry.NamespaceNames has '{nsName}' but no runtime namespace exists");
        }
    }

    [Fact]
    public void Namespaces_RuntimeEntries_HaveRegistryMetadata()
    {
        var globals = CreateGlobals();

        foreach (var binding in globals)
        {
            if (binding.Value is not StashNamespace ns)
            {
                continue;
            }

            Assert.True(StdlibRegistry.IsBuiltInNamespace(ns.Name),
                $"Runtime has namespace '{ns.Name}' but StdlibRegistry has no metadata for it");
        }
    }

    [Fact]
    public void NamespaceFunctions_RegistryEntries_HaveRuntimeImplementation()
    {
        var globals = CreateGlobals();

        var runtimeQualifiedNames = new HashSet<string>();
        foreach (var binding in globals)
        {
            if (binding.Value is not StashNamespace ns)
            {
                continue;
            }

            foreach (var (memberKey, memberValue) in ns.GetAllMembers())
            {
                if (memberValue is RuntimeBuiltInFunction)
                {
                    runtimeQualifiedNames.Add($"{ns.Name}.{memberKey}");
                }
            }
        }

        foreach (var fn in StdlibRegistry.NamespaceFunctions)
        {
            Assert.True(runtimeQualifiedNames.Contains(fn.QualifiedName),
                $"StdlibRegistry.NamespaceFunctions has '{fn.QualifiedName}' but no runtime implementation exists");
        }
    }

    [Fact]
    public void NamespaceFunctions_RuntimeEntries_HaveRegistryMetadata()
    {
        var globals = CreateGlobals();

        foreach (var binding in globals)
        {
            if (binding.Value is not StashNamespace ns)
            {
                continue;
            }

            foreach (var (memberKey, memberValue) in ns.GetAllMembers())
            {
                if (memberValue is not RuntimeBuiltInFunction)
                {
                    continue;
                }

                string qualifiedName = $"{ns.Name}.{memberKey}";
                Assert.True(StdlibRegistry.TryGetNamespaceFunction(qualifiedName, out _),
                    $"Runtime has '{qualifiedName}' but StdlibRegistry has no metadata for it");
            }
        }
    }

    [Fact]
    public void NamespaceFunctions_Arity_MatchesRegistryParameterCount()
    {
        var globals = CreateGlobals();

        foreach (var binding in globals)
        {
            if (binding.Value is not StashNamespace ns)
            {
                continue;
            }

            foreach (var (memberKey, memberValue) in ns.GetAllMembers())
            {
                if (memberValue is not RuntimeBuiltInFunction runtimeFn || runtimeFn.Arity < 0)
                {
                    continue;
                }

                string qualifiedName = $"{ns.Name}.{memberKey}";
                if (!StdlibRegistry.TryGetNamespaceFunction(qualifiedName, out var meta) || meta.IsVariadic)
                {
                    continue;
                }

                Assert.True(meta.Parameters.Length == runtimeFn.Arity,
                    $"'{qualifiedName}': metadata has {meta.Parameters.Length} parameter(s) but runtime arity is {runtimeFn.Arity}");
            }
        }
    }

    [Fact]
    public void NamespaceConstants_RegistryEntries_HaveRuntimeValue()
    {
        var globals = CreateGlobals();

        var runtimeNamespaces = globals
            .Where(b => b.Value is StashNamespace)
            .ToDictionary(b => b.Key, b => (StashNamespace)b.Value!);

        foreach (var constant in StdlibRegistry.NamespaceConstants)
        {
            Assert.True(runtimeNamespaces.TryGetValue(constant.Namespace, out var ns),
                $"StdlibRegistry.NamespaceConstants references namespace '{constant.Namespace}' which has no runtime namespace");

            var members = ns.GetAllMembers();
            Assert.True(members.ContainsKey(constant.Name),
                $"StdlibRegistry.NamespaceConstants has '{constant.QualifiedName}' but the runtime namespace has no such member");
        }
    }

    [Fact]
    public void NamespaceConstants_RuntimeEntries_HaveRegistryMetadata()
    {
        var globals = CreateGlobals();

        foreach (var binding in globals)
        {
            if (binding.Value is not StashNamespace ns)
                continue;

            foreach (var (memberKey, memberValue) in ns.GetAllMembers())
            {
                if (memberValue is not (long or double or string or bool))
                    continue;

                string qualifiedName = $"{ns.Name}.{memberKey}";
                Assert.True(StdlibRegistry.TryGetNamespaceConstant(qualifiedName, out _),
                    $"Runtime has constant '{qualifiedName}' but StdlibRegistry has no metadata for it");
            }
        }
    }

    [Fact]
    public void Construction_WithRestrictedCapabilities_DoesNotThrow()
    {
        var ex = Record.Exception(() => CreateGlobals(StashCapabilities.None));
        Assert.Null(ex);
    }

    [Fact]
    public void Construction_WithNoCapabilities_ExcludesCapabilityGatedNamespaces()
    {
        var globals = CreateGlobals(StashCapabilities.None);
        var definedNamespaces = globals
            .Where(b => b.Value is StashNamespace)
            .Select(b => b.Key)
            .ToHashSet();

        // Capability-gated namespaces must not be present
        string[] gated = ["env", "fs", "http", "ssh", "sftp", "process", "pkg"];
        foreach (var ns in gated)
        {
            Assert.False(definedNamespaces.Contains(ns),
                $"Namespace '{ns}' should not be defined when capabilities are None");
        }

        // Core namespaces must still be present
        string[] core = ["arr", "str", "math"];
        foreach (var ns in core)
        {
            Assert.True(definedNamespaces.Contains(ns),
                $"Core namespace '{ns}' should always be defined regardless of capabilities");
        }
    }

    // ── Stability exhaustiveness ──

    [Fact]
    public void NamespaceMemberPayload_UnknownStability_Throws()
    {
        // (Stability)99 is not a valid variant. Invoke must throw InvalidOperationException
        // rather than silently falling through to the Cached or Live branch.
        var payload = new NamespaceMemberPayload(
            getter: _ => default,
            stability: (Stability)99,
            returnType: null);

        Assert.Throws<InvalidOperationException>(() => payload.Invoke(null!));
    }

    // ── UFCS map validation ──

    [Fact]
    public void ValidateUfcsTargets_MissingNamespace_Throws()
    {
        // A UFCS map entry whose value is not in the namespace list must throw.
        // We call the internal helper directly so the real StdlibRegistry static ctor
        // is never triggered with invalid data (TypeInitializationException is process-sticky).
        var bogusMap = new Dictionary<string, string>
        {
            ["string"] = "str",
            ["widget"] = "nonexistent_ns",
        };
        var knownNamespaces = new List<string> { "str", "arr", "math" };

        Assert.Throws<InvalidOperationException>(
            () => StdlibRegistry.ValidateUfcsTargets(bogusMap, knownNamespaces));
    }

    [Fact]
    public void ValidateUfcsTargets_AllTargetsPresent_DoesNotThrow()
    {
        // The production map is valid — test the green-path too.
        var validMap = new Dictionary<string, string>
        {
            ["string"] = "str",
            ["array"] = "arr",
        };
        var knownNamespaces = new List<string> { "str", "arr", "math" };

        var ex = Record.Exception(() => StdlibRegistry.ValidateUfcsTargets(validMap, knownNamespaces));
        Assert.Null(ex);
    }
}
