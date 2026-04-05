namespace Stash.Interpreting.Exceptions;

/// <summary>
/// Thrown when a script exceeds the configured maximum number of execution steps.
/// Derives from the shared base type in Stash.Core.
/// </summary>
public class StepLimitExceededException : Stash.Runtime.StepLimitExceededException
{
    public StepLimitExceededException(long stepLimit) : base(stepLimit) { }
}
