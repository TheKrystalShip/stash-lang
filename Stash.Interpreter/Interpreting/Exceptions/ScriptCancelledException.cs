namespace Stash.Interpreting.Exceptions;

/// <summary>
/// Thrown when script execution is cancelled via a CancellationToken.
/// Derives from the shared base type in Stash.Core.
/// </summary>
public class ScriptCancelledException : Stash.Runtime.ScriptCancelledException
{
    public ScriptCancelledException() : base() { }
}
