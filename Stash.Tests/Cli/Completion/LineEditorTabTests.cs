using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Bytecode;
using Stash.Cli.Completion;
using Stash.Cli.Shell;
using Stash.Stdlib;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Tests for the Tab state machine (spec §4) via the <see cref="TabCompletionAction"/>
/// helper and the <see cref="LineEditor"/> completion properties (spec §10.3, §15.8).
/// Strategy A: the Tab logic is extracted into a testable static class so no
/// <see cref="Console.ReadKey"/> simulation is required.
/// </summary>
public class LineEditorTabTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CompletionEngine MakeEngine(Func<string, bool>? isExec = null, string? vmSource = null)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;
        if (vmSource != null)
            ShellRunner.EvaluateSource(vmSource, vm);

        var cache = new PathExecutableCache(isExec ?? (_ => false));
        var registry = new CustomCompleterRegistry();
        var shellCtx = new ShellContext
        {
            Vm = vm,
            PathCache = cache,
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        var classifier = new ShellLineClassifier(shellCtx);
        return new CompletionEngine(vm, cache, registry, classifier, TextWriter.Null);
    }

    /// <summary>
    /// Runs <see cref="TabCompletionAction.Apply"/> and returns the result.
    /// The <paramref name="menuCalled"/> output indicates whether renderMenu was invoked.
    /// </summary>
    private static TabActionResult Apply(
        CompletionEngine engine,
        ref StringBuilder buffer,
        ref int cursor,
        ref bool lastKeyWasTab,
        out bool menuCalled)
    {
        bool called = false;
        var result = TabCompletionAction.Apply(
            buffer,
            ref cursor,
            ref lastKeyWasTab,
            engine,
            _ => { called = true; });
        menuCalled = called;
        return result;
    }

    // ── 0 candidates → bell ───────────────────────────────────────────────────

    [Fact]
    public void ZeroCandidates_FirstTab_ReturnsBell()
    {
        var engine = MakeEngine();
        var buffer = new StringBuilder("xyznotacommand_unique_9999");
        int cursor = buffer.Length;
        bool lastKeyWasTab = false;

        var result = Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out bool menuCalled);

        Assert.Equal(TabActionKind.Bell, result.Kind);
        Assert.True(lastKeyWasTab, "lastKeyWasTab should be set after bell");
        Assert.False(menuCalled);
        // Buffer should be unchanged
        Assert.Equal("xyznotacommand_unique_9999", buffer.ToString());
    }

    [Fact]
    public void ZeroCandidates_SecondTab_StillReturnsBell()
    {
        var engine = MakeEngine();
        var buffer = new StringBuilder("xyznotacommand_unique_9999");
        int cursor = buffer.Length;
        bool lastKeyWasTab = true; // simulate second Tab

        var result = Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out _);

        Assert.Equal(TabActionKind.Bell, result.Kind);
    }

    // ── 1 candidate → insert ──────────────────────────────────────────────────

    [Fact]
    public void OneCandidate_InsertsCandidateAndResetsState()
    {
        // "cd" is a unique sugar candidate when the prefix is "c" and no PATH executables match
        // We use a Stash-mode completion for a unique known prefix.
        // "println" is the only stdlib function starting with "printl"
        var engine = MakeEngine();
        var buffer = new StringBuilder("printl");
        int cursor = buffer.Length;
        bool lastKeyWasTab = false;

        var result = Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out bool menuCalled);

        Assert.Equal(TabActionKind.Modified, result.Kind);
        Assert.False(lastKeyWasTab, "lastKeyWasTab must reset after a successful single insertion");
        Assert.False(menuCalled);
        // Buffer should now contain the inserted candidate
        Assert.Equal("println", buffer.ToString());
        Assert.Equal(buffer.Length, cursor);
    }

    [Fact]
    public void OneCandidate_CursorMovedToEndOfInserted()
    {
        var engine = MakeEngine();
        var buffer = new StringBuilder("printl");
        int cursor = buffer.Length;
        bool lastKeyWasTab = false;

        Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out _);

        // Cursor must be at end of the inserted string
        Assert.Equal(buffer.Length, cursor);
        Assert.Equal("println", buffer.ToString());
    }

    // ── N>1 candidates, first Tab → insert LCP ────────────────────────────────

    [Fact]
    public void MultipleCandidates_FirstTab_InsertsLcpWhenProgress()
    {
        // "pri" → candidates include at least "print" and "println"
        // LCP = "print" which is longer than "pri"
        var engine = MakeEngine();
        var buffer = new StringBuilder("pri");
        int cursor = buffer.Length;
        bool lastKeyWasTab = false;

        var result = Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out bool menuCalled);

        // Should insert LCP "print" (common prefix of "print" and "println")
        Assert.Equal(TabActionKind.Modified, result.Kind);
        Assert.True(lastKeyWasTab, "lastKeyWasTab must be set (still multiple candidates)");
        Assert.False(menuCalled, "menu must NOT show on first Tab");
        Assert.StartsWith("print", buffer.ToString());
        Assert.Equal(buffer.Length, cursor);
    }

    [Fact]
    public void MultipleCandidates_FirstTab_NoProgress_ReturnsNoOp()
    {
        // "print" is already the LCP of {print, println} — no further progress
        var engine = MakeEngine();
        var buffer = new StringBuilder("print");
        int cursor = buffer.Length;
        bool lastKeyWasTab = false;

        var result = Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out bool menuCalled);

        Assert.Equal(TabActionKind.NoOp, result.Kind);
        Assert.True(lastKeyWasTab);
        Assert.False(menuCalled);
        Assert.Equal("print", buffer.ToString()); // unchanged
    }

    // ── N>1 candidates, second Tab → list candidates ──────────────────────────

    [Fact]
    public void MultipleCandidates_SecondTab_InvokesMenuAndResetsState()
    {
        // "print" is already at LCP → first Tab gives NoOp, second Tab shows menu
        var engine = MakeEngine();
        var buffer = new StringBuilder("print");
        int cursor = buffer.Length;
        bool lastKeyWasTab = true; // simulate already having pressed Tab once

        var result = Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out bool menuCalled);

        Assert.Equal(TabActionKind.ListedCandidates, result.Kind);
        Assert.True(menuCalled, "menu render callback must be invoked on second Tab");
        Assert.False(lastKeyWasTab, "lastKeyWasTab must reset after listing");
    }

    [Fact]
    public void MultipleCandidates_SecondTab_BufferUnchanged()
    {
        var engine = MakeEngine();
        var buffer = new StringBuilder("print");
        int cursor = buffer.Length;
        bool lastKeyWasTab = true;

        Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out _);

        // Buffer stays unchanged — only listing happens
        Assert.Equal("print", buffer.ToString());
        Assert.Equal(5, cursor);
    }

    // ── Non-Tab key resets state ───────────────────────────────────────────────

    [Fact]
    public void AfterFirstTabNoOp_NonTabKeyMustResetState()
    {
        // Simulate: press Tab (sets lastKeyWasTab=true) then press a letter key
        // The LineEditor resets _lastKeyWasTab=false before the switch on any non-Tab key.
        // Here we just verify that after a Tab→NoOp, calling Apply again with lastKeyWasTab=false
        // (as the LineEditor would set it) gives a first-Tab behavior again.
        var engine = MakeEngine();
        var buffer = new StringBuilder("print");
        int cursor = buffer.Length;
        bool lastKeyWasTab = false;

        // First Tab
        Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out _);
        Assert.True(lastKeyWasTab);

        // Simulate non-Tab key (LineEditor sets lastKeyWasTab = false)
        lastKeyWasTab = false;

        // Second Apply with reset state → should behave like first Tab again
        buffer.Clear(); buffer.Append("print"); cursor = buffer.Length;
        var result2 = Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out bool menuCalled2);

        Assert.Equal(TabActionKind.NoOp, result2.Kind);
        Assert.False(menuCalled2, "menu must NOT show — state was reset by non-Tab key");
    }

    // ── CompletionEnabled = false bypasses engine ─────────────────────────────

    [Fact]
    public void LineEditor_CompletionEnabled_False_EnginePropIsStillStorable()
    {
        // Verify that CompletionEngine and CompletionEnabled are settable
        var editor = new LineEditor();
        var engine = MakeEngine();
        editor.CompletionEngine = engine;
        editor.CompletionEnabled = false;

        Assert.Same(engine, editor.CompletionEngine);
        Assert.False(editor.CompletionEnabled);
    }

    [Fact]
    public void LineEditor_CompletionEnabled_DefaultsToTrue()
    {
        var editor = new LineEditor();
        Assert.True(editor.CompletionEnabled);
    }

    [Fact]
    public void LineEditor_CompletionEngine_DefaultsToNull()
    {
        var editor = new LineEditor();
        Assert.Null(editor.CompletionEngine);
    }

    // ── Insert replaces only the token span, not the whole buffer ─────────────

    [Fact]
    public void SingleCandidate_InsertReplacesTokenSpanOnly()
    {
        // Buffer: "echo printl" — cursor at end; should complete to "echo println"
        var engine = MakeEngine();
        var buffer = new StringBuilder("echo printl");
        int cursor = buffer.Length;
        bool lastKeyWasTab = false;

        Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out _);

        // "echo " stays; "printl" is replaced by "println"
        Assert.Equal("echo println", buffer.ToString());
        Assert.Equal(buffer.Length, cursor);
    }

    // ── LCP is inserted correctly mid-buffer ─────────────────────────────────

    [Fact]
    public void MultipleCandidates_LcpInsertedAtCorrectPosition()
    {
        // "echo pri" → cursor at end, engine should complete "pri" → "print"
        var engine = MakeEngine();
        var buffer = new StringBuilder("echo pri");
        int cursor = buffer.Length;
        bool lastKeyWasTab = false;

        var result = Apply(engine, ref buffer, ref cursor, ref lastKeyWasTab, out _);

        // Should have inserted LCP (at minimum extends to "print")
        Assert.Equal(TabActionKind.Modified, result.Kind);
        Assert.StartsWith("echo print", buffer.ToString());
        Assert.Equal(buffer.Length, cursor);
    }
}
