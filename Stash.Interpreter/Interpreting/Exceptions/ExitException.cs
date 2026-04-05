namespace Stash.Interpreting.Exceptions;

/// <summary>
/// Thrown when a Stash script calls process.exit() in embedded mode.
/// Derives from the shared base type in Stash.Core.
/// </summary>
public class ExitException : Stash.Runtime.ExitException
{
    public ExitException(int exitCode) : base(exitCode) { }
}
