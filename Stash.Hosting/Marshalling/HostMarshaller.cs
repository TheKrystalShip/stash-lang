namespace Stash.Hosting.Marshalling;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Stash.Hosting.Internal;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// The single CLR↔Stash conversion chokepoint in <c>Stash.Hosting</c>.
/// All <c>object?→StashValue</c> (argument marshalling) and
/// <c>StashValue→T</c> (return marshalling) flows route through this class.
/// No other code in <c>Stash.Hosting</c> performs inline value conversion.
/// </summary>
internal static class HostMarshaller
{
    // ── Per-host registration map ─────────────────────────────────────────
    // The map is NOT static — registered types are per-host and must never bleed across
    // host instances. HostMarshaller.ToStash is called only when a registration map is
    // available; the overload without a map preserves backward-compat for paths that
    // have no host context (e.g. standalone engine usage).

    // ── Arg marshalling: object? → StashValue ─────────────────────────────

    /// <summary>
    /// Marshals a single CLR object to a <see cref="StashValue"/> for use as a
    /// Stash function argument.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="arg"/> is of a type with no registered marshaller.
    /// </exception>
    public static StashValue ToStash(object? arg)
        => ToStash(arg, registrations: null);

    /// <summary>
    /// Marshals a single CLR object to a <see cref="StashValue"/>, consulting the
    /// per-host <paramref name="registrations"/> map to wrap registered host types as
    /// <see cref="HostHandle"/> values before falling through to the generic marshallers.
    /// </summary>
    /// <param name="arg">The CLR value to marshal.</param>
    /// <param name="registrations">
    /// The per-host registration map, or <c>null</c> when called without a host context.
    /// When non-null, a registered type is wrapped as a <see cref="HostHandle"/> rather
    /// than throwing the generic <see cref="ArgumentException"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="arg"/> is of a type with no registered marshaller.
    /// </exception>
    public static StashValue ToStash(
        object? arg,
        IReadOnlyDictionary<Type, HostTypeRegistration>? registrations)
    {
        if (arg is null) return StashValue.Null;

        // StashValue passthrough — zero conversion
        if (arg is StashValue sv) return sv;

        // Primitives
        if (arg is bool b) return StashValue.FromBool(b);
        if (arg is long l) return StashValue.FromInt(l);
        if (arg is int i) return StashValue.FromInt(i);
        if (arg is double d) return StashValue.FromFloat(d);
        if (arg is float f) return StashValue.FromFloat(f);
        if (arg is string s) return StashValue.FromObj(s);
        if (arg is byte by) return StashValue.FromByte(by);

        // byte[] → buffer
        if (arg is byte[] buf)
            return StashValue.FromObj(buf);

        // JsonElement → recursive bridge
        if (arg is JsonElement je)
            return JsonStashBridge.ToStash(je);

        // Registered host type → HostHandle.
        // MUST precede IDictionary and IEnumerable: a registered host class that also
        // implements IEnumerable or IDictionary would otherwise be misrouted to those
        // branches and silently serialised as a collection instead of staying by-reference.
        if (registrations is not null)
        {
            Type argType = arg.GetType();
            if (registrations.TryGetValue(argType, out HostTypeRegistration? reg))
                return StashValue.FromObj(new HostHandle(arg, reg));
        }

        // IDictionary<string, object?> → StashDictionary
        if (arg is IDictionary<string, object?> dict)
        {
            var sd = new StashDictionary();
            foreach (KeyValuePair<string, object?> kv in dict)
                sd.Set(kv.Key, ToStash(kv.Value, registrations));
            return StashValue.FromObj(sd);
        }

        // Anonymous objects → StashDictionary (detected via reflection on compiler-generated type names)
        Type type = arg.GetType();
        if (IsAnonymousType(type))
        {
            var sd = new StashDictionary();
            foreach (System.Reflection.PropertyInfo prop in type.GetProperties())
                sd.Set(prop.Name, ToStash(prop.GetValue(arg), registrations));
            return StashValue.FromObj(sd);
        }

        // IEnumerable (excluding string — already handled above) → List<StashValue>
        // Note: check after IDictionary to avoid treating a dict as a flat sequence.
        if (arg is IEnumerable enumerable)
        {
            var list = new List<StashValue>();
            foreach (object? item in enumerable)
                list.Add(ToStash(item, registrations));
            return StashValue.FromObj(list);
        }

        // Numeric widening for other integer types
        if (arg is short sh) return StashValue.FromInt(sh);
        if (arg is ushort us) return StashValue.FromInt(us);
        if (arg is uint ui) return StashValue.FromInt(ui);
        if (arg is ulong ul) return StashValue.FromInt((long)ul);
        if (arg is sbyte sb) return StashValue.FromInt(sb);
        if (arg is decimal dc) return StashValue.FromFloat((double)dc);

        throw new ArgumentException(
            $"No marshaller registered for type '{type.FullName ?? type.Name}'. " +
            $"Supported types: primitives (long, double, string, bool, byte), byte[], " +
            $"IDictionary<string,object?>, IEnumerable, anonymous objects, " +
            $"StashValue passthrough, and JsonElement.");
    }

    /// <summary>
    /// Marshals CLR arguments to a <see cref="StashValue"/> array for a Stash call.
    /// </summary>
    /// <remarks>
    /// Argument-unpacking rules:
    /// <list type="bullet">
    ///   <item><c>null</c> → no arguments (zero-arity call).</item>
    ///   <item>Any array (<c>T[]</c>) → each element is a separate positional argument.</item>
    ///   <item>Any other single value → treated as the sole argument.</item>
    /// </list>
    /// Use <c>new object?[] { a, b }</c> to pass multiple typed arguments of different types,
    /// or <c>new T[] { a, b }</c> for same-type arguments (e.g. <c>new long[] { 2L, 3L }</c>).
    /// </remarks>
    public static StashValue[] ToStashArgs(object? args)
    {
        if (args is null) return Array.Empty<StashValue>();

        // Any array type → each element is a separate positional argument.
        // This handles both object?[] (heterogeneous) and typed arrays like long[], string[].
        Type argsType = args.GetType();
        if (argsType.IsArray)
        {
            System.Array arr = (System.Array)args;
            var result = new StashValue[arr.Length];
            for (int i = 0; i < arr.Length; i++)
                result[i] = ToStash(arr.GetValue(i));
            return result;
        }

        // Any single non-null, non-array value → one argument
        return new[] { ToStash(args) };
    }

    // ── Return marshalling: StashValue → T ────────────────────────────────

    /// <summary>
    /// Converts a <see cref="StashValue"/> returned by a Stash function to
    /// a CLR value of type <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidCastException">
    /// Thrown when <typeparamref name="T"/> is a POCO, record, or other unsupported
    /// type. Reflection-based POCO marshalling is v2.
    /// </exception>
    public static T? FromStash<T>(StashValue value)
    {
        // StashValue passthrough — zero conversion
        if (typeof(T) == typeof(StashValue))
            return (T)(object)value;

        // JsonElement — round-trip through JSON
        if (typeof(T) == typeof(JsonElement))
            return (T)(object)JsonStashBridge.FromStash(value);

        // null / Null → default(T)
        if (value.IsNull)
            return default;

        // Primitives
        if (typeof(T) == typeof(string))
        {
            if (value.Tag == StashValueTag.Obj && value.AsObj is string s) return (T)(object)s;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'string'.");
        }

        if (typeof(T) == typeof(long))
        {
            if (value.Tag == StashValueTag.Int) return (T)(object)value.AsInt;
            if (value.Tag == StashValueTag.Byte) return (T)(object)(long)value.AsByte;
            if (value.Tag == StashValueTag.Float) return (T)(object)(long)value.AsFloat;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'long'.");
        }

        if (typeof(T) == typeof(int))
        {
            if (value.Tag == StashValueTag.Int) return (T)(object)(int)value.AsInt;
            if (value.Tag == StashValueTag.Byte) return (T)(object)(int)value.AsByte;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'int'.");
        }

        if (typeof(T) == typeof(double))
        {
            if (value.Tag == StashValueTag.Float) return (T)(object)value.AsFloat;
            if (value.Tag == StashValueTag.Int) return (T)(object)(double)value.AsInt;
            if (value.Tag == StashValueTag.Byte) return (T)(object)(double)value.AsByte;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'double'.");
        }

        if (typeof(T) == typeof(float))
        {
            if (value.Tag == StashValueTag.Float) return (T)(object)(float)value.AsFloat;
            if (value.Tag == StashValueTag.Int) return (T)(object)(float)value.AsInt;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'float'.");
        }

        if (typeof(T) == typeof(bool))
        {
            if (value.Tag == StashValueTag.Bool) return (T)(object)value.AsBool;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'bool'.");
        }

        if (typeof(T) == typeof(byte))
        {
            if (value.Tag == StashValueTag.Byte) return (T)(object)value.AsByte;
            if (value.Tag == StashValueTag.Int) return (T)(object)(byte)value.AsInt;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'byte'.");
        }

        if (typeof(T) == typeof(byte[]))
        {
            if (value.Tag == StashValueTag.Obj && value.AsObj is byte[] bytes) return (T)(object)bytes;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'byte[]'.");
        }

        // Dictionary<string, object?> — flatten via StashTypeConverter
        if (typeof(T) == typeof(Dictionary<string, object?>))
        {
            if (value.Tag == StashValueTag.Obj && value.AsObj is StashDictionary)
                return (T)(object)Stash.Bytecode.StashTypeConverter.ToDictionary(value.AsObj);
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'Dictionary<string,object?>'.");
        }

        // List<StashValue> — shallow copy via StashTypeConverter
        if (typeof(T) == typeof(List<StashValue>))
        {
            if (value.Tag == StashValueTag.Obj && value.AsObj is List<StashValue>)
                return (T)(object)Stash.Bytecode.StashTypeConverter.ToList(value.AsObj);
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'List<StashValue>'.");
        }

        // StashFuture passthrough — returned by async fn calls; caller bridges via InvokeAsync<T>
        if (typeof(T) == typeof(StashFuture))
        {
            if (value.Tag == StashValueTag.Obj && value.AsObj is StashFuture future)
                return (T)(object)future;
            throw new InvalidCastException(
                $"Cannot convert Stash value tag '{value.Tag}' to 'StashFuture': " +
                $"the value is not a Future. Ensure the Stash function is declared with 'async fn'.");
        }

        // Host handle — unwrap when the wrapped CLR type matches T.
        // This branch handles the case where a Stash script returns (or passes back) a
        // registered host object. The HostHandle's ClrType must match typeof(T) exactly;
        // a wrong-type handle is an unrecoverable contract violation, so we throw clearly.
        if (typeof(T).IsClass && !typeof(T).IsAbstract)
        {
            if (value.Tag == StashValueTag.Obj && value.AsObj is HostHandle handle)
            {
                if (handle.ClrType == typeof(T))
                    return (T)handle.Target;

                throw new InvalidCastException(
                    $"Cannot convert Stash HostHandle<{handle.ClrType.Name}> to '{typeof(T).Name}': " +
                    $"the handle wraps a different registered host type. " +
                    $"Expected handle for '{typeof(T).Name}', got '{handle.ClrType.Name}'.");
            }
        }

        // All other types (POCO, records, etc.) → v2
        throw new InvalidCastException(
            $"Cannot convert Stash value to '{typeof(T).Name}': reflection-based POCO/record " +
            $"marshalling is v2. Supported return types: StashValue, JsonElement, " +
            $"string, long, int, double, float, bool, byte, byte[], " +
            $"Dictionary<string,object?>, List<StashValue>, registered host types.");
    }

    /// <summary>
    /// Lifts a raw <c>object?</c> (as returned by a Stash VM execution) to a
    /// <see cref="StashValue"/> via <see cref="StashValue.FromObject"/>, then
    /// marshals it to <typeparamref name="T"/> via <see cref="FromStash{T}"/>.
    ///
    /// This is the single place in <c>Stash.Hosting</c> that calls
    /// <see cref="StashValue.FromObject"/>; all callers route through here so the
    /// "no inline <c>object?→StashValue</c> conversion outside <c>HostMarshaller</c>"
    /// invariant is preserved.
    /// </summary>
    public static T? FromStashObject<T>(object? raw) => FromStash<T>(StashValue.FromObject(raw));

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> for compiler-generated anonymous types whose names follow the
    /// <c>&lt;&gt;f__AnonymousType…</c> or <c>&lt;&gt;__AnonType…</c> convention.
    /// </summary>
    private static bool IsAnonymousType(Type type)
    {
        // Anonymous types are always generic, sealed, have no namespace, and their CLR names
        // contain angle brackets (a convention shared by all C# compilers for anonymous types).
        return type.IsClass
               && type.IsSealed
               && type.Namespace is null
               && type.Name.Contains('<');
    }
}
