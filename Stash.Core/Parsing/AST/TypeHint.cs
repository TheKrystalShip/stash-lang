namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a type annotation in Stash source code.
/// Supports simple types (<c>int</c>, <c>string</c>, <c>MyStruct</c>) and
/// typed array types (<c>int[]</c>, <c>float[]</c>, <c>string[]</c>, <c>bool[]</c>).
/// </summary>
/// <param name="Name">The type name token (e.g., <c>int</c>, <c>string</c>, <c>MyStruct</c>).</param>
/// <param name="IsArray">Whether this is a typed array type (<c>T[]</c>).</param>
/// <param name="Span">The source span covering the entire type annotation, including brackets if present.</param>
public record TypeHint(Token Name, bool IsArray, SourceSpan Span)
{
    /// <summary>
    /// Gets the full type string, including array brackets if applicable.
    /// For example: <c>"int"</c>, <c>"int[]"</c>, <c>"string[]"</c>, <c>"MyStruct"</c>.
    /// </summary>
    public string Lexeme => IsArray ? $"{Name.Lexeme}[]" : Name.Lexeme;
}
