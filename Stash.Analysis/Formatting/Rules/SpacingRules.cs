using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting.Rules;

internal static class SpacingRules
{
    internal enum StmtCategory
    {
        Import,
        VarDecl,
        FnDecl,
        TypeDecl,      // struct, enum, interface
        ExtendDecl,
        ControlFlow,
        SimpleStatement,
        Block,
    }

    internal static int BlankLinesBetween(Stmt prev, Stmt current, ScopeKind scope, FormatConfig config)
    {
        var prevKind = Classify(prev);
        var currentKind = Classify(current);

        return scope switch
        {
            ScopeKind.TopLevel => TopLevelSpacing(prevKind, currentKind, config),
            ScopeKind.FunctionBody => FunctionBodySpacing(prevKind, currentKind, config),
            ScopeKind.StructBody => StructBodySpacing(prevKind, currentKind, config),
            ScopeKind.EnumBody => 0,
            ScopeKind.InterfaceBody => InterfaceBodySpacing(prevKind, currentKind, config),
            ScopeKind.ExtendBody => ExtendBodySpacing(prevKind, currentKind, config),
            _ => DefaultSpacing(prevKind, currentKind, config),
        };
    }

    internal static StmtCategory Classify(Stmt stmt) => stmt switch
    {
        ImportStmt or ImportAsStmt => StmtCategory.Import,
        VarDeclStmt or ConstDeclStmt or DestructureStmt => StmtCategory.VarDecl,
        FnDeclStmt => StmtCategory.FnDecl,
        StructDeclStmt or EnumDeclStmt or InterfaceDeclStmt => StmtCategory.TypeDecl,
        ExtendStmt => StmtCategory.ExtendDecl,
        IfStmt or WhileStmt or DoWhileStmt or ForStmt or ForInStmt
            or SwitchStmt or TryCatchStmt or ElevateStmt => StmtCategory.ControlFlow,
        BlockStmt => StmtCategory.Block,
        _ => StmtCategory.SimpleStatement,
    };

    // TopLevel: blank lines between declarations, imports cluster, vars cluster
    private static int TopLevelSpacing(StmtCategory prev, StmtCategory current, FormatConfig config)
    {
        if (prev == StmtCategory.Import && current == StmtCategory.Import) return 0;
        if (prev == StmtCategory.Import) return config.BlankLinesBetweenBlocks;
        if (prev == StmtCategory.VarDecl && current == StmtCategory.VarDecl) return 0;
        if (IsDecl(prev) || IsDecl(current)) return config.BlankLinesBetweenBlocks;
        return 0;
    }

    private static int FunctionBodySpacing(StmtCategory prev, StmtCategory current, FormatConfig config)
    {
        if (prev == StmtCategory.FnDecl || current == StmtCategory.FnDecl) return config.BlankLinesBetweenBlocks;
        return 0;
    }

    private static int StructBodySpacing(StmtCategory prev, StmtCategory current, FormatConfig config)
    {
        if (prev == StmtCategory.VarDecl && current == StmtCategory.VarDecl) return 0;
        if (prev == StmtCategory.FnDecl && current == StmtCategory.FnDecl) return config.BlankLinesBetweenBlocks;
        if (prev == StmtCategory.VarDecl && current == StmtCategory.FnDecl) return config.BlankLinesBetweenBlocks;
        return 0;
    }

    private static int InterfaceBodySpacing(StmtCategory prev, StmtCategory current, FormatConfig config)
    {
        return 0; // interface members are always single-newline separated
    }

    private static int ExtendBodySpacing(StmtCategory prev, StmtCategory current, FormatConfig config)
    {
        if (prev == StmtCategory.FnDecl && current == StmtCategory.FnDecl) return config.BlankLinesBetweenBlocks;
        return 0;
    }

    private static int DefaultSpacing(StmtCategory prev, StmtCategory current, FormatConfig config)
    {
        if (IsDecl(prev) || IsDecl(current)) return config.BlankLinesBetweenBlocks;
        return 0;
    }

    private static bool IsDecl(StmtCategory cat) =>
        cat is StmtCategory.FnDecl or StmtCategory.TypeDecl or StmtCategory.ExtendDecl;
}
