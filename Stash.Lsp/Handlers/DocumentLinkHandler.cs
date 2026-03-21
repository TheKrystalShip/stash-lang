namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Common;
using Stash.Lexing;
using Stash.Lsp.Analysis;
using Stash.Parsing.AST;

public class DocumentLinkHandler : DocumentLinkHandlerBase
{
    private readonly AnalysisEngine _analysis;

    public DocumentLinkHandler(AnalysisEngine analysis)
    {
        _analysis = analysis;
    }

    protected override DocumentLinkRegistrationOptions CreateRegistrationOptions(
        DocumentLinkCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            ResolveProvider = false
        };

    public override Task<DocumentLink> Handle(DocumentLink request, CancellationToken cancellationToken) =>
        Task.FromResult(request);

    public override Task<DocumentLinkContainer?> Handle(DocumentLinkParams request,
        CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var result = _analysis.GetCachedResult(uri);
        if (result == null)
        {
            return Task.FromResult<DocumentLinkContainer?>(null);
        }

        var links = new List<DocumentLink>();
        var documentDir = uri.IsFile ? Path.GetDirectoryName(uri.LocalPath) : null;

        foreach (var stmt in result.Statements)
        {
            switch (stmt)
            {
                case ImportStmt importStmt:
                    AddLink(links, importStmt.Path, documentDir);
                    break;
                case ImportAsStmt importAsStmt:
                    AddLink(links, importAsStmt.Path, documentDir);
                    break;
            }
        }

        return Task.FromResult<DocumentLinkContainer?>(new DocumentLinkContainer(links));
    }

    private static void AddLink(List<DocumentLink> links, Token pathToken, string? documentDir)
    {
        // The path token's Literal is the string value (without quotes)
        var importPath = pathToken.Literal as string;
        if (string.IsNullOrEmpty(importPath))
        {
            return;
        }

        // Resolve relative to the document's directory
        string? resolvedPath = null;
        if (documentDir != null)
        {
            if (ModuleResolver.IsBareSpecifier(importPath))
            {
                var fullPath = Path.GetFullPath(importPath, documentDir);
                if (File.Exists(fullPath))
                {
                    resolvedPath = fullPath;
                }
                else if (!Path.HasExtension(importPath))
                {
                    resolvedPath = ModuleResolver.ResolvePackageImport(importPath, documentDir);
                }
            }
            else
            {
                var fullPath = Path.GetFullPath(importPath, documentDir);
                if (File.Exists(fullPath))
                {
                    resolvedPath = fullPath;
                }
            }
        }

        // The token span covers the string literal including quotes; map to 0-based LSP positions
        var span = pathToken.Span;
        var range = span.ToLspRange();

        var link = new DocumentLink
        {
            Range = range,
            Tooltip = importPath
        };

        if (resolvedPath != null)
        {
            link = link with { Target = new Uri($"file://{resolvedPath}") };
        }

        links.Add(link);
    }
}
