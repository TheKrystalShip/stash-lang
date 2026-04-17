using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Stash.Bytecode;
using Stash.Runtime;
using Stash.Runtime.Stdlib;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Xunit;

namespace Stash.Tests.Bytecode;

public class StdlibAbstractionTests
{
    // =========================================================================
    // Test helper types
    // =========================================================================

    private sealed class TestProvider : IStdlibProvider
    {
        private readonly List<StdlibNamespaceEntry> _namespaces = [];
        private readonly List<StdlibGlobalEntry> _globals = [];

        public TestProvider AddNamespace(StdlibNamespaceEntry entry) { _namespaces.Add(entry); return this; }
        public TestProvider AddGlobal(StdlibGlobalEntry entry) { _globals.Add(entry); return this; }

        public IReadOnlyList<StdlibNamespaceEntry> GetNamespaces(StashCapabilities capabilities) => _namespaces;
        public IReadOnlyList<StdlibGlobalEntry> GetGlobals(StashCapabilities capabilities) => _globals;
    }

    private static StdlibNamespaceEntry BuildTestNamespace(string name, params string[] functionNames)
    {
        var builder = new StdlibNamespaceBuilder(name);
        foreach (string fn in functionNames)
        {
            builder.Function(fn, 0, static (ctx, args) => StashValue.Null);
        }
        return builder.Build();
    }

    private static Chunk RoundTrip(Chunk original)
    {
        using var ms = new MemoryStream();
        BytecodeWriter.Write(ms, original);
        ms.Position = 0;
        return BytecodeReader.Read(ms);
    }

    // =========================================================================
    // StdlibComposer Tests
    // =========================================================================

    [Fact]
    public void Composer_EmptyBuild_ReturnsEmptyDict()
    {
        Dictionary<string, StashValue> dict = new StdlibComposer().Build();

        Assert.Empty(dict);
    }

    [Fact]
    public void Composer_SingleProvider_ContainsAllEntries()
    {
        StdlibNamespaceEntry ns = BuildTestNamespace("math", "abs");
        var globalFn = new BuiltInFunction("print", 1, static (ctx, args) => StashValue.Null);
        var globalEntry = new StdlibGlobalEntry("print", StashValue.FromObj(globalFn));

        var provider = new TestProvider()
            .AddNamespace(ns)
            .AddGlobal(globalEntry);

        Dictionary<string, StashValue> dict = new StdlibComposer().Add(provider).Build();

        Assert.True(dict.ContainsKey("math"));
        Assert.True(dict.ContainsKey("print"));
    }

    [Fact]
    public void Composer_TwoProviders_NoOverlap_UnionOfBoth()
    {
        var p1 = new TestProvider().AddNamespace(BuildTestNamespace("ns1", "fn1"));
        var p2 = new TestProvider().AddNamespace(BuildTestNamespace("ns2", "fn2"));

        Dictionary<string, StashValue> dict = new StdlibComposer().Add(p1).Add(p2).Build();

        Assert.True(dict.ContainsKey("ns1"));
        Assert.True(dict.ContainsKey("ns2"));
    }

    [Fact]
    public void Composer_TwoProviders_Collision_LastWins()
    {
        StdlibNamespaceEntry first = BuildTestNamespace("ns", "fn1");
        StdlibNamespaceEntry second = BuildTestNamespace("ns", "fn2");

        var p1 = new TestProvider().AddNamespace(first);
        var p2 = new TestProvider().AddNamespace(second);

        Dictionary<string, StashValue> dict = new StdlibComposer().Add(p1).Add(p2).Build();

        var ns = (StashNamespace)dict["ns"].AsObj!;
        Assert.Same(second.Namespace, ns);
    }

    [Fact]
    public void Composer_Exclude_RemovesEntries()
    {
        var provider = new TestProvider().AddNamespace(BuildTestNamespace("fs", "read"));

        Dictionary<string, StashValue> dict = new StdlibComposer().Add(provider).Exclude("fs").Build();

        Assert.False(dict.ContainsKey("fs"));
    }

    [Fact]
    public void Composer_AddGlobal_PresentInDict()
    {
        Dictionary<string, StashValue> dict = new StdlibComposer()
            .AddGlobal("myGlobal", StashValue.FromInt(42))
            .Build();

        Assert.True(dict.ContainsKey("myGlobal"));
    }

    [Fact]
    public void Composer_AddGlobalAndExclude_Excluded()
    {
        Dictionary<string, StashValue> dict = new StdlibComposer()
            .AddGlobal("myGlobal", StashValue.FromInt(42))
            .Exclude("myGlobal")
            .Build();

        Assert.False(dict.ContainsKey("myGlobal"));
    }

    [Fact]
    public void Composer_WithCapabilities_FiltersNamespaces()
    {
        StdlibNamespaceEntry fsEntry = new StdlibNamespaceBuilder("fs")
            .RequiresCapability(StashCapabilities.FileSystem)
            .Build();

        var provider = new TestProvider().AddNamespace(fsEntry);

        Dictionary<string, StashValue> dictNone = new StdlibComposer()
            .Add(provider)
            .WithCapabilities(StashCapabilities.None)
            .Build();

        Dictionary<string, StashValue> dictAll = new StdlibComposer()
            .Add(provider)
            .WithCapabilities(StashCapabilities.All)
            .Build();

        Assert.False(dictNone.ContainsKey("fs"));
        Assert.True(dictAll.ContainsKey("fs"));
    }

    [Fact]
    public void Composer_BuildWithManifest_ManifestContainsNames()
    {
        StdlibNamespaceEntry ns = BuildTestNamespace("math", "abs");
        var globalFn = new BuiltInFunction("len", 1, static (ctx, args) => StashValue.Null);
        var globalEntry = new StdlibGlobalEntry("len", StashValue.FromObj(globalFn));

        var provider = new TestProvider()
            .AddNamespace(ns)
            .AddGlobal(globalEntry);

        (Dictionary<string, StashValue> _, StdlibManifest manifest) =
            new StdlibComposer().Add(provider).BuildWithManifest();

        Assert.Contains("math", manifest.RequiredNamespaces);
        Assert.Contains("len", manifest.RequiredGlobals);
    }

    // =========================================================================
    // StdlibNamespaceBuilder Tests
    // =========================================================================

    [Fact]
    public void Builder_EmptyNamespace_FrozenOnBuild()
    {
        StdlibNamespaceEntry entry = new StdlibNamespaceBuilder("test").Build();

        Assert.True(entry.Namespace.IsFrozen);
    }

    [Fact]
    public void Builder_AddFunction_CallableViaMemberValue()
    {
        StdlibNamespaceEntry entry = new StdlibNamespaceBuilder("test")
            .Function("greet", 0, static (ctx, args) => StashValue.Null)
            .Build();

        StashValue member = entry.Namespace.GetMemberValue("greet", null);

        Assert.False(member.IsNull);
    }

    [Fact]
    public void Builder_FunctionWithMetadata_MetadataPresent()
    {
        StdlibParamMeta[] parameters = [new StdlibParamMeta("x", "int")];

        StdlibNamespaceEntry entry = new StdlibNamespaceBuilder("test")
            .Function("add", 1, static (ctx, args) => StashValue.Null, parameters)
            .Build();

        Assert.NotNull(entry.Functions);
        Assert.Single(entry.Functions);
        Assert.Equal("add", entry.Functions[0].Name);
    }

    [Fact]
    public void Builder_FunctionWithoutMetadata_FunctionsNull()
    {
        StdlibNamespaceEntry entry = new StdlibNamespaceBuilder("test")
            .Function("noMeta", 0, static (ctx, args) => StashValue.Null)
            .Build();

        Assert.Null(entry.Functions);
    }

    [Fact]
    public void Builder_AddConstant_AccessibleInNamespace()
    {
        StdlibNamespaceEntry entry = new StdlibNamespaceBuilder("test")
            .Constant("PI", StashValue.FromFloat(3.14159), "float", "3.14159")
            .Build();

        StashValue member = entry.Namespace.GetMemberValue("PI", null);

        Assert.False(member.IsNull);
    }

    [Fact]
    public void Builder_RequiresCapability_SetOnEntry()
    {
        StdlibNamespaceEntry entry = new StdlibNamespaceBuilder("fs")
            .RequiresCapability(StashCapabilities.FileSystem)
            .Build();

        Assert.Equal(StashCapabilities.FileSystem, entry.RequiredCapability);
    }

    // =========================================================================
    // StdlibManifest Serialization Tests
    // =========================================================================

    [Fact]
    public void Manifest_RoundTrip_PreservesAllFields()
    {
        var manifest = new StdlibManifest(
            new[] { "arr", "str" },
            new[] { "len", "typeof" },
            StashCapabilities.FileSystem);

        var builder = new ChunkBuilder();
        builder.EmitA(OpCode.Return, 0);
        builder.SetStdlibManifest(manifest);
        Chunk original = builder.Build();

        Chunk result = RoundTrip(original);

        Assert.NotNull(result.StdlibManifest);
        Assert.Equal(new[] { "arr", "str" }, result.StdlibManifest.RequiredNamespaces);
        Assert.Equal(new[] { "len", "typeof" }, result.StdlibManifest.RequiredGlobals);
        Assert.Equal(StashCapabilities.FileSystem, result.StdlibManifest.MinimumCapabilities);
    }

    // =========================================================================
    // StashStdlibProvider Tests
    // =========================================================================

    [Fact]
    public void StashProvider_GetNamespaces_ReturnsNonEmpty()
    {
        var provider = new StashStdlibProvider();

        IReadOnlyList<StdlibNamespaceEntry> namespaces = provider.GetNamespaces(StashCapabilities.All);

        Assert.NotEmpty(namespaces);
    }

    [Fact]
    public void StashProvider_GetGlobals_ReturnsNonEmpty()
    {
        var provider = new StashStdlibProvider();

        IReadOnlyList<StdlibGlobalEntry> globals = provider.GetGlobals(StashCapabilities.All);

        Assert.NotEmpty(globals);
    }

    [Fact]
    public void StashProvider_GetNamespaces_CapabilityFiltering()
    {
        var provider = new StashStdlibProvider();

        IReadOnlyList<StdlibNamespaceEntry> namespaces = provider.GetNamespaces(StashCapabilities.None);

        Assert.DoesNotContain(namespaces, e => e.Name == "fs");
        Assert.DoesNotContain(namespaces, e => e.Name == "http");
    }

    // =========================================================================
    // VM Manifest Validation Tests
    // =========================================================================

    [Fact]
    public void VM_ManifestValidation_MissingNamespace_ThrowsRuntimeError()
    {
        var vm = new VirtualMachine(new Dictionary<string, StashValue>(), CancellationToken.None);
        var builder = new ChunkBuilder();
        builder.EmitA(OpCode.Return, 0);
        builder.SetStdlibManifest(new StdlibManifest(
            new[] { "nonexistent" }, Array.Empty<string>(), StashCapabilities.None));
        Chunk chunk = builder.Build();

        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    [Fact]
    public void VM_ManifestValidation_AllPresent_Succeeds()
    {
        Dictionary<string, StashValue> globals = StdlibDefinitions.CreateVMGlobals(StashCapabilities.All);
        var vm = new VirtualMachine(globals, CancellationToken.None);
        var builder = new ChunkBuilder();
        builder.EmitA(OpCode.Return, 0);
        builder.SetStdlibManifest(new StdlibManifest(
            new[] { "arr" }, new[] { "len" }, StashCapabilities.None));
        Chunk chunk = builder.Build();

        vm.Execute(chunk); // Should not throw
    }

    [Fact]
    public void VM_ManifestValidation_NoManifest_Succeeds()
    {
        var vm = new VirtualMachine(new Dictionary<string, StashValue>(), CancellationToken.None);
        var builder = new ChunkBuilder();
        builder.EmitA(OpCode.Return, 0);
        Chunk chunk = builder.Build();

        vm.Execute(chunk); // Should not throw
    }

    // =========================================================================
    // IBuiltInContext Tests
    // =========================================================================

    [Fact]
    public void IBuiltInContext_VMContextImplements()
    {
        // IInterpreterContext : IExecutionContext : IBuiltInContext
        Assert.True(typeof(IInterpreterContext).IsAssignableTo(typeof(IBuiltInContext)));
    }
}
