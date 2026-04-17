namespace Stash.Runtime.Types;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents an exclusive range of integers: start..end or start..end..step
/// </summary>
public class StashRange : IVMTyped, IVMIterable, IVMStringifiable
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

    /// <summary>
    /// Iterates the range yielding StashValue integers directly (no boxing).
    /// </summary>
    public IEnumerable<StashValue> IterateValues()
    {
        if (Step > 0)
        {
            for (long i = Start; i < End; i += Step)
                yield return StashValue.FromInt(i);
        }
        else if (Step < 0)
        {
            for (long i = Start; i > End; i += Step)
                yield return StashValue.FromInt(i);
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

    // --- VM Protocol Implementations ---

    public string VMTypeName => "range";

    public IVMIterator VMGetIterator(bool indexed) => new StashRangeIterator(this);

    public string VMToString() => ToString();
}

internal sealed class StashRangeIterator : IVMIterator
{
    private readonly StashRange _range;
    private int _index;
    private long _current;
    private bool _started;

    public StashRangeIterator(StashRange range)
    {
        _range = range;
        _index = -1;
        _current = range.Start;
        _started = false;
    }

    public bool MoveNext()
    {
        if (!_started)
        {
            _started = true;
            _index = 0;
            _current = _range.Start;
        }
        else
        {
            _index++;
            _current = _range.Start + _range.Step * _index;
        }

        return _range.Step > 0 ? _current < _range.End : _current > _range.End;
    }

    public StashValue Current => StashValue.FromInt(_current);
    public StashValue CurrentKey => StashValue.FromInt(_index);
}
