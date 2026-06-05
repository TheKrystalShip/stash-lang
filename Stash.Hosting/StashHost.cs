namespace Stash.Hosting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Stash.Bytecode;
using Stash.Hosting.Internal;
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

    // Per-host registration map: CLR Type → HostTypeRegistration.
    // Keyed by CLR Type so SetGlobal and HostMarshaller.ToStash can look up registrations.
    // Populated only via RegisterType<T> before VM materialisation; never mutated after the
    // first script runs. Per-instance state — never static — for hermetic isolation.
    private readonly Dictionary<Type, HostTypeRegistration> _typeRegistrations = new();

    // Per-host observed-targets table for OnRelease lifetime hooks (P4).
    // Keys: live CLR host instances seen by the engine (via SetGlobal or returned from a
    // method/property call). Values: the HostTypeRegistration for that instance's type
    // (carries the OnRelease callback, if any was registered).
    //
    // ConditionalWeakTable properties:
    // - Keys are held by WEAK reference — a target GC'd before DisposeAsync is simply absent.
    // - TryAdd is idempotent by reference identity — the same instance observed multiple
    //   times is registered only once.
    // - NOT static — per-host, so two hosts never share observed-target state.
    private readonly ConditionalWeakTable<object, HostTypeRegistration> _observedTargets = new();

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
    public void RegisterType<T>(Action<HostTypeBuilder<T>> configure) where T : class
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        ThrowIfDisposed();
        // After ThrowIfDisposed(), _engine is non-null.

        var builder = new HostTypeBuilder<T>();
        configure(builder);
        HostTypeRegistration reg = builder.Build();

        // Register with the VM engine for typeof / is support.
        // Predicate: the VM always sees a HostHandle in the Obj slot — never a bare T.
        // So the default predicate (obj => obj is T) would always return false.
        // We must check that the handle wraps the correct CLR type.
        try
        {
            _engine!.RegisterType<T>(
                vmTypeName: reg.VmTypeName,
                predicate:  obj => obj is HostHandle handle && handle.ClrType == typeof(T));
        }
        catch (InvalidOperationException ex)
        {
            // Re-surface with a clear host-level message that names the host contract.
            throw new InvalidOperationException(
                $"RegisterType<{typeof(T).Name}> must be called before the first " +
                $"CompileAsync / RunAsync / CallAsync. " +
                $"The underlying VM has already been created. ({ex.Message})",
                ex);
        }

        // Store in the per-host registration map (keyed by CLR Type) so that
        // HostMarshaller.ToStash and SetGlobal can look up registrations by instance type.
        _typeRegistrations[typeof(T)] = reg;
    }

    /// <inheritdoc/>
    public void SetGlobal(string name, object hostObject)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (hostObject is null) throw new ArgumentNullException(nameof(hostObject));
        ThrowIfDisposed();

        Type clrType = hostObject.GetType();
        if (!_typeRegistrations.TryGetValue(clrType, out HostTypeRegistration? reg))
        {
            throw new ArgumentException(
                $"Type '{clrType.FullName ?? clrType.Name}' has not been registered. " +
                $"Call RegisterType<{clrType.Name}>(...) before SetGlobal.",
                nameof(hostObject));
        }

        // Wrap in a HostHandle and bind as a VM global.
        // Passing _observedTargets registers the target for OnRelease callbacks at dispose.
        // StashValue.FromObject falls through to FromObj for unknown CLR types, so passing
        // the HostHandle to SetGlobal(string, object?) stores it as an Obj-tagged StashValue —
        // exactly what we need. The handle is always created here, keyed through the
        // registration map, so the single-chokepoint invariant is preserved.
        var handle = new HostHandle(hostObject, reg, _typeRegistrations, _observedTargets);
        _engine!.SetGlobal(name, handle);
    }

    /// <inheritdoc/>
    public Task<CompiledScript> CompileAsync(string source, CancellationToken ct = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        // Compilation is CPU-bound but fast (lex+parse+resolve), and it doesn't touch the
        // per-host engine — so it runs directly on the calling thread inside a completed task.
        StashScript? script = _engine!.Compile(source, out IReadOnlyList<string> errors);
        if (script is null || errors.Count > 0)
        {
            string msg = errors.Count > 0 ? string.Join("; ", errors) : "Compilation failed.";
            throw new StashScriptException(new StashError(
                StashError.KindParseError, msg, null, Array.Empty<StackFrameInfo>()));
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
                        new StashError(StashError.KindStepLimitExceeded, ex.Message,
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
                        new StashError(StashError.KindStepLimitExceeded, ex.Message,
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
            // When a mid-await cancel fires (e.g. while awaiting an async host method),
            // the VM's StashFuture.GetResult() converts OperationCanceledException to
            // RuntimeError("Future was cancelled."). The ct here is the call's
            // CancellationToken — if it fired, surface OCE rather than StashScriptException
            // so callers get the standard cancellation signal.
            // This does not affect the pure-script cancellation path (time.sleep etc.):
            // that path throws OCE directly from Task.Run without going through RuntimeError,
            // so it never reaches this catch.
            ct.ThrowIfCancellationRequested();
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
                        new StashError(StashError.KindStepLimitExceeded, ex.Message,
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

        // Invoke OnRelease callbacks for all host targets observed by this engine.
        // Iterate the ConditionalWeakTable: only entries still reachable (non-GC'd) are
        // visited, so GC'd targets are silently skipped.  Each target's registration
        // carries the OnRelease callback (null when none was registered).
        // Per-callback try/catch: a crashing release hook must not prevent the remaining
        // hooks — or the MVP's process-global resets below — from running.
        foreach (KeyValuePair<object, HostTypeRegistration> entry in _observedTargets)
        {
            if (entry.Value.OnRelease is { } release)
            {
                try
                {
                    release(entry.Key);
                }
                catch
                {
                    // Swallow — a crashing release hook is a host bug, but we must not
                    // let it abort disposal of the remaining cleanup pipeline.
                }
            }
        }

        // Null process-global static hook slots so subsequent test fixtures (and future
        // host instances) see a clean slate. These are CLI-only hooks; a pure embedder
        // never sets them, so resetting on dispose is test-isolation–friendly and safe.
        // (Decision logged in brief.md: "Disposal nulls process-global static hooks".)
        // done_when #5: MVP resets still run after the OnRelease loop.
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
                // A linked CTS scoped to this call is used so that when rawTask wins we can
                // cancel the Task.Delay immediately (via linked.Cancel()) and dispose the linked
                // CTS (via `using`) to remove its registration from ct.  Without this, a
                // Task.Delay(Infinite, ct) left as an orphan would hold a ct callback registration
                // for the lifetime of ct — accumulating on every call against a long-lived token.
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var cancelTask = Task.Delay(Timeout.Infinite, linked.Token);
                Task winner = await Task.WhenAny(rawTask, cancelTask).ConfigureAwait(false);
                if (winner == cancelTask)
                {
                    // ct fired before the future resolved — propagate as OCE.
                    ct.ThrowIfCancellationRequested();
                    // If ThrowIfCancellationRequested doesn't throw (race: ct was reset),
                    // fall through to await rawTask normally.
                }
                else
                {
                    // rawTask won — cancel the Task.Delay so it reaches a terminal state
                    // promptly rather than lingering until ct fires or is disposed.
                    // The `using` above will dispose linked (and unregister from ct) when
                    // the block exits.
                    linked.Cancel();
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

        // Convert the raw CLR value (object?) back to T via the single conversion chokepoint.
        // HostMarshaller.FromStashObject<T> is the only place in Stash.Hosting that calls
        // StashValue.FromObject, preserving the "single chokepoint" invariant.
        return HostMarshaller.FromStashObject<T>(rawResult)!;
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
