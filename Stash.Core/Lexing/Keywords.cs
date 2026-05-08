namespace Stash.Lexing;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Single source of truth for the names of all Stash keywords.
/// Hard keywords are tokenized by the <see cref="Lexer"/>; soft keywords are
/// context-matched by the <see cref="Parsing.Parser"/> via lexeme comparison.
/// </summary>
public static class Keywords
{
    /// <summary>Hard keywords — tokenized by the lexer into dedicated TokenTypes.</summary>
    public static readonly FrozenSet<string> HardKeywords = Lexer.KeywordNames;

    /// <summary>
    /// Soft keywords — recognized by the parser via lexeme comparison after the lexer
    /// emits them as <c>TokenType.Identifier</c>. They are reserved words for tooling
    /// purposes (completion, syntax highlighting) but coexist with identifier scope.
    /// </summary>
    public static readonly FrozenSet<string> SoftKeywords = new[]
    {
        "defer", "async", "await", "retry", "timeout", "elevate", "lock"
    }.ToFrozenSet();

    /// <summary>Union of <see cref="HardKeywords"/> and <see cref="SoftKeywords"/>, sorted.</summary>
    public static readonly IReadOnlyList<string> All =
        HardKeywords.Concat(SoftKeywords).OrderBy(s => s).ToArray();
}
