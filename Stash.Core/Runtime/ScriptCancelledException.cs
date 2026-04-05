namespace Stash.Runtime;

/// <summary>
/// Thrown when script execution is cancelled via a CancellationToken.
/// </summary>
public class ScriptCancelledException : System.Exception
{
    public ScriptCancelledException()
        : base("Script execution was cancelled.")
    {
    }
}
