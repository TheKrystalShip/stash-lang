namespace Stash.Hosting.Benchmarks;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Stopwatch median-of-3 measurements for the coarse host lifecycle and warm
/// per-call overhead, following the mandatory methodology in
/// <c>.claude/performance.md</c>: Release build, median-of-3, machine/runtime
/// info recorded.
/// </summary>
public static class HostLifecycleBenchmarks
{
    // Trivial scripts used for lifecycle and call measurements.
    private const string TrivialScript = "fn noop() { return 0; }";
    private const string NativeNoop    = "noop";

    // Number of Stopwatch repetitions for the median.
    private const int Reps = 3;

    // ── Full lifecycle: create → RunAsync("fn noop()…") → DisposeAsync ────

    /// <summary>
    /// Measures the total wall-clock cost of constructing a host, running a
    /// trivial script (which also forces stdlib initialization), and disposing.
    /// Runs <paramref name="iterations"/> times; returns the median in microseconds.
    /// </summary>
    public static async Task<double> MeasureFullLifecycleAsync(int iterations = 50)
    {
        // Warm up: one unmeasured pass so any JIT lazy-init is flushed before timing.
        {
            await using var warmHost = new StashHost(QuietOptions());
            var warmScript = await warmHost.CompileAsync(TrivialScript);
            await warmHost.RunAsync(warmScript);
        }

        var samples = new List<double>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            {
                await using var host   = new StashHost(QuietOptions());
                var compiled           = await host.CompileAsync(TrivialScript);
                await host.RunAsync(compiled);
            }
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMicroseconds);
        }

        return Median(samples);
    }

    // ── Warm per-call overhead (CallAsync per single call) ────────────────

    /// <summary>
    /// Measures the per-call overhead of <see cref="IStashHost.CallAsync{T}"/> on an
    /// already-warmed host whose function has already been loaded.  The
    /// create/compile/first-run cost is excluded from the timed window.
    /// Runs <paramref name="iterations"/> times; returns the median in microseconds.
    /// </summary>
    public static async Task<double> MeasureWarmCallAsync(int iterations = 200)
    {
        // Setup: create a long-lived host, compile + run the script so 'noop' is defined.
        await using var host     = new StashHost(QuietOptions());
        var compiled             = await host.CompileAsync(TrivialScript);
        await host.RunAsync(compiled);

        // Warm up: a few unmeasured calls to flush any lazy-init paths.
        for (int i = 0; i < 5; i++)
            await host.CallAsync<long>(NativeNoop);

        var samples = new List<double>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await host.CallAsync<long>(NativeNoop);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMicroseconds);
        }

        return Median(samples);
    }

    // ── Quick variants (reduced iteration counts for the --quick verify flag) ─

    public static Task<double> MeasureFullLifecycleQuickAsync()  => MeasureFullLifecycleAsync(iterations: 5);
    public static Task<double> MeasureWarmCallQuickAsync()       => MeasureWarmCallAsync(iterations: 10);

    // ── Helpers ───────────────────────────────────────────────────────────

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = new List<double>(values);
        sorted.Sort();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static StashHostOptions QuietOptions() =>
        new StashHostOptions { Output = TextWriter.Null, ErrorOutput = TextWriter.Null };
}
