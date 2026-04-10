namespace Stash.Analysis;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// Classifies whether applying a <see cref="CodeFix"/> is safe to do automatically or
/// requires explicit user opt-in.
/// </summary>
public enum FixApplicability
{
    /// <summary>The fix preserves semantics and can be applied automatically with <c>--fix</c>.</summary>
    Safe,

    /// <summary>The fix may change observable behaviour and requires <c>--unsafe-fixes</c>.</summary>
    Unsafe,

    /// <summary>The fix is one of several alternatives; only offered via LSP quick-fix, not auto-applied.</summary>
    Suggestion
}

/// <summary>
/// A single source-text substitution produced by a <see cref="CodeFix"/>.
/// </summary>
/// <param name="Span">The region of source text to replace. Positions are 1-based.</param>
/// <param name="NewText">The text that replaces the region; use <see cref="string.Empty"/> to delete.</param>
public sealed record SourceEdit(SourceSpan Span, string NewText);

/// <summary>
/// A named, applicability-classified set of <see cref="SourceEdit"/> operations that collectively
/// fix a <see cref="SemanticDiagnostic"/>.
/// </summary>
/// <param name="Title">A short human-readable description shown in editor quick-fix menus.</param>
/// <param name="Applicability">Whether the fix is safe, unsafe, or suggestion-only.</param>
/// <param name="Edits">The ordered list of source edits. Apply in reverse document order to preserve offsets.</param>
public sealed record CodeFix(
    string Title,
    FixApplicability Applicability,
    IReadOnlyList<SourceEdit> Edits
);
