namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Analysis.SymbolKind;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

/// <summary>
/// Handles the three-step LSP call hierarchy protocol:
/// <c>textDocument/prepareCallHierarchy</c>,
/// <c>callHierarchy/incomingCalls</c>, and
/// <c>callHierarchy/outgoingCalls</c>.
/// </summary>
/// <remarks>
/// <para>
/// The protocol works in three steps:
/// </para>
/// <list type="number">
///   <item>
///     <term>Prepare (<c>prepareCallHierarchy</c>)</term>
///     <description>
///       Resolves the symbol at the cursor position via <see cref="AnalysisEngine.GetContextAt"/>
///       and returns a <see cref="CallHierarchyItem"/> if the symbol is a function.
///     </description>
///   </item>
///   <item>
///     <term>Incoming calls (<c>callHierarchy/incomingCalls</c>)</term>
///     <description>
///       Iterates over all open documents, finds <see cref="ReferenceKind.Call"/> references
///       to the target function name, groups them by their enclosing function (nearest
///       containing symbol with a <c>FullSpan</c>), and returns one
///       <see cref="CallHierarchyIncomingCall"/> per caller.
///     </description>
///   </item>
///   <item>
///     <term>Outgoing calls (<c>callHierarchy/outgoingCalls</c>)</term>
///     <description>
///       Locates the source function in the cached analysis, collects all
///       <see cref="ReferenceKind.Call"/> references whose positions fall inside the function's
///       <c>FullSpan</c>, groups them by callee name, and returns one
///       <see cref="CallHierarchyOutgoingCall"/> per callee.
///     </description>
///   </item>
/// </list>
/// </remarks>
public class CallHierarchyHandler : CallHierarchyHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;
    private readonly ILogger<CallHierarchyHandler> _logger;

    /// <summary>
    /// Initialises the handler with the required analysis engine and document manager.
    /// </summary>
    /// <param name="analysis">The analysis engine that supplies cached results and context resolution.</param>
    /// <param name="documents">The document manager used to enumerate open documents and retrieve text.</param>
    public CallHierarchyHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<CallHierarchyHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the prepare request and returns a <see cref="CallHierarchyItem"/> for the function at the cursor.
    /// </summary>
    /// <param name="request">The request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A single-item container with the resolved function's hierarchy descriptor, or
    /// <see langword="null"/> if the cursor is not over a function symbol.
    /// </returns>
    public override Task<Container<CallHierarchyItem>?> Handle(CallHierarchyPrepareParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("CallHierarchy prepare at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<Container<CallHierarchyItem>?>(null);
        }

        var (result, word) = ctx.Value;
        var line = (int)request.Position.Line + 1;
        var col = (int)request.Position.Character + 1;

        var symbol = result.Symbols.FindDefinition(word, line, col);
        if (symbol == null || symbol.Kind != StashSymbolKind.Function)
        {
            return Task.FromResult<Container<CallHierarchyItem>?>(null);
        }

        var item = BuildItem(symbol, request.TextDocument.Uri);
        _logger.LogDebug("CallHierarchy prepare: resolved {Name} at {Uri}", word, request.TextDocument.Uri);
        return Task.FromResult<Container<CallHierarchyItem>?>(new Container<CallHierarchyItem>(item));
    }

    /// <summary>
    /// Processes the incoming calls request and returns all callers of the specified function across open documents.
    /// </summary>
    /// <param name="request">The request identifying the target function via a <see cref="CallHierarchyItem"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A container of <see cref="CallHierarchyIncomingCall"/> entries, one per enclosing caller function,
    /// each with the specific call site ranges.
    /// </returns>
    public override Task<Container<CallHierarchyIncomingCall>?> Handle(CallHierarchyIncomingCallsParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("CallHierarchy incoming calls for {Name}", request.Item.Name);
        var targetName = request.Item.Name;
        var incomingCalls = new List<CallHierarchyIncomingCall>();

        foreach (var docUri in _documents.GetOpenDocumentUris())
        {
            var docResult = _analysis.GetCachedResult(docUri);
            if (docResult == null)
            {
                continue;
            }

            // Find all Call references to the target function in this document
            var callSites = docResult.Symbols.References
                .Where(r => r.Kind == ReferenceKind.Call && r.Name == targetName)
                .ToList();

            if (callSites.Count == 0)
            {
                continue;
            }

            // Group call sites by their enclosing function
            var allSymbols = docResult.Symbols.All;
            var callerGroups = new Dictionary<SymbolInfo, List<ReferenceInfo>>();

            foreach (var callSite in callSites)
            {
                var enclosingFn = FindEnclosingFunction(allSymbols, callSite.Span.StartLine, callSite.Span.StartColumn);
                if (enclosingFn == null)
                {
                    continue;
                }

                if (!callerGroups.TryGetValue(enclosingFn, out var siteList))
                {
                    siteList = new List<ReferenceInfo>();
                    callerGroups[enclosingFn] = siteList;
                }
                siteList.Add(callSite);
            }

            var docDocumentUri = DocumentUri.From(docUri);
            foreach (var (callerSymbol, sites) in callerGroups)
            {
                var fromRanges = sites.Select(s => s.Span.ToLspRange()).ToArray();
                incomingCalls.Add(new CallHierarchyIncomingCall
                {
                    From = BuildItem(callerSymbol, docDocumentUri),
                    FromRanges = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Range>(fromRanges)
                });
            }
        }

        _logger.LogDebug("CallHierarchy incoming: {Count} callers for {Name}", incomingCalls.Count, request.Item.Name);
        return Task.FromResult<Container<CallHierarchyIncomingCall>?>(new Container<CallHierarchyIncomingCall>(incomingCalls));
    }

    /// <summary>
    /// Processes the outgoing calls request and returns all functions called within the specified function's body.
    /// </summary>
    /// <param name="request">The request identifying the source function via a <see cref="CallHierarchyItem"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A container of <see cref="CallHierarchyOutgoingCall"/> entries, one per distinct callee function,
    /// each with the call site ranges within the source function, or <see langword="null"/> if the
    /// source function cannot be resolved.
    /// </returns>
    public override Task<Container<CallHierarchyOutgoingCall>?> Handle(CallHierarchyOutgoingCallsParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("CallHierarchy outgoing calls for {Name}", request.Item.Name);
        var sourceName = request.Item.Name;
        var sourceUri = request.Item.Uri.ToUri();

        var sourceResult = _analysis.GetCachedResult(sourceUri);
        if (sourceResult == null)
        {
            return Task.FromResult<Container<CallHierarchyOutgoingCall>?>(null);
        }

        // Find the source function symbol
        var allSymbols = sourceResult.Symbols.All;
        var sourceSymbol = allSymbols.FirstOrDefault(s => s.Kind == StashSymbolKind.Function && s.Name == sourceName);
        if (sourceSymbol?.FullSpan == null)
        {
            return Task.FromResult<Container<CallHierarchyOutgoingCall>?>(null);
        }

        var bodySpan = sourceSymbol.FullSpan;

        // Find all Call references within the function body
        var outgoingCallSites = sourceResult.Symbols.References
            .Where(r => r.Kind == ReferenceKind.Call && IsInsideSpan(bodySpan, r.Span.StartLine, r.Span.StartColumn))
            .ToList();

        // Group by callee name
        var calleeGroups = new Dictionary<string, List<ReferenceInfo>>();
        foreach (var callSite in outgoingCallSites)
        {
            if (!calleeGroups.TryGetValue(callSite.Name, out var siteList))
            {
                siteList = new List<ReferenceInfo>();
                calleeGroups[callSite.Name] = siteList;
            }
            siteList.Add(callSite);
        }

        var outgoingCalls = new List<CallHierarchyOutgoingCall>();
        var sourceDocUri = DocumentUri.From(sourceUri);

        foreach (var (calleeName, sites) in calleeGroups)
        {
            // Try to resolve the callee definition using the first call site
            var firstSite = sites[0];
            SymbolInfo? calleeSymbol = null;

            if (firstSite.ResolvedSymbol?.Kind == StashSymbolKind.Function)
            {
                calleeSymbol = firstSite.ResolvedSymbol;
            }
            else
            {
                calleeSymbol = allSymbols.FirstOrDefault(s => s.Kind == StashSymbolKind.Function && s.Name == calleeName);
            }

            if (calleeSymbol == null)
            {
                continue;
            }

            var fromRanges = sites.Select(s => s.Span.ToLspRange()).ToArray();
            outgoingCalls.Add(new CallHierarchyOutgoingCall
            {
                To = BuildItem(calleeSymbol, sourceDocUri),
                FromRanges = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Range>(fromRanges)
            });
        }

        _logger.LogDebug("CallHierarchy outgoing: {Count} callees for {Name}", outgoingCalls.Count, request.Item.Name);
        return Task.FromResult<Container<CallHierarchyOutgoingCall>?>(new Container<CallHierarchyOutgoingCall>(outgoingCalls));
    }

    /// <summary>
    /// Creates the registration options restricting this handler to Stash language documents.
    /// </summary>
    /// <param name="capability">The client's call hierarchy capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override CallHierarchyRegistrationOptions CreateRegistrationOptions(
        CallHierarchyCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    /// <summary>
    /// Constructs a <see cref="CallHierarchyItem"/> from a symbol and its containing document URI.
    /// </summary>
    /// <param name="symbol">The function symbol to represent.</param>
    /// <param name="uri">The document URI where the symbol is defined.</param>
    /// <returns>A <see cref="CallHierarchyItem"/> with name, range, and selection range populated.</returns>
    private static CallHierarchyItem BuildItem(SymbolInfo symbol, DocumentUri uri) =>
        new()
        {
            Name = symbol.Name,
            Kind = LspSymbolKind.Function,
            Uri = uri,
            Range = (symbol.FullSpan ?? symbol.Span).ToLspRange(),
            SelectionRange = symbol.Span.ToLspRange(),
            Detail = symbol.Detail
        };

    /// <summary>
    /// Finds the innermost function symbol whose <c>FullSpan</c> contains the given position.
    /// </summary>
    /// <param name="symbols">All symbols in the document.</param>
    /// <param name="line">1-based line number to test.</param>
    /// <param name="col">1-based column number to test.</param>
    /// <returns>The smallest enclosing function symbol, or <see langword="null"/> if none is found.</returns>
    private static SymbolInfo? FindEnclosingFunction(IReadOnlyList<SymbolInfo> symbols, int line, int col)
    {
        SymbolInfo? best = null;
        int bestSize = int.MaxValue;

        foreach (var sym in symbols)
        {
            if (sym.Kind != StashSymbolKind.Function || sym.FullSpan == null)
            {
                continue;
            }

            var span = sym.FullSpan;
            if (!IsInsideSpan(span, line, col))
            {
                continue;
            }

            // Prefer the smallest enclosing function (innermost)
            int size = (span.EndLine - span.StartLine) * 10000 + (span.EndColumn - span.StartColumn);
            if (size < bestSize)
            {
                bestSize = size;
                best = sym;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the given 1-based position falls within <paramref name="span"/>.
    /// </summary>
    /// <param name="span">The nullable source span to test; returns <see langword="false"/> when <see langword="null"/>.</param>
    /// <param name="line">1-based line number.</param>
    /// <param name="col">1-based column number.</param>
    private static bool IsInsideSpan(Stash.Common.SourceSpan? span, int line, int col)
    {
        if (span == null)
        {
            return false;
        }

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
}
