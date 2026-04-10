using System.Collections.Generic;
using Stash.Common;

namespace Stash.Analysis;

/// <summary>
/// A secondary source location referenced by a <see cref="SemanticDiagnostic"/>, such as
/// the declaration site that conflicts with the primary diagnostic location.
/// </summary>
/// <param name="Message">Human-readable description of the relationship.</param>
/// <param name="Span">The related source location.</param>
public record RelatedLocation(string Message, SourceSpan Span);

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
    /// Gets whether this diagnostic marks a deprecated API usage. When <see langword="true"/>
    /// the LSP client may apply a strikethrough style in addition to the normal decoration.
    /// </summary>
    public bool IsDeprecated { get; init; }

    /// <summary>
    /// Gets the list of automated code fixes available for this diagnostic.
    /// Empty when no fix is available.
    /// </summary>
    public IReadOnlyList<CodeFix> Fixes { get; init; } = [];

    /// <summary>
    /// Gets the list of related source locations for this diagnostic (e.g. conflicting declaration sites).
    /// Empty when no related locations exist.
    /// </summary>
    public IReadOnlyList<RelatedLocation> RelatedLocations { get; init; } = [];

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
