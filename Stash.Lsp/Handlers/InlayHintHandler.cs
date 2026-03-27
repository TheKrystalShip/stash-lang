namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Analysis.SymbolKind;
using Stash.Parsing.AST;

/// <summary>
/// Handles LSP <c>textDocument/inlayHint</c> requests to display inline parameter name hints
/// at function call sites.
/// </summary>
/// <remarks>
/// <para>
/// For each call expression in the document's AST the handler looks up the target function's
/// parameter names — first in <see cref="BuiltInRegistry"/> (built-in and namespace functions),
/// then in the <see cref="AnalysisEngine"/> symbol table for user-defined functions.  A
/// <see cref="InlayHintKind.Parameter"/> hint labelled <c>&lt;name&gt;:</c> is inserted before
/// each argument whose name differs from the corresponding parameter name.
/// </para>
/// <para>
/// Hint display can be toggled via <see cref="LspSettings.InlayHintsEnabled"/>.
/// </para>
/// </remarks>
public class InlayHintHandler : InlayHintsHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly LspSettings _settings;
    private readonly ILogger<InlayHintHandler> _logger;

    /// <summary>
    /// Initialises the handler with the required analysis engine, settings, and logger.
    /// </summary>
    /// <param name="analysis">The analysis engine that supplies cached per-document results.</param>
    /// <param name="settings">LSP settings used to check whether inlay hints are enabled.</param>
    /// <param name="logger">Logger for debug diagnostics.</param>
    public InlayHintHandler(AnalysisEngine analysis, LspSettings settings, ILogger<InlayHintHandler> logger)
    {
        _analysis = analysis;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Processes the inlay hint request and returns parameter name hints for the document.
    /// </summary>
    /// <param name="request">The request containing the document URI and visible range.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// An <see cref="InlayHintContainer"/> with one hint per argument that requires a label,
    /// or <see langword="null"/> if inlay hints are disabled or no analysis is available.
    /// </returns>
    public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken cancellationToken)
    {
        if (!_settings.InlayHintsEnabled)
        {
            return Task.FromResult<InlayHintContainer?>(null);
        }

        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
        {
            return Task.FromResult<InlayHintContainer?>(null);
        }

        var hints = new List<InlayHint>();

        foreach (var stmt in result.Statements)
        {
            CollectFromStmt(stmt, hints, result);
        }

        _logger.LogDebug("InlayHints: {Count} hints for {Uri}", hints.Count, request.TextDocument.Uri);
        return Task.FromResult<InlayHintContainer?>(hints.Count == 0 ? null : new InlayHintContainer(hints));
    }

    /// <summary>
    /// Resolve pass-through: returns the <see cref="InlayHint"/> unchanged.
    /// </summary>
    /// <param name="request">The inlay hint item to resolve.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The same <see cref="InlayHint"/> as received.</returns>
    public override Task<InlayHint> Handle(InlayHint request, CancellationToken cancellationToken)
        => Task.FromResult(request);

    /// <summary>
    /// Recursively walks a statement node and delegates call expression processing to
    /// <see cref="CollectFromExpr"/>.
    /// </summary>
    /// <param name="stmt">The statement node to walk.</param>
    /// <param name="hints">The accumulator list for discovered hints.</param>
    /// <param name="result">The cached analysis result for symbol lookup.</param>
    private void CollectFromStmt(Stmt stmt, List<InlayHint> hints, AnalysisResult result)
    {
        switch (stmt)
        {
            case ExprStmt exprStmt:
                CollectFromExpr(exprStmt.Expression, hints, result);
                break;
            case VarDeclStmt varDecl when varDecl.Initializer != null:
                CollectFromExpr(varDecl.Initializer, hints, result);
                break;
            case ConstDeclStmt constDecl:
                CollectFromExpr(constDecl.Initializer, hints, result);
                break;
            case ReturnStmt ret when ret.Value != null:
                CollectFromExpr(ret.Value, hints, result);
                break;
            case FnDeclStmt fnDecl:
                CollectFromStmt(fnDecl.Body, hints, result);
                break;
            case IfStmt ifStmt:
                CollectFromExpr(ifStmt.Condition, hints, result);
                CollectFromStmt(ifStmt.ThenBranch, hints, result);
                if (ifStmt.ElseBranch != null)
                {
                    CollectFromStmt(ifStmt.ElseBranch, hints, result);
                }

                break;
            case WhileStmt whileStmt:
                CollectFromExpr(whileStmt.Condition, hints, result);
                CollectFromStmt(whileStmt.Body, hints, result);
                break;
            case ForInStmt forIn:
                CollectFromExpr(forIn.Iterable, hints, result);
                CollectFromStmt(forIn.Body, hints, result);
                break;
            case BlockStmt block:
                foreach (var s in block.Statements)
                {
                    CollectFromStmt(s, hints, result);
                }

                break;
        }
    }

    /// <summary>
    /// Recursively walks an expression node to find call expressions and collect parameter hints.
    /// </summary>
    /// <param name="expr">The expression node to walk.</param>
    /// <param name="hints">The accumulator list for discovered hints.</param>
    /// <param name="result">The cached analysis result for symbol lookup.</param>
    private void CollectFromExpr(Expr expr, List<InlayHint> hints, AnalysisResult result)
    {
        switch (expr)
        {
            case CallExpr call:
                ProcessCallExpr(call, hints, result);
                CollectFromExpr(call.Callee, hints, result);
                foreach (var arg in call.Arguments)
                {
                    CollectFromExpr(arg, hints, result);
                }

                break;
            case BinaryExpr binary:
                CollectFromExpr(binary.Left, hints, result);
                CollectFromExpr(binary.Right, hints, result);
                break;
            case UnaryExpr unary:
                CollectFromExpr(unary.Right, hints, result);
                break;
            case GroupingExpr grouping:
                CollectFromExpr(grouping.Expression, hints, result);
                break;
            case AssignExpr assign:
                CollectFromExpr(assign.Value, hints, result);
                break;
            case DotExpr dot:
                CollectFromExpr(dot.Object, hints, result);
                break;
            case DotAssignExpr dotAssign:
                CollectFromExpr(dotAssign.Object, hints, result);
                CollectFromExpr(dotAssign.Value, hints, result);
                break;
            case IndexExpr index:
                CollectFromExpr(index.Object, hints, result);
                CollectFromExpr(index.Index, hints, result);
                break;
            case IndexAssignExpr indexAssign:
                CollectFromExpr(indexAssign.Object, hints, result);
                CollectFromExpr(indexAssign.Index, hints, result);
                CollectFromExpr(indexAssign.Value, hints, result);
                break;
            case ArrayExpr array:
                foreach (var element in array.Elements)
                {
                    CollectFromExpr(element, hints, result);
                }

                break;
            case TernaryExpr ternary:
                CollectFromExpr(ternary.Condition, hints, result);
                CollectFromExpr(ternary.ThenBranch, hints, result);
                CollectFromExpr(ternary.ElseBranch, hints, result);
                break;
            case TryExpr tryExpr:
                CollectFromExpr(tryExpr.Expression, hints, result);
                break;
            case InterpolatedStringExpr interpolated:
                foreach (var part in interpolated.Parts)
                {
                    CollectFromExpr(part, hints, result);
                }

                break;
            case PipeExpr pipe:
                CollectFromExpr(pipe.Left, hints, result);
                CollectFromExpr(pipe.Right, hints, result);
                break;
            case RedirectExpr redirect:
                CollectFromExpr(redirect.Expression, hints, result);
                CollectFromExpr(redirect.Target, hints, result);
                break;
            case NullCoalesceExpr nullCoalesce:
                CollectFromExpr(nullCoalesce.Left, hints, result);
                CollectFromExpr(nullCoalesce.Right, hints, result);
                break;
            case StructInitExpr structInit:
                foreach (var field in structInit.FieldValues)
                {
                    CollectFromExpr(field.Value, hints, result);
                }

                break;
            // IdentifierExpr, LiteralExpr, CommandExpr: no children to recurse
        }
    }

    /// <summary>
    /// Emits inlay hints for each argument of a call expression that differs from its matching parameter name.
    /// </summary>
    /// <param name="call">The call expression to process.</param>
    /// <param name="hints">The accumulator list for discovered hints.</param>
    /// <param name="result">The cached analysis result for symbol lookup.</param>
    private void ProcessCallExpr(CallExpr call, List<InlayHint> hints, AnalysisResult result)
    {
        if (call.Arguments.Count == 0)
        {
            return;
        }

        var funcName = GetFunctionName(call.Callee);
        if (funcName == null)
        {
            return;
        }

        string[]? paramNames = null;

        if (BuiltInRegistry.TryGetFunction(funcName, out var builtInFn))
        {
            paramNames = builtInFn.ParamNames;
        }
        else if (BuiltInRegistry.TryGetNamespaceFunction(funcName, out var nsFn))
        {
            paramNames = nsFn.ParamNames;
        }
        else
        {
            var callSpan = call.Span;
            var simpleName = funcName.Contains('.') ? funcName[(funcName.LastIndexOf('.') + 1)..] : funcName;
            var definition = result.Symbols.FindDefinition(simpleName, callSpan.StartLine, callSpan.StartColumn);
            if (definition != null && definition.Kind == StashSymbolKind.Function)
            {
                paramNames = definition.ParameterNames ?? ExtractParamNames(definition.Detail ?? "");
            }
        }

        if (paramNames == null || paramNames.Length == 0 || call.Arguments.Count > paramNames.Length)
        {
            return;
        }

        for (int i = 0; i < call.Arguments.Count; i++)
        {
            var arg = call.Arguments[i];
            var paramName = paramNames[i];

            if (arg is IdentifierExpr id && id.Name.Lexeme == paramName)
            {
                continue;
            }

            var argSpan = arg.Span;
            hints.Add(new InlayHint
            {
                Position = new Position(argSpan.StartLine - 1, argSpan.StartColumn - 1),
                Label = new StringOrInlayHintLabelParts($"{paramName}:"),
                Kind = InlayHintKind.Parameter,
            });
        }
    }

    /// <summary>
    /// Extracts the simple or qualified function name from a callee expression.
    /// </summary>
    /// <param name="callee">The callee expression (typically an <c>IdentifierExpr</c> or <c>DotExpr</c>).</param>
    /// <returns>
    /// The function name as a string (e.g., <c>"len"</c> or <c>"arr.map"</c>),
    /// or <see langword="null"/> if the expression is not a resolvable call target.
    /// </returns>
    private static string? GetFunctionName(Expr callee)
    {
        if (callee is IdentifierExpr id)
        {
            return id.Name.Lexeme;
        }

        if (callee is DotExpr dot && dot.Object is IdentifierExpr obj)
        {
            return $"{obj.Name.Lexeme}.{dot.Name.Lexeme}";
        }

        return null;
    }

    /// <summary>
    /// Parses parameter names from a function detail string such as <c>"fn foo(a: int, b = 5)"</c>.
    /// </summary>
    /// <param name="detail">The detail string from a <see cref="SymbolInfo"/>.</param>
    /// <returns>An array of parameter name strings, or an empty array if parsing fails.</returns>
    private static string[] ExtractParamNames(string detail)
    {
        var openParen = detail.IndexOf('(');
        var closeParen = detail.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen + 1)
        {
            return Array.Empty<string>();
        }

        var inside = detail[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(inside))
        {
            return Array.Empty<string>();
        }

        var parts = inside.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            // Strip type annotation (e.g., "a: int" → "a")
            var colonIdx = part.IndexOf(':');
            if (colonIdx >= 0)
            {
                part = part[..colonIdx].Trim();
            }
            else
            {
                // Strip default value (e.g., "b = 5" → "b")
                var equalsIdx = part.IndexOf('=');
                if (equalsIdx >= 0)
                {
                    part = part[..equalsIdx].Trim();
                }
            }
            parts[i] = part;
        }

        return parts;
    }

    /// <summary>
    /// Creates the registration options restricting this handler to Stash language documents.
    /// </summary>
    /// <param name="capability">The client's inlay hints capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents.</returns>
    protected override InlayHintRegistrationOptions CreateRegistrationOptions(
        InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
