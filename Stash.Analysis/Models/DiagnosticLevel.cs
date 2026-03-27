namespace Stash.Analysis;

/// <summary>
/// Severity level of a <see cref="SemanticDiagnostic"/> produced by <see cref="SemanticValidator"/>.
/// Maps directly to the LSP <c>DiagnosticSeverity</c> values sent to the editor.
/// </summary>
public enum DiagnosticLevel
{
    /// <summary>A definite semantic error (e.g. <c>break</c> outside a loop, constant reassignment).</summary>
    Error,
    /// <summary>A likely mistake that may still run (e.g. type mismatch, undefined identifier).</summary>
    Warning,
    /// <summary>An informational hint such as unreachable code, rendered with a "faded" style.</summary>
    Information
}
