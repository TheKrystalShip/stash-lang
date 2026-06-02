namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Shared signal-handler state and implementations for both <c>signal.*</c>
/// and the deprecated <c>sys.*</c> shims.
/// </summary>
internal static class SignalImpl
{
    /// <summary>
    /// Multiplexed per-signal handler registry.
    /// Each signal name maps to a list of registered entries in registration order.
    /// All entries for the same signal share the same <see cref="PosixSignalRegistration"/> reference
    /// (the OS registration is created once on the first registration for a signal and disposed
    /// when the last entry is removed).
    /// Access must be under <see cref="SignalLock"/>.
    /// </summary>
    internal static readonly ConcurrentDictionary<string, List<(IInterpreterContext Context, IStashCallable Handler, PosixSignalRegistration? Registration)>>
        SignalHandlers = new();

    internal static readonly object SignalLock = new();

    internal static StashValue OnSignal(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var signal = SvArgs.EnumValue(args, 0, "Signal", callerQualified);
        var handler = SvArgs.Callable(args, 1, callerQualified);

        string name = signal.MemberName;

        lock (SignalLock)
        {
            // Get or create the list for this signal.
            if (!SignalHandlers.TryGetValue(name, out var entries))
            {
                entries = new List<(IInterpreterContext, IStashCallable, PosixSignalRegistration?)>();
                SignalHandlers[name] = entries;
            }

            // Determine the registration to share across all entries for this signal.
            // If entries already exist, reuse their shared registration.
            // If this is the first entry, create a new OS registration.
            PosixSignalRegistration? registration = entries.Count > 0
                ? entries[0].Registration
                : null;

            if (entries.Count == 0)
            {
                // First registration for this signal — create the OS registration.
                PosixSignal? posixSignal = MapToPosixSignal(name);
                if (posixSignal is not null)
                {
                    try
                    {
                        registration = PosixSignalRegistration.Create(posixSignal.Value, context =>
                        {
                            context.Cancel = true;
                            Dispatch(name);
                        });
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // Signal not supported on this platform — store handler but no registration.
                    }
                }
            }

            entries.Add((ctx, handler, registration));
        }

        return StashValue.Null;
    }

    internal static StashValue OffSignal(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var signal = SvArgs.EnumValue(args, 0, "Signal", callerQualified);
        string name = signal.MemberName;

        lock (SignalLock)
        {
            if (!SignalHandlers.TryGetValue(name, out var entries))
                return StashValue.Null;

            // Capture the shared registration before we remove entries (all entries share the same ref).
            PosixSignalRegistration? sharedReg = entries.Count > 0 ? entries[0].Registration : null;

            // Remove entries whose context identity matches the calling VM's context.
            // Other VMs' handlers must remain registered.
            entries.RemoveAll(e => ReferenceEquals(e.Context, ctx));

            // When the last entry is removed, dispose the shared OS registration
            // and remove the signal from the dictionary.
            if (entries.Count == 0)
            {
                SignalHandlers.TryRemove(name, out _);
                sharedReg?.Dispose();
            }
        }

        return StashValue.Null;
    }

    /// <summary>
    /// Invokes all registered handlers for <paramref name="signalName"/> in registration order.
    /// Each handler is invoked in its own try/catch so a throwing handler does not prevent
    /// subsequent handlers from running.
    /// Called from the <see cref="PosixSignalRegistration"/> callback (may run on a thread-pool
    /// thread) and also directly by tests as a synthetic signal raise.
    /// </summary>
    internal static void Dispatch(string signalName)
    {
        // Snapshot the list under the lock so handlers run outside the lock
        // (handlers may call signal.on/off or be slow).
        List<(IInterpreterContext Context, IStashCallable Handler, PosixSignalRegistration? Registration)>? snapshot = null;
        lock (SignalLock)
        {
            if (SignalHandlers.TryGetValue(signalName, out var entries) && entries.Count > 0)
            {
                snapshot = new List<(IInterpreterContext, IStashCallable, PosixSignalRegistration?)>(entries);
            }
        }

        if (snapshot is null) return;

        foreach (var (handlerCtx, handlerFn, _) in snapshot)
        {
            try
            {
                handlerCtx.InvokeCallbackDirect(handlerFn, ReadOnlySpan<StashValue>.Empty);
            }
            catch
            {
                // Errors in signal handlers are non-fatal; continue to the next handler.
            }
        }
    }

    internal static PosixSignal? MapToPosixSignal(string signalName)
    {
        return signalName switch
        {
            "SIGHUP"  => PosixSignal.SIGHUP,
            "SIGINT"  => PosixSignal.SIGINT,
            "SIGQUIT" => PosixSignal.SIGQUIT,
            "SIGTERM" => PosixSignal.SIGTERM,
            "SIGUSR1" => MapUserSignal(linuxNum: 10, macNum: 30),
            "SIGUSR2" => MapUserSignal(linuxNum: 12, macNum: 31),
            _ => null,
        };
    }

    private static PosixSignal? MapUserSignal(int linuxNum, int macNum)
    {
        if (OperatingSystem.IsLinux()) return (PosixSignal)linuxNum;
        if (OperatingSystem.IsMacOS()) return (PosixSignal)macNum;
        return null;
    }
}
