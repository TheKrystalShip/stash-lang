using Stash.Common;

namespace Stash.Analysis;

/// <summary>
/// Represents a single diagnostic message produced by <see cref="SemanticValidator"/>,
/// carrying the message text, severity, source location, and an optional "unnecessary code" flag.
/// </summary>
/// <remarks>
/// Diagnostics are forwarded by <see cref="AnalysisEngine"/> to the LSP
/// <c>textDocument/publishDiagnostics</c> notification and rendered as squiggly underlines
/// in the editor. The <see cref="IsUnnecessary"/> flag triggers an additional faded rendering
/// style for unreachable-code hints.
/// </remarks>
public class SemanticDiagnostic
{
    /// <summary>Gets the human-readable diagnostic message shown in the editor tooltip.</summary>
    public string Message { get; }

    /// <summary>Gets the severity (Error, Warning, or Information) of this diagnostic.</summary>
    public DiagnosticLevel Level { get; }

    /// <summary>Gets the source span to underline in the editor.</summary>
    public SourceSpan Span { get; }

    /// <summary>
    /// Gets whether this diagnostic marks code that is present but will never execute
    /// (unreachable code). When <see langword="true"/> the LSP client may apply a faded style
    /// in addition to the normal diagnostic decoration.
    /// </summary>
    public bool IsUnnecessary { get; }

    /// <summary>
    /// Initializes a new <see cref="SemanticDiagnostic"/> with the given message, level, span,
    /// and optional unnecessary-code flag.
    /// </summary>
    /// <param name="message">Human-readable diagnostic text.</param>
    /// <param name="level">Severity of the diagnostic.</param>
    /// <param name="span">Source location to highlight.</param>
    /// <param name="isUnnecessary"><see langword="true"/> to enable faded rendering for unreachable code.</param>
    public SemanticDiagnostic(string message, DiagnosticLevel level, SourceSpan span, bool isUnnecessary = false)
    {
        Message = message;
        Level = level;
        Span = span;
        IsUnnecessary = isUnnecessary;
    }
}
