namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA0109 — Emits an information diagnostic when a function or lambda has a cyclomatic
/// complexity score exceeding the configured threshold (default: 10).
/// </summary>
/// <remarks>
/// Cyclomatic complexity counts the number of independent decision paths through a function.
/// Each of the following adds 1 to the starting score of 1:
/// <list type="bullet">
///   <item><c>if</c> / <c>else if</c></item>
///   <item><c>while</c>, <c>for</c>, <c>for-in</c>, <c>do-while</c></item>
///   <item><c>catch</c> clause</item>
///   <item><c>&amp;&amp;</c> and <c>||</c> logical operators</item>
///   <item>ternary (<c>? :</c>)</item>
/// </list>
/// The threshold is configurable via <see cref="CyclomaticComplexityRule.Threshold"/>.
/// </remarks>
public sealed class CyclomaticComplexityRule : IAnalysisRule, IConfigurableRule
{
    /// <summary>Default cyclomatic complexity threshold.</summary>
    public const int DefaultThreshold = 10;

    /// <summary>Configurable threshold; defaults to <see cref="DefaultThreshold"/>.</summary>
    public int Threshold { get; private set; } = DefaultThreshold;

    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0109;

    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("maxComplexity", out string? val) && int.TryParse(val, out int v) && v > 0)
            Threshold = v;
    }

    /// <summary>Subscribed to FnDeclStmt — analyzed once per function declaration.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(FnDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not FnDeclStmt fn)
        {
            return;
        }

        int complexity = ComputeComplexity(fn.Body.Statements);

        if (complexity > Threshold)
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0109.CreateDiagnostic(fn.Name.Span, fn.Name.Lexeme, complexity, Threshold));
        }
    }

    /// <summary>
    /// Computes the cyclomatic complexity of a statement block (starting at 1).
    /// </summary>
    internal static int ComputeComplexity(IReadOnlyList<Stmt> stmts)
    {
        int count = 1;
        CountStmts(stmts, ref count);
        return count;
    }

    private static void CountStmts(IReadOnlyList<Stmt> stmts, ref int count)
    {
        foreach (var stmt in stmts)
        {
            CountStmt(stmt, ref count);
        }
    }

    private static void CountStmt(Stmt stmt, ref int count)
    {
        switch (stmt)
        {
            case IfStmt ifStmt:
                count++; // the if condition
                CountStmt(ifStmt.ThenBranch, ref count);
                if (ifStmt.ElseBranch != null)
                    CountStmt(ifStmt.ElseBranch, ref count);
                break;

            case WhileStmt whileStmt:
                count++;
                CountStmts(whileStmt.Body.Statements, ref count);
                break;

            case DoWhileStmt doWhileStmt:
                count++;
                CountStmts(doWhileStmt.Body.Statements, ref count);
                break;

            case ForStmt forStmt:
                count++;
                CountStmts(forStmt.Body.Statements, ref count);
                break;

            case ForInStmt forInStmt:
                count++;
                CountStmts(forInStmt.Body.Statements, ref count);
                break;

            case TryCatchStmt tryCatch:
                if (tryCatch.CatchClauses.Count > 0)
                    count++; // catch clause counts as a branch
                CountStmts(tryCatch.TryBody.Statements, ref count);
                foreach (var clause in tryCatch.CatchClauses)
                    CountStmts(clause.Body.Statements, ref count);
                if (tryCatch.FinallyBody != null)
                    CountStmts(tryCatch.FinallyBody.Statements, ref count);
                break;

            case BlockStmt block:
                CountStmts(block.Statements, ref count);
                break;

            case ExprStmt exprStmt:
                CountExpr(exprStmt.Expression, ref count);
                break;

            case VarDeclStmt varDecl:
                if (varDecl.Initializer != null)
                    CountExpr(varDecl.Initializer, ref count);
                break;

            case ReturnStmt ret:
                if (ret.Value != null)
                    CountExpr(ret.Value, ref count);
                break;

            case FnDeclStmt:
                // Nested function declarations are independent units — do not count their
                // decision points toward the enclosing function's complexity.
                break;
        }
    }

    private static void CountExpr(Expr expr, ref int count)
    {
        switch (expr)
        {
            case BinaryExpr binary:
                if (binary.Operator.Type is TokenType.AmpersandAmpersand or TokenType.PipePipe)
                    count++;
                CountExpr(binary.Left, ref count);
                CountExpr(binary.Right, ref count);
                break;

            case TernaryExpr ternary:
                count++;
                CountExpr(ternary.Condition, ref count);
                CountExpr(ternary.ThenBranch, ref count);
                CountExpr(ternary.ElseBranch, ref count);
                break;

            case CallExpr call:
                CountExpr(call.Callee, ref count);
                foreach (var arg in call.Arguments)
                    CountExpr(arg, ref count);
                break;

            case AssignExpr assign:
                CountExpr(assign.Value, ref count);
                break;

            case LambdaExpr:
                // Lambda bodies are independent units — skip.
                break;
        }
    }
}
