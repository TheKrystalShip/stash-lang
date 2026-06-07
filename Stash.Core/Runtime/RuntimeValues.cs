namespace Stash.Runtime;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;

/// <summary>
/// Static utility methods for Stash runtime value operations:
/// truthiness testing, stringification, equality, and numeric helpers.
/// </summary>
public static class RuntimeValues
{
    /// <summary>
    /// Determines whether a runtime value is truthy according to Stash's truthiness rules.
    /// </summary>
    public static bool IsTruthy(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is StashError)
        {
            return false;
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is long i)
        {
            return i != 0;
        }

        if (value is byte by)
        {
            return by != 0;
        }

        if (value is double d)
        {
            return d != 0.0;
        }

        if (value is string s)
        {
            return s.Length != 0;
        }

        return true;
    }

    /// <summary>
    /// Converts a runtime value to its Stash string representation.
    /// </summary>
    public static string Stringify(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is StashSecret)
        {
            return StashSecret.RedactedText;
        }

        if (value is byte by)
        {
            return by.ToString();
        }

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        if (value is double d)
        {
            return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (value is StashError error)
        {
            return error.ToString();
        }

        if (value is StashInstance instance)
        {
            return instance.ToString();
        }

        if (value is StashStruct structDef)
        {
            return structDef.ToString();
        }

        if (value is StashEnumValue enumVal)
        {
            return enumVal.ToString();
        }

        if (value is StashEnum enumType)
        {
            return enumType.ToString();
        }

        if (value is StashInterface iface)
        {
            return iface.ToString();
        }

        if (value is StashNamespace ns)
        {
            return ns.ToString();
        }

        if (value is StashByteArray ba)
        {
            return ba.Stringify();
        }

        if (value is StashTypedArray ta)
        {
            return ta.Stringify();
        }

        if (value is List<StashValue> svList)
        {
            var elements = new StringBuilder("[");
            for (int i = 0; i < svList.Count; i++)
            {
                if (i > 0)
                {
                    elements.Append(", ");
                }
                elements.Append(Stringify(svList[i].ToObject()));
            }
            elements.Append(']');
            return elements.ToString();
        }

        if (value is StashDictionary dict)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (StashValue key in dict.Keys())
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                sb.Append(Stringify(key.ToObject()));
                sb.Append(": ");
                sb.Append(Stringify(dict.Get(key).ToObject()));
            }
            sb.Append('}');
            return sb.ToString();
        }

        if (value is StashRange range)
        {
            return range.ToString();
        }

        if (value is StashFuture future)
        {
            return future.ToString();
        }

        if (value is StashDuration duration)
        {
            return duration.ToString();
        }

        if (value is StashByteSize byteSize)
        {
            return byteSize.ToString();
        }

        if (value is StashSemVer semVer)
        {
            return semVer.ToString();
        }

        if (value is StashIpAddress ipAddr)
        {
            return ipAddr.ToString();
        }

        return value.ToString()!;
    }

    /// <summary>
    /// Checks whether a runtime value is a numeric type (long or double).
    /// </summary>
    public static bool IsNumeric(object? value) => value is long or double;

    /// <summary>
    /// Converts a numeric value to double for type-promoted arithmetic.
    /// </summary>
    public static double ToDouble(object? value) => value is long i ? (double)i : (double)value!;

    /// <summary>
    /// Converts a string into an enumerable of single-character strings.
    /// </summary>
    public static IEnumerable<object?> StringToChars(string str)
    {
        foreach (char c in str)
        {
            yield return c.ToString();
        }
    }

    /// <summary>
    /// Converts a string into an enumerable of StashValues (single-character strings).
    /// </summary>
    public static IEnumerable<StashValue> StringToStashValues(string str)
    {
        foreach (char c in str)
        {
            yield return StashValue.FromObj(c.ToString());
        }
    }

    /// <summary>
    /// Implements string padding (padStart / padEnd).
    /// </summary>
    public static string PadString(string funcName, List<StashValue> args, bool padLeft)
    {
        if (args.Count < 2 || args.Count > 3)
        {
            throw new RuntimeError($"'{funcName}' requires 2 or 3 arguments.");
        }

        if (args[0].ToObject() is not string s)
        {
            throw new RuntimeError($"First argument to '{funcName}' must be a string.");
        }

        if (args[1].ToObject() is not long length)
        {
            throw new RuntimeError($"Second argument to '{funcName}' must be an integer.");
        }

        char fillChar = ' ';
        if (args.Count == 3)
        {
            if (args[2].ToObject() is not string fill || fill.Length != 1)
            {
                throw new RuntimeError($"Third argument to '{funcName}' must be a single-character string.");
            }

            fillChar = fill[0];
        }
        return padLeft ? s.PadLeft((int)length, fillChar) : s.PadRight((int)length, fillChar);
    }

    /// <summary>
    /// Creates a CommandResult StashInstance.
    /// </summary>
    public static StashInstance CreateCommandResult(string stdout, string stderr, long exitCode)
    {
        return new StashInstance("CommandResult", new Dictionary<string, StashValue>
        {
            ["stdout"] = StashValue.FromObj(stdout),
            ["stderr"] = StashValue.FromObj(stderr),
            ["exitCode"] = StashValue.FromInt(exitCode)
        }) { StringifyField = "stdout" };
    }

    /// <summary>
    /// Deep-freezes a Stash runtime value in place, making the entire transitive
    /// object graph immutable. Safe to call on primitives (no-op). Cycle-safe.
    ///
    /// <para>
    /// Frozen flag placement per type:
    /// <list type="bullet">
    ///   <item><see cref="StashArray"/> — calls <see cref="StashArray.Freeze()"/>, then recurses into elements.</item>
    ///   <item><see cref="StashDictionary"/> — calls <see cref="StashDictionary.Freeze()"/>, then recurses into values.</item>
    ///   <item><see cref="StashInstance"/> — calls <see cref="StashInstance.Freeze()"/>, then recurses into fields.</item>
    ///   <item><see cref="StashError"/> — no write-guard added (already rejects field writes); recurses into <c>Properties</c> values.</item>
    ///   <item>Primitives, strings, functions, closures, etc. — skipped (no-op).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="value">The <see cref="StashValue"/> to freeze. Primitives are accepted as a no-op.</param>
    public static void DeepFreeze(StashValue value)
    {
        if (!value.IsObj) return; // primitives: int, float, bool, null, byte — already immutable
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        DeepFreezeObject(value.AsObj, visited);
    }

    private static void DeepFreezeObject(object? obj, HashSet<object> visited)
    {
        if (obj is null) return;
        // Cycle guard — also prevents double-traversal of already-frozen nodes
        if (!visited.Add(obj)) return;

        switch (obj)
        {
            case StashArray arr:
                arr.Freeze();
                for (int i = 0; i < arr.Count; i++)
                {
                    StashValue elem = arr[i];
                    if (elem.IsObj) DeepFreezeObject(elem.AsObj, visited);
                }
                break;

            case StashDictionary dict:
                dict.Freeze();
                foreach (var kv in dict.RawEntries())
                {
                    StashValue val = kv.Value;
                    if (val.IsObj) DeepFreezeObject(val.AsObj, visited);
                }
                break;

            case StashInstance inst:
                inst.Freeze();
                foreach (var kv in inst.GetAllFields())
                {
                    StashValue val = kv.Value;
                    if (val.IsObj) DeepFreezeObject(val.AsObj, visited);
                }
                break;

            case StashError err:
                // StashError is already write-blocked (no IVMFieldMutable).
                // DeepFreeze only needs to traverse into its Properties dict to
                // freeze any nested reference-typed values.
                if (err.Properties is not null)
                {
                    foreach (var propVal in err.Properties.Values)
                    {
                        if (propVal is not null)
                            DeepFreezeObject(propVal, visited);
                    }
                }
                break;

            case StashTypedArray ta:
                // Element types are primitives (int/float/bool/byte/string) — no recursion needed.
                ta.Freeze();
                break;

            // Defense-in-depth: a bare List<StashValue> (not a StashArray subclass) can slip
            // through if any producer is not yet migrated to StashArray.  The list has no IsFrozen
            // bit so it cannot be frozen itself, but we can still recurse into its elements and
            // freeze any nested freezable carriers (StashArray/StashDictionary/StashInstance).
            case System.Collections.Generic.List<StashValue> list:
                foreach (var elem in list)
                {
                    if (elem.IsObj) DeepFreezeObject(elem.AsObj, visited);
                }
                break;

            // Functions, closures, bound methods, namespaces, etc.
            // are treated as opaque — they are skipped but do not throw.
            default:
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // IsFrozen — unified frozen predicate for all reference-typed Stash values
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the <see cref="StashValue"/> is a reference-typed
    /// mutable collection that has been frozen (i.e. <c>readonly</c> / <c>freeze</c>).
    ///
    /// <para>
    /// Primitives, strings, enums, ranges, IP addresses, semvers, functions, closures, and
    /// bound methods are inherently immutable and always return <see langword="false"/> here
    /// (they are never "frozen" — they are simply immutable by construction; callers that need
    /// "copy-by-value safe?" should check <c>!value.IsObj || IsFrozen(value)</c>).
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFrozen(StashValue value)
    {
        if (!value.IsObj) return false;
        return value.AsObj switch
        {
            StashArray arr        => arr.IsFrozen,
            StashDictionary dict  => dict.IsFrozen,
            StashInstance inst    => inst.IsFrozen,
            StashTypedArray ta    => ta.IsFrozen,
            StashNamespace ns     => ns.IsFrozen,
            _                     => false,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // DeepClone — cycle-safe deep clone for async-child global isolation
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a deep clone of a Stash runtime value for async-child global isolation.
    ///
    /// <para>
    /// Sharing rules:
    /// <list type="bullet">
    ///   <item>Primitives (<c>int</c>, <c>float</c>, <c>bool</c>, <c>null</c>, <c>byte</c>)
    ///   and strings are returned as-is (copy-by-value / immutable).</item>
    ///   <item>Frozen reference values (<see cref="IsFrozen"/>) are shared by reference
    ///   (immutable; safe across threads).</item>
    ///   <item>Non-frozen reference values are deep-cloned.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Cycle detection: uses an <em>active-path</em> stack (add on entry, remove on exit)
    /// rather than a permanent visited set. This correctly distinguishes:
    /// <list type="bullet">
    ///   <item><em>Cycles</em> (back-edges to a node still on the active path) — throws
    ///   <see cref="ValueError"/> with the cycle path in the message.</item>
    ///   <item><em>Diamond / shared-acyclic</em> sub-graphs (a node referenced from two
    ///   places but not on the active path) — cloned independently (no false positive).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="value">The value to clone.</param>
    /// <param name="activePath">
    /// The set of object references currently on the active recursion path.
    /// Pass <see langword="null"/> at the top call; the method manages its own state.
    /// </param>
    /// <param name="pathBreadcrumbs">
    /// Human-readable path segments accumulating as the walker descends.
    /// Pass <see langword="null"/> at the top call.
    /// </param>
    /// <returns>A deep-cloned (or shared-frozen) value safe for independent mutation.</returns>
    /// <exception cref="ValueError">Thrown when a cycle is detected in the object graph.</exception>
    public static StashValue DeepClone(StashValue value,
        HashSet<object>? activePath = null,
        List<string>? pathBreadcrumbs = null)
    {
        // Primitives and strings: copy-by-value / immutable — no clone needed.
        if (!value.IsObj) return value;

        object? obj = value.AsObj;
        if (obj is null) return value;

        // Frozen: share by reference — immutable can't race.
        if (IsFrozen(value)) return value;

        // Initialise state on the top-level call.
        activePath ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        pathBreadcrumbs ??= new List<string> { "<root>" };

        // Cycle guard: the node is already on the active path — it IS the cycle.
        if (activePath.Contains(obj))
        {
            string path = string.Join(" -> ", pathBreadcrumbs);
            throw new ValueError(
                $"Cannot deep-clone a value that contains a cycle (path: {path}). " +
                $"Use freeze() on one node in the cycle to share it by reference instead.");
        }

        // Push onto the active path before recursing.
        activePath.Add(obj);

        try
        {
            return obj switch
            {
                StashArray arr       => DeepCloneArray(arr, activePath, pathBreadcrumbs),
                StashDictionary dict => DeepCloneDictionary(dict, activePath, pathBreadcrumbs),
                StashInstance inst   => DeepCloneInstance(inst, activePath, pathBreadcrumbs),
                StashTypedArray ta   => StashValue.FromObj(ta.Clone()),
                // Immutable-by-construction: share by reference.
                _ => value,
            };
        }
        finally
        {
            // Pop off the active path on the way back up.
            activePath.Remove(obj);
        }
    }

    /// <summary>
    /// Maximum retry attempts for the cross-thread snapshot loops in
    /// <see cref="DeepCloneArray"/> and <see cref="DeepCloneDictionary"/>.
    /// Matches the value used by <c>IsolationHelpers.SnapshotEntries</c> in
    /// <c>Stash.Bytecode</c> so the retry discipline is consistent across both
    /// layers.  A single-writer dictionary/array clears a structural-change
    /// collision within an attempt or two; the 64-retry ceiling is a safety
    /// backstop and is never approached in normal operation.
    /// </summary>
    private const int SnapshotMaxRetries = 64;

    private static StashValue DeepCloneArray(StashArray arr, HashSet<object> activePath, List<string> pathBreadcrumbs)
    {
        // Snapshot the parent's live List<StashValue> into a private array BEFORE walking.
        // This handles the cross-thread callback path (VMContext.InvokeCallbackDirect background
        // branch) where the parent's main thread may concurrently call arr.push() / arr.pop()
        // while this background thread is cloning.  The List<StashValue> version-checked
        // enumerator (foreach) throws InvalidOperationException on a concurrent structural
        // mutation (Add/Remove/resize); we catch-and-retry — the single-writer guarantee
        // (only the owning VM thread mutates the array) ensures the retry converges.
        //
        // Do NOT use indexed access (arr[i] for i in 0..arr.Count) here: a concurrent Add
        // can resize the backing array between the Count read and the indexer, producing
        // stale/out-of-range reads that do not throw and silently tear the snapshot.
        // Do NOT pass arr.Count as initial capacity: it may be stale from a concurrent Add,
        // allocating a larger-than-needed (potentially OOM-inducing) initial backing array.
        StashValue[] elements = null!;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                // Do NOT pass arr.Count as initial capacity: it may be stale from a
                // concurrent Add, causing an arbitrarily large (and possibly OOM) allocation.
                // Let the list grow organically from the snapshot.
                var list = new List<StashValue>();
                foreach (StashValue e in arr)
                {
                    list.Add(e);
                }
                elements = list.ToArray();
                break;
            }
            catch (System.InvalidOperationException) when (attempt < SnapshotMaxRetries)
            {
                // Owner thread added/removed an element mid-walk — retry.
                // Bounded at SnapshotMaxRetries (64) for consistency with SnapshotEntries
                // and SnapshotImportStack in IsolationHelpers.  The single-writer guarantee
                // (only the owning VM thread mutates the array via arr.push/pop/splice) means
                // a concurrent-mutation collision clears within an attempt or two in practice;
                // the bound is a safety backstop and is never approached during normal use.
                System.Threading.Thread.SpinWait(4 << System.Math.Min(attempt, 10));
            }
        }

        var clone = new StashArray(elements.Length);
        for (int i = 0; i < elements.Length; i++)
        {
            StashValue elem = elements[i];
            if (elem.IsObj)
            {
                pathBreadcrumbs.Add($"[{i}]");
                elem = DeepClone(elem, activePath, pathBreadcrumbs);
                pathBreadcrumbs.RemoveAt(pathBreadcrumbs.Count - 1);
            }
            clone.Add(elem);
        }
        return StashValue.FromObj(clone);
    }

    private static StashValue DeepCloneDictionary(StashDictionary dict, HashSet<object> activePath, List<string> pathBreadcrumbs)
    {
        // Snapshot the parent's live Dictionary<StashValue,StashValue> BEFORE walking.
        // RawEntries() uses ToList() which dispatches to ICollection.CopyTo — that does
        // NOT check the version flag and silently produces a torn snapshot without throwing.
        // Instead use RawEntriesEnumerable() which yields via a foreach over _entries,
        // triggering the version-checked Dictionary struct enumerator and throwing on
        // concurrent structural mutation (new-key Set/Remove from the parent thread).
        // Unbounded-retry, single-writer safe — mirrors SnapshotEntries in IsolationHelpers.
        KeyValuePair<StashValue, StashValue>[] entries = null!;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var list = new List<KeyValuePair<StashValue, StashValue>>(dict.Count);
                foreach (var kv in dict.RawEntriesEnumerable())
                {
                    list.Add(kv);
                }
                entries = list.ToArray();
                break;
            }
            catch (System.InvalidOperationException) when (attempt < SnapshotMaxRetries)
            {
                // Owner thread mutated the dict mid-walk — retry.
                // Bounded at SnapshotMaxRetries (64) for consistency with SnapshotEntries.
                System.Threading.Thread.SpinWait(4 << System.Math.Min(attempt, 10));
            }
        }

        var clone = new StashDictionary();
        foreach (var kv in entries)
        {
            StashValue val = kv.Value;
            if (val.IsObj)
            {
                string keyStr = RuntimeValues.Stringify(kv.Key.ToObject());
                pathBreadcrumbs.Add($"[\"{keyStr}\"]");
                val = DeepClone(val, activePath, pathBreadcrumbs);
                pathBreadcrumbs.RemoveAt(pathBreadcrumbs.Count - 1);
            }
            clone.Set(kv.Key, val);
        }
        return StashValue.FromObj(clone);
    }

    private static StashValue DeepCloneInstance(StashInstance inst, HashSet<object> activePath, List<string> pathBreadcrumbs)
    {
        if (inst.FieldSlots is not null && inst.Struct is not null)
        {
            var slotsCopy = new StashValue[inst.FieldSlots.Length];
            for (int i = 0; i < slotsCopy.Length; i++)
            {
                StashValue slot = inst.FieldSlots[i];
                if (slot.IsObj)
                {
                    string fieldName = i < inst.Struct.Fields.Count
                        ? inst.Struct.Fields[i]
                        : $"[{i}]";
                    pathBreadcrumbs.Add($".{fieldName}");
                    slot = DeepClone(slot, activePath, pathBreadcrumbs);
                    pathBreadcrumbs.RemoveAt(pathBreadcrumbs.Count - 1);
                }
                slotsCopy[i] = slot;
            }
            return StashValue.FromObj(new StashInstance(inst.TypeName, inst.Struct, slotsCopy));
        }

        var fieldsCopy = new Dictionary<string, StashValue>();
        foreach (var kvp in inst.GetAllFields())
        {
            StashValue val = kvp.Value;
            if (val.IsObj)
            {
                pathBreadcrumbs.Add($".{kvp.Key}");
                val = DeepClone(val, activePath, pathBreadcrumbs);
                pathBreadcrumbs.RemoveAt(pathBreadcrumbs.Count - 1);
            }
            fieldsCopy[kvp.Key] = val;
        }
        return StashValue.FromObj(new StashInstance(inst.TypeName, fieldsCopy, inst.Struct));
    }

    /// <summary>
    /// Creates a deep copy of a Stash runtime value. Immutable values (primitives, enums,
    /// ranges, namespaces) are returned as-is. Mutable values (lists, dictionaries, struct
    /// instances, functions/lambdas with closures) are recursively cloned.
    /// </summary>
    public static object? DeepCopy(object? value)
    {
        return value switch
        {
            // Immutable — return as-is
            null => null,
            bool => value,
            long => value,
            double => value,
            string => value,
            StashEnumValue => value,
            StashEnum => value,
            StashStruct => value,
            StashRange => value,
            StashIpAddress => value,        // Immutable value type
            StashSemVer => value,           // Immutable value type
            StashError => value,
            StashNamespace => value,        // Frozen after init
            BuiltInFunction => value,       // Stateless delegate wrapper

            // Mutable collections — deep clone
            List<StashValue> svList => DeepCopyStashList(svList),
            StashDictionary dict => DeepCopyDictionary(dict),
            StashInstance instance => DeepCopyInstance(instance),

            // Callables with closures — use IDeepCopyable interface
            IDeepCopyable copyable => copyable.DeepCopy(),

            // StashBoundMethod — deep copy instance + method
            StashBoundMethod bound => DeepCopyBoundMethod(bound),

            // Unknown — return as-is (safety fallback)
            _ => value,
        };
    }

    private static List<StashValue> DeepCopyStashList(List<StashValue> list)
    {
        // Preserve StashArray subtype so frozen-flag semantics survive the copy.
        var copy = list is StashArray ? new StashArray(list.Count) : new List<StashValue>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            object? deepCopied = DeepCopy(list[i].ToObject());
            copy.Add(StashValue.FromObject(deepCopied));
        }
        return copy;
    }

    private static StashDictionary DeepCopyDictionary(StashDictionary dict)
    {
        var copy = new StashDictionary();
        foreach (var (key, val) in dict.GetAllEntries())
        {
            copy.Set(key, StashValue.FromObject(DeepCopy(val.ToObject())));
        }
        return copy;
    }

    private static StashInstance DeepCopyInstance(StashInstance instance)
    {
        if (instance.FieldSlots is not null && instance.Struct is not null)
        {
            var slotsCopy = new StashValue[instance.FieldSlots.Length];
            for (int i = 0; i < slotsCopy.Length; i++)
                slotsCopy[i] = StashValue.FromObject(DeepCopy(instance.FieldSlots[i].ToObject()));
            return new StashInstance(instance.TypeName, instance.Struct, slotsCopy);
        }

        var fieldsCopy = new Dictionary<string, StashValue>();
        foreach (KeyValuePair<string, StashValue> kvp in instance.GetAllFields())
        {
            fieldsCopy[kvp.Key] = StashValue.FromObject(DeepCopy(kvp.Value.ToObject()));
        }
        return new StashInstance(instance.TypeName, fieldsCopy, instance.Struct);
    }

    private static StashBoundMethod DeepCopyBoundMethod(StashBoundMethod bound)
    {
        var copiedInstance = (StashInstance)DeepCopy(bound.Instance)!;
        IStashCallable copiedMethod = bound.Method is IDeepCopyable dc
            ? (IStashCallable)dc.DeepCopy()
            : bound.Method;
        return new StashBoundMethod(copiedInstance, copiedMethod);
    }
}
