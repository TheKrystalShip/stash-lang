namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Parsing.AST;

/// <summary>
/// SA0804 — Emits an information diagnostic (with a safe autofix) when import statements
/// are not in canonical order.
/// </summary>
/// <remarks>
/// Canonical import order:
/// <list type="number">
///   <item>Standard library imports: <c>import { ... } from "std:..."</c> (sorted alphabetically)</item>
///   <item>Package imports: <c>import { ... } from "@pkg/..."</c> or <c>import ... from "@..."</c> (sorted)</item>
///   <item>Relative imports: <c>import { ... } from "./..."</c> or <c>import { ... } from "../..."</c> (sorted)</item>
/// </list>
/// All non-import statements (or dynamic-path imports) are treated as a section break.
/// Only the leading consecutive block of import statements is analyzed.
/// </remarks>
public sealed class ImportOrderingRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0804;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        // Collect the leading consecutive block of static import statements
        var imports = new List<(Stmt stmt, string path)>();
        foreach (var stmt in context.AllStatements)
        {
            if (stmt is ImportStmt importStmt && importStmt.IsStaticPath)
            {
                imports.Add((stmt, importStmt.StaticPathValue!));
            }
            else if (stmt is ImportAsStmt importAs && importAs.IsStaticPath)
            {
                imports.Add((stmt, importAs.StaticPathValue!));
            }
            else
            {
                break; // Stop at the first non-import statement
            }
        }

        if (imports.Count < 2)
        {
            return; // Nothing to order
        }

        // Compute the ordered list
        var ordered = imports
            .OrderBy(x => GetImportGroup(x.path))
            .ThenBy(x => x.path, StringComparer.Ordinal)
            .ToList();

        // Compare original vs ordered
        bool isOrdered = true;
        for (int i = 0; i < imports.Count; i++)
        {
            if (imports[i].stmt != ordered[i].stmt)
            {
                isOrdered = false;
                break;
            }
        }

        if (isOrdered)
        {
            return;
        }

        // Emit one SA0804 on the first out-of-order import (the whole block span)
        var firstSpan = imports[0].stmt.Span;
        var lastSpan = imports[^1].stmt.Span;
        var blockSpan = new Stash.Common.SourceSpan(
            firstSpan.File,
            firstSpan.StartLine,
            firstSpan.StartColumn,
            lastSpan.EndLine,
            lastSpan.EndColumn);

        // Build autofix: replace the block with the sorted order
        var sortedText = string.Join(
            "\n",
            ordered.Select(x => BuildImportText(x.stmt)));

        var fix = new CodeFix(
            "Sort imports into canonical order",
            FixApplicability.Safe,
            [new SourceEdit(blockSpan, sortedText)]);

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA0804.CreateDiagnosticWithFix(blockSpan, fix));
    }

    /// <summary>
    /// Returns 0 for stdlib, 1 for package (@), 2 for relative.
    /// </summary>
    private static int GetImportGroup(string path)
    {
        if (path.StartsWith("std:", StringComparison.Ordinal))
            return 0;
        if (path.StartsWith("@", StringComparison.Ordinal))
            return 1;
        return 2; // Relative ./  ../
    }

    private static string BuildImportText(Stmt stmt)
    {
        // Reconstruct a minimal import text from span source is not available here —
        // we rely on the SourceEdit covering the full block span to replace the text.
        // This is a best-effort reconstruction used only when span source is unavailable.
        return stmt switch
        {
            ImportStmt imp => $"import {{ {string.Join(", ", imp.Names.ConvertAll(n => n.Lexeme))} }} from \"{imp.StaticPathValue}\";",
            ImportAsStmt impAs => $"import \"{impAs.StaticPathValue}\" as {impAs.Alias.Lexeme};",
            _ => ""
        };
    }
}
