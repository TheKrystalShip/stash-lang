namespace Stash.Runtime.Types;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Protocols;

/// <summary>
/// Handle to a running external process (or pipe chain of processes) spawned by the
/// streaming command sigil (<c>$&lt;(cmd)</c> / <c>$!&lt;(cmd)</c> / <c>$&lt;(a | b | c)</c>
/// / <c>$!&lt;(a | b | c)</c>). Iterating it yields stdout lines (single mode) or
/// interleaved (out, err) pairs (dual mode).
/// <para>
/// For pipeline forms, intermediate stages are captured-piped to the next stage's stdin via
/// OS pipes, and the last stage's stdout becomes the streaming source. The handle's
/// <c>pid</c> / <c>exitCode</c> / <c>signal</c> reflect the last stage. Cleanup, .kill(),
/// and .wait() apply to every stage.
/// </para>
/// </summary>
public sealed class StashStreamingProcess
    : IVMTyped, IVMFieldAccessible, IVMTruthiness, IVMStringifiable,
      IVMIterable, IVMIterator, IDisposable
{
    private readonly IReadOnlyList<Process> _stages;
    private readonly Process _process; // alias for _stages[^1] (last stage)
    private readonly StreamReader _stdout;
    private readonly StringBuilder _stderrBuffer = new();
    private readonly string _command;
    private readonly bool _isStrict;
    private readonly SourceSpan? _span;
    private readonly Func<CancellationToken> _ctProvider;

    private bool _consumed;
    private bool _finalized;
    private bool _naturallyExhausted;
    private int? _exitCode;
    private string? _signalName;

    // Single-mode (line) iteration state.
    private string? _currentLine;
    private Task? _stderrDrainTask;

    // Dual-mode iteration state.
    private bool _dualMode;
    private string? _currentOut;
    private string? _currentErr;
    private BlockingCollection<(string? Out, string? Err)>? _dualChannel;
    private Task? _dualStdoutTask;
    private Task[]? _dualStderrTasks; // one per stage in pipeline (or single-element)

    public StashStreamingProcess(Process process, string command, bool isStrict, SourceSpan? span, Func<CancellationToken>? ctProvider = null)
        : this(new[] { process }, command, isStrict, span, ctProvider) { }

    public StashStreamingProcess(IReadOnlyList<Process> stages, string command, bool isStrict, SourceSpan? span, Func<CancellationToken>? ctProvider = null)
    {
        if (stages == null || stages.Count == 0)
            throw new ArgumentException("StashStreamingProcess requires at least one stage", nameof(stages));
        _stages = stages;
        _process = stages[stages.Count - 1];
        _stdout = _process.StandardOutput;
        _command = command;
        _isStrict = isStrict;
        _span = span;
        _ctProvider = ctProvider ?? (() => CancellationToken.None);
    }

    public string VMTypeName => "StreamingProcess";
    public bool VMIsFalsy => false;
    public string VMToString() => $"<StreamingProcess pid={_process.Id}>";

    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        switch (name)
        {
            case "pid":
                value = StashValue.FromInt(_process.Id);
                return true;
            case "pids":
            {
                var list = new List<StashValue>(_stages.Count);
                for (int i = 0; i < _stages.Count; i++)
                {
                    int pid;
                    try { pid = _stages[i].Id; } catch { pid = 0; }
                    list.Add(StashValue.FromInt(pid));
                }
                value = StashValue.FromObj(list);
                return true;
            }
            case "exitCode":
                value = _exitCode.HasValue ? StashValue.FromInt(_exitCode.Value) : StashValue.Null;
                return true;
            case "signal":
                value = _signalName is null
                    ? StashValue.Null
                    : StashValue.FromObj(new StashEnumValue("Signal", _signalName));
                return true;

            case "lines":
                value = StashValue.FromObj(new BuiltInFunction("lines", -1, (_, _) =>
                {
                    ConsumeOrThrow();
                    return StashValue.FromObj(new StreamingIterableWrapper(this, IterMode.Lines, 0, "\n"));
                }));
                return true;
            case "json":
                value = StashValue.FromObj(new BuiltInFunction("json", -1, (_, _) =>
                {
                    ConsumeOrThrow();
                    return StashValue.FromObj(new StreamingIterableWrapper(this, IterMode.Json, 0, "\n"));
                }));
                return true;
            case "bytes":
                value = StashValue.FromObj(new BuiltInFunction("bytes", -1, (_, args) =>
                {
                    if (args.Length != 1)
                        throw new RuntimeError("StreamingProcess.bytes(size) requires 1 argument", _span);
                    object? raw = args[0].ToObject();
                    if (raw is not long n || n <= 0)
                        throw new RuntimeError("StreamingProcess.bytes(size) requires a positive integer", _span);
                    ConsumeOrThrow();
                    return StashValue.FromObj(new StreamingIterableWrapper(this, IterMode.Bytes, (int)n, "\n"));
                }));
                return true;
            case "framed":
                value = StashValue.FromObj(new BuiltInFunction("framed", -1, (_, args) =>
                {
                    if (args.Length != 1)
                        throw new RuntimeError("StreamingProcess.framed(delim) requires 1 argument", _span);
                    if (args[0].ToObject() is not string delim || delim.Length == 0)
                        throw new RuntimeError("StreamingProcess.framed(delim) requires a non-empty string", _span);
                    ConsumeOrThrow();
                    return StashValue.FromObj(new StreamingIterableWrapper(this, IterMode.Framed, 0, delim));
                }));
                return true;
            case "kill":
                value = StashValue.FromObj(new BuiltInFunction("kill", -1, (_, args) =>
                {
                    string sigName = "Term";
                    if (args.Length >= 1)
                    {
                        if (args[0].ToObject() is StashEnumValue ev && ev.TypeName == "Signal")
                            sigName = ev.MemberName;
                        else if (args[0].Tag != StashValueTag.Null)
                            throw new RuntimeError("StreamingProcess.kill expects a Signal enum value", _span);
                    }
                    KillWithSignal(sigName);
                    return StashValue.Null;
                }));
                return true;
            case "wait":
                value = StashValue.FromObj(new BuiltInFunction("wait", -1, (_, _) =>
                {
                    if (!_consumed) _consumed = true;
                    EnsureCleanedUp(naturalExit: true);
                    return StashValue.FromInt(_exitCode ?? 0);
                }));
                return true;

            default:
                value = StashValue.Null;
                return false;
        }
    }

    // ── Iteration (parent acts as its own iterator for line / dual modes) ──

    public IVMIterator VMGetIterator(bool indexed)
    {
        ConsumeOrThrow();
        if (indexed)
        {
            _dualMode = true;
            StartDualPumps();
        }
        else
        {
            StartStderrDrain();
        }
        return this;
    }

    public StashValue Current
    {
        get
        {
            if (_dualMode)
                return _currentErr is null ? StashValue.Null : StashValue.FromObj(_currentErr);
            return _currentLine is null ? StashValue.Null : StashValue.FromObj(_currentLine);
        }
    }

    public StashValue CurrentKey
    {
        get
        {
            if (_dualMode)
                return _currentOut is null ? StashValue.Null : StashValue.FromObj(_currentOut);
            return StashValue.Null;
        }
    }

    public bool MoveNext()
    {
        if (_dualMode)
            return MoveNextDual();
        return MoveNextLine();
    }

    private bool MoveNextLine()
    {
        string? line;
        try { line = ReadLineCancellable(_ctProvider()); }
        catch (OperationCanceledException) { throw; }
        catch { line = null; }
        if (line is not null)
        {
            _currentLine = line;
            return true;
        }
        _naturallyExhausted = true;
        EnsureCleanedUp(naturalExit: true);
        if (_isStrict && _exitCode.GetValueOrDefault() != 0)
            ThrowStrictFailure();
        return false;
    }

    private bool MoveNextDual()
    {
        if (_dualChannel is null)
        {
            _naturallyExhausted = true;
            EnsureCleanedUp(naturalExit: true);
            return false;
        }
        try
        {
            var item = _dualChannel.Take(_ctProvider());
            _currentOut = item.Out;
            _currentErr = item.Err;
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (ObjectDisposedException) { /* fall through to natural-exit cleanup */ }
        catch (InvalidOperationException)
        {
            // CompleteAdding called — channel exhausted.
        }

        _naturallyExhausted = true;
        EnsureCleanedUp(naturalExit: true);
        if (_isStrict && _exitCode.GetValueOrDefault() != 0)
            ThrowStrictFailure();
        return false;
    }

    // ── Cleanup contract ────────────────────────────────────────────────

    internal void EnsureCleanedUp(bool naturalExit)
    {
        if (_finalized) return;
        _finalized = true;
        try
        {
            // For pipelines: signal all stages first, then wait, then SIGKILL survivors.
            // This minimizes total termination latency vs stage-by-stage handling.
            if (!naturalExit)
            {
                // Send SIGTERM (or platform equivalent) to every stage that's still alive.
                foreach (var p in _stages)
                {
                    try
                    {
                        if (!SafeHasExited(p))
                        {
                            if (OperatingSystem.IsWindows())
                            {
                                // No SIGTERM on Windows; rely on graceful close, then Kill below.
                            }
                            else
                            {
                                try { PosixKill(p.Id, 15 /* SIGTERM */); } catch { }
                            }
                        }
                    }
                    catch { }
                }
                // Wait up to 5 seconds for ALL stages to exit.
                var deadline = DateTime.UtcNow.AddSeconds(5);
                foreach (var p in _stages)
                {
                    int remainingMs = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    try { p.WaitForExit(remainingMs); } catch { }
                }
                // SIGKILL any survivors.
                foreach (var p in _stages)
                {
                    if (!SafeHasExited(p))
                    {
                        try
                        {
                            if (OperatingSystem.IsWindows())
                                p.Kill(entireProcessTree: true);
                            else
                                PosixKill(p.Id, 9 /* SIGKILL */);
                        }
                        catch { }
                    }
                }
            }

            // Final wait + drain task wait for all stages.
            foreach (var p in _stages)
            {
                try { p.WaitForExit(); } catch { }
            }
            try { _stderrDrainTask?.Wait(2000); } catch { }
            try { _dualStdoutTask?.Wait(2000); } catch { }
            if (_dualStderrTasks != null)
            {
                try { Task.WaitAll(_dualStderrTasks, 2000); } catch { }
            }
            try { _dualChannel?.CompleteAdding(); } catch { }
            try { _exitCode = _process.ExitCode; } catch { }
            PopulateSignalIfKilled();
        }
        finally
        {
            try { _stdout?.Dispose(); } catch { }
            // Dispose all stage processes & streams.
            foreach (var p in _stages)
            {
                try { p.StandardError?.Dispose(); } catch { }
                try { p.Dispose(); } catch { }
            }
            try { _dualChannel?.Dispose(); } catch { }
        }
    }

    public void Dispose() => EnsureCleanedUp(naturalExit: _naturallyExhausted);

    private static bool SafeHasExited(Process p)
    {
        try { return p.HasExited; } catch { return true; }
    }

    private void PopulateSignalIfKilled()
    {
        if (OperatingSystem.IsWindows()) return;
        if (!_exitCode.HasValue) return;
        int code = _exitCode.Value;
        if (code <= 128 || code >= 256) return;
        int sig = code - 128;
        _signalName = sig switch
        {
            1 => "Hup",
            2 => "Int",
            3 => "Quit",
            9 => "Kill",
            10 => "Usr1",
            12 => "Usr2",
            15 => "Term",
            _ => null,
        };
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int PosixKill(int pid, int sig);

    private void KillWithSignal(string sigName)
    {
        if (_exitCode.HasValue) return;

        if (OperatingSystem.IsWindows())
        {
            if (sigName != "Term" && sigName != "Kill")
                throw new RuntimeError($"signal {sigName} not supported on Windows", _span,
                    StashErrorTypes.NotSupportedError);
            foreach (var p in _stages)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }
            return;
        }

        int num = sigName switch
        {
            "Hup" => 1,
            "Int" => 2,
            "Quit" => 3,
            "Kill" => 9,
            "Usr1" => 10,
            "Usr2" => 12,
            "Term" => 15,
            _ => 15,
        };
        // Pipelines: signal every stage, not just the last (the handle represents the
        // whole pipeline; killing only the last would leave intermediate stages alive).
        foreach (var p in _stages)
        {
            try { PosixKill(p.Id, num); } catch { }
        }
    }

    // ── Background pump helpers ─────────────────────────────────────────

    private void ConsumeOrThrow()
    {
        if (_consumed)
            throw new StateError(
                "StreamingProcess has already been consumed",
                _span);
        _consumed = true;
    }

    private string? ReadLineCancellable(CancellationToken ct)
    {
        var task = _stdout.ReadLineAsync();
        while (!task.IsCompleted)
        {
            try { task.Wait(50, ct); }
            catch (OperationCanceledException) { throw; }
        }
        return task.Result;
    }

    internal int ReadCharCancellable(char[] one, CancellationToken ct)
    {
        var task = _stdout.ReadAsync(one, 0, 1);
        while (!task.IsCompleted)
        {
            try { task.Wait(50, ct); }
            catch (OperationCanceledException) { throw; }
        }
        return task.Result;
    }

    internal void StartStderrDrain()
    {
        if (_stderrDrainTask is not null) return;
        // Drain stderr from EVERY stage to prevent any stage's pipe buffer from filling.
        var drainTasks = new Task[_stages.Count];
        for (int i = 0; i < _stages.Count; i++)
        {
            var reader = _stages[i].StandardError;
            drainTasks[i] = Task.Run(async () =>
            {
                try
                {
                    char[] buf = new char[4096];
                    int n;
                    while ((n = await reader.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                    {
                        lock (_stderrBuffer) _stderrBuffer.Append(buf, 0, n);
                    }
                }
                catch { /* swallow — reported via CommandError if strict */ }
            });
        }
        _stderrDrainTask = Task.WhenAll(drainTasks);
    }

    private void StartDualPumps()
    {
        if (_dualChannel is not null) return;
        _dualChannel = new BlockingCollection<(string? Out, string? Err)>(boundedCapacity: 256);

        // Pump last stage's stdout into the channel.
        _dualStdoutTask = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = _stdout.ReadLine()) is not null)
                {
                    try { _dualChannel.Add((line, null)); }
                    catch { return; }
                }
            }
            catch { }
        });

        // Pump EVERY stage's stderr into the channel (interleaved arrival order).
        _dualStderrTasks = new Task[_stages.Count];
        for (int i = 0; i < _stages.Count; i++)
        {
            var reader = _stages[i].StandardError;
            _dualStderrTasks[i] = Task.Run(() =>
            {
                try
                {
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        try { _dualChannel.Add((null, line)); }
                        catch { return; }
                    }
                }
                catch { }
            });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var all = new List<Task> { _dualStdoutTask! };
                all.AddRange(_dualStderrTasks!);
                await Task.WhenAll(all).ConfigureAwait(false);
            }
            catch { }
            try { _dualChannel.CompleteAdding(); } catch { }
        });
    }

    internal void ThrowStrictFailure()
    {
        string stderrText;
        lock (_stderrBuffer) stderrText = _stderrBuffer.ToString();
        throw new CommandError(
            $"Command failed with exit code {_exitCode}: {_command}",
            exitCode: (long)_exitCode!.Value,
            stderr: stderrText,
            stdout: "",
            command: _command,
            span: _span);
    }

    internal enum IterMode { Lines, Json, Bytes, Framed }

    /// <summary>
    /// Iterable wrapper returned by the framing methods (.lines(), .json(),
    /// .bytes(n), .framed(d)). Reads from the parent process's stdout in the
    /// requested mode and cleans up the parent on disposal/exhaustion.
    /// </summary>
    internal sealed class StreamingIterableWrapper
        : IVMIterable, IVMIterator, IDisposable
    {
        private readonly StashStreamingProcess _parent;
        private readonly IterMode _mode;
        private readonly int _chunkSize;
        private readonly string _delim;
        private bool _exhausted;
        private bool _disposed;
        private object? _currentValue;
        private readonly StringBuilder _frameBuf = new();

        public StreamingIterableWrapper(StashStreamingProcess parent, IterMode mode, int chunkSize, string delim)
        {
            _parent = parent;
            _mode = mode;
            _chunkSize = chunkSize;
            _delim = delim;
            _parent.StartStderrDrain();
        }

        public IVMIterator VMGetIterator(bool indexed) => this;

        public StashValue Current => _currentValue is null ? StashValue.Null : StashValue.FromObject(_currentValue);
        public StashValue CurrentKey => StashValue.Null;

        public bool MoveNext()
        {
            switch (_mode)
            {
                case IterMode.Lines:  return MoveNextLines();
                case IterMode.Json:   return MoveNextJson();
                case IterMode.Bytes:  return MoveNextBytes();
                case IterMode.Framed: return MoveNextFramed();
            }
            return false;
        }

        private bool MoveNextLines()
        {
            string? line;
            try { line = _parent.ReadLineCancellable(_parent._ctProvider()); }
            catch (OperationCanceledException) { throw; }
            catch { line = null; }
            if (line is null)
            {
                FinishNaturally();
                return false;
            }
            _currentValue = line;
            return true;
        }

        private bool MoveNextJson()
        {
            string? line;
            try { line = _parent.ReadLineCancellable(_parent._ctProvider()); }
            catch (OperationCanceledException) { throw; }
            catch { line = null; }
            if (line is null)
            {
                FinishNaturally();
                return false;
            }
            try
            {
                using var doc = JsonDocument.Parse(line);
                _currentValue = ConvertJsonElement(doc.RootElement);
                return true;
            }
            catch (JsonException ex)
            {
                FinishNaturally();
                throw new ParseError(
                    "StreamingProcess.json: malformed JSON line — " + ex.Message,
                    _parent._span);
            }
        }

        private bool MoveNextBytes()
        {
            // Use the underlying byte stream directly. NOTE: this assumes no prior
            // reads via the StreamReader; mixing line and byte modes is unsupported.
            var stream = _parent._stdout.BaseStream;
            byte[] buf = new byte[_chunkSize];
            int read = 0;
            try
            {
                while (read < _chunkSize)
                {
                    int n;
                    try
                    {
                        n = stream.ReadAsync(buf.AsMemory(read, _chunkSize - read), _parent._ctProvider()).AsTask().GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { n = 0; }
                    if (n <= 0) break;
                    read += n;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                FinishNaturally();
                return false;
            }
            if (read == 0)
            {
                FinishNaturally();
                return false;
            }
            if (read < _chunkSize)
            {
                byte[] trimmed = new byte[read];
                Array.Copy(buf, trimmed, read);
                _currentValue = trimmed;
            }
            else
            {
                _currentValue = buf;
            }
            return true;
        }

        private bool MoveNextFramed()
        {
            char[] one = new char[1];
            while (true)
            {
                int n;
                try { n = _parent.ReadCharCancellable(one, _parent._ctProvider()); }
                catch (OperationCanceledException) { throw; }
                catch { n = 0; }
                if (n <= 0)
                {
                    if (_frameBuf.Length > 0)
                    {
                        _currentValue = _frameBuf.ToString();
                        _frameBuf.Clear();
                        return true;
                    }
                    FinishNaturally();
                    return false;
                }
                _frameBuf.Append(one[0]);
                if (EndsWith(_frameBuf, _delim))
                {
                    _frameBuf.Length -= _delim.Length;
                    _currentValue = _frameBuf.ToString();
                    _frameBuf.Clear();
                    return true;
                }
            }
        }

        private static bool EndsWith(StringBuilder sb, string s)
        {
            if (sb.Length < s.Length) return false;
            int offset = sb.Length - s.Length;
            for (int i = 0; i < s.Length; i++)
                if (sb[offset + i] != s[i]) return false;
            return true;
        }

        private void FinishNaturally()
        {
            _exhausted = true;
            _parent.EnsureCleanedUp(naturalExit: true);
            if (_parent._isStrict && _parent._exitCode.GetValueOrDefault() != 0)
                _parent.ThrowStrictFailure();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _parent.EnsureCleanedUp(naturalExit: _exhausted);
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String: return element.GetString();
            case JsonValueKind.Number: return element.TryGetInt64(out long l) ? (object)l : element.GetDouble();
            case JsonValueKind.True:   return true;
            case JsonValueKind.False:  return false;
            case JsonValueKind.Null:   return null;
            case JsonValueKind.Array:
            {
                var list = new List<StashValue>();
                foreach (var item in element.EnumerateArray())
                    list.Add(StashValue.FromObject(ConvertJsonElement(item)));
                return list;
            }
            case JsonValueKind.Object:
            {
                var dict = new StashDictionary();
                foreach (var prop in element.EnumerateObject())
                    dict.Set(prop.Name, StashValue.FromObject(ConvertJsonElement(prop.Value)));
                return dict;
            }
            default: return null;
        }
    }
}
