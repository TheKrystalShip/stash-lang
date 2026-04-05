namespace Stash.Runtime;

/// <summary>
/// Thrown when script execution is terminated via sys.exit() or process.exit() in embedded mode.
/// </summary>
public class ExitException : System.Exception
{
    public int ExitCode { get; }

    public ExitException(int exitCode)
        : base($"Script exited with code {exitCode}")
    {
        ExitCode = exitCode;
    }
}
