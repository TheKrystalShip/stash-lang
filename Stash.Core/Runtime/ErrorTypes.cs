namespace Stash.Runtime;

/// <summary>
/// String constants for all built-in Stash error types.
/// Use these instead of inline string literals when throwing <see cref="RuntimeError"/>
/// to ensure error type names are centralised and consistent.
/// </summary>
public static class StashErrorTypes
{
    public const string ValueError        = "ValueError";
    public const string TypeError         = "TypeError";
    public const string ParseError        = "ParseError";
    public const string IndexError        = "IndexError";
    public const string IOError           = "IOError";
    public const string NotSupportedError = "NotSupportedError";
    public const string TimeoutError      = "TimeoutError";
    public const string CommandError      = "CommandError";
    public const string LockError         = "LockError";
}
