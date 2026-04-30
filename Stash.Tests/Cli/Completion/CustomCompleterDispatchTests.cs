using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Cli.Completion;
using Stash.Cli.Completion.Completers;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Unit tests for <see cref="CustomCompleterRegistry"/> and <see cref="CustomCompleterDispatch"/>
/// covering register/unregister, dispatch returning arrays, fallback on null/empty, and
/// once-per-session error logging.
/// </summary>
public class CustomCompleterDispatchTests
{
    private static Stash.Bytecode.VirtualMachine MakeVm(string? source = null)
    {
        var vm = new Stash.Bytecode.VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;
        if (source != null)
            ShellRunner.EvaluateSource(source, vm);
        return vm;
    }

    private static CompletionDeps MakeDeps(
        Stash.Bytecode.VirtualMachine? vm = null,
        CustomCompleterRegistry? registry = null,
        System.IO.TextWriter? errorOutput = null) =>
        new(vm ?? MakeVm(), new PathExecutableCache(), registry ?? new CustomCompleterRegistry(), errorOutput ?? System.IO.TextWriter.Null);

    private static CursorContext MakeCtx(string token, IReadOnlyList<string>? priorArgs = null) =>
        new(CompletionMode.Shell, 0, token.Length, token, false, '\0', false,
            priorArgs ?? Array.Empty<string>());

    // ─── Callable helper that returns a string array ───────────────────────────────

    private static IStashCallable MakeStringArrayCallable(params string[] values)
    {
        return new LambdaCallable((_, _) =>
        {
            var list = new List<StashValue>(values.Length);
            foreach (string v in values)
                list.Add(StashValue.FromObj(v));
            return list;
        });
    }

    // ─── Registry: register / unregister / get ────────────────────────────────────

    [Fact]
    public void Registry_Register_Then_Get_ReturnsCallable()
    {
        var registry = new CustomCompleterRegistry();
        var fn = MakeStringArrayCallable("a");
        registry.Register("git", fn);

        Assert.Same(fn, registry.Get("git"));
    }

    [Fact]
    public void Registry_Get_UnknownName_ReturnsNull()
    {
        var registry = new CustomCompleterRegistry();
        Assert.Null(registry.Get("unknown"));
    }

    [Fact]
    public void Registry_Unregister_ReturnsTrueIfWasRegistered()
    {
        var registry = new CustomCompleterRegistry();
        registry.Register("git", MakeStringArrayCallable("a"));

        bool removed = registry.Unregister("git");

        Assert.True(removed);
        Assert.Null(registry.Get("git"));
    }

    [Fact]
    public void Registry_Unregister_ReturnsFalseIfNotRegistered()
    {
        var registry = new CustomCompleterRegistry();
        Assert.False(registry.Unregister("nobody"));
    }

    [Fact]
    public void Registry_Register_Replaces_Existing()
    {
        var registry = new CustomCompleterRegistry();
        var fn1 = MakeStringArrayCallable("a");
        var fn2 = MakeStringArrayCallable("b");
        registry.Register("git", fn1);
        registry.Register("git", fn2);

        Assert.Same(fn2, registry.Get("git"));
    }

    [Fact]
    public void Registry_RegisteredNames_IsSortedAlphabetically()
    {
        var registry = new CustomCompleterRegistry();
        registry.Register("zsh", MakeStringArrayCallable());
        registry.Register("bash", MakeStringArrayCallable());
        registry.Register("Apt", MakeStringArrayCallable());

        IReadOnlyList<string> names = registry.RegisteredNames();

        var sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(sorted, names.ToArray());
    }

    [Fact]
    public void Registry_ErrorTracking_HasReportedError_InitiallyFalse()
    {
        var registry = new CustomCompleterRegistry();
        Assert.False(registry.HasReportedError("git"));
    }

    [Fact]
    public void Registry_RecordError_SetsHasReportedError()
    {
        var registry = new CustomCompleterRegistry();
        registry.RecordError("git");
        Assert.True(registry.HasReportedError("git"));
    }

    // ─── Dispatch: no completer registered ───────────────────────────────────────

    [Fact]
    public void Dispatch_NoCompleterRegistered_ReturnsNull()
    {
        var deps = MakeDeps();
        var dispatch = new CustomCompleterDispatch();

        IReadOnlyList<Candidate>? result = dispatch.TryDispatch(MakeCtx("arg"), deps, "git");

        Assert.Null(result);
    }

    // ─── Dispatch: array of strings ───────────────────────────────────────────────

    [Fact]
    public void Dispatch_StringArrayResult_ReturnsCandidates()
    {
        var vm = MakeVm();
        var registry = new CustomCompleterRegistry();
        registry.Register("git", MakeStringArrayCallable("status", "checkout", "commit"));
        var deps = MakeDeps(vm, registry);
        var dispatch = new CustomCompleterDispatch();

        IReadOnlyList<Candidate>? result = dispatch.TryDispatch(MakeCtx("che"), deps, "git");

        Assert.NotNull(result);
        Assert.All(result!, c => Assert.Equal(CandidateKind.Custom, c.Kind));
        var inserts = result!.Select(c => c.Insert).ToArray();
        Assert.Contains("status", inserts);
        Assert.Contains("checkout", inserts);
        Assert.Contains("commit", inserts);
    }

    // ─── Dispatch: empty array → null (signal fallback) ───────────────────────────

    [Fact]
    public void Dispatch_EmptyArray_ReturnsNull()
    {
        var vm = MakeVm();
        var registry = new CustomCompleterRegistry();
        registry.Register("foo", MakeStringArrayCallable(/* empty */));
        var deps = MakeDeps(vm, registry);
        var dispatch = new CustomCompleterDispatch();

        IReadOnlyList<Candidate>? result = dispatch.TryDispatch(MakeCtx(""), deps, "foo");

        Assert.Null(result);
    }

    // ─── Dispatch: callable returns null → null ───────────────────────────────────

    [Fact]
    public void Dispatch_NullReturn_ReturnsNull()
    {
        var vm = MakeVm();
        var registry = new CustomCompleterRegistry();
        registry.Register("foo", new LambdaCallable((_, _) => null));
        var deps = MakeDeps(vm, registry);
        var dispatch = new CustomCompleterDispatch();

        IReadOnlyList<Candidate>? result = dispatch.TryDispatch(MakeCtx(""), deps, "foo");

        Assert.Null(result);
    }

    // ─── Dispatch: throwing completer logs once ───────────────────────────────────

    [Fact]
    public void Dispatch_ThrowingCompleter_ReturnsNullAndLogsOnce()
    {
        var vm = MakeVm();
        var registry = new CustomCompleterRegistry();
        registry.Register("kaboom", new LambdaCallable((_, _) => throw new InvalidOperationException("boom")));
        var sw = new System.IO.StringWriter();
        var deps = MakeDeps(vm, registry, sw);
        var dispatch = new CustomCompleterDispatch();

        dispatch.TryDispatch(MakeCtx(""), deps, "kaboom");
        string stderr1 = sw.ToString();

        // First call: logs the error
        Assert.Contains("kaboom", stderr1);
        Assert.Contains("boom", stderr1);
        Assert.True(registry.HasReportedError("kaboom"));

        sw.GetStringBuilder().Clear();
        dispatch.TryDispatch(MakeCtx(""), deps, "kaboom");
        string stderr2 = sw.ToString();

        // Second call: error already recorded, no output
        Assert.Empty(stderr2);
    }

    [Fact]
    public void Dispatch_ThrowingCompleter_ReturnsNull()
    {
        var vm = MakeVm();
        var registry = new CustomCompleterRegistry();
        registry.Register("bad", new LambdaCallable((_, _) => throw new Exception("fail")));
        var deps = MakeDeps(vm, registry, System.IO.TextWriter.Null);
        var dispatch = new CustomCompleterDispatch();

        IReadOnlyList<Candidate>? result = dispatch.TryDispatch(MakeCtx(""), deps, "bad");

        Assert.Null(result);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static string CaptureStderr(Action action)
    {
        TextWriter orig = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try { action(); }
        finally { Console.SetError(orig); }
        return sw.ToString();
    }

    private static T? CaptureStderrResult<T>(Func<T?> func)
    {
        TextWriter orig = Console.Error;
        Console.SetError(TextWriter.Null);
        try { return func(); }
        finally { Console.SetError(orig); }
    }

    /// <summary>Simple IStashCallable wrapper backed by a delegate.</summary>
    private sealed class LambdaCallable : IStashCallable
    {
        private readonly Func<IInterpreterContext, List<object?>, object?> _impl;
        public int Arity => -1;
        public int MinArity => 0;

        public LambdaCallable(Func<IInterpreterContext, List<object?>, object?> impl)
            => _impl = impl;

        public object? Call(IInterpreterContext context, List<object?> arguments)
            => _impl(context, arguments);
    }
}
