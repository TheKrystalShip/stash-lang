namespace Stash.Interpreting;

using System;

/// <summary>
/// Thrown when script execution is cancelled via a <see cref="CancellationToken"/>.
/// The host application can catch this to handle graceful cancellation
/// (e.g., when an HTTP request is aborted or a timeout fires).
/// </summary>
public class ScriptCancelledException : Exception
{
    public ScriptCancelledException() : base("Script execution was cancelled.")
    {
    }
}
