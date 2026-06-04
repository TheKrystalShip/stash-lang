namespace Stash.Hosting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Stash.Bytecode;
using Stash.Hosting.Marshalling;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib.BuiltIns;

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
                engine.CancellationToken = ct;
                try
                {
                    StashValue rawValue = engine.RunRaw(script.Inner);
                    return (rawValue, (IReadOnlyList<StashError>)Array.Empty<StashError>());
                }
                catch (RuntimeError ex)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[] { BuildStashError(ex) });
                }
                catch (OperationCanceledException)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[]
                    {
                        new StashError(StashError.KindCancelled, "Script execution was cancelled.",
                            null, Array.Empty<StackFrameInfo>())
                    });
                }
                catch (StepLimitExceededException ex)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[]
                    {
                        new StashError("StepLimitExceeded", ex.Message,
                            null, Array.Empty<StackFrameInfo>())
                    });
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
                    return (rawValue, (IReadOnlyList<StashError>)Array.Empty<StashError>());
                }
                catch (RuntimeError ex)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[] { BuildStashError(ex) });
                }
                catch (OperationCanceledException)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[]
                    {
                        new StashError(StashError.KindCancelled, "Script execution was cancelled.",
                            null, Array.Empty<StackFrameInfo>())
                    });
                }
                catch (StepLimitExceededException ex)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[]
                    {
                        new StashError("StepLimitExceeded", ex.Message,
                            null, Array.Empty<StackFrameInfo>())
                    });
                }
            }, ct).ConfigureAwait(false);

            bool success = errors.Count == 0;
            if (!success)
                return new StashResult<T>(false, default, errors);

            T? converted = HostMarshaller.FromStash<T>(value);
            return new StashResult<T>(true, converted, Array.Empty<StashError>());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<T> CallAsync<T>(string fn, object? args = null, CancellationToken ct = default)
    {
        if (fn is null) throw new ArgumentNullException(nameof(fn));
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        // After WaitAsync returns without throwing, we own the semaphore.
        try
        {
            StashEngine engine = _engine!;
            StashValue[] marshalledArgs = HostMarshaller.ToStashArgs(args);
            StashValue result = await Task.Run(() =>
            {
                engine.CancellationToken = ct;
                // RuntimeError and OperationCanceledException propagate raw.
                // RuntimeError is caught OUTSIDE the Task.Run (see catch below).
                // OCE is let through as-is — CallAsync<T> propagates it to the caller.
                return engine.CallFunction(fn, marshalledArgs);
            }, ct).ConfigureAwait(false);

            return HostMarshaller.FromStash<T>(result)!;
        }
        catch (RuntimeError ex)
        {
            throw new StashScriptException(BuildStashError(ex));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<StashResult<T>> TryCallAsync<T>(string fn, object? args = null, CancellationToken ct = default)
    {
        if (fn is null) throw new ArgumentNullException(nameof(fn));
        ThrowIfDisposed();

        // TryCallAsync never throws on script-level failure or cancellation.
        // Use a two-phase cancel check: WaitAsync with the ct (may throw OCE if already
        // cancelled), then run the body. Both OCE sources are caught below.
        bool acquired = false;
        try
        {
            try
            {
                await _gate.WaitAsync(ct).ConfigureAwait(false);
                acquired = true;
            }
            catch (OperationCanceledException)
            {
                return MakeCancelledResult<T>("Semaphore wait was cancelled.");
            }

            StashEngine engine = _engine!;
            StashValue[] marshalledArgs = HostMarshaller.ToStashArgs(args);

            // Run in Task.Run without the ct so that inner OCE comes only from the
            // CancellationToken the VM sees (engine.CancellationToken = ct), not from
            // Task.Run's early-cancel fast path (which would also throw OCE but bypass
            // the inner try/catch below).
            var (value, errors) = await Task.Run(() =>
            {
                engine.CancellationToken = ct;
                try
                {
                    StashValue raw = engine.CallFunction(fn, marshalledArgs);
                    return (raw, (IReadOnlyList<StashError>)Array.Empty<StashError>());
                }
                catch (RuntimeError ex)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[] { BuildStashError(ex) });
                }
                catch (OperationCanceledException)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[]
                    {
                        new StashError(StashError.KindCancelled, "Operation was cancelled.",
                            null, Array.Empty<StackFrameInfo>())
                    });
                }
                catch (StepLimitExceededException ex)
                {
                    return (StashValue.Null, (IReadOnlyList<StashError>)new[]
                    {
                        new StashError("StepLimitExceeded", ex.Message,
                            null, Array.Empty<StackFrameInfo>())
                    });
                }
            }).ConfigureAwait(false);

            bool success = errors.Count == 0;
            if (!success)
                return new StashResult<T>(false, default, errors);

            T? converted = HostMarshaller.FromStash<T>(value);
            return new StashResult<T>(true, converted, Array.Empty<StashError>());
        }
        finally
        {
            if (acquired) _gate.Release();
        }
    }

    private static StashResult<T> MakeCancelledResult<T>(string message)
    {
        var errors = new[] { new StashError(StashError.KindCancelled, message, null, Array.Empty<StackFrameInfo>()) };
        return new StashResult<T>(false, default, errors);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _engine = null;

        // Null process-global static hook slots so subsequent test fixtures (and future
        // host instances) see a clean slate. These are CLI-only hooks; a pure embedder
        // never sets them, so resetting on dispose is test-isolation–friendly and safe.
        // (Decision logged in brief.md: "Disposal nulls process-global static hooks".)
        PromptBuiltIns.ResetPromptFn();
        PromptBuiltIns.ResetContinuationFn();
        PromptBuiltIns.ResetBootstrapHandler = null;
        ProcessBuiltIns.HistoryListProvider = null;
        ProcessBuiltIns.HistoryClearHandler = null;
        ProcessBuiltIns.HistoryAddHandler = null;
        CompleteBuiltIns.ResetAllForTesting();

        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<T> InvokeAsync<T>(StashFuture future, CancellationToken ct = default)
    {
        if (future is null) throw new ArgumentNullException(nameof(future));

        // Bridge an already-resolving future to CLR Task<T>.
        // We do NOT drain the per-VM event-loop callback queue — this is deliberate
        // v1 contract (see IStashHost.InvokeAsync XML doc and brief.md Non-Goals).
        // If the future depends on an undelivered callback, this call will hang until
        // ct fires.
        Task<object?> rawTask = future.DotNetTask;

        object? rawResult;
        try
        {
            // Honor ct: check immediately (pre-cancelled token fires at the first opportunity),
            // then race the future against cancellation if it has not resolved yet.
            ct.ThrowIfCancellationRequested();

            if (ct.CanBeCanceled && !rawTask.IsCompleted)
            {
                // Race: await whichever of (future completion, cancellation) wins first.
                // Task.Delay(Infinite, ct) completes as cancelled when ct fires.
                var cancelTask = Task.Delay(Timeout.Infinite, ct);
                Task winner = await Task.WhenAny(rawTask, cancelTask).ConfigureAwait(false);
                if (winner == cancelTask)
                {
                    // ct fired before the future resolved — propagate as OCE.
                    ct.ThrowIfCancellationRequested();
                    // If ThrowIfCancellationRequested doesn't throw (race: ct was reset),
                    // fall through to await rawTask normally.
                }
            }

            rawResult = await rawTask.ConfigureAwait(false);
        }
        catch (RuntimeError ex)
        {
            throw new StashScriptException(BuildStashError(ex));
        }
        catch (AggregateException ae) when (ae.InnerException is RuntimeError re)
        {
            throw new StashScriptException(BuildStashError(re));
        }

        // Convert the raw CLR value (object?) back to StashValue, then to T.
        StashValue stashResult = StashValue.FromObject(rawResult);
        return HostMarshaller.FromStash<T>(stashResult)!;
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
    /// Converts a <see cref="RuntimeError"/> to a structured <see cref="StashError"/>.
    /// The <see cref="StashError.Kind"/> is derived from
    /// <see cref="RuntimeError.ErrorType"/> (which calls
    /// <see cref="BuiltInErrorRegistry.NameOf"/> for built-in errors and returns the
    /// user-supplied type name for <c>throw { type: "..." }</c> errors). The
    /// <see cref="StashError.CallStack"/> is projected from
    /// <see cref="RuntimeError.CallStack"/> if populated by the VM.
    /// </summary>
    private static StashError BuildStashError(RuntimeError error)
    {
        // Kind: error.ErrorType is the canonical name (BuiltInErrorRegistry.NameOf for
        // built-ins, user-supplied type for UserRuntimeError). No inline string constant.
        string kind = error.ErrorType;

        // CallStack: projected from the VM-captured List<StackFrame> if present.
        IReadOnlyList<StackFrameInfo> callStack = Array.Empty<StackFrameInfo>();
        if (error.CallStack is { Count: > 0 })
        {
            var frames = new List<StackFrameInfo>(error.CallStack.Count);
            foreach (Stash.Runtime.StackFrame sf in error.CallStack)
            {
                if (sf.FunctionName == "<truncated>") continue; // skip sentinel
                frames.Add(new StackFrameInfo(
                    sf.Span.File,
                    sf.Span.StartLine,
                    sf.Span.StartColumn,
                    sf.FunctionName));
            }
            callStack = frames;
        }

        return new StashError(kind, error.Message, error.Span, callStack);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StashHost));
    }
}
