namespace Stash.Parsing.AST;

/// <summary>
/// Identifies the concrete type of an <see cref="Expr"/> node for switch-based dispatch.
/// </summary>
public enum ExprType : byte
{
    Literal,
    Identifier,
    Unary,
    Binary,
    Grouping,
    Ternary,
    Assign,
    Call,
    Array,
    Index,
    IndexAssign,
    StructInit,
    Dot,
    DotAssign,
    InterpolatedString,
    Command,
    Pipe,
    Try,
    NullCoalesce,
    Switch,
    Update,
    Lambda,
    Redirect,
    Range,
    DictLiteral,
    Is,
    Await,
    Retry,
    Timeout,
    Spread,
}
