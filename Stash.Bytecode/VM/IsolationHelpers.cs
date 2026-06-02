using System.Collections.Generic;
using Stash.Runtime;

namespace Stash.Bytecode;


/// <summary>
/// Helpers for child-VM global isolation at cross-thread fork sites.
///
/// <para>
/// The single entry point — <see cref="BuildChildGlobals"/> — is the canonical
/// implementation of the <em>freeze-or-clone</em> rule: every non-frozen mutable
/// reference-typed global is deep-cloned so the child task gets a private copy;
/// frozen values are shared by reference (immutable can't race); primitives copy
/// by struct value automatically.
/// </para>
///
/// <para>
/// Every <strong>cross-thread</strong> child-VM construction site MUST call this
/// helper. Same-thread construction sites (module-load, template evaluator, the
/// inline same-thread branch of <c>InvokeCallbackDirect</c>) are exempt because
/// they do not introduce concurrent mutation hazards.
/// </para>
/// </summary>
internal static class IsolationHelpers
{
    /// <summary>
    /// Produces a child-task-safe snapshot of a parent VM's captured upvalues.
    ///
    /// <para>
    /// For each upvalue, reads the current value and applies the freeze-or-clone rule:
    /// <list type="bullet">
    ///   <item>Primitive or string → copy by value (struct semantics).</item>
    ///   <item>Frozen reference → share by reference (immutable can't race).</item>
    ///   <item>Non-frozen mutable reference → deep-clone (child gets a private copy).</item>
    /// </list>
    /// The returned array contains new, pre-closed <see cref="Upvalue"/> objects that the
    /// child VM can read safely from its thread without touching the parent's stack or heap.
    /// </para>
    /// </summary>
    /// <param name="parentUpvalues">The upvalue array from the async closure being spawned.</param>
    /// <returns>
    /// A new <see cref="Upvalue"/> array of the same length whose entries are pre-closed
    /// snapshots of the parent upvalues, each isolated via the freeze-or-clone rule.
    /// </returns>
    internal static Upvalue[] SnapshotUpvalues(Upvalue[] parentUpvalues)
    {
        if (parentUpvalues.Length == 0) return parentUpvalues;

        var snapshot = new Upvalue[parentUpvalues.Length];
        for (int i = 0; i < parentUpvalues.Length; i++)
        {
            StashValue value = parentUpvalues[i].Value;
            StashValue isolated;

            if (!value.IsObj)
            {
                // Primitive: struct copy — safe by value.
                isolated = value;
            }
            else if (RuntimeValues.IsFrozen(value))
            {
                // Frozen reference: share — immutable can't race.
                isolated = value;
            }
            else
            {
                // Mutable reference: deep-clone.
                isolated = RuntimeValues.DeepClone(value);
            }

            // Create a pre-closed upvalue holding the isolated value.
            // Upvalue.Close() copies _stack[StackIndex] → _closed; so we create a
            // one-element scratch array, put the value in it, construct the upvalue
            // pointing to slot 0, then close it.  After closing, _closed == isolated
            // and the scratch array is unreachable (GC'd).
            var scratchStack = new StashValue[] { isolated };
            var uv = new Upvalue(scratchStack, 0);
            uv.Close();
            snapshot[i] = uv;
        }
        return snapshot;
    }

    /// <summary>
    /// Builds the <c>_globals</c> dictionary for a cross-thread child VM by applying
    /// the freeze-or-clone rule to every entry of the parent's globals dictionary.
    ///
    /// <para>
    /// Per-entry decision:
    /// <list type="bullet">
    ///   <item>
    ///     <strong>Primitive (<c>!IsObj</c>) or string</strong> — <c>StashValue</c> is a
    ///     struct; the dictionary copies it by value automatically.  No special handling
    ///     needed.
    ///   </item>
    ///   <item>
    ///     <strong>Frozen reference</strong> (<see cref="RuntimeValues.IsFrozen"/>) — share
    ///     by reference.  The value is immutable; concurrent reads from two threads are safe.
    ///   </item>
    ///   <item>
    ///     <strong>Non-frozen mutable reference</strong> — call
    ///     <see cref="RuntimeValues.DeepClone"/> to produce a private copy for the child.
    ///     If the object graph contains a cycle, <c>DeepClone</c> throws
    ///     <see cref="Stash.Runtime.Errors.ValueError"/> with the cycle path; the caller
    ///     (<c>SpawnAsyncFunction</c>) lets this propagate so the spawning script sees the
    ///     error.
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="parentGlobals">The parent VM's globals dictionary.</param>
    /// <returns>
    /// A new <see cref="Dictionary{TKey,TValue}"/> whose entries are either
    /// struct-copied primitives, shared-frozen references, or deep-cloned mutable copies.
    /// </returns>
    internal static Dictionary<string, StashValue> BuildChildGlobals(
        Dictionary<string, StashValue> parentGlobals)
    {
        var childGlobals = new Dictionary<string, StashValue>(parentGlobals.Count);

        foreach (var (name, value) in parentGlobals)
        {
            if (!value.IsObj)
            {
                // Primitive (int/float/bool/null/byte): struct copy — safe by value.
                childGlobals[name] = value;
            }
            else if (RuntimeValues.IsFrozen(value))
            {
                // Frozen reference: share — immutable can't race.
                childGlobals[name] = value;
            }
            else
            {
                // Mutable reference: deep-clone so the child gets a private copy.
                // DeepClone throws ValueError on cycle; let it propagate to the caller.
                childGlobals[name] = RuntimeValues.DeepClone(value);
            }
        }

        return childGlobals;
    }
}
