namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Parsing.AST;

/// <summary>
/// SA0311 — Reports an error when a regex-using str function is called with a literal pattern
/// that fails .NET regex compilation.
/// SA0312 — Reports a warning when the pattern compiles but contains nested quantifiers that
/// may cause catastrophic backtracking.
/// </summary>
public sealed class InvalidRegexPatternRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0311;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(CallExpr) };

    private static readonly HashSet<string> RegexFunctions = new(StringComparer.Ordinal)
    {
        "match", "matchAll", "isMatch", "capture", "captureAll", "replaceRegex"
    };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not CallExpr call) return;
        if (call.Callee is not DotExpr dot) return;
        if (dot.Object is not IdentifierExpr nsId || nsId.Name.Lexeme != "str") return;
        if (!RegexFunctions.Contains(dot.Name.Lexeme)) return;
        if (call.Arguments.Count < 2) return;
        if (call.Arguments[1] is not LiteralExpr literal || literal.Value is not string pattern) return;

        // SA0311: Check for a compilable regex
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            string message = ex.Message;
            int dotIndex = message.IndexOf('.');
            if (dotIndex > 0) message = message[..dotIndex];
            string truncatedPattern = pattern.Length > 80 ? pattern[..77] + "..." : pattern;
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0311.CreateDiagnostic(literal.Span, truncatedPattern, message));
            return; // Skip SA0312 when the pattern is already invalid
        }

        // SA0312: Check for nested quantifiers that can cause catastrophic backtracking
        int dangerPos = FindCatastrophicQuantifier(pattern);
        if (dangerPos >= 0)
        {
            string snippet = pattern.Length > 40 ? pattern[..37] + "..." : pattern;
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0312.CreateDiagnostic(literal.Span, dangerPos, snippet));
        }
    }

    /// <summary>
    /// Detects nested quantifiers that can cause catastrophic backtracking,
    /// such as <c>(a+)+</c> or <c>(a*)*</c>.
    /// Returns the character position in the pattern where the issue was found, or -1.
    /// </summary>
    private static int FindCatastrophicQuantifier(string pattern)
    {
        var groupStack = new Stack<(int Start, bool HasInnerQuantifier)>();

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            // Skip escaped characters
            if (c == '\\' && i + 1 < pattern.Length)
            {
                i++;
                continue;
            }

            // Skip character classes [...] entirely
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
                // Atomic group (?>...) — safe, skip its contents
                if (i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '>')
                {
                    int atomicDepth = 1;
                    i++;
                    while (i < pattern.Length && atomicDepth > 0)
                    {
                        i++;
                        if (i < pattern.Length)
                        {
                            if (pattern[i] == '(') atomicDepth++;
                            else if (pattern[i] == ')') atomicDepth--;
                        }
                    }
                    continue;
                }

                groupStack.Push((i, false));
            }
            else if (c == ')' && groupStack.Count > 0)
            {
                var (groupStart, hasInnerQuantifier) = groupStack.Pop();

                // Check whether this closing paren is followed by a repeating quantifier
                bool outerIsRepeating = i + 1 < pattern.Length &&
                    (pattern[i + 1] == '+' || pattern[i + 1] == '*');

                if (outerIsRepeating && hasInnerQuantifier)
                {
                    return groupStart; // Catastrophic nesting found
                }

                // Propagate quantifier info to the enclosing group
                if (groupStack.Count > 0)
                {
                    bool propagate = hasInnerQuantifier || outerIsRepeating;
                    if (propagate)
                    {
                        var parent = groupStack.Pop();
                        groupStack.Push((parent.Start, true));
                    }
                }
            }
            else if ((c == '+' || c == '*') && groupStack.Count > 0)
            {
                // Mark the innermost open group as containing a quantifier
                var top = groupStack.Pop();
                groupStack.Push((top.Start, true));
            }
        }

        return -1;
    }
}
