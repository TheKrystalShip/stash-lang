# Stash.Hosting Host-Object-Dispatch Benchmark Results

> Recorded: 2026-06-05
> Feature: host-object-dispatch (P5)
> Harness: `Stash.Hosting.Benchmarks` console project (Release-only, not part of `dotnet test`)

## Machine / Runtime Info

| Field      | Value                                                    |
| ---------- | -------------------------------------------------------- |
| OS         | Arch Linux (x64)                                         |
| CPU        | AMD Ryzen 7 3800X 8-Core Processor (8 physical, 16 logical cores) |
| .NET       | .NET 10.0.8 (42.42.42.42424), X64 RyuJIT AVX2           |
| GC         | Concurrent Workstation                                   |
| Build      | Release, `dotnet run -c Release`                        |
| BDN        | BenchmarkDotNet v0.13.12 (SimpleJob: 1 launch, 10 warmup, 30 measured) |

---

## Host member access

**Benchmark class:** `HostMemberAccessBenchmarks`

Each method is a warm `CallAsync` that invokes a one-line Stash function on an already-warmed
host with a registered `Player` type and `player` global. The timed window is one full
`CallAsync` round-trip — including `SemaphoreSlim.WaitAsync`, `Task.Run` dispatch, script
execution, and return-value marshalling.

| Method                               | Median     | Mean       | StdDev     | Ratio | Alloc | Notes |
|--------------------------------------|------------|------------|------------|-------|-------|-------|
| `PlainStashFunctionBaseline` (baseline) | 1.304 μs | 1.302 μs | 0.017 μs  | 1.00× | 487 B | `fn plainBaseline() { return 42; }` — no host dispatch |
| `WarmPropertyRead`                   | 1.331 μs   | 1.320 μs   | 0.061 μs   | 1.01× | 535 B | `fn readHp() { return player.hp; }` |
| `WarmMethodCall`                     | 1.452 μs   | 1.456 μs   | 0.053 μs   | 1.12× | 704 B | `fn callAttack() { return player.attack(0); }` |

> BDN SimpleJob: 1 launch, 10 warmup iterations, 30 measured iterations (N=26–28 after outlier removal).
> Priority escalation failed (Permission denied); numbers may be ±5–10 % noisier than root-level runs.

### Interpretation

With 30 measured iterations the results are clear and internally consistent. The previous
`ShortRun` table (3 iterations) showed a 15 μs figure for `WarmPropertyRead` — that was a
statistical artifact of the 3-iteration budget; the larger sample eliminates it entirely.

`WarmPropertyRead` (1.33 μs median, Ratio = 1.01×) is statistically indistinguishable from the
plain-Stash-function baseline (1.30 μs) — the getter delegate adds no measurable overhead above
the `CallAsync` infra floor. `WarmMethodCall` (1.45 μs, Ratio = 1.12×) is ~12 % above baseline,
consistent with the extra allocations and work it performs (HostBoundMethod alloc, arity check,
arg marshal, `CallDirect` → `InvokeHostDelegate` → return marshal). The ordering
— baseline ≤ property-read < method-call — matches the expected work profile.

**Verdict:** Host member access via delegate registration is comparable to the existing
`CallAsync` baseline. The dispatch added by `HostHandle` (`VMTryGetField` →
registered getter / `HostBoundMethod`) is not the bottleneck — the `SemaphoreSlim`/`Task.Run`
infrastructure dominates. **No specialised opcode, promoted IC path, or optimisation to the
`GetFieldValue` / `ExecuteCall` hot path is justified in v1.** The throughput floor for
host-member-based APIs is set by the per-call `CallAsync` cost (~1.3 μs warm), not by
member-dispatch overhead.

---

## Notes on BDN Environment

- **Priority escalation failed** (`Permission denied`) on this machine; BDN ran without elevated
  priority. Numbers may be ±5–10 % noisier than root-level runs.
- The `HostMemberAccessBenchmarks` class uses `[SimpleJob(warmupCount: 10, iterationCount: 30)]`
  for a statistically sound sample (N=26–28 after BDN outlier removal). `HostConstructionBenchmarks`
  retains its `[ShortRunJob]` (3 iterations), which is appropriate for its coarser order-of-magnitude
  verdict.
- `.NET 10.0` is not yet in BDN 0.13.12's `RuntimeMoniker` enum; `[SimpleJob]` with bare counts
  (rather than `[SimpleJob(RuntimeMoniker.Net10_0)]`) targets the host runtime automatically.
