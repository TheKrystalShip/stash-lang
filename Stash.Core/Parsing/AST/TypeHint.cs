namespace Stash.Parsing.AST;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a type annotation in Stash source code.
/// Supports simple types (<c>int</c>, <c>string</c>, <c>MyStruct</c>),
/// namespace-qualified dotted types (<c>types.DiffOptions</c>, <c>pkg.types.DiffResult</c>),
/// and typed array types (<c>int[]</c>, <c>types.DiffOptions[]</c>).
/// </summary>
/// <param name="Name">The head (first) identifier token. For a simple type this is the type name;
/// for a dotted type this is the namespace alias.</param>
/// <param name="Path">Optional list of all segment tokens (including <paramref name="Name"/>) for dotted type names.
/// Null or empty for simple, single-identifier types. When non-null, contains <paramref name="Name"/> at index 0
/// followed by each subsequent dot-separated identifier.</param>
/// <param name="IsArray">Whether this is a typed array type (<c>T[]</c>).</param>
/// <param name="Span">The source span covering the entire type annotation, including brackets if present.</param>
public record TypeHint(Token Name, IReadOnlyList<Token>? Path, bool IsArray, SourceSpan Span)
{
    /// <summary>
    /// Convenience constructor for single-identifier type hints (no dotted path).
    /// </summary>
    public TypeHint(Token Name, bool IsArray, SourceSpan Span)
        : this(Name, null, IsArray, Span)
    {
    }

    /// <summary>
    /// Gets the full type string, including dotted segments and array brackets if applicable.
    /// For example: <c>"int"</c>, <c>"int[]"</c>, <c>"MyStruct"</c>, <c>"types.DiffOptions"</c>,
    /// <c>"types.DiffOptions[]"</c>.
    /// </summary>
    public string Lexeme
    {
        get
        {
            string baseName = Path is { Count: > 0 }
                ? string.Join('.', Path.Select(t => t.Lexeme))
                : Name.Lexeme;
            return IsArray ? $"{baseName}[]" : baseName;
        }
    }
}
