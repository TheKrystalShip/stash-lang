namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Common;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Parsing.AST;

/// <summary>
/// Handles LSP <c>textDocument/foldingRange</c> requests to provide code folding regions
/// for Stash documents.
/// </summary>
/// <remarks>
/// <para>
/// Folding ranges are produced from two sources:
/// </para>
/// <list type="bullet">
///   <item>
///     <term>AST-based regions</term>
///     <description>
///       The handler walks the statement tree from the <see cref="AnalysisEngine"/> cached
///       result, emitting a <see cref="FoldingRangeKind.Region"/> for every multi-line block —
///       function bodies, struct/enum declarations, if/else branches, loops, and explicit
///       block statements.
///     </description>
///   </item>
///   <item>
///     <term>Comment regions</term>
///     <description>
///       The raw document text from <see cref="DocumentManager"/> is scanned for block
///       comments (<c>/* … */</c>) and runs of two or more consecutive line comments
///       (<c>// …</c>), which are folded as <see cref="FoldingRangeKind.Comment"/>.
///     </description>
///   </item>
/// </list>
/// </remarks>
public class FoldingRangeHandler : FoldingRangeHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;
    private readonly ILogger<FoldingRangeHandler> _logger;

    /// <summary>
    /// Initialises the handler with the required analysis engine and document manager.
    /// </summary>
    /// <param name="analysis">The analysis engine that supplies cached per-document results.</param>
    /// <param name="documents">The document manager used to retrieve raw document text for comment scanning.</param>
    public FoldingRangeHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<FoldingRangeHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Creates the registration options restricting this handler to Stash language documents.
    /// </summary>
    /// <param name="capability">The client's folding range capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
        FoldingRangeCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    /// <summary>
    /// Processes the folding range request and returns all foldable regions for the document.
    /// </summary>
    /// <param name="request">The request containing the document URI.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="Container{FoldingRange}"/> combining AST-based and comment-based ranges,
    /// or <see langword="null"/> if no cached analysis is available.
    /// </returns>
    public override Task<Container<FoldingRange>?> Handle(FoldingRangeRequestParam request,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FoldingRange request for {Uri}", request.TextDocument.Uri);
        var uri = request.TextDocument.Uri.ToUri();
        var result = _analysis.GetCachedResult(uri);
        if (result == null)
        {
            return Task.FromResult<Container<FoldingRange>?>(null);
        }

        var ranges = new List<FoldingRange>();

        // Walk AST for block-based folding
        foreach (var stmt in result.Statements)
        {
            CollectFoldingRanges(stmt, ranges);
        }

        // Scan source text for comment regions
        var text = _documents.GetText(uri);
        if (text != null)
        {
            CollectCommentRanges(text, ranges);
        }

        _logger.LogDebug("FoldingRange: {Count} ranges for {Uri}", ranges.Count, request.TextDocument.Uri);
        return Task.FromResult<Container<FoldingRange>?>(new Container<FoldingRange>(ranges));
    }

    /// <summary>
    /// Recursively walks a statement node and appends a <see cref="FoldingRange"/> for every
    /// multi-line block it encounters.
    /// </summary>
    /// <param name="stmt">The statement to walk.</param>
    /// <param name="ranges">The accumulator list that receives discovered ranges.</param>
    private void CollectFoldingRanges(Stmt stmt, List<FoldingRange> ranges)
    {
        switch (stmt)
        {
            case FnDeclStmt fn:
                AddRegion(ranges, fn.Span, FoldingRangeKind.Region);
                CollectFoldingRanges(fn.Body, ranges);
                break;

            case StructDeclStmt structDecl:
                AddRegion(ranges, structDecl.Span, FoldingRangeKind.Region);
                break;

            case EnumDeclStmt enumDecl:
                AddRegion(ranges, enumDecl.Span, FoldingRangeKind.Region);
                break;

            case IfStmt ifStmt:
                if (ifStmt.ThenBranch is BlockStmt thenBlock)
                {
                    AddRegion(ranges, thenBlock.Span, FoldingRangeKind.Region);
                    foreach (var s in thenBlock.Statements)
                    {
                        CollectFoldingRanges(s, ranges);
                    }
                }
                else
                {
                    CollectFoldingRanges(ifStmt.ThenBranch, ranges);
                }

                if (ifStmt.ElseBranch != null)
                {
                    if (ifStmt.ElseBranch is BlockStmt elseBlock)
                    {
                        AddRegion(ranges, elseBlock.Span, FoldingRangeKind.Region);
                        foreach (var s in elseBlock.Statements)
                        {
                            CollectFoldingRanges(s, ranges);
                        }
                    }
                    else
                    {
                        CollectFoldingRanges(ifStmt.ElseBranch, ranges);
                    }
                }
                break;

            case WhileStmt whileStmt:
                AddRegion(ranges, whileStmt.Span, FoldingRangeKind.Region);
                CollectFoldingRanges(whileStmt.Body, ranges);
                break;

            case DoWhileStmt doWhileStmt:
                AddRegion(ranges, doWhileStmt.Span, FoldingRangeKind.Region);
                CollectFoldingRanges(doWhileStmt.Body, ranges);
                break;

            case ForInStmt forInStmt:
                AddRegion(ranges, forInStmt.Span, FoldingRangeKind.Region);
                CollectFoldingRanges(forInStmt.Body, ranges);
                break;

            case BlockStmt block:
                AddRegion(ranges, block.Span, FoldingRangeKind.Region);
                foreach (var s in block.Statements)
                {
                    CollectFoldingRanges(s, ranges);
                }

                break;

            default:
                // VarDecl, ConstDecl, Return, Break, Continue, ExprStmt, etc. are not foldable
                break;
        }
    }

    /// <summary>
    /// Adds a <see cref="FoldingRange"/> for a source span if the span covers more than one line.
    /// </summary>
    /// <param name="ranges">The list to append the range to.</param>
    /// <param name="span">The 1-based source span to convert.</param>
    /// <param name="kind">The folding kind (<c>Region</c> or <c>Comment</c>).</param>
    private static void AddRegion(List<FoldingRange> ranges, SourceSpan span, FoldingRangeKind kind)
    {
        // Only fold multi-line regions
        if (span.EndLine <= span.StartLine)
        {
            return;
        }

        ranges.Add(new FoldingRange
        {
            StartLine = span.StartLine - 1,     // Convert to 0-based
            StartCharacter = span.StartColumn - 1,
            EndLine = span.EndLine - 1,
            EndCharacter = span.EndColumn - 1,
            Kind = kind
        });
    }

    /// <summary>
    /// Scans the raw document text for block comments and runs of consecutive line comments,
    /// appending a <see cref="FoldingRangeKind.Comment"/> range for each one.
    /// </summary>
    /// <param name="text">The full document text to scan.</param>
    /// <param name="ranges">The accumulator list that receives discovered comment ranges.</param>
    private static void CollectCommentRanges(string text, List<FoldingRange> ranges)
    {
        var lines = text.Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();

            // Block comments: /* ... */
            if (trimmed.StartsWith("/*"))
            {
                int startLine = i;
                while (i < lines.Length && !lines[i].Contains("*/"))
                {
                    i++;
                }

                if (i > startLine)
                {
                    ranges.Add(new FoldingRange
                    {
                        StartLine = startLine,
                        EndLine = i,
                        Kind = FoldingRangeKind.Comment
                    });
                }
                i++;
                continue;
            }

            // Consecutive line comments: // ...
            if (trimmed.StartsWith("//"))
            {
                int startLine = i;
                while (i < lines.Length && lines[i].TrimStart().StartsWith("//"))
                {
                    i++;
                }

                if (i - startLine > 1) // Only fold 2+ consecutive line comments
                {
                    ranges.Add(new FoldingRange
                    {
                        StartLine = startLine,
                        EndLine = i - 1,
                        Kind = FoldingRangeKind.Comment
                    });
                }
                continue;
            }

            i++;
        }
    }
}
