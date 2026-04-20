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
    /// <summary>
    /// Registry of all namespace factories with their required capabilities.
    /// Lambdas ensure types are resolved lazily — platform-incompatible types
    /// (e.g. Renci.SshNet in WASM) are never loaded when their capability is absent.
    /// </summary>
    private static readonly (Func<NamespaceDefinition> Factory, StashCapabilities Required)[] _registry =
    [
        (() => IoBuiltIns.Define(),       StashCapabilities.None),
        (() => ConvBuiltIns.Define(),     StashCapabilities.None),
        (() => EnvBuiltIns.Define(),      StashCapabilities.Environment),
        (() => ProcessBuiltIns.Define(),  StashCapabilities.Process),
        (() => FsBuiltIns.Define(),       StashCapabilities.FileSystem),
        (() => PathBuiltIns.Define(),     StashCapabilities.None),
        (() => ArchiveBuiltIns.Define(),  StashCapabilities.FileSystem),
        (() => ArrBuiltIns.Define(),      StashCapabilities.None),
        (() => DictBuiltIns.Define(),     StashCapabilities.None),
        (() => StrBuiltIns.Define(),      StashCapabilities.None),
        (() => AssertBuiltIns.Define(),   StashCapabilities.None),
        (() => TestBuiltIns.Define(),     StashCapabilities.None),
        (() => MathBuiltIns.Define(),     StashCapabilities.None),
        (() => TimeBuiltIns.Define(),     StashCapabilities.None),
        (() => JsonBuiltIns.Define(),     StashCapabilities.None),
        (() => CsvBuiltIns.Define(),      StashCapabilities.None),
        (() => HttpBuiltIns.Define(),     StashCapabilities.Network),
        (() => IniBuiltIns.Define(),      StashCapabilities.None),
        (() => YamlBuiltIns.Define(),     StashCapabilities.None),
        (() => TomlBuiltIns.Define(),     StashCapabilities.None),
        (() => ConfigBuiltIns.Define(),   StashCapabilities.None),
        (() => TplBuiltIns.Define(),      StashCapabilities.None),
        (() => ArgsBuiltIns.Define(),     StashCapabilities.Process),
        (() => CryptoBuiltIns.Define(),   StashCapabilities.None),
        (() => EncodingBuiltIns.Define(), StashCapabilities.None),
        (() => BufBuiltIns.Define(),      StashCapabilities.None),
        (() => TermBuiltIns.Define(),     StashCapabilities.None),
        (() => SysBuiltIns.Define(),      StashCapabilities.None),
        (() => PkgBuiltIns.Define(),      StashCapabilities.FileSystem),
        (() => TaskBuiltIns.Define(),     StashCapabilities.None),
        (() => SshBuiltIns.Define(),       StashCapabilities.Network),
        (() => SftpBuiltIns.Define(),      StashCapabilities.Network),
        (() => NetBuiltIns.Define(),       StashCapabilities.Network),
        (() => SchedulerBuiltIns.Define(), StashCapabilities.Process | StashCapabilities.FileSystem),
        (() => LogBuiltIns.Define(),       StashCapabilities.None),
    ];

    private static readonly ConcurrentDictionary<StashCapabilities, IReadOnlyList<NamespaceDefinition>> _namespacesCache = new();
    private static readonly ConcurrentDictionary<StashCapabilities, NamespaceDefinition> _globalsCache = new();
    private static readonly ConcurrentDictionary<StashCapabilities, Dictionary<string, StashValue>> _vmGlobalsCache = new();

    private static readonly Lazy<IReadOnlyList<BuiltInStruct>> _structs =
        new(() => Namespaces.SelectMany(d => d.Structs).ToArray());

    private static readonly Lazy<IReadOnlyList<BuiltInEnum>> _enums =
        new(() => Namespaces.SelectMany(d => d.Enums).ToArray());

    /// <summary>
    /// All namespaces with full capabilities. Used by LSP, analysis, and registry
    /// where the complete namespace catalogue is needed regardless of runtime platform.
    /// </summary>
    public static IReadOnlyList<NamespaceDefinition> Namespaces => GetNamespaces(StashCapabilities.All);

    /// <summary>Returns namespaces filtered by the given capabilities.</summary>
    public static IReadOnlyList<NamespaceDefinition> GetNamespaces(StashCapabilities capabilities)
        => _namespacesCache.GetOrAdd(capabilities, static caps => BuildNamespaces(caps));

    public static IReadOnlyList<BuiltInStruct> Structs => _structs.Value;

    public static IReadOnlyList<BuiltInEnum> Enums => _enums.Value;

    public static NamespaceDefinition GetGlobalNamespace(StashCapabilities capabilities)
        => _globalsCache.GetOrAdd(capabilities, static caps => GlobalBuiltIns.Define(caps));

    /// <summary>
    /// Creates the globals dictionary for a bytecode VM, populated with all built-in
    /// functions, namespaces, and types filtered by the given capabilities.
    /// Returns a copy of a cached template so repeated VM creation avoids 38 individual inserts.
    /// </summary>
    public static Dictionary<string, StashValue> CreateVMGlobals(StashCapabilities capabilities = StashCapabilities.All)
    {
        var template = _vmGlobalsCache.GetOrAdd(capabilities, static caps => BuildVMGlobals(caps));
        return new Dictionary<string, StashValue>(template);
    }

    private static Dictionary<string, StashValue> BuildVMGlobals(StashCapabilities capabilities)
    {
        var globals = new Dictionary<string, StashValue>();

        var globalNs = GetGlobalNamespace(capabilities);
        foreach (var (key, value) in globalNs.Namespace.GetAllMemberValues())
        {
            globals[key] = value;
        }

        // Namespace list is already capability-filtered by GetNamespaces
        foreach (var nsDef in GetNamespaces(capabilities))
        {
            globals[nsDef.Name] = StashValue.FromObj(nsDef.Namespace);
        }

        return globals;
    }

    private static IReadOnlyList<NamespaceDefinition> BuildNamespaces(StashCapabilities capabilities)
    {
        var namespaces = new List<NamespaceDefinition>(_registry.Length);

        foreach (var (factory, required) in _registry)
        {
            if (required != StashCapabilities.None && !capabilities.HasFlag(required))
                continue;

            namespaces.Add(factory());
        }

        return namespaces;
    }
}
