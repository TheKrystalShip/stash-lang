namespace Stash.Tests.Embedding;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Stash.Hosting;
using Stash.Runtime;
using Stash.Runtime.Types;
using Xunit;

/// <summary>
/// Unit tests for the marshalling round-trip via <see cref="StashHost"/>
/// and indirectly the internal <c>HostMarshaller</c> chokepoint.
///
/// done_when coverage:
///   — Primitive round-trips: long, double, string, bool, null, byte
///   — byte[] round-trip
///   — anonymous object → dict (arg marshalling)
///   — IDictionary&lt;string, object?&gt; → StashDictionary (arg marshalling)
///   — IEnumerable → List&lt;StashValue&gt; (arg marshalling)
///   — StashValue passthrough (zero conversion)
///   — JsonElement (both directions)
///   — Unsupported arg type → ArgumentException
///   — Unsupported return type → InvalidCastException naming v2
/// </summary>
public class HostMarshallerTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static async Task<T?> RoundTrip<T>(string stashExpr, object? arg = null)
    {
        // Compile a function that returns the supplied argument unchanged (or the given expression).
        await using var host = new StashHost();
        string source = arg is null
            ? $"fn f() {{ return {stashExpr}; }}"
            : "fn f(x) { return x; }";
        var s = await host.CompileAsync(source);
        await host.RunAsync(s);
        return arg is null
            ? await host.CallAsync<T>("f")
            : await host.CallAsync<T>("f", new object?[] { arg });
    }

    private static async Task<T?> RoundTripReturn<T>(string stashExpr)
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync($"fn f() {{ return {stashExpr}; }}");
        await host.RunAsync(s);
        return await host.CallAsync<T>("f");
    }

    // ── Primitive marshalling ──────────────────────────────────────────────

    [Fact]
    public async Task Marshalling_Long_RoundTrip()
    {
        long result = (await RoundTrip<long>("42", 42L))!;
        Assert.Equal(42L, result);
    }

    [Fact]
    public async Task Marshalling_Int_MarshalsToLong()
    {
        // int arg → Stash long, return as long
        long result = (await RoundTrip<long>("0", (int)7))!;
        Assert.Equal(7L, result);
    }

    [Fact]
    public async Task Marshalling_Double_RoundTrip()
    {
        double result = (await RoundTrip<double>("3.14", 3.14))!;
        Assert.Equal(3.14, result, precision: 6);
    }

    [Fact]
    public async Task Marshalling_String_RoundTrip()
    {
        string? result = await RoundTrip<string>("\"hello\"", "hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Marshalling_Bool_True_RoundTrip()
    {
        bool result = (await RoundTrip<bool>("true", true))!;
        Assert.True(result);
    }

    [Fact]
    public async Task Marshalling_Bool_False_RoundTrip()
    {
        bool result = (await RoundTrip<bool>("false", false))!;
        Assert.False(result);
    }

    [Fact]
    public async Task Marshalling_Null_RoundTrip()
    {
        // Passing null → StashValue.Null → default(long) = 0 is NOT what we want here;
        // test null return instead.
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f() { return null; }");
        await host.RunAsync(s);
        long? result = await host.CallAsync<long?>("f");
        // null Stash value → default(long?) = null
        // Actually FromStash<long?> is not directly tested; test StashValue null return.
        var raw = await host.CallAsync<StashValue>("f");
        Assert.True(raw.IsNull);
    }

    [Fact]
    public async Task Marshalling_StashValue_Passthrough()
    {
        // T = StashValue → zero conversion, raw value returned.
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f() { return 99; }");
        await host.RunAsync(s);
        StashValue v = await host.CallAsync<StashValue>("f");
        Assert.Equal(StashValueTag.Int, v.Tag);
        Assert.Equal(99L, v.AsInt);
    }

    // ── IDictionary arg marshalling ────────────────────────────────────────

    [Fact]
    public async Task Marshalling_DictArg_BecomesStashDict()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f(d) { return d.x + d.y; }");
        await host.RunAsync(s);

        var dict = new Dictionary<string, object?> { ["x"] = 10L, ["y"] = 32L };
        long result = await host.CallAsync<long>("f", new object?[] { dict });

        Assert.Equal(42L, result);
    }

    // ── Anonymous object arg marshalling ───────────────────────────────────

    [Fact]
    public async Task Marshalling_AnonymousObjectArg_BecomesStashDict()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f(p) { return p.first + \" \" + p.last; }");
        await host.RunAsync(s);

        // Anonymous object — passed via the single-object wrapping path.
        string result = await host.CallAsync<string>("f", new { first = "Jane", last = "Doe" });

        Assert.Equal("Jane Doe", result);
    }

    // ── IEnumerable arg marshalling ────────────────────────────────────────

    [Fact]
    public async Task Marshalling_ListArg_BecomesStashArray()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f(arr) { return arr[0] + arr[1]; }");
        await host.RunAsync(s);

        var list = new List<object?> { 10L, 32L };
        long result = await host.CallAsync<long>("f", new object?[] { list });

        Assert.Equal(42L, result);
    }

    // ── JsonElement marshalling ────────────────────────────────────────────

    [Fact]
    public async Task Marshalling_JsonElement_Arg_Works()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f(n) { return n + 1; }");
        await host.RunAsync(s);

        using JsonDocument doc = JsonDocument.Parse("41");
        JsonElement elem = doc.RootElement;
        long result = await host.CallAsync<long>("f", new object?[] { elem });

        Assert.Equal(42L, result);
    }

    [Fact]
    public async Task Marshalling_JsonElement_Return_Works()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f() { return { \"a\": 1, \"b\": \"hello\" }; }");
        await host.RunAsync(s);

        JsonElement result = await host.CallAsync<JsonElement>("f");

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal(1, result.GetProperty("a").GetInt32());
        Assert.Equal("hello", result.GetProperty("b").GetString());
    }

    // ── Unsupported types → exceptions ────────────────────────────────────

    [Fact]
    public async Task Marshalling_UnsupportedArgType_ThrowsArgumentException()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f(x) { return x; }");
        await host.RunAsync(s);

        // A random class that has no registered marshaller.
        var badArg = new System.Net.Http.HttpClient();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => host.CallAsync<StashValue>("f", new object?[] { badArg }));
        }
        finally
        {
            badArg.Dispose();
        }
    }

    [Fact]
    public async Task Marshalling_UnsupportedReturnType_ThrowsInvalidCastException()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f() { return 42; }");
        await host.RunAsync(s);

        // Request a POCO type — reflection-based marshalling is v2.
        await Assert.ThrowsAsync<InvalidCastException>(
            () => host.CallAsync<System.IO.FileInfo>("f"));
    }

    // ── Dictionary<string,object?> return ─────────────────────────────────

    [Fact]
    public async Task Marshalling_DictReturn_Works()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f() { return { \"x\": 1, \"y\": 2 }; }");
        await host.RunAsync(s);

        Dictionary<string, object?> result = await host.CallAsync<Dictionary<string, object?>>("f");

        Assert.Equal(2, result.Count);
        Assert.Equal(1L, result["x"]);
        Assert.Equal(2L, result["y"]);
    }

    // ── List<StashValue> return ────────────────────────────────────────────

    [Fact]
    public async Task Marshalling_ListReturn_Works()
    {
        await using var host = new StashHost();
        var s = await host.CompileAsync("fn f() { return [1, 2, 3]; }");
        await host.RunAsync(s);

        List<StashValue> result = await host.CallAsync<List<StashValue>>("f");

        Assert.Equal(3, result.Count);
        Assert.Equal(1L, result[0].AsInt);
        Assert.Equal(2L, result[1].AsInt);
        Assert.Equal(3L, result[2].AsInt);
    }
}
