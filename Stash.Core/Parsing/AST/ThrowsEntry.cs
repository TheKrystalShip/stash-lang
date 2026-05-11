namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// Records a single error type declared in a <c>@throws</c> doc-comment tag
/// on a user-defined function or method.
/// </summary>
/// <param name="ErrorType">The bare error-type name (e.g. <c>"IOError"</c>).</param>
/// <param name="Description">Optional prose description of when this error is thrown, or <see langword="null"/>.</param>
/// <param name="Span">Approximate source span used by the analyzer for diagnostic placement.</param>
public record ThrowsEntry(string ErrorType, string? Description, SourceSpan Span);
