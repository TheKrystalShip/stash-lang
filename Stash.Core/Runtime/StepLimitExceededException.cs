namespace Stash.Runtime;

/// <summary>
/// Thrown when script execution exceeds the configured step limit.
/// </summary>
public class StepLimitExceededException : System.Exception
{
    public long StepLimit { get; }

    public StepLimitExceededException(long stepLimit)
        : base($"Script exceeded the maximum step limit of {stepLimit}.")
    {
        StepLimit = stepLimit;
    }
}
