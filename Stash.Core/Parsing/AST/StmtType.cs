namespace Stash.Parsing.AST;

/// <summary>
/// Identifies the concrete type of a <see cref="Stmt"/> node for switch-based dispatch.
/// </summary>
public enum StmtType : byte
{
    Expr,
    VarDecl,
    ConstDecl,
    Block,
    If,
    While,
    DoWhile,
    ForIn,
    For,
    Break,
    Continue,
    FnDecl,
    Return,
    Throw,
    StructDecl,
    EnumDecl,
    InterfaceDecl,
    Extend,
    Import,
    ImportAs,
    Destructure,
    Elevate,
    TryCatch,
    Switch,
}
