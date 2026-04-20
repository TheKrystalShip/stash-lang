namespace Stash.Runtime;

/// <summary>
/// Per-execution logger configuration: level, format, and output target.
/// Used by LogBuiltIns. State is per-execution-context (no process-global statics).
/// </summary>
public interface ILoggerContext
{
    LoggerState LoggerState { get; }
}
