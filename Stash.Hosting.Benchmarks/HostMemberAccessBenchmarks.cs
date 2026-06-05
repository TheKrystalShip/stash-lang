namespace Stash.Hosting.Benchmarks;

using System;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

/// <summary>
/// BenchmarkDotNet micro-benchmarks for warm host-object member access via registered
/// delegates, measured against a plain Stash function call as a baseline.
///
/// Each benchmark operates on a single already-warmed <see cref="StashHost"/> whose
/// Player type is registered and whose per-benchmark function is already loaded —
/// so the timed window measures only the member-access dispatch overhead, not
/// compilation, registration, or lazy stdlib init.
///
/// Three methods:
///   1. <see cref="WarmPropertyRead"/> — one warm property-read via
///      HostHandle.VMTryGetField → registered getter.
///   2. <see cref="WarmMethodCall"/> — one warm sync method call via
///      HostBoundMethod.CallDirect → InvokeHostDelegate.
///   3. <see cref="PlainStashFunctionBaseline"/> (Baseline=true) — one warm
///      CallAsync to an equivalent plain Stash function (no host-object dispatch).
///
/// The verdict question: is host member access comparable to plain Stash function
/// overhead, or does it add a cost that would justify a specialised IC / promoted
/// opcode? Expected: comparable — no optimisation warranted in v1.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class HostMemberAccessBenchmarks
{
    // ── Domain fixture ──────────────────────────────────────────────────────

    private sealed class Player
    {
        public long Hp { get; set; } = 100;
        public int AttackCallCount { get; private set; }

        public long Attack(long dmg)
        {
            AttackCallCount++;
            Hp -= dmg;
            return dmg;
        }
    }

    // ── State ───────────────────────────────────────────────────────────────

    private StashHost _host = null!;

    // ── Setup ───────────────────────────────────────────────────────────────

    [GlobalSetup]
    public void Setup()
    {
        var player = new Player { Hp = 1_000_000 };  // large so hp never goes negative

        _host = new StashHost(new StashHostOptions
        {
            Output = TextWriter.Null,
            ErrorOutput = TextWriter.Null,
        });

        _host.RegisterType<Player>(b => b
            .Property("hp",    p => p.Hp, (p, v) => p.Hp = v)
            .Method("attack",  (Player p, long dmg) => p.Attack(dmg))
        );
        _host.SetGlobal("player", player);

        // Load all three benchmark functions in a single RunAsync.
        const string setup = """
            fn readHp()        { return player.hp; }
            fn callAttack()    { return player.attack(0); }
            fn plainBaseline() { return 42; }
            """;

        var compiled = _host.CompileAsync(setup).GetAwaiter().GetResult();
        _host.RunAsync(compiled).GetAwaiter().GetResult();

        // Warm-up: a few unmeasured calls to flush any lazy-init / JIT paths.
        for (int i = 0; i < 5; i++)
        {
            _host.CallAsync<long>("readHp").GetAwaiter().GetResult();
            _host.CallAsync<long>("callAttack").GetAwaiter().GetResult();
            _host.CallAsync<long>("plainBaseline").GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Benchmarks ──────────────────────────────────────────────────────────

    /// <summary>
    /// Warm property-read: script calls <c>player.hp</c> via the registered getter.
    /// Measures one CallAsync + HostHandle.VMTryGetField + getter-delegate invoke.
    /// </summary>
    [Benchmark(Description = "Warm property-read: player.hp via registered getter")]
    public async Task<long> WarmPropertyRead()
    {
        return await _host.CallAsync<long>("readHp");
    }

    /// <summary>
    /// Warm method call: script calls <c>player.attack(0)</c> via HostBoundMethod.
    /// Measures one CallAsync + HostHandle.VMTryGetField + HostBoundMethod.CallDirect
    /// + InvokeHostDelegate + HostMarshaller arg/return marshal.
    /// </summary>
    [Benchmark(Description = "Warm method-call: player.attack(0) via HostBoundMethod")]
    public async Task<long> WarmMethodCall()
    {
        return await _host.CallAsync<long>("callAttack");
    }

    /// <summary>
    /// Baseline: a plain Stash function that returns an integer literal.
    /// No host-object dispatch — isolates the host/engine overhead common to all
    /// three benchmarks so the delta to <see cref="WarmPropertyRead"/> and
    /// <see cref="WarmMethodCall"/> is the pure member-access cost.
    /// </summary>
    [Benchmark(Description = "Baseline: plain Stash fn returning 42 (no host dispatch)", Baseline = true)]
    public async Task<long> PlainStashFunctionBaseline()
    {
        return await _host.CallAsync<long>("plainBaseline");
    }
}
