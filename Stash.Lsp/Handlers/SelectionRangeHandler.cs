namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Common;
using Stash.Lsp.Analysis;
using Stash.Parsing.AST;

/// <summary>
/// Handles LSP <c>textDocument/selectionRange</c> requests to support smart selection
/// expansion (also known as structural selection).
/// </summary>
/// <remarks>
/// <para>
/// For each cursor position the handler collects all AST nodes (statements and expressions)
/// whose source spans contain that position, sorts them by size (smallest/innermost first),
/// deduplicates identical spans, and then chains them into a linked list of
/// <see cref="SelectionRange"/> nodes from innermost to outermost.
/// </para>
/// <para>
/// This allows editors to expand the selection one syntactic level at a time:
/// e.g., argument → call expression → statement → block → function body.
/// </para>
/// <para>
/// Uses the <see cref="AnalysisEngine"/> cached result's statement list for traversal.
/// </para>
/// </remarks>
public class SelectionRangeHandler : SelectionRangeHandlerBase
{
    private readonly AnalysisEngine _analysis;

    /// <summary>
    /// Initialises the handler with the analysis engine used to retrieve cached document results.
    /// </summary>
    /// <param name="analysis">The analysis engine that supplies cached per-document results.</param>
    public SelectionRangeHandler(AnalysisEngine analysis)
    {
        _analysis = analysis;
    }

    /// <summary>
    /// Creates the registration options restricting this handler to Stash language documents.
    /// </summary>
    /// <param name="capability">The client's selection range capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override SelectionRangeRegistrationOptions CreateRegistrationOptions(
        SelectionRangeCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    /// <summary>
    /// Processes the selection range request and returns a linked chain of expanding ranges for each position.
    /// </summary>
    /// <param name="request">The request containing the document URI and one or more cursor positions.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="Container{SelectionRange}"/> with one entry per requested position,
    /// or <see langword="null"/> if no cached analysis is available.
    /// </returns>
    public override Task<Container<SelectionRange>?> Handle(SelectionRangeParams request,
        CancellationToken cancellationToken)
    {
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.FromResult<Container<SelectionRange>?>(null);
        }

        var ranges = new List<SelectionRange>();

        foreach (var position in request.Positions)
        {
            var line = position.Line + 1;    // to 1-based
            var col = position.Character + 1;

            var containingSpans = new List<SourceSpan>();
            foreach (var stmt in result.Statements)
            {
                CollectContainingSpans(stmt, line, col, containingSpans);
            }

            // Sort by span size (smallest/innermost first)
            containingSpans.Sort((a, b) =>
            {
                int sizeA = SpanSize(a);
                int sizeB = SpanSize(b);
                return sizeA.CompareTo(sizeB);
            });

            // Deduplicate identical spans
            var unique = new List<SourceSpan>();
            foreach (var span in containingSpans)
            {
                if (unique.Count == 0 || !SpansEqual(unique[^1], span))
                {
                    unique.Add(span);
                }
            }

            // Build the chain from innermost to outermost
            SelectionRange? current = null;
            for (int i = unique.Count - 1; i >= 0; i--)
            {
                current = new SelectionRange
                {
                    Range = unique[i].ToLspRange(),
                    Parent = current!
                };
            }

            ranges.Add(current ?? new SelectionRange
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(position, position)
            });
        }

        return Task.FromResult<Container<SelectionRange>?>(new Container<SelectionRange>(ranges));
    }

    /// <summary>
    /// Recursively collects all statement-level source spans that contain the given position.
    /// </summary>
    /// <param name="stmt">The statement node to examine.</param>
    /// <param name="line">1-based line number of the cursor.</param>
    /// <param name="col">1-based column number of the cursor.</param>
    /// <param name="spans">The accumulator list for matching spans.</param>
    private static void CollectContainingSpans(Stmt stmt, int line, int col, List<SourceSpan> spans)
    {
        if (!Contains(stmt.Span, line, col))
        {
            return;
        }

        spans.Add(stmt.Span);

        switch (stmt)
        {
            case FnDeclStmt fn:
                CollectContainingSpans(fn.Body, line, col, spans);
                break;
            case BlockStmt block:
                foreach (var s in block.Statements)
                {
                    CollectContainingSpans(s, line, col, spans);
                }

                break;
            case IfStmt ifStmt:
                CollectContainingSpansExpr(ifStmt.Condition, line, col, spans);
                CollectContainingSpans(ifStmt.ThenBranch, line, col, spans);
                if (ifStmt.ElseBranch != null)
                {
                    CollectContainingSpans(ifStmt.ElseBranch, line, col, spans);
                }

                break;
            case WhileStmt whileStmt:
                CollectContainingSpansExpr(whileStmt.Condition, line, col, spans);
                CollectContainingSpans(whileStmt.Body, line, col, spans);
                break;
            case ForInStmt forInStmt:
                CollectContainingSpansExpr(forInStmt.Iterable, line, col, spans);
                CollectContainingSpans(forInStmt.Body, line, col, spans);
                break;
            case ExprStmt exprStmt:
                CollectContainingSpansExpr(exprStmt.Expression, line, col, spans);
                break;
            case VarDeclStmt varDecl:
                if (varDecl.Initializer != null)
                {
                    CollectContainingSpansExpr(varDecl.Initializer, line, col, spans);
                }

                break;
            case ConstDeclStmt constDecl:
                CollectContainingSpansExpr(constDecl.Initializer, line, col, spans);
                break;
            case ReturnStmt returnStmt:
                if (returnStmt.Value != null)
                {
                    CollectContainingSpansExpr(returnStmt.Value, line, col, spans);
                }

                break;
        }
    }

    /// <summary>
    /// Recursively collects all expression-level source spans that contain the given position.
    /// </summary>
    /// <param name="expr">The expression node to examine.</param>
    /// <param name="line">1-based line number of the cursor.</param>
    /// <param name="col">1-based column number of the cursor.</param>
    /// <param name="spans">The accumulator list for matching spans.</param>
    private static void CollectContainingSpansExpr(Expr expr, int line, int col, List<SourceSpan> spans)
    {
        if (!Contains(expr.Span, line, col))
        {
            return;
        }

        spans.Add(expr.Span);

        switch (expr)
        {
            case BinaryExpr binary:
                CollectContainingSpansExpr(binary.Left, line, col, spans);
                CollectContainingSpansExpr(binary.Right, line, col, spans);
                break;
            case UnaryExpr unary:
                CollectContainingSpansExpr(unary.Right, line, col, spans);
                break;
            case GroupingExpr grouping:
                CollectContainingSpansExpr(grouping.Expression, line, col, spans);
                break;
            case CallExpr call:
                CollectContainingSpansExpr(call.Callee, line, col, spans);
                foreach (var arg in call.Arguments)
                {
                    CollectContainingSpansExpr(arg, line, col, spans);
                }

                break;
            case AssignExpr assign:
                CollectContainingSpansExpr(assign.Value, line, col, spans);
                break;
            case DotExpr dot:
                CollectContainingSpansExpr(dot.Object, line, col, spans);
                break;
            case DotAssignExpr dotAssign:
                CollectContainingSpansExpr(dotAssign.Object, line, col, spans);
                CollectContainingSpansExpr(dotAssign.Value, line, col, spans);
                break;
            case IndexExpr index:
                CollectContainingSpansExpr(index.Object, line, col, spans);
                CollectContainingSpansExpr(index.Index, line, col, spans);
                break;
            case IndexAssignExpr indexAssign:
                CollectContainingSpansExpr(indexAssign.Object, line, col, spans);
                CollectContainingSpansExpr(indexAssign.Index, line, col, spans);
                CollectContainingSpansExpr(indexAssign.Value, line, col, spans);
                break;
            case TernaryExpr ternary:
                CollectContainingSpansExpr(ternary.Condition, line, col, spans);
                CollectContainingSpansExpr(ternary.ThenBranch, line, col, spans);
                CollectContainingSpansExpr(ternary.ElseBranch, line, col, spans);
                break;
            case ArrayExpr array:
                foreach (var element in array.Elements)
                {
                    CollectContainingSpansExpr(element, line, col, spans);
                }

                break;
            case InterpolatedStringExpr interp:
                foreach (var part in interp.Parts)
                {
                    CollectContainingSpansExpr(part, line, col, spans);
                }

                break;
            case TryExpr tryExpr:
                CollectContainingSpansExpr(tryExpr.Expression, line, col, spans);
                break;
            case NullCoalesceExpr nullCoalesce:
                CollectContainingSpansExpr(nullCoalesce.Left, line, col, spans);
                CollectContainingSpansExpr(nullCoalesce.Right, line, col, spans);
                break;
            case PipeExpr pipe:
                CollectContainingSpansExpr(pipe.Left, line, col, spans);
                CollectContainingSpansExpr(pipe.Right, line, col, spans);
                break;
            case RedirectExpr redirect:
                CollectContainingSpansExpr(redirect.Expression, line, col, spans);
                CollectContainingSpansExpr(redirect.Target, line, col, spans);
                break;
            case UpdateExpr update:
                CollectContainingSpansExpr(update.Operand, line, col, spans);
                break;
            case StructInitExpr structInit:
                foreach (var (_, value) in structInit.FieldValues)
                {
                    CollectContainingSpansExpr(value, line, col, spans);
                }

                break;
            // LiteralExpr, IdentifierExpr, CommandExpr — leaf nodes, no children to recurse into
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="span"/> contains the given 1-based position.
    /// </summary>
    /// <param name="span">The source span to test.</param>
    /// <param name="line">1-based line number.</param>
    /// <param name="col">1-based column number.</param>
    private static bool Contains(SourceSpan span, int line, int col)
    {
        if (line < span.StartLine || line > span.EndLine)
        {
            return false;
        }

        if (line == span.StartLine && col < span.StartColumn)
        {
            return false;
        }

        if (line == span.EndLine && col > span.EndColumn)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns an integer approximation of the area covered by <paramref name="span"/>,
    /// used for sorting spans from innermost (smallest) to outermost (largest).
    /// </summary>
    /// <param name="span">The source span to measure.</param>
    /// <returns>A numeric size value where line count dominates column count.</returns>
    private static int SpanSize(SourceSpan span)
    {
        // Approximate size for sorting — lines are the primary dimension
        return (span.EndLine - span.StartLine) * 10000 + (span.EndColumn - span.StartColumn);
    }

    /// <summary>Returns <see langword="true"/> when two source spans have identical start and end positions.</summary>
    private static bool SpansEqual(SourceSpan a, SourceSpan b) =>
        a.StartLine == b.StartLine && a.StartColumn == b.StartColumn &&
        a.EndLine == b.EndLine && a.EndColumn == b.EndColumn;

}
