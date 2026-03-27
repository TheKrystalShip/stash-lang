namespace Stash.Playground.Services;

public sealed class PlaygroundResult
{
    public string Output { get; init; } = string.Empty;
    public IReadOnlyList<string> ErrorMessages { get; init; } = [];
    public double ElapsedMs { get; init; }
    public long StepCount { get; init; }
    public bool Success { get; init; }
    public bool TimedOut { get; init; }
    public bool StepLimitExceeded { get; init; }
}
