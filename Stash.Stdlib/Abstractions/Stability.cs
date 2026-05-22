namespace Stash.Stdlib.Abstractions;

/// <summary>
/// Controls how a <see cref="StashMemberAttribute"/>-decorated namespace member's getter
/// is invoked at runtime.
/// </summary>
public enum Stability
{
    /// <summary>
    /// Getter is invoked on first access; the result is stored in the namespace's frozen
    /// dictionary and returned for all subsequent accesses. Identity is stable for the
    /// process lifetime. This is the default.
    /// </summary>
    Cached,

    /// <summary>
    /// Getter is invoked on every access. No caching. Use when the underlying source can
    /// change during execution (e.g. <c>env.cwd</c> after <c>os.chdir</c>).
    /// </summary>
    Live,
}
