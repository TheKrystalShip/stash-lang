namespace Stash.Runtime;

using Stash.Common;

/// <summary>
/// A runtime error representing a failed assertion. Carries structured data
/// (expected vs actual) for rich test reporting.
/// </summary>
public class AssertionError : RuntimeError
{
    /// <summary>
    /// The expected value (may be null for assertions like assert.true).
    /// </summary>
    public object? Expected { get; }

    /// <summary>
    /// The actual value encountered.
    /// </summary>
    public object? Actual { get; }

    public AssertionError(string message, object? expected, object? actual, SourceSpan? span = null)
        : base(message, span)
    {
        Expected = expected;
        Actual = actual;
    }
}
