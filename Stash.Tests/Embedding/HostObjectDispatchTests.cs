namespace Stash.Tests.Embedding;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Hosting;
using Xunit;

/// <summary>
/// P5 end-to-end consolidation suite for host-object-dispatch.
///
/// Ties together the full brief Acceptance Criteria: register a Player type with
/// property dispatch, sync method dispatch, and async method dispatch; bind an
/// instance as a Stash global; run the brief's canonical script; verify the
/// return value and CLR-side mutation.
///
/// Also includes the two-host isolation test that re-asserts the hermetic-VM
/// foundation through the host-object-dispatch surface (done_when item #2).
///
/// done_when coverage:
///   #1  — EndToEnd_PlayerExample_FullRoundTrip_ClrMutationObservable
///   #2  — TwoHosts_SameType_ZeroSharedState_MembersIsolated
/// </summary>
[Collection("ProcessGlobalSlots")]
public class HostObjectDispatchTests
{
    // ── Domain class: Player (mirrors the brief's Acceptance Criteria) ─────────

    /// <summary>
    /// Player CLR class matching the brief's Acceptance Criteria exactly:
    ///   string Name; long Hp; long Attack(long dmg); Task&lt;bool&gt; FetchAsync(string);
    /// </summary>
    private sealed class Player
    {
        public string Name { get; set; } = "Hero";
        public long Hp { get; set; } = 100;
        public int AttackCallCount { get; private set; }

        /// <summary>Attack for <paramref name="dmg"/> damage. Decrements Hp and returns dmg.</summary>
        public long Attack(long dmg)
        {
            AttackCallCount++;
            Hp -= dmg;
            return dmg;
        }

        /// <summary>
        /// Async fetch — always resolves to <c>true</c>. The brief uses Task&lt;bool&gt;.
        /// </summary>
        public async Task<bool> FetchAsync(string url)
        {
            await Task.Yield(); // ensure the task actually goes async
            _ = url;            // consume the arg (not observable here)
            return true;
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a host with the full Player member table (brief Acceptance Criteria).
    /// </summary>
    private static StashHost MakePlayerHost(out Player player, long hp = 100)
    {
        var host = new StashHost();
        host.RegisterType<Player>(b => b
            .Property("name",  p => p.Name)
            .Property("hp",    p => p.Hp,    (p, v) => p.Hp = v)
            .Method("attack",  (Player p, long dmg) => p.Attack(dmg))
            .AsyncMethod("fetch", (Player p, string url) => p.FetchAsync(url))
        );
        player = new Player { Hp = hp };
        host.SetGlobal("player", player);
        return host;
    }

    // ── #1: End-to-end Player example (brief Acceptance Criteria) ────────────

    /// <summary>
    /// Runs the brief's canonical acceptance script:
    /// <code>
    ///   fn run() {
    ///       let dmg = player.attack(10);
    ///       player.hp = player.hp - dmg;
    ///       let ok = await player.fetch("/url");
    ///       return { name: player.name, hp: player.hp, ok: ok };
    ///   }
    /// </code>
    /// Note: the brief writes `player.hp -= dmg` but the Stash language does not
    /// have compound-assignment operators; the semantically equivalent form
    /// `player.hp = player.hp - dmg` is used here.
    ///
    /// Verifies:
    /// - The returned dict contains the expected keys/values.
    /// - CLR-side mutation is observable after the script completes (Hp decreased,
    ///   Attack was called exactly once).
    /// </summary>
    [Fact]
    public async Task EndToEnd_PlayerExample_FullRoundTrip_ClrMutationObservable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Player player;
        await using var host = MakePlayerHost(out player, hp: 100);

        // The brief's canonical script — translated to valid Stash syntax.
        var script = await host.CompileAsync("""
            fn run() {
                let dmg = player.attack(10);
                player.hp = player.hp - dmg;
                let ok = await player.fetch("/url");
                return { "name": player.name, "hp": player.hp, "ok": ok };
            }
            """, timeout.Token);

        await host.RunAsync(script, timeout.Token);

        // Call the function and capture the dict return value.
        var result = await host.CallAsync<Dictionary<string, object?>>(
            "run", null, timeout.Token);

        // ── Verify returned dict ───────────────────────────────────────────────
        Assert.Equal(3, result.Count);
        Assert.Equal("Hero", result["name"]);
        // attack(10) decrements hp by 10; the script then writes player.hp = player.hp - dmg
        // = 90 - 10 = 80. (attack returns the damage AND mutates Hp CLR-side — hp goes to 90
        // when attack runs; then the script assignment subtracts dmg=10 again → 80).
        Assert.Equal(80L, result["hp"]);
        Assert.Equal(true, result["ok"]);

        // ── Verify CLR-side mutation ───────────────────────────────────────────
        Assert.Equal(80L, player.Hp);          // mutation visible CLR-side
        Assert.Equal(1, player.AttackCallCount); // Attack invoked exactly once
    }

    // ── #2: Two-host isolation — same type, independent instances ────────────

    /// <summary>
    /// Registers the same <see cref="Player"/> type in two independent
    /// <see cref="StashHost"/> instances with different <see cref="Player"/> instances.
    /// Mutates each host's player via script and asserts ZERO state bleed:
    /// - host1's mutation is visible only in player1 CLR-side.
    /// - host2's mutation is visible only in player2 CLR-side.
    /// - Neither host can observe the other's instance through dispatch.
    /// </summary>
    [Fact]
    public async Task TwoHosts_SameType_ZeroSharedState_MembersIsolated()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // ── Set up two independent hosts ────────────────────────────────────
        var host1 = new StashHost();
        var host2 = new StashHost();

        var player1 = new Player { Hp = 100 };
        var player2 = new Player { Hp = 200 };

        host1.RegisterType<Player>(b => b
            .Property("hp",   p => p.Hp,    (p, v) => p.Hp = v)
            .Method("attack", (Player p, long dmg) => p.Attack(dmg))
        );
        host2.RegisterType<Player>(b => b
            .Property("hp",   p => p.Hp,    (p, v) => p.Hp = v)
            .Method("attack", (Player p, long dmg) => p.Attack(dmg))
        );

        host1.SetGlobal("player", player1);
        host2.SetGlobal("player", player2);

        // Define the same attack function in both hosts.
        const string attackScript = "fn attack(dmg) { player.attack(dmg); return player.hp; }";
        var compiled1 = await host1.CompileAsync(attackScript, timeout.Token);
        var compiled2 = await host2.CompileAsync(attackScript, timeout.Token);

        await host1.RunAsync(compiled1, timeout.Token);
        await host2.RunAsync(compiled2, timeout.Token);

        // ── Mutate via host1 ────────────────────────────────────────────────
        long hp1After = await host1.CallAsync<long>("attack", new object?[] { 30L }, timeout.Token);

        // ── Mutate via host2 ────────────────────────────────────────────────
        long hp2After = await host2.CallAsync<long>("attack", new object?[] { 50L }, timeout.Token);

        // ── Verify ZERO state bleed ─────────────────────────────────────────
        // host1: player1.Hp started at 100, attack(30) → 70
        Assert.Equal(70L, hp1After);
        Assert.Equal(70L, player1.Hp);
        Assert.Equal(1, player1.AttackCallCount);

        // host2: player2.Hp started at 200, attack(50) → 150
        Assert.Equal(150L, hp2After);
        Assert.Equal(150L, player2.Hp);
        Assert.Equal(1, player2.AttackCallCount);

        // Cross-check: no bleed (player1 still at 70, player2 at 150 — not each other's value)
        Assert.NotEqual(player1.Hp, player2.Hp);

        await host1.DisposeAsync();
        await host2.DisposeAsync();
    }
}
