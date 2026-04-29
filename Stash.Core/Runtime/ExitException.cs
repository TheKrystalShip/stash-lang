namespace Stash.Runtime;

using System.Collections.Generic;
using Stash.Runtime.Types;

/// <summary>
/// Thrown when script execution is terminated via process.exit(). Propagates through the VM
/// dispatch loop, which runs all pending defer blocks before terminating. Not intercepted by
/// any Stash try/catch clause — always propagates to the interpreter top level.
/// </summary>
public class ExitException : System.Exception
{
    public int ExitCode { get; }

    /// <summary>Errors thrown by deferred blocks during exit unwinding. Never fatal.</summary>
    public List<StashError>? SuppressedErrors { get; set; }

    public ExitException(int exitCode)
        : base($"Script exited with code {exitCode}")
    {
        ExitCode = exitCode;
    }
}
