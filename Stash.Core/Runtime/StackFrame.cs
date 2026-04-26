namespace Stash.Runtime;

using Stash.Common;

/// <summary>
/// Represents a single frame in a Stash call stack, captured at the moment of an error.
/// Used for stack trace display in error messages.
/// </summary>
public record StackFrame(string FunctionName, SourceSpan Span);
