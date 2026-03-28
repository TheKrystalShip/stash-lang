namespace Stash.Testing;

/// <summary>
/// Backwards-compatibility shim — AssertionError now lives in Stash.Runtime.AssertionError.
/// </summary>
public class AssertionError : Stash.Runtime.AssertionError
{
    public AssertionError(string message, object? expected, object? actual, Stash.Common.SourceSpan? span = null)
        : base(message, expected, actual, span)
    {
    }
}
