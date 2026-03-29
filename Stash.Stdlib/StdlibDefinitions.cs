namespace Stash.Stdlib;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
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
    private static readonly ConcurrentDictionary<StashCapabilities, GlobalDefinition> _globalsCache = new();

    public static IReadOnlyList<NamespaceDefinition> Namespaces => _namespaces.Value;

    public static IReadOnlyList<BuiltInStruct> Structs => _structs.Value;

    public static IReadOnlyList<BuiltInEnum> Enums => _enums.Value;

    public static GlobalDefinition GetGlobals(StashCapabilities capabilities)
        => _globalsCache.GetOrAdd(capabilities, static caps => GlobalBuiltIns.Define(caps));

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
            StoreBuiltIns.Define(),
            ArgsBuiltIns.Define(),
            CryptoBuiltIns.Define(),
            EncodingBuiltIns.Define(),
            TermBuiltIns.Define(),
            SysBuiltIns.Define(),
            LogBuiltIns.Define(),
            PkgBuiltIns.Define(),
            TaskBuiltIns.Define(),
            SshBuiltIns.Define(),
            SftpBuiltIns.Define(),
        ];
    }
}
