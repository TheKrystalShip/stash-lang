namespace Stash.Interpreting;

using System;

/// <summary>
/// Thrown when a script exceeds the configured maximum number of execution steps.
/// Prevents runaway scripts from consuming unbounded resources in embedded scenarios.
/// </summary>
public class StepLimitExceededException : Exception
{
    /// <summary>The step limit that was exceeded.</summary>
    public long StepLimit { get; }

    public StepLimitExceededException(long stepLimit) : base($"Script exceeded the maximum step limit of {stepLimit}.")
    {
        StepLimit = stepLimit;
    }
}
