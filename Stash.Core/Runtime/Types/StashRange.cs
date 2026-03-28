namespace Stash.Runtime.Types;

using System.Collections.Generic;

/// <summary>
/// Represents an exclusive range of integers: start..end or start..end..step
/// </summary>
public class StashRange
{
    public long Start { get; }
    public long End { get; }
    public long Step { get; }

    public StashRange(long start, long end, long step)
    {
        Start = start;
        End = end;
        Step = step;
    }

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
    }

    public override string ToString()
    {
        return Step == 1 ? $"{Start}..{End}" : $"{Start}..{End}..{Step}";
    }

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
