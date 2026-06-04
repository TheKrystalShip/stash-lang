namespace Stash.Hosting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Stash.Bytecode;
using Stash.Runtime;

/// <summary>
/// Default implementation of <see cref="IStashHost"/>.
/// </summary>
/// <remarks>
/// One host owns one <see cref="StashEngine"/>. Two hosts in the same process share no
/// observable state (hermetic-VM foundation). Sequential calls on the same host accumulate
/// global state; the only reset is dispose-and-create-new (deliberate v1 lua_State contract).
/// </remarks>
public sealed class StashHost : IStashHost
{
    private StashEngine? _engine;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly StashHostOptions _options;
    private bool _disposed;

    /// <summary>
    /// Creates a new <see cref="StashHost"/> with default options
    /// (all capabilities, no step limit, output discarded).
    /// </summary>
    public StashHost() : this(new StashHostOptions())
    {
    }

    /// <summary>
    /// Creates a new <see cref="StashHost"/> with the specified options.
    /// </summary>
    /// <param name="options">Configuration options for this host.</param>
    public StashHost(StashHostOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _engine = CreateEngine(options);
    }

    // ── IStashHost ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<CompiledScript> CompileAsync(string source, CancellationToken ct = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        ThrowIfDisposed();

        // Compilation is CPU-bound but fast (lex+parse+resolve), and it doesn't touch the
        // per-host engine — so it runs directly on the calling thread inside a completed task.
        StashScript? script = _engine!.Compile(source, out IReadOnlyList<string> errors);
        if (script is null || errors.Count > 0)
        {
            string msg = errors.Count > 0 ? errors[0] : "Compilation failed.";
            throw new InvalidOperationException($"Stash compilation failed: {msg}");
        }

        return Task.FromResult(new CompiledScript(script));
    }

    /// <inheritdoc/>
    public async Task<StashResult> RunAsync(CompiledScript script, CancellationToken ct = default)
    {
        if (script is null) throw new ArgumentNullException(nameof(script));
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            StashEngine engine = _engine!;
            var (value, errors) = await Task.Run(() =>
            {
                // Best-effort per-call cancellation: setting the engine property updates
                // _cancellationToken for any future VM creation; the VM's own _ct field is
                // private and frozen at construction time.  The SemaphoreSlim(1,1) guarantees
                // only one RunAsync is in-flight, so this assignment is safe.
                engine.CancellationToken = ct;
                try
                {
                    StashValue rawValue = engine.RunRaw(script.Inner);
                    return (rawValue, (IReadOnlyList<string>)Array.Empty<string>());
                }
                catch (RuntimeError ex)
                {
                    return (StashValue.Null, (IReadOnlyList<string>)new[] { ex.Message });
                }
                catch (OperationCanceledException)
                {
                    return (StashValue.Null, (IReadOnlyList<string>)new[] { "Script execution was cancelled." });
                }
                catch (StepLimitExceededException ex)
                {
                    return (StashValue.Null, (IReadOnlyList<string>)new[] { ex.Message });
                }
            }, ct).ConfigureAwait(false);

            bool success = errors.Count == 0;
            return new StashResult(success, success ? value : null, errors);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<StashResult<T>> RunAsync<T>(CompiledScript script, CancellationToken ct = default)
    {
        if (script is null) throw new ArgumentNullException(nameof(script));
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            StashEngine engine = _engine!;
            var (value, errors) = await Task.Run(() =>
            {
                engine.CancellationToken = ct;
                try
                {
                    StashValue rawValue = engine.RunRaw(script.Inner);
                    return (rawValue, (IReadOnlyList<string>)Array.Empty<string>());
                }
                catch (RuntimeError ex)
                {
                    return (StashValue.Null, (IReadOnlyList<string>)new[] { ex.Message });
                }
                catch (OperationCanceledException)
                {
                    return (StashValue.Null, (IReadOnlyList<string>)new[] { "Script execution was cancelled." });
                }
                catch (StepLimitExceededException ex)
                {
                    return (StashValue.Null, (IReadOnlyList<string>)new[] { ex.Message });
                }
            }, ct).ConfigureAwait(false);

            bool success = errors.Count == 0;
            if (!success)
                return new StashResult<T>(false, default, errors);

            T? converted = ConvertValue<T>(value);
            return new StashResult<T>(true, converted, Array.Empty<string>());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _engine = null;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static StashEngine CreateEngine(StashHostOptions options)
    {
        var engine = new StashEngine(options.Capabilities);
        engine.Output      = options.Output      ?? TextWriter.Null;
        engine.ErrorOutput = options.ErrorOutput ?? TextWriter.Null;
        engine.StepLimit   = options.StepLimit;
        return engine;
    }

    /// <summary>
    /// Minimal P1 conversion: <typeparamref name="T"/> = <see cref="StashValue"/> is a passthrough;
    /// primitives are unboxed from <see cref="StashValue.ToObject"/>.
    /// The full <c>HostMarshaller</c> chokepoint (with anonymous-object, dict, list, JsonElement
    /// support) lands in P2.
    /// </summary>
    private static T? ConvertValue<T>(StashValue value)
    {
        if (typeof(T) == typeof(StashValue))
            return (T)(object)value;

        object? raw = value.ToObject();
        if (raw is null)
            return default;

        if (raw is T direct)
            return direct;

        // Allow widening numeric conversions for the common int → long case.
        if (raw is IConvertible conv)
        {
            try
            {
                return (T)System.Convert.ChangeType(conv, typeof(T),
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (InvalidCastException) { }
            catch (OverflowException) { }
            catch (FormatException) { }
        }

        throw new InvalidCastException(
            $"Cannot convert Stash value of type '{raw.GetType().Name}' to '{typeof(T).Name}'. " +
            $"Full marshalling support (POCO, anonymous objects, JsonElement) lands in P2.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StashHost));
    }
}
