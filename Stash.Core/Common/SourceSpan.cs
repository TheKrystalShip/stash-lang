namespace Stash.Common;

/// <summary>
/// Represents a contiguous region of source code, used for error messages, stack traces,
/// and future debugger integration.
/// </summary>
/// <remarks>
/// All line and column positions are 1-based to match the conventions of most editors and
/// terminal error output. A zero-length span (where start equals end) represents a single
/// point in the source, such as the position of an EOF token.
/// </remarks>
/// <param name="File">
/// The file path or display name (e.g. <c>"&lt;stdin&gt;"</c>) that the span originates from.
/// </param>
/// <param name="StartLine">
/// The 1-based line number where the span begins.
/// </param>
/// <param name="StartColumn">
/// The 1-based column number where the span begins.
/// </param>
/// <param name="EndLine">
/// The 1-based line number where the span ends (inclusive).
/// </param>
/// <param name="EndColumn">
/// The 1-based column number where the span ends (inclusive).
/// </param>
public readonly record struct SourceSpan(
    string File,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn
);
