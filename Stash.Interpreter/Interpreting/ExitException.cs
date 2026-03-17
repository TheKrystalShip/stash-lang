namespace Stash.Interpreting;

using System;

/// <summary>
/// Thrown when a Stash script calls process.exit() in embedded mode.
/// The host application can catch this to handle script-requested exits
/// without terminating the entire process.
/// </summary>
public class ExitException : Exception
{
    public int ExitCode { get; }

    public ExitException(int exitCode) : base($"Script exited with code {exitCode}")
    {
        ExitCode = exitCode;
    }
}
