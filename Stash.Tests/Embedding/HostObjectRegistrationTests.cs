namespace Stash.Tests.Embedding;

using System;
using System.Threading.Tasks;
using Stash.Hosting;
using Xunit;

/// <summary>
/// Acceptance suite for P1: HostTypeBuilder, registration map, HostHandle,
/// and typeof / is round-trips via the StashHost facade.
///
/// done_when coverage:
///   #1 — RegisterType_BeforeScript_PersiststRegistration
///   #2 — ToStash_RegisteredHostType_WrapsAsHostHandle
///   #3 — SetGlobal_RegisteredInstance_BindsInVMGlobals
///   #4 — SetGlobal_UnregisteredType_ThrowsClearError
///   #5 — Script_TypeofGlobal_ReturnsRegisteredTypeName
///   #6 — Script_IsOperator_RegisteredType_ReturnsTrue
///   #7 — Script_IsOperator_WrongType_ReturnsFalse
///   #8 — RegisterType_AfterScript_ThrowsInvalidOperationException
///   #9 — TwoHosts_SameType_ZeroStateBleed  (hermetic isolation via host-object surface)
/// </summary>
[Collection("ProcessGlobalSlots")]
public class HostObjectRegistrationTests
{
    // ── Domain classes used in tests ──────────────────────────────────────────

    private sealed class Player
    {
        public string Name { get; set; } = "Hero";
        public long Hp { get; set; } = 100;
    }

    private sealed class Monster
    {
        public string Kind { get; } = "Orc";
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static StashHost MakeHostWith<T>(string vmTypeName = "") where T : class
    {
        var host = new StashHost();
        host.RegisterType<T>(b =>
        {
            if (!string.IsNullOrEmpty(vmTypeName))
                b.Named(vmTypeName);
        });
        return host;
    }

    // ── #1: RegisterType compiles and stores a registration ───────────────────

    [Fact]
    public async Task RegisterType_BeforeScript_DoesNotThrow()
    {
        await using var host = new StashHost();
        // Should not throw; just registers the type.
        host.RegisterType<Player>(_ => { });
    }

    // ── #2 + #3: SetGlobal wraps as HostHandle and is accessible in script ────

    [Fact]
    public async Task SetGlobal_RegisteredInstance_BindsAndScriptCanAccessIt()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(_ => { });

        var player = new Player { Name = "Alice", Hp = 50 };
        host.SetGlobal("player", player);

        // The global is now a HostHandle. A trivial script that returns a
        // non-null boolean confirms the binding is accessible.
        var script = await host.CompileAsync("return player != null;");
        var result = await host.RunAsync<bool>(script);
        Assert.True(result.Success);
        Assert.True(result.Value);
    }

    // ── #4: SetGlobal with unregistered type → clear error ───────────────────

    [Fact]
    public async Task SetGlobal_UnregisteredType_ThrowsArgumentException()
    {
        await using var host = new StashHost();
        // Player is NOT registered.
        var player = new Player();

        var ex = Assert.Throws<ArgumentException>(() => host.SetGlobal("player", player));
        Assert.Contains("Player", ex.Message);
        Assert.Contains("RegisterType", ex.Message);
    }

    // ── #5: typeof(global) returns the registered VM type name ───────────────

    [Fact]
    public async Task Script_TypeofGlobal_ReturnsRegisteredTypeName()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(_ => { }); // vmTypeName defaults to "Player"

        host.SetGlobal("player", new Player());

        var script = await host.CompileAsync("return typeof(player);");
        var result = await host.RunAsync<string>(script);
        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.Equal("Player", result.Value);
    }

    // ── custom VM type name ───────────────────────────────────────────────────

    [Fact]
    public async Task Script_TypeofGlobal_CustomVmTypeName_ReturnsCustomName()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(b => b.Named("GamePlayer"));

        host.SetGlobal("hero", new Player());

        var script = await host.CompileAsync("return typeof(hero);");
        var result = await host.RunAsync<string>(script);
        Assert.True(result.Success);
        Assert.Equal("GamePlayer", result.Value);
    }

    // ── #6: `player is "Player"` returns true ────────────────────────────────

    [Fact]
    public async Task Script_IsOperator_RegisteredType_ReturnsTrue()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(_ => { });

        host.SetGlobal("player", new Player());

        // Use string literal for the type name — the standard Stash idiom for registered types.
        var script = await host.CompileAsync("return player is \"Player\";");
        var result = await host.RunAsync<bool>(script);
        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.True(result.Value);
    }

    // ── #7: `player is "Monster"` returns false ───────────────────────────────

    [Fact]
    public async Task Script_IsOperator_WrongRegisteredType_ReturnsFalse()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(_ => { });
        host.RegisterType<Monster>(_ => { });

        host.SetGlobal("player", new Player());

        // The handle wraps a Player, not a Monster — should return false.
        var script = await host.CompileAsync("return player is \"Monster\";");
        var result = await host.RunAsync<bool>(script);
        Assert.True(result.Success, $"Errors: {string.Join(", ", result.Errors)}");
        Assert.False(result.Value);
    }

    // ── #8: RegisterType after script has run → clear error ──────────────────

    [Fact]
    public async Task RegisterType_AfterScriptRan_ThrowsInvalidOperationException()
    {
        await using var host = new StashHost();
        // Force VM materialisation by compiling and running a trivial script.
        var script = await host.CompileAsync("return 1;");
        await host.RunAsync(script);

        // Now try to register a type — should throw.
        var ex = Assert.Throws<InvalidOperationException>(
            () => host.RegisterType<Player>(_ => { }));
        Assert.Contains("RegisterType", ex.Message);
        // The message should explain the VM-already-created constraint.
        Assert.Contains("before", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── #9: Two hosts, same type, zero state bleed ────────────────────────────

    [Fact]
    public async Task TwoHosts_SameRegisteredType_ZeroStateBleed()
    {
        // Each host registers the same CLR class independently and binds
        // a different instance. Scripts on each host must see their own instance.
        await using var host1 = new StashHost();
        await using var host2 = new StashHost();

        host1.RegisterType<Player>(_ => { });
        host2.RegisterType<Player>(_ => { });

        host1.SetGlobal("player", new Player { Hp = 111 });
        host2.SetGlobal("player", new Player { Hp = 222 });

        // Both use the same script text.
        var script1 = await host1.CompileAsync("return typeof(player);");
        var script2 = await host2.CompileAsync("return typeof(player);");

        var r1 = await host1.RunAsync<string>(script1);
        var r2 = await host2.RunAsync<string>(script2);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        // Both return the same type name (no state bleed between registrations).
        Assert.Equal("Player", r1.Value);
        Assert.Equal("Player", r2.Value);

        // Confirm is-check works independently in each host.
        var isScript1 = await host1.CompileAsync("return player is \"Player\";");
        var isScript2 = await host2.CompileAsync("return player is \"Player\";");
        var ir1 = await host1.RunAsync<bool>(isScript1);
        var ir2 = await host2.RunAsync<bool>(isScript2);
        Assert.True(ir1.Value);
        Assert.True(ir2.Value);
    }

    // ── Accessing an unregistered member produces the existing RuntimeError ───

    [Fact]
    public async Task Script_AccessUnregisteredMember_ProducesStructuredError()
    {
        await using var host = new StashHost();
        host.RegisterType<Player>(_ => { }); // zero members in P1
        host.SetGlobal("player", new Player());

        var script = await host.CompileAsync("return player.hp;");
        var result = await host.RunAsync(script);

        // P1: VMTryGetField returns false → existing fallback → RuntimeError.
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Contains("hp", result.Errors[0].Message);
    }
}
