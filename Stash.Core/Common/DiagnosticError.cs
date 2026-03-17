namespace Stash.Common;

/// <summary>
/// An immutable diagnostic record representing a compile-time or runtime error with its source location.
/// </summary>
/// <remarks>
/// Used throughout the Stash pipeline (lexer, parser, semantic analysis) to collect structured
/// error information. Each error pairs a human-readable <see cref="Message"/> with the
/// <see cref="SourceSpan"/> that pinpoints exactly where in the source code the problem occurred.
/// Errors are accumulated in lists rather than thrown as exceptions, enabling the lexer and parser
/// to continue processing and report multiple errors in a single pass.
/// </remarks>
/// <param name="Span">The source location (file, line, column range) where the error occurred.</param>
/// <param name="Message">A human-readable description of the error.</param>
public record DiagnosticError(
    SourceSpan Span,
    string Message
);
