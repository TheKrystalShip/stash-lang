namespace Stash.Interpreting;

using System.IO;
using System.Text;

/// <summary>
/// A thread-safe <see cref="TextWriter"/> wrapper that serializes all write operations
/// through a shared lock. Used by <see cref="Interpreter.Fork"/> to prevent interleaved
/// output when multiple parallel tasks write to the same underlying writer.
/// </summary>
internal sealed class SynchronizedTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly object _lock;

    public SynchronizedTextWriter(TextWriter inner)
    {
        _inner = inner;
        _lock = new object();
    }

    /// <summary>The underlying writer being synchronized.</summary>
    internal TextWriter Inner => _inner;

    public override Encoding Encoding
    {
        get { lock (_lock) return _inner.Encoding; }
    }

    public override void Write(char value)
    {
        lock (_lock) _inner.Write(value);
    }

    public override void Write(string? value)
    {
        lock (_lock) _inner.Write(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (_lock) _inner.Write(buffer, index, count);
    }

    public override void WriteLine()
    {
        lock (_lock) _inner.WriteLine();
    }

    public override void WriteLine(string? value)
    {
        lock (_lock) _inner.WriteLine(value);
    }

    public override void Flush()
    {
        lock (_lock) _inner.Flush();
    }

    public override void Close()
    {
        lock (_lock) _inner.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock) _inner.Dispose();
        }
    }
}
