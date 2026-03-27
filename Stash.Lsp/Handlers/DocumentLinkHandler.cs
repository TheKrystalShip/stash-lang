namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Common;
using Stash.Lexing;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Parsing.AST;

/// <summary>
/// Handles LSP <c>textDocument/documentLink</c> requests to make import path strings
/// clickable hyperlinks in editors.
/// </summary>
/// <remarks>
/// <para>
/// Walks the top-level statement list from the <see cref="AnalysisEngine"/> cached result,
/// locates all <c>import</c> and <c>import … as</c> statements, and produces a
/// <see cref="DocumentLink"/> for each import path token.
/// </para>
/// <para>
/// Relative and bare specifier paths are resolved against the document's directory using
/// <c>ModuleResolver</c>.  When a resolved file exists on disk the link's
/// <see cref="DocumentLink.Target"/> is set to a <c>file://</c> URI; otherwise the tooltip
/// shows the raw import string.
/// </para>
/// </remarks>
public class DocumentLinkHandler : DocumentLinkHandlerBase
{
    private readonly AnalysisEngine _analysis;

    private readonly ILogger<DocumentLinkHandler> _logger;

    /// <summary>
    /// Initialises the handler with the analysis engine used to retrieve cached document results.
    /// </summary>
    /// <param name="analysis">The analysis engine that supplies cached per-document results.</param>
    public DocumentLinkHandler(AnalysisEngine analysis, ILogger<DocumentLinkHandler> logger)
    {
        _analysis = analysis;
        _logger = logger;
    }

    /// <summary>
    /// Creates the registration options for document link support.
    /// </summary>
    /// <param name="capability">The client's document link capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents without a resolve provider.</returns>
    protected override DocumentLinkRegistrationOptions CreateRegistrationOptions(
        DocumentLinkCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            ResolveProvider = false
        };

    /// <summary>
    /// Resolve pass-through: returns the <see cref="DocumentLink"/> unchanged.
    /// </summary>
    /// <param name="request">The document link item to resolve.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The same <see cref="DocumentLink"/> as received.</returns>
    public override Task<DocumentLink> Handle(DocumentLink request, CancellationToken cancellationToken) =>
        Task.FromResult(request);

    /// <summary>
    /// Processes the document link request and returns a link for each import statement in the document.
    /// </summary>
    /// <param name="request">The request containing the document URI.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="DocumentLinkContainer"/> with one link per import statement,
    /// or <see langword="null"/> if no cached analysis is available.
    /// </returns>
    public override Task<DocumentLinkContainer?> Handle(DocumentLinkParams request,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("DocumentLink request for {Uri}", request.TextDocument.Uri);
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

        _logger.LogDebug("DocumentLink: {Count} links for {Uri}", links.Count, request.TextDocument.Uri);
        return Task.FromResult<DocumentLinkContainer?>(new DocumentLinkContainer(links));
    }

    /// <summary>
    /// Constructs a <see cref="DocumentLink"/> for a single import path token, resolving the path
    /// to a <c>file://</c> URI when possible.
    /// </summary>
    /// <param name="links">The accumulator list to append the link to.</param>
    /// <param name="pathToken">The string literal token containing the import path.</param>
    /// <param name="documentDir">
    /// The directory of the importing document, used as the base for relative path resolution.
    /// May be <see langword="null"/> for non-file URIs.
    /// </param>
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
