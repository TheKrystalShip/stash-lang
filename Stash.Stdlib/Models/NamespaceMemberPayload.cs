namespace Stash.Stdlib.Models;

using System;
using System.Threading;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Runtime payload stored in a namespace slot for entries registered via <c>[StashMember]</c>.
/// The payload bundles the getter delegate, stability tag, and declaration metadata so that the
/// VM can recognize the slot as a <c>DataMember</c> entry and invoke the getter appropriately.
/// This is an opaque value from the perspective of <c>Stash.Core</c>; P2 teaches
/// <c>StashNamespace.VMGetField</c> to recognise and dispatch it.
/// </summary>
public sealed class NamespaceMemberPayload
{
    /// <summary>The getter delegate that produces the member's current value.</summary>
    public Func<IInterpreterContext, StashValue> Getter { get; }

    /// <summary>The evaluation strategy for this member.</summary>
    public Stability Stability { get; }

    /// <summary>The Stash-visible return type label (e.g. "array", "string").</summary>
    public string? ReturnType { get; }

    // Cache for Stability.Cached members.
    private StashValue _cachedValue;
    private int _cacheState; // 0 = unset, 1 = set; access via Interlocked/Volatile

    public NamespaceMemberPayload(
        Func<IInterpreterContext, StashValue> getter,
        Stability stability,
        string? returnType)
    {
        Getter = getter ?? throw new ArgumentNullException(nameof(getter));
        Stability = stability;
        ReturnType = returnType;
    }

    /// <summary>
    /// Invokes the getter, honouring the stability contract:
    /// <list type="bullet">
    ///   <item><c>Cached</c> — computed once on first call; subsequent calls return the same reference.</item>
    ///   <item><c>Live</c> — computed on every call.</item>
    /// </list>
    /// </summary>
    public StashValue Invoke(IInterpreterContext ctx)
    {
        switch (Stability)
        {
            case Stability.Live:
                return Getter(ctx);

            case Stability.Cached:
            {
                // Once-set pattern. Accept a benign race: two threads may invoke
                // the getter concurrently on first access; the last write wins. For Cached
                // members whose getters are idempotent process-identity reads this is safe.
                if (Volatile.Read(ref _cacheState) == 1)
                    return _cachedValue;

                var result = Getter(ctx);
                _cachedValue = result;
                Volatile.Write(ref _cacheState, 1);
                return result;
            }

            default:
                throw new InvalidOperationException($"unhandled Stability: {Stability}");
        }
    }
}
