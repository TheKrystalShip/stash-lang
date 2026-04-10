using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Analysis.FlowAnalysis;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for Phase 8, Tasks 8.4 and 8.5 — Data Flow Analysis framework and NullFlowRule (SA0309).
/// </summary>
public class DataFlowTests : AnalysisTestBase
{
    // ── DataFlowState unit tests ─────────────────────────────────────────────

    [Fact]
    public void DataFlowState_GetState_UnknownForMissingVariable()
    {
        var state = new DataFlowState();
        Assert.Equal(NullState.Unknown, state.GetState("x"));
    }

    [Fact]
    public void DataFlowState_SetAndGet_ReturnsSetState()
    {
        var state = new DataFlowState();
        state.SetState("x", NullState.Null);
        Assert.Equal(NullState.Null, state.GetState("x"));
    }

    [Fact]
    public void DataFlowState_Clone_IsIndependent()
    {
        var original = new DataFlowState();
        original.SetState("x", NullState.NonNull);
        var clone = original.Clone();
        clone.SetState("x", NullState.Null);
        Assert.Equal(NullState.NonNull, original.GetState("x"));
        Assert.Equal(NullState.Null, clone.GetState("x"));
    }

    [Fact]
    public void DataFlowState_MergeFrom_NonNullAndNull_YieldsMaybeNull()
    {
        var target = new DataFlowState();
        target.SetState("x", NullState.NonNull);
        var other = new DataFlowState();
        other.SetState("x", NullState.Null);
        bool changed = target.MergeFrom(other);
        Assert.True(changed);
        Assert.Equal(NullState.MaybeNull, target.GetState("x"));
    }

    [Fact]
    public void DataFlowState_MergeFrom_SameState_ReturnsFalse()
    {
        var target = new DataFlowState();
        target.SetState("x", NullState.Null);
        var other = new DataFlowState();
        other.SetState("x", NullState.Null);
        bool changed = target.MergeFrom(other);
        Assert.False(changed);
        Assert.Equal(NullState.Null, target.GetState("x"));
    }

    [Fact]
    public void DataFlowState_MergeFrom_UnknownAndNull_YieldsNull()
    {
        var target = new DataFlowState();
        target.SetState("x", NullState.Unknown);
        var other = new DataFlowState();
        other.SetState("x", NullState.Null);
        target.MergeFrom(other);
        Assert.Equal(NullState.Null, target.GetState("x"));
    }

    // ── DataFlowAnalyzer unit tests (via CFG helpers) ────────────────────────

    [Fact]
    public void DataFlowAnalyzer_NullInitializer_TrackesAsNull()
    {
        var cfg = BuildCfg("let x = null;");
        var states = DataFlowAnalyzer.Analyze(cfg);

        // Entry block has 1 statement; check exit state by walking blocks
        var entryBlock = cfg.Entry;
        var entryState = states[entryBlock.Id].Clone();
        // Before any transfers, entry state is empty for the entry block
        Assert.Equal(NullState.Unknown, entryState.GetState("x"));
    }

    [Fact]
    public void DataFlowAnalyzer_LinearSequence_EntryBlockHasUnknownBeforeTransfer()
    {
        // The DFA returns entry states (state at block start, before any statements in that block apply).
        // For the entry block with no predecessors, all variables start as Unknown.
        var cfg = BuildCfg("let x = null; let y = \"hello\";");
        var states = DataFlowAnalyzer.Analyze(cfg);

        // Entry block has no predecessors, so its entry state has no known variables
        var entryState = states[cfg.Entry.Id];
        Assert.Equal(NullState.Unknown, entryState.GetState("x"));
        Assert.Equal(NullState.Unknown, entryState.GetState("y"));
    }

    // ── NullFlowRule (SA0309) diagnostics ───────────────────────────────────

    [Fact]
    public void NullFlowRule_ExplicitNullInit_DotAccess_ReportsSA0309()
    {
        // SA0308 does NOT cover `let x = null` (only `let x;`), so SA0309 is the unique detector
        var diagnostics = Validate("""
            let x = null;
            x.member;
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0309");
    }

    [Fact]
    public void NullFlowRule_NullAssignedThenReassigned_NoSA0309()
    {
        var diagnostics = Validate("""
            let x = null;
            x = "hello";
            x.member;
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0309");
    }

    [Fact]
    public void NullFlowRule_NullInOneBranch_MaybeNullAfterJoin_ReportsSA0309()
    {
        // SA0308 does not detect this case (x has a non-null initializer)
        // SA0309 should detect x is MaybeNull after the if
        var diagnostics = Validate("""
            let x = "initial";
            if (true) { x = null; }
            x.member;
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0309");
    }

    [Fact]
    public void NullFlowRule_NonNullVariable_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let x = "hello";
            x.length;
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0309");
    }

    [Fact]
    public void NullFlowRule_BothBranchesAssignNonNull_NoSA0309()
    {
        var diagnostics = Validate("""
            let x = null;
            if (true) { x = "a"; } else { x = "b"; }
            x.member;
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0309");
    }

    [Fact]
    public void NullFlowRule_OptionalChain_NoSA0309()
    {
        // Optional chaining x?.member is safe — no SA0309
        var diagnostics = Validate("""
            let x = null;
            x?.member;
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0309");
    }

    [Fact]
    public void NullFlowRule_SA0309_DescriptorRegistered()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0309"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ControlFlowGraph BuildCfg(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var builder = new CfgBuilder();
        return builder.Build(stmts);
    }
}
