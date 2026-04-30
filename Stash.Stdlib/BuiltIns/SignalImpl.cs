namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Shared signal-handler state and implementations for both <c>signal.*</c>
/// and the deprecated <c>sys.*</c> shims.
/// </summary>
internal static class SignalImpl
{
    internal static readonly ConcurrentDictionary<string, (IInterpreterContext Context, IStashCallable Handler, PosixSignalRegistration? Registration)>
        SignalHandlers = new();

    internal static readonly object SignalLock = new();

    internal static StashValue OnSignal(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var signal = SvArgs.EnumValue(args, 0, "Signal", callerQualified);
        var handler = SvArgs.Callable(args, 1, callerQualified);

        string name = signal.MemberName;

        lock (SignalLock)
        {
            if (SignalHandlers.TryRemove(name, out var existing))
            {
                existing.Registration?.Dispose();
            }

            PosixSignal? posixSignal = MapToPosixSignal(name);
            if (posixSignal is null)
            {
                SignalHandlers[name] = (ctx, handler, null);
                return StashValue.Null;
            }

            PosixSignalRegistration? registration = null;
            try
            {
                registration = PosixSignalRegistration.Create(posixSignal.Value, context =>
                {
                    context.Cancel = true;
                    if (SignalHandlers.TryGetValue(name, out var entry))
                    {
                        try
                        {
                            entry.Context.InvokeCallbackDirect(entry.Handler, ReadOnlySpan<StashValue>.Empty);
                        }
                        catch
                        {
                            // Errors in signal handlers are non-fatal
                        }
                    }
                });
            }
            catch (PlatformNotSupportedException)
            {
                // Signal not supported on this platform — store handler but no registration
            }

            SignalHandlers[name] = (ctx, handler, registration);
        }
        return StashValue.Null;
    }

    internal static StashValue OffSignal(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var signal = SvArgs.EnumValue(args, 0, "Signal", callerQualified);

        lock (SignalLock)
        {
            if (SignalHandlers.TryRemove(signal.MemberName, out var existing))
            {
                existing.Registration?.Dispose();
            }
        }

        return StashValue.Null;
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
