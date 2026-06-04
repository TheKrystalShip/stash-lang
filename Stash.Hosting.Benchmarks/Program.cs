// Stash.Hosting.Benchmarks — Release-only console benchmark harness.
//
// USAGE
//   Default (full BDN protocol for construction, full Stopwatch loops for lifecycle):
//     dotnet run -c Release --project Stash.Hosting.Benchmarks
//
//   Quick mode (single BDN dry-run + reduced Stopwatch loops — for verify/CI):
//     dotnet run -c Release --project Stash.Hosting.Benchmarks -- --quick
//
// This project is NOT part of dotnet test. It must never be referenced by
// Stash.Tests or any test project (see brief.md / AOT-self-test precedent).

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Stash.Hosting.Benchmarks;

bool quick = args.Contains("--quick", StringComparer.OrdinalIgnoreCase);

PrintMachineInfo();
Console.WriteLine();

if (quick)
{
    // --quick: fast single dry-run via BDN InProcess + reduced Stopwatch loops.
    Console.WriteLine("=== QUICK MODE (reduced iterations — for verify/CI only) ===");
    Console.WriteLine();
    RunBdnQuick();
    await RunStopwatchAsync(isQuick: true);
}
else
{
    // Default: full BDN protocol for construction + full Stopwatch for lifecycle.
    Console.WriteLine("=== FULL BENCHMARK (production-quality numbers) ===");
    Console.WriteLine();
    RunBdnFull();
    await RunStopwatchAsync(isQuick: false);
}

Console.WriteLine();
Console.WriteLine("Done.");

// ── BDN runners ──────────────────────────────────────────────────────────

static void RunBdnFull()
{
    Console.WriteLine("--- HostConstructionBenchmarks (BenchmarkDotNet full protocol) ---");
    BenchmarkRunner.Run<HostConstructionBenchmarks>();
}

static void RunBdnQuick()
{
    Console.WriteLine("--- HostConstructionBenchmarks (BDN dry-run, in-process) ---");

    // InProcess + single dry iteration so it completes in seconds.
    var config = ManualConfig.Create(DefaultConfig.Instance)
        .AddJob(Job.Dry
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithId("Quick"));

    var summary = BenchmarkRunner.Run<HostConstructionBenchmarks>(config);

    // Print condensed results.
    Console.WriteLine();
    Console.WriteLine("BDN quick results:");
    foreach (var report in summary.Reports)
    {
        if (report.ResultStatistics is null) continue;
        double meanUs = report.ResultStatistics.Mean / 1000.0; // ns → µs
        Console.WriteLine($"  {report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo}: mean ≈ {meanUs:F1} µs");
    }
    Console.WriteLine();
}

// ── Stopwatch runners ─────────────────────────────────────────────────────

static async Task RunStopwatchAsync(bool isQuick)
{
    Console.WriteLine("--- HostLifecycleBenchmarks (Stopwatch median) ---");

    double lifecycleUs = isQuick
        ? await HostLifecycleBenchmarks.MeasureFullLifecycleQuickAsync()
        : await HostLifecycleBenchmarks.MeasureFullLifecycleAsync();

    double callUs = isQuick
        ? await HostLifecycleBenchmarks.MeasureWarmCallQuickAsync()
        : await HostLifecycleBenchmarks.MeasureWarmCallAsync();

    Console.WriteLine();
    Console.WriteLine("Stopwatch results (median):");
    Console.WriteLine($"  Full lifecycle (new → RunAsync → DisposeAsync): {lifecycleUs:F1} µs");
    Console.WriteLine($"  Warm CallAsync<long>(\"noop\") per call:          {callUs:F1} µs");
    Console.WriteLine();
}

// ── Machine info ──────────────────────────────────────────────────────────

static void PrintMachineInfo()
{
    Console.WriteLine("=== Stash.Hosting.Benchmarks ===");
    Console.WriteLine($"Date:       {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine($"OS:         {RuntimeInformation.OSDescription}");
    Console.WriteLine($"Arch:       {RuntimeInformation.OSArchitecture}");
    Console.WriteLine($"CPU:        {GetCpuInfo()}");
    Console.WriteLine($".NET:       {RuntimeInformation.FrameworkDescription}");
    Console.WriteLine($"Config:     Release");
}

static string GetCpuInfo()
{
    // Best-effort: read /proc/cpuinfo on Linux.
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var lines = System.IO.File.ReadAllLines("/proc/cpuinfo");
            var model = lines.FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
            if (model is not null)
                return model.Split(':')[1].Trim();
        }
    }
    catch { /* ignore — best effort */ }
    return RuntimeInformation.ProcessArchitecture.ToString();
}
