namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Common;
using Stash.Lsp.Analysis;
using Stash.Parsing.AST;

public class FoldingRangeHandler : FoldingRangeHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public FoldingRangeHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
        FoldingRangeCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    public override Task<Container<FoldingRange>?> Handle(FoldingRangeRequestParam request,
        CancellationToken cancellationToken)
    {
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

        return Task.FromResult<Container<FoldingRange>?>(new Container<FoldingRange>(ranges));
    }

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
