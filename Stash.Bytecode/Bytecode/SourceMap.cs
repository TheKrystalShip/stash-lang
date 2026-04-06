using Stash.Common;

namespace Stash.Bytecode;

/// <summary>
/// Maps a bytecode offset to the original source location.
/// </summary>
public readonly struct SourceMapEntry
{
    /// <summary>Starting offset in the chunk's bytecode array.</summary>
    public readonly int BytecodeOffset;

    /// <summary>Original source location for this instruction.</summary>
    public readonly SourceSpan Span;

    public SourceMapEntry(int bytecodeOffset, SourceSpan span)
    {
        BytecodeOffset = bytecodeOffset;
        Span = span;
    }
}

/// <summary>
/// Maps bytecode offsets to source locations. Entries are sorted by BytecodeOffset.
/// Uses binary search for O(log n) lookups.
/// </summary>
public class SourceMap
{
    private readonly SourceMapEntry[] _entries;

    public SourceMap(SourceMapEntry[] entries)
    {
        _entries = entries;
#if DEBUG
        for (int i = 1; i < _entries.Length; i++)
        {
            System.Diagnostics.Debug.Assert(_entries[i].BytecodeOffset >= _entries[i - 1].BytecodeOffset,
                "SourceMap entries must be sorted by offset");
        }
#endif
    }

    /// <summary>Number of entries in this source map.</summary>
    public int Count => _entries.Length;

    /// <summary>Get entry by index (for disassembler iteration).</summary>
    public SourceMapEntry this[int index] => _entries[index];

    /// <summary>
    /// Finds the source span for the given bytecode offset.
    /// Returns the span of the entry with the largest offset that is &lt;= the given offset,
    /// or null if no entry covers this offset.
    /// </summary>
    public SourceSpan? GetSpan(int bytecodeOffset)
    {
        if (_entries.Length == 0)
        {
            return null;
        }

        int lo = 0;
        int hi = _entries.Length - 1;
        int result = -1;

        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_entries[mid].BytecodeOffset <= bytecodeOffset)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result >= 0 ? _entries[result].Span : null;
    }

    /// <summary>
    /// Returns the source line number for the given bytecode offset, or -1 if not found.
    /// </summary>
    public int GetLine(int bytecodeOffset)
    {
        var span = GetSpan(bytecodeOffset);
        return span?.StartLine ?? -1;
    }
}
