namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// SA1110 — Reports an informational diagnostic when a numeric literal appears more than
/// once in the file without being extracted as a named constant.
/// </summary>
/// <remarks>
/// Literals in the exempt set (<c>-1</c>, <c>0</c>, <c>1</c>, <c>2</c>, <c>100</c>) and literals that
/// appear as the direct initializer of a <c>const</c> declaration are excluded.
/// Configurable options: <c>magic_number_threshold</c> (default 2) and
/// <c>magic_number_exemptions</c> (comma-separated list of numbers).
/// </remarks>
public sealed class MagicNumberRule : IAnalysisRule, IConfigurableRule
{
    private static readonly HashSet<double> DefaultExemptions = new() { -1, 0, 1, 2, 100 };

    private int _threshold = 2;
    private HashSet<double> _exemptions = new(DefaultExemptions);

    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1110;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>(); // Post-walk

    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("magic_number_threshold", out string? tVal) &&
            int.TryParse(tVal, out int t) && t > 0)
        {
            _threshold = t;
        }

        if (options.TryGetValue("magic_number_exemptions", out string? eVal))
        {
            var exemptions = new HashSet<double>();
            foreach (var part in eVal.Split(','))
            {
                if (double.TryParse(part.Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                {
                    exemptions.Add(v);
                }
            }
            _exemptions = exemptions;
        }
    }

    public void Analyze(RuleContext context)
    {
        var occurrences = new Dictionary<double, List<SourceSpan>>();

        foreach (var stmt in context.AllStatements)
            CollectNumbers(stmt, occurrences);

        foreach (var (value, spans) in occurrences)
        {
            if (spans.Count < _threshold) continue;

            string formatted = value == Math.Floor(value) && value >= long.MinValue && value <= long.MaxValue
                ? ((long)value).ToString()
                : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            foreach (var span in spans)
            {
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA1110.CreateDiagnostic(span, formatted, spans.Count));
            }
        }
    }

    private void CollectNumbers(Stmt stmt, Dictionary<double, List<SourceSpan>> occurrences)
    {
        switch (stmt)
        {
            case ConstDeclStmt constDecl:
                // Exclude the const initializer itself — it IS the named constant definition.
                // Still recurse if the initializer is complex (e.g. const X = A * 42 + 7).
                CollectNumbersExpr(constDecl.Initializer, occurrences, skipRoot: true);
                break;
            case VarDeclStmt varDecl:
                if (varDecl.Initializer != null)
                    CollectNumbersExpr(varDecl.Initializer, occurrences, skipRoot: false);
                break;
            case ExprStmt exprStmt:
                CollectNumbersExpr(exprStmt.Expression, occurrences, skipRoot: false);
                break;
            case BlockStmt block:
                foreach (var s in block.Statements) CollectNumbers(s, occurrences);
                break;
            case IfStmt ifStmt:
                CollectNumbersExpr(ifStmt.Condition, occurrences, skipRoot: false);
                CollectNumbers(ifStmt.ThenBranch, occurrences);
                if (ifStmt.ElseBranch != null) CollectNumbers(ifStmt.ElseBranch, occurrences);
                break;
            case WhileStmt whileStmt:
                CollectNumbersExpr(whileStmt.Condition, occurrences, skipRoot: false);
                CollectNumbers(whileStmt.Body, occurrences);
                break;
            case DoWhileStmt doWhile:
                CollectNumbers(doWhile.Body, occurrences);
                CollectNumbersExpr(doWhile.Condition, occurrences, skipRoot: false);
                break;
            case ForStmt forStmt:
                if (forStmt.Initializer != null) CollectNumbers(forStmt.Initializer, occurrences);
                if (forStmt.Condition != null) CollectNumbersExpr(forStmt.Condition, occurrences, skipRoot: false);
                if (forStmt.Increment != null) CollectNumbersExpr(forStmt.Increment, occurrences, skipRoot: false);
                CollectNumbers(forStmt.Body, occurrences);
                break;
            case ReturnStmt ret:
                if (ret.Value != null) CollectNumbersExpr(ret.Value, occurrences, skipRoot: false);
                break;
            case FnDeclStmt fn:
                // Skip default parameter values (they serve as documentation at the call site).
                CollectNumbers(fn.Body, occurrences);
                break;
        }
    }

    private void CollectNumbersExpr(Expr expr, Dictionary<double, List<SourceSpan>> occurrences, bool skipRoot)
    {
        if (expr is LiteralExpr lit)
        {
            if (!skipRoot)
            {
                double? numVal = lit.Value switch
                {
                    long l => (double)l,
                    int i => (double)i,
                    double d => d,
                    float f => (double)f,
                    _ => (double?)null
                };
                if (numVal.HasValue && !_exemptions.Contains(numVal.Value))
                {
                    if (!occurrences.TryGetValue(numVal.Value, out var list))
                        occurrences[numVal.Value] = list = new List<SourceSpan>();
                    list.Add(expr.Span);
                }
            }
            return; // LiteralExpr has no children
        }

        switch (expr)
        {
            case BinaryExpr bin:
                CollectNumbersExpr(bin.Left, occurrences, skipRoot: false);
                CollectNumbersExpr(bin.Right, occurrences, skipRoot: false);
                break;
            case UnaryExpr unary:
                CollectNumbersExpr(unary.Right, occurrences, skipRoot: false);
                break;
            case GroupingExpr group:
                CollectNumbersExpr(group.Expression, occurrences, skipRoot: false);
                break;
            case CallExpr call:
                CollectNumbersExpr(call.Callee, occurrences, skipRoot: false);
                foreach (var arg in call.Arguments)
                    CollectNumbersExpr(arg, occurrences, skipRoot: false);
                break;
            case AssignExpr assign:
                CollectNumbersExpr(assign.Value, occurrences, skipRoot: false);
                break;
            case LambdaExpr lambda:
                if (lambda.ExpressionBody != null)
                    CollectNumbersExpr(lambda.ExpressionBody, occurrences, skipRoot: false);
                if (lambda.BlockBody != null)
                    CollectNumbers(lambda.BlockBody, occurrences);
                break;
        }
    }
}
