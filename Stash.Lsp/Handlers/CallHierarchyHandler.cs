namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Lsp.Analysis.SymbolKind;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

public class CallHierarchyHandler : CallHierarchyHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public CallHierarchyHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<Container<CallHierarchyItem>?> Handle(CallHierarchyPrepareParams request, CancellationToken cancellationToken)
    {
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
        return Task.FromResult<Container<CallHierarchyItem>?>(new Container<CallHierarchyItem>(item));
    }

    public override Task<Container<CallHierarchyIncomingCall>?> Handle(CallHierarchyIncomingCallsParams request, CancellationToken cancellationToken)
    {
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

        return Task.FromResult<Container<CallHierarchyIncomingCall>?>(new Container<CallHierarchyIncomingCall>(incomingCalls));
    }

    public override Task<Container<CallHierarchyOutgoingCall>?> Handle(CallHierarchyOutgoingCallsParams request, CancellationToken cancellationToken)
    {
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

        return Task.FromResult<Container<CallHierarchyOutgoingCall>?>(new Container<CallHierarchyOutgoingCall>(outgoingCalls));
    }

    protected override CallHierarchyRegistrationOptions CreateRegistrationOptions(
        CallHierarchyCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

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
