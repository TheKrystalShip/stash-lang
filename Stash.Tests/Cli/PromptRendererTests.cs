using System;
using System.IO;
using Stash.Bytecode;
using Stash.Cli.Repl;
using Stash.Cli.Shell;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="PromptRenderer"/> covering primary prompt rendering,
/// fallback behavior, the one-shot warning, auto-reset after 5 failures, re-entry
/// guard, and continuation prompt rendering.
/// </summary>
[Collection("PromptTests")]
public class PromptRendererTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static VirtualMachine MakeVm()
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;
        return vm;
    }

    private static void LoadSource(string source, VirtualMachine vm)
        => ShellRunner.EvaluateSource(source, vm);

    /// <summary>Strips \x01..\x02 zero-width regions (OSC shell-integration markers).</summary>
    private static string StripOsc(string s)
    {
        if (!s.Contains('\x01')) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        bool inZW = false;
        foreach (char c in s)
        {
            if (c == '\x01') { inZW = true; continue; }
            if (c == '\x02') { inZW = false; continue; }
            if (!inZW) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Captures Console.Error output produced by <paramref name="action"/>.</summary>
    private static string CaptureStderr(Action action)
    {
        TextWriter orig = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try { action(); }
        finally { Console.SetError(orig); }
        return sw.ToString();
    }

    // ── Primary prompt rendering ───────────────────────────────────────────────

    [Fact]
    public void Render_NoFnRegistered_NoConventionGlobal_ReturnsDefaultPrompt()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        string result = PromptRenderer.Render(vm);
        Assert.Contains("stash> ", StripOsc(result));
    }

    [Fact]
    public void Render_WithRegisteredFn_ReturnsCustomPrompt()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.set((ctx) => { return "$ "; });""", vm);

        string result = PromptRenderer.Render(vm);
        Assert.Contains("$ ", StripOsc(result));
    }

    [Fact]
    public void Render_ConventionGlobal_UsedWhenNoSetRegistered()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        // Override the `prompt` namespace entry with a callable convention global
        LoadSource("""let prompt = (ctx) => { return "conv> "; };""", vm);

        string result = PromptRenderer.Render(vm);
        Assert.Contains("conv> ", StripOsc(result));
    }

    [Fact]
    public void Render_SetTakesPrecedenceOverConventionGlobal()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        // Register via prompt.set FIRST (while `prompt` is still the namespace),
        // then override the `prompt` global with a convention fn — set must win.
        LoadSource("""prompt.set((ctx) => { return "set> "; });""", vm);
        LoadSource("""let prompt = (ctx) => { return "conv> "; };""", vm);

        string result = PromptRenderer.Render(vm);
        Assert.Contains("set> ", StripOsc(result));
    }

    [Fact]
    public void Render_FnThrowsRuntimeError_FallsBackToDefault_WritesWarning()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.set((ctx) => { throw "boom"; });""", vm);

        string result = "";
        string stderr = CaptureStderr(() => { result = PromptRenderer.Render(vm); });

        Assert.Contains("stash> ", StripOsc(result));
        Assert.Contains("error in user prompt fn", stderr);
    }

    [Fact]
    public void Render_FnReturnsNonString_FallsBackToDefault_WritesWarning()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.set((ctx) => { return 42; });""", vm);

        string result = "";
        string stderr = CaptureStderr(() => { result = PromptRenderer.Render(vm); });

        Assert.Contains("stash> ", StripOsc(result));
        Assert.Contains("expected string", stderr);
    }

    [Fact]
    public void Render_SecondConsecutiveFailure_NoAdditionalWarning()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.set((ctx) => { throw "boom"; });""", vm);

        // First call emits warning
        string firstStderr = CaptureStderr(() => PromptRenderer.Render(vm));
        Assert.NotEmpty(firstStderr);

        // Second call: _warningEmitted is true, no additional warning
        string secondStderr = CaptureStderr(() => PromptRenderer.Render(vm));
        Assert.Empty(secondStderr);
    }

    [Fact]
    public void Render_FiveConsecutiveFailures_AutoResetsAndPrintsAutoResetWarning()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.set((ctx) => { throw "boom"; });""", vm);

        string allStderr = CaptureStderr(() =>
        {
            for (int i = 0; i < 5; i++)
                PromptRenderer.Render(vm);
        });

        // After 5 failures, the fn is cleared
        Assert.Null(PromptBuiltIns.GetRegisteredPromptFn());
        Assert.Contains("auto-reset to default", allStderr);
    }

    [Fact]
    public void Render_AfterAutoReset_SubsequentCallReturnsDefault()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.set((ctx) => { throw "boom"; });""", vm);

        // Trigger auto-reset
        CaptureStderr(() =>
        {
            for (int i = 0; i < 5; i++)
                PromptRenderer.Render(vm);
        });

        // After reset, next call should return default without error
        string result = PromptRenderer.Render(vm);
        Assert.Contains("stash> ", StripOsc(result));
    }

    [Fact]
    public void Render_ReentryGuard_WhenFlagSet_ReturnsDefaultWithoutCallingFn()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.set((ctx) => { return "custom> "; });""", vm);

        // Simulate re-entry: flag is already set when Render is called
        PromptRenderer.SetRenderingForTesting(true);
        try
        {
            string result = PromptRenderer.Render(vm);
            // Should return default without invoking the fn
            Assert.Contains("stash> ", StripOsc(result));
        }
        finally
        {
            PromptRenderer.SetRenderingForTesting(false);
        }
    }

    // ── Continuation prompt ────────────────────────────────────────────────────

    [Fact]
    public void Continuation_NoFnRegistered_ReturnsDots()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        string result = PromptRenderer.Continuation(vm, depth: 1);
        Assert.Equal("... ", result);
    }

    [Fact]
    public void Continuation_WithRegisteredFn_ReturnsCustomContinuation()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.setContinuation((ctx) => { return ">>> "; });""", vm);

        string result = PromptRenderer.Continuation(vm, depth: 1);
        Assert.Equal(">>> ", result);
    }

    [Fact]
    public void Continuation_WithRegisteredFn_PassesDepthInContext()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        // The fn accesses ctx.continuationDepth and returns it as a string
        LoadSource("""
            prompt.setContinuation((ctx) => {
                return conv.toStr(ctx.continuationDepth) + "> ";
            });
            """, vm);

        string result = PromptRenderer.Continuation(vm, depth: 3);
        Assert.Equal("3> ", result);
    }

    [Fact]
    public void Continuation_FnThrows_FallsBackToDots_WritesWarning()
    {
        PromptBuiltIns.ResetAllForTesting();
        PromptRenderer.ResetStateForTesting();
        var vm = MakeVm();

        LoadSource("""prompt.setContinuation((ctx) => { throw "oops"; });""", vm);

        string result = "";
        string stderr = CaptureStderr(() => { result = PromptRenderer.Continuation(vm); });

        Assert.Equal("... ", result);
        Assert.Contains("error in continuation fn", stderr);
    }

    // ── WriteCommandStart / WriteCommandEnd ────────────────────────────────────

    [Fact]
    public void WriteCommandStart_DoesNotThrow()
    {
        // WriteCommandStart writes to Console.Out; it may or may not emit OSC depending
        // on the runtime environment. We only verify it does not throw.
        var origOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            PromptRenderer.WriteCommandStart();
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    [Fact]
    public void WriteCommandEnd_DoesNotThrow()
    {
        var origOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            PromptRenderer.WriteCommandEnd(0);
            PromptRenderer.WriteCommandEnd(1);
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }
}
