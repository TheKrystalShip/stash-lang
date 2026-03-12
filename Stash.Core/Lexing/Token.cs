namespace Stash.Lexing;

using Stash.Common;

/// <summary>
/// Represents a single lexical token produced by the <see cref="Lexer"/>.
/// </summary>
/// <remarks>
/// A token is the smallest meaningful unit of the Stash language. The lexer scans the raw
/// source text and groups characters into tokens that the <see cref="Stash.Parsing.Parser"/>
/// then assembles into an AST. The record is immutable by design — once produced, a token
/// is never modified.
/// </remarks>
/// <param name="Type">
/// The classification of this token (e.g. <see cref="TokenType.Plus"/>,
/// <see cref="TokenType.Identifier"/>, <see cref="TokenType.If"/>).
/// </param>
/// <param name="Lexeme">
/// The raw source text that was matched to produce this token. For example, a string
/// literal token's lexeme includes the surrounding quotes (<c>"hello"</c>), while its
/// <paramref name="Literal"/> holds the unescaped value (<c>hello</c>).
/// </param>
/// <param name="Literal">
/// The parsed runtime value of the token, or <see langword="null"/> if the token carries
/// no literal value. The type depends on the token:
/// <list type="bullet">
///   <item><description><see cref="long"/> for <see cref="TokenType.IntegerLiteral"/>.</description></item>
///   <item><description><see cref="double"/> for <see cref="TokenType.FloatLiteral"/>.</description></item>
///   <item><description><see cref="string"/> for <see cref="TokenType.StringLiteral"/> (unescaped content without quotes).</description></item>
///   <item><description><see cref="bool"/> for <see cref="TokenType.True"/> (<see langword="true"/>) and <see cref="TokenType.False"/> (<see langword="false"/>).</description></item>
///   <item><description><see langword="null"/> for all other token types.</description></item>
/// </list>
/// </param>
/// <param name="Span">
/// The <see cref="SourceSpan"/> indicating where this token appears in the source file,
/// used for error reporting and diagnostics.
/// </param>
public record Token(TokenType Type, string Lexeme, object? Literal, SourceSpan Span);
