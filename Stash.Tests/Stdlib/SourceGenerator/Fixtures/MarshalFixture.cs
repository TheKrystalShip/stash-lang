namespace Stash.Tests.Stdlib.SourceGenerator.Fixtures;

using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Test-only fixture exercising every parameter shape supported by the source generator.
/// Used by <c>MarshalErrorTests</c>, <c>OptionalAndVariadicTests</c>.
/// </summary>
[StashNamespace]
public static partial class MarshalFixture
{
    /// <summary>Returns the long it received.</summary>
    /// <param name="n">The number.</param>
    [StashFn]
    public static long LongParam(long n) => n;

    /// <summary>Returns the double it received.</summary>
    /// <param name="n">The number.</param>
    [StashFn]
    public static double DoubleParam(double n) => n;

    /// <summary>Returns the numeric (int-or-float) value as double.</summary>
    /// <param name="n">The number.</param>
    [StashFn]
    public static double NumericParam([StashParam(Type = "number")] double n) => n;

    /// <summary>Returns the string it received.</summary>
    /// <param name="s">The string.</param>
    [StashFn]
    public static string StringParam(string s) => s;

    /// <summary>Returns the bool it received.</summary>
    /// <param name="b">The boolean.</param>
    [StashFn]
    public static bool BoolParam(bool b) => b;

    /// <summary>Returns the byte it received.</summary>
    /// <param name="b">The byte.</param>
    [StashFn]
    public static byte ByteParam(byte b) => b;

    /// <summary>Returns the buffer length.</summary>
    /// <param name="buf">The buffer.</param>
    [StashFn]
    public static long BufferParam(byte[] buf) => buf.Length;

    /// <summary>Returns the array length.</summary>
    /// <param name="arr">The array.</param>
    [StashFn]
    public static long ListParam(List<StashValue> arr) => arr.Count;

    /// <summary>Returns the dict size.</summary>
    /// <param name="d">The dict.</param>
    [StashFn]
    public static long DictParam(StashDictionary d) => d.Count;

    /// <summary>Passes through any value.</summary>
    /// <param name="v">The value.</param>
    [StashFn]
    public static StashValue AnyParam(StashValue v) => v;

    /// <summary>Returns 0 (just exercises callable extraction).</summary>
    /// <param name="fn">The callable.</param>
    [StashFn]
    public static long CallableParam(IStashCallable fn) => 0L;

    /// <summary>Returns the count of variadic args.</summary>
    /// <param name="rest">The rest of the arguments.</param>
    [StashFn]
    public static long Variadic(params StashValue[] rest) => rest.Length;

    /// <summary>Returns n with default 42.</summary>
    /// <param name="n">Optional number, defaults to 42.</param>
    [StashFn]
    public static long OptionalDefault(long n = 42) => n;

    /// <summary>Returns s or "default" when null/missing.</summary>
    /// <param name="s">Optional string, defaults to null.</param>
    [StashFn]
    public static string OptionalNullable(string? s = null) => s ?? "default";

    /// <summary>Returns n + 1 to prove ctx injection works.</summary>
    /// <param name="ctx">Interpreter context.</param>
    /// <param name="n">The number.</param>
    [StashFn]
    public static long WithCtx(IInterpreterContext ctx, long n) => n + 1;

    /// <summary>Throws if n &lt; 0; otherwise returns null.</summary>
    /// <param name="n">The number.</param>
    [StashFn]
    public static void VoidReturn(long n)
    {
        if (n < 0) throw new RuntimeError("negative");
    }
}
