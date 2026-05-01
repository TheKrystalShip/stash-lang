namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents an <c>unset</c> statement that removes one or more top-level bindings from global scope.
/// </summary>
/// <remarks>
/// <code>
/// unset x;
/// unset a, b, c;
/// unset _temp, MyStruct, helper;
/// </code>
/// </remarks>
public sealed class UnsetStmt : Stmt
{
    /// <summary>The <c>unset</c> keyword token.</summary>
    public Token UnsetKeyword { get; }

    /// <summary>The list of binding names to remove.</summary>
    public IReadOnlyList<UnsetTarget> Targets { get; }

    public UnsetStmt(Token unsetKeyword, IReadOnlyList<UnsetTarget> targets, SourceSpan span)
        : base(span, StmtType.Unset)
    {
        UnsetKeyword = unsetKeyword;
        Targets = targets;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitUnsetStmt(this);
}

/// <summary>A single target identifier in an <see cref="UnsetStmt"/>.</summary>
public readonly record struct UnsetTarget(string Name, SourceSpan Span);
