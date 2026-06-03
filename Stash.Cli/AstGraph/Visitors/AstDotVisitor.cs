using System.Collections.Generic;
using System.Text;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;

namespace Stash.Cli.AstGraph.Visitors;

/// <summary>
/// Walks the Stash AST and emits a Graphviz DOT graph.
/// Implements both <see cref="IExprVisitor{T}"/> and <see cref="IStmtVisitor{T}"/>
/// where <c>T</c> is the DOT node ID string.
/// </summary>
internal sealed class AstDotVisitor : IExprVisitor<string>, IStmtVisitor<string>
{
    private readonly StringBuilder _dot = new();
    private readonly bool _includeSemantic;
    private int _counter;

    public AstDotVisitor(bool includeSemantic)
    {
        _includeSemantic = includeSemantic;
    }

    /// <summary>
    /// Generates a complete DOT graph from a list of top-level statements.
    /// </summary>
    public string Generate(List<Stmt> statements, string fileName)
    {
        _dot.Clear();
        _counter = 0;

        _dot.AppendLine("digraph AST {");
        _dot.AppendLine("  rankdir=TB;");
        _dot.AppendLine("  node [shape=box, fontname=\"monospace\", fontsize=10];");
        _dot.AppendLine("  edge [fontsize=8];");
        _dot.AppendLine();

        var programId = NextId("program");
        _dot.AppendLine($"  {programId} [label=\"Program\\n{Escape(fileName)}\"];");

        foreach (var stmt in statements)
        {
            var childId = stmt.Accept(this);
            _dot.AppendLine($"  {programId} -> {childId};");
        }

        _dot.AppendLine("}");
        return _dot.ToString();
    }

    private string NextId(string? prefix = null)
    {
        int id = _counter++;
        return prefix is not null ? $"{prefix}_{id}" : $"n{id}";
    }

    private void EmitNode(string id, string label)
    {
        _dot.AppendLine($"  {id} [label=\"{Escape(label)}\"];");
    }

    private void EmitEdge(string from, string to)
    {
        _dot.AppendLine($"  {from} -> {to};");
    }

    private string FormatSpan(SourceSpan span)
    {
        return $"[{span.StartLine}:{span.StartColumn}-{span.EndLine}:{span.EndColumn}]";
    }

    private string Escape(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private string SemanticInfo(Expr expr)
    {
        if (!_includeSemantic)
            return string.Empty;

        string result = string.Empty;
        if (expr.ResolvedSlot >= 0)
            result += $"\\nSlot: {expr.ResolvedSlot}";
        if (expr.ResolvedDistance >= 0)
            result += $"\\nDistance: {expr.ResolvedDistance}";
        return result;
    }

    // ── Expressions ──────────────────────────────────────────────────────

    public string VisitLiteralExpr(LiteralExpr expr)
    {
        var id = NextId("literal");
        string valueStr = expr.Value switch
        {
            null => "null",
            string s => $"\\\"{Escape(s)}\\\"",
            bool b => b ? "true" : "false",
            _ => expr.Value.ToString() ?? "?"
        };
        EmitNode(id, $"LiteralExpr\\n{FormatSpan(expr.Span)}\\nValue: {valueStr}{SemanticInfo(expr)}");
        return id;
    }

    public string VisitIdentifierExpr(IdentifierExpr expr)
    {
        var id = NextId("ident");
        EmitNode(id, $"IdentifierExpr\\n{FormatSpan(expr.Span)}\\nName: \\\"{Escape(expr.Name.Lexeme)}\\\"{SemanticInfo(expr)}");
        return id;
    }

    public string VisitUnaryExpr(UnaryExpr expr)
    {
        var id = NextId("unary");
        EmitNode(id, $"UnaryExpr\\n{FormatSpan(expr.Span)}\\nOp: {Escape(expr.Operator.Lexeme)}{SemanticInfo(expr)}");
        var rightId = expr.Right.Accept(this);
        EmitEdge(id, rightId);
        return id;
    }

    public string VisitBinaryExpr(BinaryExpr expr)
    {
        var id = NextId("binary");
        EmitNode(id, $"BinaryExpr\\n{FormatSpan(expr.Span)}\\nOp: {Escape(expr.Operator.Lexeme)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Left.Accept(this));
        EmitEdge(id, expr.Right.Accept(this));
        return id;
    }

    public string VisitGroupingExpr(GroupingExpr expr)
    {
        var id = NextId("group");
        EmitNode(id, $"GroupingExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Expression.Accept(this));
        return id;
    }

    public string VisitTernaryExpr(TernaryExpr expr)
    {
        var id = NextId("ternary");
        EmitNode(id, $"TernaryExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Condition.Accept(this));
        EmitEdge(id, expr.ThenBranch.Accept(this));
        EmitEdge(id, expr.ElseBranch.Accept(this));
        return id;
    }

    public string VisitAssignExpr(AssignExpr expr)
    {
        var id = NextId("assign");
        EmitNode(id, $"AssignExpr\\n{FormatSpan(expr.Span)}\\nTarget: \\\"{Escape(expr.Name.Lexeme)}\\\"{SemanticInfo(expr)}");
        EmitEdge(id, expr.Value.Accept(this));
        return id;
    }

    public string VisitCallExpr(CallExpr expr)
    {
        var id = NextId("call");
        EmitNode(id, $"CallExpr\\n{FormatSpan(expr.Span)}\\nOptional: {expr.IsOptional}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Callee.Accept(this));
        foreach (var arg in expr.Arguments)
            EmitEdge(id, arg.Accept(this));
        return id;
    }

    public string VisitArrayExpr(ArrayExpr expr)
    {
        var id = NextId("array");
        EmitNode(id, $"ArrayExpr\\n{FormatSpan(expr.Span)}\\nCount: {expr.Elements.Count}{SemanticInfo(expr)}");
        foreach (var elem in expr.Elements)
            EmitEdge(id, elem.Accept(this));
        return id;
    }

    public string VisitIndexExpr(IndexExpr expr)
    {
        var id = NextId("index");
        EmitNode(id, $"IndexExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Object.Accept(this));
        EmitEdge(id, expr.Index.Accept(this));
        return id;
    }

    public string VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        var id = NextId("indexassign");
        EmitNode(id, $"IndexAssignExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Object.Accept(this));
        EmitEdge(id, expr.Index.Accept(this));
        EmitEdge(id, expr.Value.Accept(this));
        return id;
    }

    public string VisitStructInitExpr(StructInitExpr expr)
    {
        var id = NextId("structinit");
        EmitNode(id, $"StructInitExpr\\n{FormatSpan(expr.Span)}\\nName: \\\"{Escape(expr.Name.Lexeme)}\\\"{SemanticInfo(expr)}");
        if (expr.Target is not null)
            EmitEdge(id, expr.Target.Accept(this));
        foreach (var (_, value) in expr.FieldValues)
            EmitEdge(id, value.Accept(this));
        return id;
    }

    public string VisitDotExpr(DotExpr expr)
    {
        var id = NextId("dot");
        EmitNode(id, $"DotExpr\\n{FormatSpan(expr.Span)}\\nName: \\\"{Escape(expr.Name.Lexeme)}\\\"\\nOptional: {expr.IsOptional}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Object.Accept(this));
        return id;
    }

    public string VisitDotAssignExpr(DotAssignExpr expr)
    {
        var id = NextId("dotassign");
        EmitNode(id, $"DotAssignExpr\\n{FormatSpan(expr.Span)}\\nName: \\\"{Escape(expr.Name.Lexeme)}\\\"{SemanticInfo(expr)}");
        EmitEdge(id, expr.Object.Accept(this));
        EmitEdge(id, expr.Value.Accept(this));
        return id;
    }

    public string VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        var id = NextId("interpstr");
        EmitNode(id, $"InterpolatedStringExpr\\n{FormatSpan(expr.Span)}\\nParts: {expr.Parts.Count}{SemanticInfo(expr)}");
        foreach (var part in expr.Parts)
            EmitEdge(id, part.Accept(this));
        return id;
    }

    public string VisitCommandExpr(CommandExpr expr)
    {
        var id = NextId("cmd");
        string mode = expr.Mode switch
        {
            CommandMode.Capture => "Capture",
            CommandMode.Stream => "Stream",
            CommandMode.Passthrough => "Passthrough",
            _ => "?"
        };
        EmitNode(id, $"CommandExpr\\n{FormatSpan(expr.Span)}\\nMode: {mode}\\nStrict: {expr.IsStrict}{SemanticInfo(expr)}");
        foreach (var part in expr.Parts)
            EmitEdge(id, part.Accept(this));
        return id;
    }

    public string VisitPipeExpr(PipeExpr expr)
    {
        var id = NextId("pipe");
        EmitNode(id, $"PipeExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Left.Accept(this));
        EmitEdge(id, expr.Right.Accept(this));
        return id;
    }

    public string VisitTryExpr(TryExpr expr)
    {
        var id = NextId("try");
        EmitNode(id, $"TryExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Expression.Accept(this));
        return id;
    }

    public string VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        var id = NextId("nullcoal");
        EmitNode(id, $"NullCoalesceExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Left.Accept(this));
        EmitEdge(id, expr.Right.Accept(this));
        return id;
    }

    public string VisitSwitchExpr(SwitchExpr expr)
    {
        var id = NextId("switchexpr");
        EmitNode(id, $"SwitchExpr\\n{FormatSpan(expr.Span)}\\nArms: {expr.Arms.Count}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Subject.Accept(this));
        foreach (var arm in expr.Arms)
        {
            if (arm.Pattern is not null)
                EmitEdge(id, arm.Pattern.Accept(this));
            EmitEdge(id, arm.Body.Accept(this));
        }
        return id;
    }

    public string VisitUpdateExpr(UpdateExpr expr)
    {
        var id = NextId("update");
        EmitNode(id, $"UpdateExpr\\n{FormatSpan(expr.Span)}\\nOp: {Escape(expr.Operator.Lexeme)}\\nPrefix: {expr.IsPrefix}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Operand.Accept(this));
        return id;
    }

    public string VisitLambdaExpr(LambdaExpr expr)
    {
        var id = NextId("lambda");
        EmitNode(id, $"LambdaExpr\\n{FormatSpan(expr.Span)}\\nAsync: {expr.IsAsync}\\nParams: {expr.Parameters.Count}\\nRest: {expr.HasRestParam}{SemanticInfo(expr)}");
        if (expr.ExpressionBody is not null)
            EmitEdge(id, expr.ExpressionBody.Accept(this));
        if (expr.BlockBody is not null)
            EmitEdge(id, expr.BlockBody.Accept(this));
        return id;
    }

    public string VisitRedirectExpr(RedirectExpr expr)
    {
        var id = NextId("redirect");
        string stream = expr.Stream switch
        {
            RedirectStream.Stdout => ">",
            RedirectStream.Stderr => "2>",
            RedirectStream.All => "&>",
            _ => "?"
        };
        string op = expr.Append ? $"{stream}{stream}" : stream;
        EmitNode(id, $"RedirectExpr\\n{FormatSpan(expr.Span)}\\nOp: {Escape(op)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Expression.Accept(this));
        EmitEdge(id, expr.Target.Accept(this));
        return id;
    }

    public string VisitRangeExpr(RangeExpr expr)
    {
        var id = NextId("range");
        EmitNode(id, $"RangeExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Start.Accept(this));
        EmitEdge(id, expr.End.Accept(this));
        if (expr.Step is not null)
            EmitEdge(id, expr.Step.Accept(this));
        return id;
    }

    public string VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        var id = NextId("dict");
        EmitNode(id, $"DictLiteralExpr\\n{FormatSpan(expr.Span)}\\nEntries: {expr.Entries.Count}{SemanticInfo(expr)}");
        foreach (var entry in expr.Entries)
        {
            if (entry.KeyExpr is not null)
                EmitEdge(id, entry.KeyExpr.Accept(this));
            EmitEdge(id, entry.Value.Accept(this));
        }
        return id;
    }

    public string VisitIsExpr(IsExpr expr)
    {
        var id = NextId("is");
        string typeName = expr.Type?.ToCanonicalString() ?? "?";
        EmitNode(id, $"IsExpr\\n{FormatSpan(expr.Span)}\\nType: {Escape(typeName)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Left.Accept(this));
        return id;
    }

    public string VisitAwaitExpr(AwaitExpr expr)
    {
        var id = NextId("await");
        EmitNode(id, $"AwaitExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Expression.Accept(this));
        return id;
    }

    public string VisitRetryExpr(RetryExpr expr)
    {
        var id = NextId("retry");
        EmitNode(id, $"RetryExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.MaxAttempts.Accept(this));
        if (expr.OptionsExpr is not null)
            EmitEdge(id, expr.OptionsExpr.Accept(this));
        if (expr.UntilClause is not null)
            EmitEdge(id, expr.UntilClause.Accept(this));
        if (expr.OnRetryClause?.Body is not null)
            EmitEdge(id, expr.OnRetryClause.Body.Accept(this));
        if (expr.OnRetryClause?.Reference is not null)
            EmitEdge(id, expr.OnRetryClause.Reference.Accept(this));
        EmitEdge(id, expr.Body.Accept(this));
        return id;
    }

    public string VisitTimeoutExpr(TimeoutExpr expr)
    {
        var id = NextId("timeout");
        EmitNode(id, $"TimeoutExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Duration.Accept(this));
        EmitEdge(id, expr.Body.Accept(this));
        return id;
    }

    public string VisitSpreadExpr(SpreadExpr expr)
    {
        var id = NextId("spread");
        EmitNode(id, $"SpreadExpr\\n{FormatSpan(expr.Span)}{SemanticInfo(expr)}");
        EmitEdge(id, expr.Expression.Accept(this));
        return id;
    }

    // ── Statements ───────────────────────────────────────────────────────

    public string VisitExprStmt(ExprStmt stmt)
    {
        var id = NextId("exprstmt");
        EmitNode(id, $"ExprStmt\\n{FormatSpan(stmt.Span)}");
        EmitEdge(id, stmt.Expression.Accept(this));
        return id;
    }

    public string VisitVarDeclStmt(VarDeclStmt stmt)
    {
        var id = NextId("vardecl");
        EmitNode(id, $"VarDeclStmt\\n{FormatSpan(stmt.Span)}\\nName: \\\"{Escape(stmt.Name.Lexeme)}\\\"");
        if (stmt.Initializer is not null)
            EmitEdge(id, stmt.Initializer.Accept(this));
        return id;
    }

    public string VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        var id = NextId("constdecl");
        EmitNode(id, $"ConstDeclStmt\\n{FormatSpan(stmt.Span)}\\nName: \\\"{Escape(stmt.Name.Lexeme)}\\\"");
        EmitEdge(id, stmt.Initializer.Accept(this));
        return id;
    }

    public string VisitBlockStmt(BlockStmt stmt)
    {
        var id = NextId("block");
        EmitNode(id, $"BlockStmt\\n{FormatSpan(stmt.Span)}\\nStatements: {stmt.Statements.Count}");
        foreach (var s in stmt.Statements)
            EmitEdge(id, s.Accept(this));
        return id;
    }

    public string VisitIfStmt(IfStmt stmt)
    {
        var id = NextId("if");
        EmitNode(id, $"IfStmt\\n{FormatSpan(stmt.Span)}");
        EmitEdge(id, stmt.Condition.Accept(this));
        EmitEdge(id, stmt.ThenBranch.Accept(this));
        if (stmt.ElseBranch is not null)
            EmitEdge(id, stmt.ElseBranch.Accept(this));
        return id;
    }

    public string VisitWhileStmt(WhileStmt stmt)
    {
        var id = NextId("while");
        EmitNode(id, $"WhileStmt\\n{FormatSpan(stmt.Span)}");
        EmitEdge(id, stmt.Condition.Accept(this));
        EmitEdge(id, stmt.Body.Accept(this));
        return id;
    }

    public string VisitDoWhileStmt(DoWhileStmt stmt)
    {
        var id = NextId("dowhile");
        EmitNode(id, $"DoWhileStmt\\n{FormatSpan(stmt.Span)}");
        EmitEdge(id, stmt.Body.Accept(this));
        EmitEdge(id, stmt.Condition.Accept(this));
        return id;
    }

    public string VisitForInStmt(ForInStmt stmt)
    {
        var id = NextId("forin");
        EmitNode(id, $"ForInStmt\\n{FormatSpan(stmt.Span)}\\nVar: \\\"{Escape(stmt.VariableName.Lexeme)}\\\"");
        EmitEdge(id, stmt.Iterable.Accept(this));
        EmitEdge(id, stmt.Body.Accept(this));
        return id;
    }

    public string VisitForStmt(ForStmt stmt)
    {
        var id = NextId("for");
        EmitNode(id, $"ForStmt\\n{FormatSpan(stmt.Span)}");
        if (stmt.Initializer is not null)
            EmitEdge(id, stmt.Initializer.Accept(this));
        if (stmt.Condition is not null)
            EmitEdge(id, stmt.Condition.Accept(this));
        if (stmt.Increment is not null)
            EmitEdge(id, stmt.Increment.Accept(this));
        EmitEdge(id, stmt.Body.Accept(this));
        return id;
    }

    public string VisitBreakStmt(BreakStmt stmt)
    {
        var id = NextId("break");
        EmitNode(id, $"BreakStmt\\n{FormatSpan(stmt.Span)}");
        return id;
    }

    public string VisitContinueStmt(ContinueStmt stmt)
    {
        var id = NextId("continue");
        EmitNode(id, $"ContinueStmt\\n{FormatSpan(stmt.Span)}");
        return id;
    }

    public string VisitFnDeclStmt(FnDeclStmt stmt)
    {
        var id = NextId("fndecl");
        string paramNames = string.Join(", ", stmt.Parameters.ConvertAll(p => p.Lexeme));
        EmitNode(id, $"FnDeclStmt\\n{FormatSpan(stmt.Span)}\\nName: \\\"{Escape(stmt.Name.Lexeme)}\\\"\\nParams: [{Escape(paramNames)}]\\nAsync: {stmt.IsAsync}\\nRest: {stmt.HasRestParam}");
        foreach (var bodyStmt in stmt.Body.Statements)
            EmitEdge(id, bodyStmt.Accept(this));
        return id;
    }

    public string VisitReturnStmt(ReturnStmt stmt)
    {
        var id = NextId("return");
        EmitNode(id, $"ReturnStmt\\n{FormatSpan(stmt.Span)}");
        if (stmt.Value is not null)
            EmitEdge(id, stmt.Value.Accept(this));
        return id;
    }

    public string VisitThrowStmt(ThrowStmt stmt)
    {
        var id = NextId("throw");
        EmitNode(id, $"ThrowStmt\\n{FormatSpan(stmt.Span)}");
        if (stmt.Value is not null)
            EmitEdge(id, stmt.Value.Accept(this));
        return id;
    }

    public string VisitStructDeclStmt(StructDeclStmt stmt)
    {
        var id = NextId("structdecl");
        EmitNode(id, $"StructDeclStmt\\n{FormatSpan(stmt.Span)}\\nName: \\\"{Escape(stmt.Name.Lexeme)}\\\"\\nFields: {stmt.Fields.Count}\\nMethods: {stmt.Methods.Count}");
        foreach (var method in stmt.Methods)
            EmitEdge(id, method.Accept(this));
        return id;
    }

    public string VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        var id = NextId("enumdecl");
        string members = string.Join(", ", stmt.Members.ConvertAll(m => m.Lexeme));
        EmitNode(id, $"EnumDeclStmt\\n{FormatSpan(stmt.Span)}\\nName: \\\"{Escape(stmt.Name.Lexeme)}\\\"\\nMembers: [{Escape(members)}]");
        return id;
    }

    public string VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        var id = NextId("ifacedecl");
        EmitNode(id, $"InterfaceDeclStmt\\n{FormatSpan(stmt.Span)}\\nName: \\\"{Escape(stmt.Name.Lexeme)}\\\"\\nFields: {stmt.Fields.Count}\\nMethods: {stmt.Methods.Count}");
        return id;
    }

    public string VisitExtendStmt(ExtendStmt stmt)
    {
        var id = NextId("extend");
        EmitNode(id, $"ExtendStmt\\n{FormatSpan(stmt.Span)}\\nType: {Escape(stmt.TypeName.ToCanonicalString())}");
        foreach (var method in stmt.Methods)
            EmitEdge(id, method.Accept(this));
        return id;
    }

    public string VisitImportStmt(ImportStmt stmt)
    {
        var id = NextId("import");
        string names = string.Join(", ", stmt.Names.ConvertAll(n => n.Lexeme));
        EmitNode(id, $"ImportStmt\\n{FormatSpan(stmt.Span)}\\nNames: [{Escape(names)}]");
        EmitEdge(id, stmt.Path.Accept(this));
        return id;
    }

    public string VisitImportAsStmt(ImportAsStmt stmt)
    {
        var id = NextId("importas");
        EmitNode(id, $"ImportAsStmt\\n{FormatSpan(stmt.Span)}\\nAlias: \\\"{Escape(stmt.Alias.Lexeme)}\\\"");
        EmitEdge(id, stmt.Path.Accept(this));
        return id;
    }

    public string VisitDestructureStmt(DestructureStmt stmt)
    {
        var id = NextId("destructure");
        string kind = stmt.Kind switch
        {
            DestructureStmt.PatternKind.Array => "Array",
            DestructureStmt.PatternKind.Object => "Object",
            _ => "?"
        };
        string names = string.Join(", ", stmt.Names.ConvertAll(n => n.Lexeme));
        EmitNode(id, $"DestructureStmt\\n{FormatSpan(stmt.Span)}\\nKind: {kind}\\nNames: [{Escape(names)}]\\nConst: {stmt.IsConst}");
        EmitEdge(id, stmt.Initializer.Accept(this));
        return id;
    }

    public string VisitElevateStmt(ElevateStmt stmt)
    {
        var id = NextId("elevate");
        EmitNode(id, $"ElevateStmt\\n{FormatSpan(stmt.Span)}");
        if (stmt.Elevator is not null)
            EmitEdge(id, stmt.Elevator.Accept(this));
        EmitEdge(id, stmt.Body.Accept(this));
        return id;
    }

    public string VisitTryCatchStmt(TryCatchStmt stmt)
    {
        var id = NextId("trycatch");
        EmitNode(id, $"TryCatchStmt\\n{FormatSpan(stmt.Span)}\\nCatchClauses: {stmt.CatchClauses.Count}\\nFinally: {stmt.FinallyBody is not null}");
        EmitEdge(id, stmt.TryBody.Accept(this));
        foreach (var clause in stmt.CatchClauses)
        {
            EmitEdge(id, clause.Body.Accept(this));
        }
        if (stmt.FinallyBody is not null)
            EmitEdge(id, stmt.FinallyBody.Accept(this));
        return id;
    }

    public string VisitSwitchStmt(SwitchStmt stmt)
    {
        var id = NextId("switchstmt");
        EmitNode(id, $"SwitchStmt\\n{FormatSpan(stmt.Span)}\\nCases: {stmt.Cases.Count}");
        EmitEdge(id, stmt.Subject.Accept(this));
        foreach (var c in stmt.Cases)
        {
            foreach (var pattern in c.Patterns)
                EmitEdge(id, pattern.Accept(this));
            EmitEdge(id, c.Body.Accept(this));
        }
        return id;
    }

    public string VisitDeferStmt(DeferStmt stmt)
    {
        var id = NextId("defer");
        EmitNode(id, $"DeferStmt\\n{FormatSpan(stmt.Span)}\\nAwait: {stmt.HasAwait}");
        EmitEdge(id, stmt.Body.Accept(this));
        return id;
    }

    public string VisitLockStmt(LockStmt stmt)
    {
        var id = NextId("lock");
        EmitNode(id, $"LockStmt\\n{FormatSpan(stmt.Span)}");
        EmitEdge(id, stmt.Path.Accept(this));
        if (stmt.WaitOption is not null)
            EmitEdge(id, stmt.WaitOption.Accept(this));
        if (stmt.StaleOption is not null)
            EmitEdge(id, stmt.StaleOption.Accept(this));
        EmitEdge(id, stmt.Body.Accept(this));
        return id;
    }

    public string VisitUnsetStmt(UnsetStmt stmt)
    {
        var id = NextId("unset");
        var targetNames = new List<string>();
        foreach (var t in stmt.Targets)
            targetNames.Add(t.Name);
        string targets = string.Join(", ", targetNames);
        EmitNode(id, $"UnsetStmt\\n{FormatSpan(stmt.Span)}\\nTargets: [{Escape(targets)}]");
        return id;
    }

    public string VisitExportDeclStmt(ExportDeclStmt stmt)
    {
        var id = NextId("exportdecl");
        EmitNode(id, $"ExportDeclStmt\\n{FormatSpan(stmt.Span)}");
        EmitEdge(id, stmt.Inner.Accept(this));
        return id;
    }

    public string VisitExportBlockStmt(ExportBlockStmt stmt)
    {
        var id = NextId("exportblock");
        string names = string.Join(", ", stmt.Names.ConvertAll(n => n.Lexeme));
        EmitNode(id, $"ExportBlockStmt\\n{FormatSpan(stmt.Span)}\\nNames: [{Escape(names)}]");
        return id;
    }

    public string VisitExportModuleAsStmt(ExportModuleAsStmt stmt)
    {
        var id = NextId("exportmodas");
        EmitNode(id, $"ExportModuleAsStmt\\n{FormatSpan(stmt.Span)}\\nAlias: \\\"{Escape(stmt.Alias.Lexeme)}\\\"");
        EmitEdge(id, stmt.Path.Accept(this));
        return id;
    }

    public string VisitExportFromStmt(ExportFromStmt stmt)
    {
        var id = NextId("exportfrom");
        string names = string.Join(", ", stmt.Names.ConvertAll(n => n.Lexeme));
        EmitNode(id, $"ExportFromStmt\\n{FormatSpan(stmt.Span)}\\nNames: [{Escape(names)}]");
        EmitEdge(id, stmt.Path.Accept(this));
        return id;
    }
}
