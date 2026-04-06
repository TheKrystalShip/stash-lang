namespace Stash.Stdlib;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.BuiltIns;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;

public static class StdlibDefinitions
{
    private static readonly Lazy<IReadOnlyList<NamespaceDefinition>> _namespaces = new(BuildNamespaces);

    private static readonly Lazy<IReadOnlyList<BuiltInStruct>> _structs =
        new(() => Namespaces.SelectMany(d => d.Structs).ToArray());

    private static readonly Lazy<IReadOnlyList<BuiltInEnum>> _enums =
        new(() => Namespaces.SelectMany(d => d.Enums).ToArray());

    // GetOrAdd may invoke the factory more than once under contention; Define() is pure so this is safe.
    private static readonly ConcurrentDictionary<StashCapabilities, NamespaceDefinition> _globalsCache = new();

    public static IReadOnlyList<NamespaceDefinition> Namespaces => _namespaces.Value;

    public static IReadOnlyList<BuiltInStruct> Structs => _structs.Value;

    public static IReadOnlyList<BuiltInEnum> Enums => _enums.Value;

    public static NamespaceDefinition GetGlobalNamespace(StashCapabilities capabilities)
        => _globalsCache.GetOrAdd(capabilities, static caps => GlobalBuiltIns.Define(caps));

    /// <summary>
    /// Creates the globals dictionary for a bytecode VM, populated with all built-in
    /// functions, namespaces, and types filtered by the given capabilities.
    /// </summary>
    public static Dictionary<string, object?> CreateVMGlobals(StashCapabilities capabilities = StashCapabilities.All)
    {
        var globals = new Dictionary<string, object?>();

        // Spread global functions into the flat globals dictionary
        var globalNs = GetGlobalNamespace(capabilities);
        foreach (var (key, value) in globalNs.Namespace.GetAllMembers())
        {
            globals[key] = value;
        }

        // Register namespaces (capability-filtered)
        foreach (var nsDef in Namespaces)
        {
            if (nsDef.RequiredCapability != StashCapabilities.None && !capabilities.HasFlag(nsDef.RequiredCapability))
            {
                continue;
            }

            globals[nsDef.Name] = nsDef.Namespace;
        }

        return globals;
    }

    private static IReadOnlyList<NamespaceDefinition> BuildNamespaces()
    {
        return [
            IoBuiltIns.Define(),
            ConvBuiltIns.Define(),
            EnvBuiltIns.Define(),
            ProcessBuiltIns.Define(),
            FsBuiltIns.Define(),
            PathBuiltIns.Define(),
            ArrBuiltIns.Define(),
            DictBuiltIns.Define(),
            StrBuiltIns.Define(),
            AssertBuiltIns.Define(),
            TestBuiltIns.Define(),
            MathBuiltIns.Define(),
            TimeBuiltIns.Define(),
            JsonBuiltIns.Define(),
            HttpBuiltIns.Define(),
            IniBuiltIns.Define(),
            YamlBuiltIns.Define(),
            TomlBuiltIns.Define(),
            ConfigBuiltIns.Define(),
            TplBuiltIns.Define(),
            ArgsBuiltIns.Define(),
            CryptoBuiltIns.Define(),
            EncodingBuiltIns.Define(),
            TermBuiltIns.Define(),
            SysBuiltIns.Define(),
            PkgBuiltIns.Define(),
            TaskBuiltIns.Define(),
            SshBuiltIns.Define(),
            SftpBuiltIns.Define(),
            NetBuiltIns.Define(),
        ];
    }
}
