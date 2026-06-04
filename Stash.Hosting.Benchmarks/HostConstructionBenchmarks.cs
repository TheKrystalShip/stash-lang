namespace Stash.Hosting.Benchmarks;

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

/// <summary>
/// BenchmarkDotNet micro-benchmarks for <see cref="StashHost"/> construction.
/// Measures cold (fresh process-state) and warm (subsequent) host creation cost.
/// These run under the full BDN protocol by default; use <c>--quick</c> for a
/// fast single-iteration run (see <see cref="Program"/>).
/// No explicit [SimpleJob]: BDN uses the host's runtime (net10.0 on this machine).
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class HostConstructionBenchmarks
{
    // ── Cold construction ─────────────────────────────────────────────────
    // Each iteration creates a new host and disposes it.  BDN runs warmup
    // iterations first so "cold" here means after JIT warmup — the SDK's
    // first-call cost, not process-cold.  The IterationSetup / IterationCleanup
    // pair ensures each measured call starts from a freshly-disposed state.

    private StashHost? _hostToDispose;

    [IterationCleanup(Target = nameof(ConstructionCold))]
    public void CleanupCold()
    {
        // Dispose synchronously via GetAwaiter().GetResult() in cleanup.
        _hostToDispose?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _hostToDispose = null;
    }

    /// <summary>Measures the wall-clock cost of constructing a fresh <see cref="StashHost"/>.</summary>
    [Benchmark(Description = "new StashHost() — cold (post-JIT)")]
    public StashHost ConstructionCold()
    {
        _hostToDispose = new StashHost();
        return _hostToDispose;
    }

    // ── Warm construction (allocate + dispose in the same iteration) ──────
    // Exercises the full ctor→dispose cycle back-to-back so BDN sees the
    // amortised warm allocation cost including the SemaphoreSlim and engine.

    /// <summary>Measures <see cref="StashHost"/> construction and immediate disposal.</summary>
    [Benchmark(Description = "new StashHost() + DisposeAsync — warm cycle")]
    public async Task ConstructionWarmCycle()
    {
        await using var host = new StashHost();
        // intentionally empty: just measure ctor + dispose overhead
        _ = host;
    }
}
