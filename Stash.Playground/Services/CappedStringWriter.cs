using System.Text;

namespace Stash.Playground.Services;

/// <summary>
/// A TextWriter that stops writing after a maximum character limit is reached.
/// Prevents runaway scripts from consuming unbounded memory in the browser.
/// </summary>
public sealed class CappedStringWriter : TextWriter
{
    private readonly StringBuilder _sb = new();
    private readonly long _maxLength;

    public CappedStringWriter(long maxLength)
    {
        _maxLength = maxLength;
    }

    public bool IsTruncated { get; private set; }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (IsTruncated) return;

        if (_sb.Length >= _maxLength)
        {
            IsTruncated = true;
            return;
        }

        _sb.Append(value);
    }

    public override void Write(string? value)
    {
        if (value is null || IsTruncated) return;

        long remaining = _maxLength - _sb.Length;
        if (remaining <= 0)
        {
            IsTruncated = true;
            return;
        }

        if (value.Length <= remaining)
        {
            _sb.Append(value);
        }
        else
        {
            _sb.Append(value.AsSpan(0, (int)remaining));
            IsTruncated = true;
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        if (IsTruncated) return;

        long remaining = _maxLength - _sb.Length;
        if (remaining <= 0)
        {
            IsTruncated = true;
            return;
        }

        if (count <= remaining)
        {
            _sb.Append(buffer, index, count);
        }
        else
        {
            _sb.Append(buffer, index, (int)remaining);
            IsTruncated = true;
        }
    }

    public override string ToString() => _sb.ToString();
}
