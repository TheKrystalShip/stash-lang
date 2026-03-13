namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;
using Stash.Parsing.AST;

public class InlayHintHandler : InlayHintsHandlerBase
{
    private readonly AnalysisEngine _analysis;

    private static readonly Dictionary<string, string[]> _builtInParams = new()
    {
        ["typeof"] = new[] { "value" },
        ["len"] = new[] { "value" },
        ["lastError"] = Array.Empty<string>(),
        ["parseArgs"] = new[] { "argTree" },
    };

    private static readonly Dictionary<string, string[]> _namespacedParams = new()
    {
        ["io.println"] = new[] { "value" },
        ["io.print"] = new[] { "value" },
        ["conv.toStr"] = new[] { "value" },
        ["conv.toInt"] = new[] { "value" },
        ["conv.toFloat"] = new[] { "value" },
        ["env.get"] = new[] { "name" },
        ["env.set"] = new[] { "name", "value" },
        ["process.exit"] = new[] { "code" },
        ["fs.readFile"] = new[] { "path" },
        ["fs.writeFile"] = new[] { "path", "content" },
        ["fs.exists"] = new[] { "path" },
        ["fs.dirExists"] = new[] { "path" },
        ["fs.pathExists"] = new[] { "path" },
        ["fs.createDir"] = new[] { "path" },
        ["fs.delete"] = new[] { "path" },
        ["fs.copy"] = new[] { "src", "dst" },
        ["fs.move"] = new[] { "src", "dst" },
        ["fs.size"] = new[] { "path" },
        ["fs.listDir"] = new[] { "path" },
        ["fs.appendFile"] = new[] { "path", "content" },
        ["path.abs"] = new[] { "path" },
        ["path.dir"] = new[] { "path" },
        ["path.base"] = new[] { "path" },
        ["path.ext"] = new[] { "path" },
        ["path.join"] = new[] { "a", "b" },
        ["path.name"] = new[] { "path" },
    };

    public InlayHintHandler(AnalysisEngine analysis)
    {
        _analysis = analysis;
    }

    public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken cancellationToken)
    {
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result == null)
            return Task.FromResult<InlayHintContainer?>(null);

        var hints = new List<InlayHint>();

        foreach (var stmt in result.Statements)
            CollectFromStmt(stmt, hints, result);

        return Task.FromResult<InlayHintContainer?>(hints.Count == 0 ? null : new InlayHintContainer(hints));
    }

    public override Task<InlayHint> Handle(InlayHint request, CancellationToken cancellationToken)
        => Task.FromResult(request);

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
                    CollectFromStmt(ifStmt.ElseBranch, hints, result);
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
                    CollectFromStmt(s, hints, result);
                break;
        }
    }

    private void CollectFromExpr(Expr expr, List<InlayHint> hints, AnalysisResult result)
    {
        switch (expr)
        {
            case CallExpr call:
                ProcessCallExpr(call, hints, result);
                CollectFromExpr(call.Callee, hints, result);
                foreach (var arg in call.Arguments)
                    CollectFromExpr(arg, hints, result);
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
                    CollectFromExpr(element, hints, result);
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
                    CollectFromExpr(part, hints, result);
                break;
            case PipeExpr pipe:
                CollectFromExpr(pipe.Left, hints, result);
                CollectFromExpr(pipe.Right, hints, result);
                break;
            case NullCoalesceExpr nullCoalesce:
                CollectFromExpr(nullCoalesce.Left, hints, result);
                CollectFromExpr(nullCoalesce.Right, hints, result);
                break;
            case StructInitExpr structInit:
                foreach (var field in structInit.FieldValues)
                    CollectFromExpr(field.Value, hints, result);
                break;
            // IdentifierExpr, LiteralExpr, CommandExpr: no children to recurse
        }
    }

    private void ProcessCallExpr(CallExpr call, List<InlayHint> hints, AnalysisResult result)
    {
        if (call.Arguments.Count == 0)
            return;

        var funcName = GetFunctionName(call.Callee);
        if (funcName == null)
            return;

        string[]? paramNames = null;

        if (_builtInParams.TryGetValue(funcName, out var builtIn))
            paramNames = builtIn;
        else if (_namespacedParams.TryGetValue(funcName, out var nsBuiltIn))
            paramNames = nsBuiltIn;
        else
        {
            var callSpan = call.Span;
            var simpleName = funcName.Contains('.') ? funcName[(funcName.LastIndexOf('.') + 1)..] : funcName;
            var definition = result.Symbols.FindDefinition(simpleName, callSpan.StartLine, callSpan.StartColumn);
            if (definition != null && definition.Kind == Analysis.SymbolKind.Function && definition.Detail != null)
                paramNames = ExtractParamNames(definition.Detail);
        }

        if (paramNames == null || paramNames.Length == 0 || paramNames.Length != call.Arguments.Count)
            return;

        for (int i = 0; i < call.Arguments.Count; i++)
        {
            var arg = call.Arguments[i];
            var paramName = paramNames[i];

            if (arg is IdentifierExpr id && id.Name.Lexeme == paramName)
                continue;

            var argSpan = arg.Span;
            hints.Add(new InlayHint
            {
                Position = new Position(argSpan.StartLine - 1, argSpan.StartColumn - 1),
                Label = new StringOrInlayHintLabelParts($"{paramName}:"),
                Kind = InlayHintKind.Parameter,
            });
        }
    }

    private static string? GetFunctionName(Expr callee)
    {
        if (callee is IdentifierExpr id)
            return id.Name.Lexeme;
        if (callee is DotExpr dot && dot.Object is IdentifierExpr obj)
            return $"{obj.Name.Lexeme}.{dot.Name.Lexeme}";
        return null;
    }

    private static string[] ExtractParamNames(string detail)
    {
        var openParen = detail.IndexOf('(');
        var closeParen = detail.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen + 1)
            return Array.Empty<string>();

        var inside = detail[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(inside))
            return Array.Empty<string>();

        var parts = inside.Split(',');
        for (int i = 0; i < parts.Length; i++)
            parts[i] = parts[i].Trim();

        return parts;
    }

    protected override InlayHintRegistrationOptions CreateRegistrationOptions(
        InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };
}
