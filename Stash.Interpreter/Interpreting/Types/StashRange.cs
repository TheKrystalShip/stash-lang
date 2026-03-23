namespace Stash.Interpreting.Types;

using System.Collections.Generic;

/// <summary>
/// Represents an exclusive range of integers: start..end or start..end..step
/// </summary>
public class StashRange
{
    /// <summary>
    /// Gets the inclusive start value of the range.
    /// </summary>
    public long Start { get; }

    /// <summary>
    /// Gets the exclusive end value of the range.
    /// </summary>
    public long End { get; }

    /// <summary>
    /// Gets the step increment used when iterating the range.
    /// </summary>
    public long Step { get; }

    /// <summary>
    /// Initializes a new <see cref="StashRange"/> with the given start, end, and step values.
    /// </summary>
    /// <param name="start">The inclusive start value of the range.</param>
    /// <param name="end">The exclusive end value of the range.</param>
    /// <param name="step">The step increment to advance by on each iteration.</param>
    public StashRange(long start, long end, long step)
    {
        Start = start;
        End = end;
        Step = step;
    }

    /// <summary>
    /// Iterates over all values in the range, advancing by <see cref="Step"/> each time.
    /// </summary>
    /// <returns>
    /// An <see cref="IEnumerable{T}"/> of <see cref="long"/> values (boxed as <c>object?</c>)
    /// from <see cref="Start"/> up to (but not including) <see cref="End"/>.
    /// Produces no values when <see cref="Step"/> is zero.
    /// </returns>
    public IEnumerable<object?> Iterate()
    {
        if (Step > 0)
        {
            for (long i = Start; i < End; i += Step)
            {
                yield return i;
            }
        }
        else if (Step < 0)
        {
            for (long i = Start; i > End; i += Step)
            {
                yield return i;
            }
        }
        // Step == 0 produces nothing (infinite loop protection)
    }

    /// <summary>
    /// Returns a Stash source-style string representation of the range.
    /// </summary>
    /// <returns>
    /// A string of the form <c>start..end</c> when <see cref="Step"/> is 1,
    /// or <c>start..end..step</c> otherwise.
    /// </returns>
    public override string ToString()
    {
        return Step == 1 ? $"{Start}..{End}" : $"{Start}..{End}..{Step}";
    }

    /// <summary>
    /// Determines whether the given value falls on a step-aligned position within the range.
    /// </summary>
    /// <param name="value">The integer value to test for membership.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="value"/> is reachable by iterating from
    /// <see cref="Start"/> with the configured <see cref="Step"/>; otherwise <see langword="false"/>.
    /// </returns>
    public bool Contains(long value)
    {
        if (Step > 0)
        {
            if (value < Start || value >= End)
            {
                return false;
            }
        }
        else if (Step < 0)
        {
            if (value > Start || value <= End)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        return (value - Start) % Step == 0;
    }
}
