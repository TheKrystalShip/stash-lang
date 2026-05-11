namespace Stash.Stdlib.Models;

/// <summary>
/// Records a single error type that a built-in function may throw.
/// Surfaced in LSP hover, signature help, and completions, and consumed
/// by the opt-in uncaught-throws analyzer rule.
/// </summary>
/// <param name="ErrorType">
/// The bare error-type name (e.g. <c>"IOError"</c>, <c>"ValueError"</c>).
/// Use <c>StashErrorTypes.*</c> constants — never literal strings — to avoid drift.
/// </param>
/// <param name="Description">Optional prose description of when this error is thrown.</param>
public record ThrowsEntry(string ErrorType, string? Description = null);
