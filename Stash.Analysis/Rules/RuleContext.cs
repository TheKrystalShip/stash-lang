namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Analysis.FlowAnalysis;
using Stash.Parsing.AST;

/// <summary>
/// Carries all context needed by an <see cref="IAnalysisRule"/> during analysis.
/// </summary>
public sealed class RuleContext
{
    /// <summary>The current AST statement being visited, or <see langword="null"/> for post-walk and expression-level rules.</summary>
    public Stmt? Statement { get; init; }

    /// <summary>The current AST expression being visited, or <see langword="null"/> for statement-level and post-walk rules.</summary>
    public Expr? Expression { get; init; }

    /// <summary>The scope tree built by <see cref="SymbolCollector"/> for the current document.</summary>
    public ScopeTree ScopeTree { get; init; } = null!;

    /// <summary>The set of names that are always in scope (built-in functions and namespaces).</summary>
    public IReadOnlySet<string> BuiltInNames { get; init; } = null!;

    /// <summary>The set of type names that are always valid (primitives and built-in struct names).</summary>
    public IReadOnlySet<string> ValidBuiltInTypes { get; init; } = null!;

    /// <summary>
    /// The top-level statement list for the document. Used by post-walk rules and unreachable-code
    /// block checks. For block-level unreachability checks this holds the block's statement list.
    /// </summary>
    public List<Stmt> AllStatements { get; init; } = null!;

    /// <summary>Current loop nesting depth. Non-zero when inside a loop body.</summary>
    public int LoopDepth { get; init; }

    /// <summary>Current function nesting depth. Non-zero when inside a function or lambda body.</summary>
    public int FunctionDepth { get; init; }

    /// <summary>Current elevate nesting depth. Non-zero when inside an elevate block.</summary>
    public int ElevateDepth { get; init; }

    /// <summary>Callback used to emit a <see cref="SemanticDiagnostic"/> from a rule.</summary>
    public Action<SemanticDiagnostic> ReportDiagnostic { get; init; } = null!;

    /// <summary>
    /// The control-flow graph for the current function body or top-level scope, or
    /// <see langword="null"/> when CFG analysis is not available for this context.
    /// </summary>
    public ControlFlowGraph? Cfg { get; init; }
}
