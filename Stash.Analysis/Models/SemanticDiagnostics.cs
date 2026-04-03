using Stash.Common;

namespace Stash.Analysis;

/// <summary>
/// Represents a single diagnostic message produced by <see cref="SemanticValidator"/>,
/// carrying a stable code, message text, severity, source location, and an optional
/// "unnecessary code" flag.
/// </summary>
/// <remarks>
/// Diagnostics are forwarded by <see cref="AnalysisEngine"/> to the LSP
/// <c>textDocument/publishDiagnostics</c> notification and rendered as squiggly underlines
/// in the editor. The <see cref="IsUnnecessary"/> flag triggers an additional faded rendering
/// style for unreachable-code hints.
/// </remarks>
public class SemanticDiagnostic
{
    /// <summary>Gets the stable diagnostic code (e.g. "SA0101"), or <see langword="null"/> for legacy diagnostics.</summary>
    public string? Code { get; }

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
    /// Initializes a new <see cref="SemanticDiagnostic"/> with a stable diagnostic code.
    /// </summary>
    public SemanticDiagnostic(string code, string message, DiagnosticLevel level, SourceSpan span, bool isUnnecessary = false)
    {
        Code = code;
        Message = message;
        Level = level;
        Span = span;
        IsUnnecessary = isUnnecessary;
    }

    /// <summary>
    /// Initializes a new <see cref="SemanticDiagnostic"/> without a diagnostic code (legacy).
    /// </summary>
    public SemanticDiagnostic(string message, DiagnosticLevel level, SourceSpan span, bool isUnnecessary = false)
    {
        Code = null;
        Message = message;
        Level = level;
        Span = span;
        IsUnnecessary = isUnnecessary;
    }
}
