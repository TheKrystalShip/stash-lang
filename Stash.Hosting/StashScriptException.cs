namespace Stash.Hosting;

using System;

/// <summary>
/// Thrown by <see cref="IStashHost.CallAsync{T}"/> when a Stash script raises an error
/// (a <c>throw</c> statement or a runtime failure). The structured <see cref="Error"/>
/// property carries the Stash error's <c>Kind</c>, <c>Message</c>, source <c>Span</c>,
/// and the call stack at the point of the throw.
/// </summary>
/// <remarks>
/// <see cref="StashScriptException"/> is NOT thrown for cancellation — that case
/// propagates as <see cref="OperationCanceledException"/> so callers can respond to
/// task-cancellation through the standard .NET pattern. Use
/// <see cref="IStashHost.TryCallAsync{T}"/> to receive both script errors and
/// cancellation as a <see cref="StashResult{T}"/> without throwing.
/// </remarks>
public sealed class StashScriptException : Exception
{
    /// <summary>The structured Stash error that caused this exception.</summary>
    public StashError Error { get; }

    /// <summary>
    /// Initializes a new <see cref="StashScriptException"/> from a <see cref="StashError"/>.
    /// </summary>
    public StashScriptException(StashError error)
        : base($"Stash script error ({error.Kind}): {error.Message}")
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }
}
