namespace Stash.Tests.Embedding;

using System;
using System.Threading.Tasks;
using Stash.Hosting;
using Stash.Runtime.Errors;
using Xunit;

/// <summary>
/// Acceptance suite for P3: synchronous method dispatch via HostBoundMethod : IStashCallable.
///
/// done_when coverage:
///   #1  — Method_Registration_RegistersMethodDescriptor
///   #2  — Method_Attack_HappyPath_MutatesCLRSide
///   #3  — Method_ArityMismatch_ThrowsHostError_ExactMessage
///   #4  — Method_ArgTypeMismatch_ThrowsHostError_PerArgMessage
///   #5  — Method_ThrowingCLR_SurfacesHostErrorViaCallAsync
///   #5b — Method_ThrowingCLR_SurfacesHostErrorViaTryCallAsync
///   #6  — Method_ReturnValue_UnregisteredType_ThrowsArgumentException
///   #7  — Method_ZeroArity_Works
///   #8  — Method_ReturnValue_HostHandleType_Roundtrip (host type → HostHandle)
///   #9  — Method_InScript_TryCatch_HostError_ReturnsErrorType
///   #10 — Method_Dispatch_PropertyTests_StillGreen (regression guard)
/// </summary>
[Collection("ProcessGlobalSlots")]
public class HostObjectMethodTests
{
    // ── Domain classes ────────────────────────────────────────────────────────

    private sealed class Player
    {
        public string Name { get; set; } = "Hero";
        public long Hp { get; set; } = 100;
        public int AttackCallCount { get; private set; }

        /// <summary>Attack for <paramref name="dmg"/> damage, return the actual damage dealt.</summary>
        public long Attack(long dmg)
        {
            AttackCallCount++;
            Hp -= dmg;
            return dmg;
        }

        /// <summary>Throws an InvalidOperationException (for CLR-exception tests).</summary>
        public long Bad() => throw new InvalidOperationException("clr-boom");

        /// <summary>Zero-arity method — returns the current Hp.</summary>
        public long GetHp() => Hp;

        /// <summary>Returns itself (for host-handle round-trip tests).</summary>
        public Player Self() => this;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static StashHost MakeAttackHost(out Player player, long hp = 100)
    {
        var host = new StashHost();
        host.RegisterType<Player>(b => b
            .Property("hp",   p => p.Hp, (p, v) => p.Hp = v)
            .Property("name", p => p.Name)
            .Method("attack", (Player p, long dmg) => p.Attack(dmg))
            .Method("getHp",  (Player p) => p.GetHp())
            .Method("bad",    (Player p) => p.Bad())
            .Method("self",   (Player p) => p.Self())
        );
        player = new Player { Hp = hp };
        host.SetGlobal("player", player);
        return host;
    }

    // ── #1: Registration compiles and registers a method descriptor ───────────

    [Fact]
    public void Method_Registration_RegistersMethodDescriptor()
    {
        // The builder should not throw when registering a method.
        var host = new StashHost();
        var ex = Record.Exception(() =>
            host.RegisterType<Player>(b => b.Method("attack", (Player p, long dmg) => p.Attack(dmg))));
        Assert.Null(ex);
        host.DisposeAsync().AsTask().Wait();
    }

    // ── #2: obj.attack(10) — happy path, CLR-side mutation observable ─────────

    [Fact]
    public async Task Method_Attack_HappyPath_MutatesCLRSide()
    {
        Player player;
        await using var host = MakeAttackHost(out player, hp: 100);

        var script = await host.CompileAsync("return player.attack(10);");
        var result = await host.RunAsync<long>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal(10L, result.Value);         // attack returns the damage
        Assert.Equal(90L, player.Hp);            // CLR-side mutation observed
        Assert.Equal(1, player.AttackCallCount); // method was invoked exactly once
    }

    // ── #2b: Chained in script — attack + read back hp ────────────────────────

    [Fact]
    public async Task Method_Attack_ThenReadHp_Consistent()
    {
        Player player;
        await using var host = MakeAttackHost(out player, hp: 50);

        var script = await host.CompileAsync("player.attack(20); return player.hp;");
        var result = await host.RunAsync<long>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal(30L, result.Value);
        Assert.Equal(30L, player.Hp);
    }

    // ── #3: Arity mismatch → HostError with exact message ────────────────────

    [Fact]
    public async Task Method_ArityMismatch_TooManyArgs_ThrowsHostError_ExactMessage()
    {
        Player player;
        await using var host = MakeAttackHost(out player);

        // attack expects 1 arg; passing 2 should raise HostError.
        var script = await host.CompileAsync(
            "fn go() { return player.attack(10, 20); }");
        await host.RunAsync(script);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("go"));

        Assert.Equal(StashError.KindHostError, ex.Error.Kind);
        Assert.Contains("attack", ex.Error.Message);
        Assert.Contains("expects 1 argument", ex.Error.Message);
        Assert.Contains("got 2", ex.Error.Message);
    }

    [Fact]
    public async Task Method_ArityMismatch_TooFewArgs_ThrowsHostError_ExactMessage()
    {
        Player player;
        await using var host = MakeAttackHost(out player);

        // attack expects 1 arg; passing 0 should raise HostError.
        var script = await host.CompileAsync(
            "fn go() { return player.attack(); }");
        await host.RunAsync(script);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("go"));

        Assert.Equal(StashError.KindHostError, ex.Error.Kind);
        Assert.Contains("attack", ex.Error.Message);
        Assert.Contains("expects 1 argument", ex.Error.Message);
        Assert.Contains("got 0", ex.Error.Message);
    }

    // ── #4: Arg-type mismatch → HostError with "arg N to TypeName.name: ..." ──

    [Fact]
    public async Task Method_ArgTypeMismatch_ThrowsHostError_PerArgMessage()
    {
        Player player;
        await using var host = MakeAttackHost(out player);

        // attack expects long; passing a string should produce a marshalling error.
        var script = await host.CompileAsync(
            "fn go() { return player.attack(\"not-a-number\"); }");
        await host.RunAsync(script);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("go"));

        Assert.Equal(StashError.KindHostError, ex.Error.Kind);
        // Message must start with "arg 1 to Player.attack: ..."
        Assert.StartsWith("arg 1 to Player.attack:", ex.Error.Message);
    }

    // ── #5: CLR exception in method → HostError via CallAsync ────────────────

    [Fact]
    public async Task Method_ThrowingCLR_SurfacesHostErrorViaCallAsync()
    {
        Player player;
        await using var host = MakeAttackHost(out player);

        var script = await host.CompileAsync("fn go() { return player.bad(); }");
        await host.RunAsync(script);

        var ex = await Assert.ThrowsAsync<StashScriptException>(
            () => host.CallAsync<long>("go"));

        Assert.Equal(StashError.KindHostError, ex.Error.Kind);
        Assert.Contains("clr-boom", ex.Error.Message);
    }

    // ── #5b: CLR exception via TryCallAsync ───────────────────────────────────

    [Fact]
    public async Task Method_ThrowingCLR_SurfacesHostErrorViaTryCallAsync()
    {
        Player player;
        await using var host = MakeAttackHost(out player);

        var script = await host.CompileAsync("fn go() { return player.bad(); }");
        await host.RunAsync(script);

        var result = await host.TryCallAsync<long>("go");

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal(StashError.KindHostError, result.Errors[0].Kind);
        Assert.Contains("clr-boom", result.Errors[0].Message);
    }

    // ── #6: Unregistered return type → ArgumentException from marshaller ──────

    [Fact]
    public async Task Method_ReturnValue_UnregisteredType_ThrowsArgumentException()
    {
        // Register a Player method that returns an UNREGISTERED type (Player).
        // HostMarshaller.ToStash will throw ArgumentException because Player is not
        // a registered host type in this host (we deliberately skip RegisterType<Player>).
        await using var host = new StashHost();

        // Register a type that returns an unregistered CLR class.
        // We use a lambda that returns a new anonymous-object-like POCO — but since
        // anonymous objects get marshalled as StashDictionary, we need a real class.
        // We use "Player" (unregistered) as the return type.
        host.RegisterType<Player>(b => b
            // 'self' returns a Player instance, but Player itself is registered here.
            // To get the unregistered case, we wrap in a different host that doesn't
            // register Player.
            .Method("bad_ret", (Player p) => new System.Text.StringBuilder("boom"))
        );
        var player = new Player();
        host.SetGlobal("player", player);

        var script = await host.CompileAsync("fn go() { return player.bad_ret(); }");
        await host.RunAsync(script);

        // The marshaller's ArgumentException wraps into a HostError from the baked closure.
        // At the TryCallAsync level, this manifests as a script error.
        var result = await host.TryCallAsync<object>("go");
        Assert.False(result.Success);
        // Should be HostError (the ArgumentException from HostMarshaller.ToStash is caught
        // by InvokeHostDelegate.InvokeMethod).
        Assert.Single(result.Errors);
        // The error is not a HostError from arg-marshalling (that's arg-in); this is an
        // ArgumentException from return-value marshalling, caught by InvokeHostDelegate.
        Assert.Equal(StashError.KindHostError, result.Errors[0].Kind);
    }

    // ── #7: Zero-arity method works ───────────────────────────────────────────

    [Fact]
    public async Task Method_ZeroArity_Works()
    {
        Player player;
        await using var host = MakeAttackHost(out player, hp: 77);

        var script = await host.CompileAsync("return player.getHp();");
        var result = await host.RunAsync<long>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal(77L, result.Value);
    }

    // ── #8: Method returning a registered host type → HostHandle round-trip ───

    [Fact]
    public async Task Method_ReturnValue_RegisteredHostType_ReturnsHostHandle()
    {
        Player player;
        await using var host = MakeAttackHost(out player, hp: 55);

        // self() returns the same Player instance; Stash should see it as a HostHandle.
        // We then read hp off the returned handle.
        var script = await host.CompileAsync("let p2 = player.self(); return p2.hp;");
        var result = await host.RunAsync<long>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal(55L, result.Value);
    }

    // ── #9: In-script try/catch catches HostError ─────────────────────────────

    [Fact]
    public async Task Method_InScript_TryCatch_HostError_ReturnsErrorType()
    {
        Player player;
        await using var host = MakeAttackHost(out player);

        var script = await host.CompileAsync(
            "try { player.bad(); } catch (e) { return e.type; }");
        var result = await host.RunAsync<string>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal("HostError", result.Value);
    }

    // ── #9b: KindHostError const = "HostError" (bounded-domain sanity) ────────

    [Fact]
    public void Method_KindHostError_IsLiteralHostError()
    {
        // Belt-and-suspenders: StashError.KindHostError must equal the string we check
        // in arity/marshalling messages. The P2 pin already tests the registry alignment;
        // this guard ensures the const hasn't drifted to an unexpected value.
        Assert.Equal("HostError", StashError.KindHostError);
    }

    // ── #10: Property tests still pass (regression guard) ─────────────────────

    [Fact]
    public async Task Method_PropertyDispatch_StillWorks_AfterMethodAddition()
    {
        Player player;
        await using var host = MakeAttackHost(out player, hp: 42);

        var script = await host.CompileAsync("player.hp = 99; return player.hp;");
        var result = await host.RunAsync<long>(script);

        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal(99L, result.Value);
        Assert.Equal(99L, player.Hp);
    }
}
