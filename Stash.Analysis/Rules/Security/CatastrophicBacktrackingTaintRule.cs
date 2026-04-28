namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Parsing.AST;

/// <summary>
/// SA1303 — Warns when a regex function with a catastrophic-backtracking pattern is applied
/// to externally-sourced (tainted) input, creating a ReDoS vulnerability.
/// </summary>
/// <remarks>
/// Taint sources: function parameters, <c>io.readLine()</c>/<c>io.read()</c>,
/// <c>fs.readText()</c>/<c>fs.readAll()</c>, any <c>http.*</c> call, and command output (<c>$(...)</c>).
/// The catastrophic-backtracking detection reuses the same nested-quantifier algorithm as SA0312.
/// </remarks>
public sealed class CatastrophicBacktrackingTaintRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1303;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(CallExpr) };

    private static readonly HashSet<string> RegexFunctions = new(StringComparer.Ordinal)
    {
        "match", "matchAll", "isMatch", "capture", "captureAll", "replaceRegex"
    };

    private static readonly HashSet<string> TaintedIoMethods = new(StringComparer.Ordinal)
    {
        "readLine", "read", "readText", "readAll"
    };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not CallExpr call) return;
        if (call.Callee is not DotExpr dot) return;
        if (dot.Object is not IdentifierExpr nsId || nsId.Name.Lexeme != "str") return;
        if (!RegexFunctions.Contains(dot.Name.Lexeme)) return;
        if (call.Arguments.Count < 2) return;

        // Pattern argument is at position 1
        if (call.Arguments[1] is not LiteralExpr patternLit || patternLit.Value is not string pattern) return;

        if (!IsCatastrophicPattern(pattern)) return;

        // Input (position 0) must be tainted for this rule to fire
        var input = call.Arguments[0];
        string? taintSource = GetTaintSource(input, context);
        if (taintSource == null) return;

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA1303.CreateDiagnostic(call.Span, taintSource));
    }

    private static string? GetTaintSource(Expr input, RuleContext context)
    {
        // Parameter reference
        if (input is IdentifierExpr id)
        {
            var sym = context.ScopeTree.FindDefinition(id.Name.Lexeme, id.Span.StartLine, id.Span.StartColumn);
            if (sym?.Kind == SymbolKind.Parameter)
                return id.Name.Lexeme;
        }

        // io.readLine(), io.read(), fs.readText(), fs.readAll(), http.*
        if (input is CallExpr inputCall && inputCall.Callee is DotExpr inputDot &&
            inputDot.Object is IdentifierExpr inputNs)
        {
            string ns = inputNs.Name.Lexeme;
            string method = inputDot.Name.Lexeme;

            if ((ns == "io" || ns == "fs") && TaintedIoMethods.Contains(method))
                return $"{ns}.{method}()";

            if (ns == "http")
                return $"http.{method}()";
        }

        // Command output: $(...) or $!(...) — CommandExpr
        if (input is CommandExpr)
            return "$()";

        return null;
    }

    /// <summary>
    /// Detects nested quantifiers that can cause catastrophic backtracking, such as <c>(a+)+</c>.
    /// This mirrors the algorithm in <c>InvalidRegexPatternRule.FindCatastrophicQuantifier</c>.
    /// </summary>
    private static bool IsCatastrophicPattern(string pattern)
    {
        var groupStack = new Stack<bool>(); // bool = has inner quantifier

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '\\' && i + 1 < pattern.Length) { i++; continue; }

            if (c == '[')
            {
                i++;
                while (i < pattern.Length && pattern[i] != ']')
                {
                    if (pattern[i] == '\\') i++;
                    i++;
                }
                continue;
            }

            if (c == '(')
            {
                // Atomic group (?>...) — skip safely
                if (i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '>')
                {
                    int depth = 1;
                    i++;
                    while (i < pattern.Length && depth > 0)
                    {
                        i++;
                        if (i < pattern.Length)
                        {
                            if (pattern[i] == '(') depth++;
                            else if (pattern[i] == ')') depth--;
                        }
                    }
                    continue;
                }
                groupStack.Push(false);
            }
            else if (c == ')' && groupStack.Count > 0)
            {
                bool hasInnerQuantifier = groupStack.Pop();
                bool outerIsRepeating = i + 1 < pattern.Length &&
                    (pattern[i + 1] == '+' || pattern[i + 1] == '*');

                if (outerIsRepeating && hasInnerQuantifier)
                    return true;

                if (outerIsRepeating && groupStack.Count > 0)
                {
                    groupStack.Pop();
                    groupStack.Push(true);
                }
            }
            else if ((c == '+' || c == '*') && groupStack.Count > 0)
            {
                bool top = groupStack.Pop();
                groupStack.Push(true);
            }
        }

        return false;
    }
}
