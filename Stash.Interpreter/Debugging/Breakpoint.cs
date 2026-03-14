namespace Stash.Debugging;

using System.Threading;

/// <summary>
/// Represents a breakpoint with optional condition, hit count, and log message.
/// Supports the full range of DAP breakpoint features.
/// </summary>
public class Breakpoint
{
    private static int _nextId;

    /// <summary>Unique breakpoint identifier.</summary>
    public int Id { get; }

    /// <summary>Source file path.</summary>
    public string File { get; }

    /// <summary>1-based line number.</summary>
    public int Line { get; }

    /// <summary>
    /// Optional condition expression (Stash code) that must evaluate to truthy for the breakpoint to hit.
    /// Null means the breakpoint always hits.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Optional hit condition expression (e.g., ">= 5", "== 3", "% 2 == 0").
    /// The breakpoint only hits when the hit count satisfies this condition.
    /// Null means the breakpoint hits every time (subject to Condition).
    /// </summary>
    public string? HitCondition { get; set; }

    /// <summary>
    /// Optional log message template. When set, the breakpoint logs this message
    /// instead of pausing execution (logpoint). Expressions in {braces} are evaluated.
    /// Null means the breakpoint pauses execution normally.
    /// </summary>
    public string? LogMessage { get; set; }

    /// <summary>
    /// Whether the breakpoint has been verified (confirmed to be on a valid executable line).
    /// </summary>
    public bool Verified { get; set; }

    /// <summary>
    /// The actual line the breakpoint was placed on (may differ from requested line
    /// if the requested line was not executable).
    /// </summary>
    public int? ActualLine { get; set; }

    /// <summary>
    /// Number of times this breakpoint has been hit.
    /// </summary>
    public int HitCount { get; private set; }

    /// <summary>
    /// Whether this breakpoint acts as a logpoint (logs without pausing).
    /// </summary>
    public bool IsLogpoint => LogMessage is not null;

    public Breakpoint(string file, int line)
    {
        Id = Interlocked.Increment(ref _nextId);
        File = file;
        Line = line;
        Verified = true;
    }

    /// <summary>
    /// Increments the hit count and returns the new value.
    /// </summary>
    public int IncrementHitCount() => ++HitCount;

    /// <summary>
    /// Resets the hit count to zero.
    /// </summary>
    public void ResetHitCount() => HitCount = 0;
}
