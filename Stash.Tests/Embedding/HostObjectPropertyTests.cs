namespace Stash.Tests.Embedding;

using System;
using System.Threading.Tasks;
using Stash.Hosting;
using Stash.Runtime.Errors;
using Xunit;

/// <summary>
/// Acceptance suite for P2: property dispatch (read + write), HostMarshaller.FromStash&lt;T&gt;
/// HostHandle branch, InvokeHostDelegate chokepoint, HostError registration, and the
/// StashError.KindHostError single-source-of-truth test.
///
/// done_when coverage:
///   #1  — Property_ReadOnly_RegistersAndReadsViaGetter
///   #2  — Property_ReadWrite_RegistersAndReadsAndWrites
///   #3  — Property_ReadOnly_Write_RaisesReadOnlyError
///   #4  — Property_Getter_ThrowingCLR_SurfacesHostErrorViaCallAsync
///   #5  — Property_Getter_ThrowingCLR_SurfacesHostErrorViaTryCallAsync
///   #6  — FromStash_MatchingHostHandle_ReturnsUnwrappedInstance
///   #7  — FromStash_WrongTypeHostHandle_ThrowsInvalidCastException
///   #8  — KindHostError_MatchesBuiltInErrorRegistryNameOf  (bounded-domain pin)
///   #9  — Script_TryCatch_HostError_ReturnsErrorType  (in-script catchable round-trip)
/// </summary>
[Collection("ProcessGlobalSlots")]
public class HostObjectPropertyTests
{
    // ── Domain class used in tests ────────────────────────────────────────────

    private sealed class Player
    {
        public string Name { get; set; } = "Hero";
        public long Hp { get; set; } = 100;
    }

    private sealed class Enemy
    {
        public long Power { get; set; } = 50;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static StashHost MakePlayerHost(out Player player, long hp = 100)
    {
        var host = new StashHost();
        host.RegisterType<Player>(b => b
            .Property("name", p => p.Name)
            .Property("hp", p => p.Hp, (p, v) => p.Hp = v)
        );
        player = new Player { Hp = hp };
        host.SetGlobal("player", player);
        return host;
    }

    // ── #1: Read-only property getter works ───────────────────────────────────

    [Fact]
    public async Task Property_ReadOnly_ReadsViaGetter()
    {
        Player player;
        await using var host = MakePlayerHost(out player, hp: 77);

        var script = await host.CompileAsync("return player.name;");
        var result = await host.RunAsync<string>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal("Hero", result.Value);
    }

    // ── #2: Read-write property getter and setter work ─────────────────────────

    [Fact]
    public async Task Property_ReadWrite_ReadsAndWritesViaDelegate()
    {
        Player player;
        await using var host = MakePlayerHost(out player, hp: 100);

        // Write
        var writeScript = await host.CompileAsync("player.hp = 42;");
        var writeResult = await host.RunAsync(writeScript);
        Assert.True(writeResult.Success, $"Write errors: {string.Join(", ", writeResult.Errors)}");

        // Mutation is visible CLR-side (the handle references the live object).
        Assert.Equal(42L, player.Hp);

        // Read back via script.
        var readScript = await host.CompileAsync("return player.hp;");
        var readResult = await host.RunAsync<long>(readScript);
        Assert.True(readResult.Success, $"Read errors: {string.Join(", ", readResult.Errors)}");
        Assert.Equal(42L, readResult.Value);
    }

    // ── #2b: Write and read in the same script ───────────────────────────────

    [Fact]
    public async Task Property_ReadWrite_WriteAndReadInSameScript()
    {
        Player player;
        await using var host = MakePlayerHost(out player);

        var script = await host.CompileAsync("player.hp = 55; return player.hp;");
        var result = await host.RunAsync<long>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal(55L, result.Value);
        Assert.Equal(55L, player.Hp); // CLR mutation observed
    }

    // ── #3: Assigning to a read-only property raises ReadOnlyError ────────────

    [Fact]
    public async Task Property_ReadOnly_Write_RaisesReadOnlyError()
    {
        Player player;
        await using var host = MakePlayerHost(out player);

        // 'name' is registered read-only (no setter).
        var script = await host.CompileAsync("player.name = \"X\";");
        var result = await host.RunAsync(script);

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        // ReadOnlyError name from BuiltInErrorRegistry.
        Assert.Equal("ReadOnlyError", result.Errors[0].Kind);
        Assert.Contains("name", result.Errors[0].Message);
    }

    // ── #4: Getter that throws CLR exception → HostError via CallAsync ─────────

    [Fact]
    public async Task Property_Getter_ThrowingCLR_SurfacesHostErrorViaCallAsync()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(b => b
            // Register a property whose getter throws an InvalidOperationException.
            .Property<long>("bad_prop", _ => throw new InvalidOperationException("boom"))
        );
        var player = new Player();
        host.SetGlobal("player", player);

        var script = await host.CompileAsync("fn go() { return player.bad_prop; }");
        await host.RunAsync(script);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("go"));

        Assert.Equal(StashError.KindHostError, ex.Error.Kind);
        Assert.Contains("boom", ex.Error.Message);
    }

    // ── #5: Getter that throws CLR exception → HostError via TryCallAsync ──────

    [Fact]
    public async Task Property_Getter_ThrowingCLR_SurfacesHostErrorViaTryCallAsync()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(b => b
            .Property<long>("bad_prop", _ => throw new InvalidOperationException("trycatch boom"))
        );
        var player = new Player();
        host.SetGlobal("player", player);

        var script = await host.CompileAsync("fn go() { return player.bad_prop; }");
        await host.RunAsync(script);

        var result = await host.TryCallAsync<long>("go");

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal(StashError.KindHostError, result.Errors[0].Kind);
        Assert.Contains("trycatch boom", result.Errors[0].Message);
    }

    // ── #6: FromStash<T> unwraps a matching HostHandle ───────────────────────

    [Fact]
    public async Task FromStash_MatchingHostHandle_ReturnsUnwrappedInstance()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(b => b
            .Property("hp", p => p.Hp, (p, v) => p.Hp = v)
        );
        var originalPlayer = new Player { Hp = 200 };
        host.SetGlobal("player", originalPlayer);

        // A script that returns the player handle — FromStash<Player> should unwrap it.
        var script = await host.CompileAsync("return player;");
        var result = await host.RunAsync<Player>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        // The unwrapped instance is the exact same CLR object (by reference).
        Assert.Same(originalPlayer, result.Value);
    }

    // ── #7: FromStash<T> wrong-type handle → InvalidCastException ─────────────

    [Fact]
    public async Task FromStash_WrongTypeHostHandle_ThrowsInvalidCastException()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(b => b.Property("hp", p => p.Hp, (p, v) => p.Hp = v));
        host.RegisterType<Enemy>(b => b.Property("power", e => e.Power));
        host.SetGlobal("player", new Player());

        // Return the Player handle but ask FromStash to interpret it as Enemy.
        var script = await host.CompileAsync("return player;");

        // RunAsync<Enemy> should throw InvalidCastException (not a structured StashError).
        await Assert.ThrowsAsync<InvalidCastException>(
            () => host.RunAsync<Enemy>(script));
    }

    // ── #8: KindHostError const matches BuiltInErrorRegistry.NameOf(HostError) ─

    [Fact]
    public void KindHostError_MatchesBuiltInErrorRegistryNameOf()
    {
        // This test is the single-source-of-truth pin:
        // StashError.KindHostError (the SDK-facing const in Stash.Hosting) must agree
        // with BuiltInErrorRegistry.NameOf(new HostError("")) (the runtime registry in
        // Stash.Core). If either side is renamed or misspelled, this test fails loudly.
        string registryName = BuiltInErrorRegistry.NameOf(new HostError(""));
        Assert.Equal(StashError.KindHostError, registryName);
    }

    // ── #9: In-script try/catch catches HostError and returns e.type ──────────

    [Fact]
    public async Task Script_TryCatch_HostError_ReturnsErrorType()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(b => b
            // bad_prop is a REGISTERED property whose getter throws a CLR exception.
            .Property<long>("bad_prop", _ => throw new InvalidOperationException("in-script boom"))
        );
        host.SetGlobal("player", new Player());

        // The script catches the HostError in-script and returns its .type property.
        var script = await host.CompileAsync(
            "try { let _ = player.bad_prop; } catch (e) { return e.type; }");
        var result = await host.RunAsync<string>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal("HostError", result.Value);
    }

    // ── Bonus: unregistered member still uses existing fallthrough path ─────────

    [Fact]
    public async Task Script_UnregisteredMember_FallsThrough_ToExistingError()
    {
        Player player;
        await using var host = MakePlayerHost(out player);

        // 'unknown' is not in the registration — VMTryGetField returns false,
        // the VM produces its existing "cannot access field" error (not a HostError).
        var script = await host.CompileAsync("return player.unknown;");
        var result = await host.RunAsync(script);

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        // Must NOT be a HostError — it should be a RuntimeError or similar.
        Assert.NotEqual("HostError", result.Errors[0].Kind);
        Assert.Contains("unknown", result.Errors[0].Message);
    }
}
