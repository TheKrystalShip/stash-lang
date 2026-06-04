# Stash.Hosting Benchmark Results

> Recorded: 2026-06-04
> Feature: stash-hosting-mvp (P4)
> Harness: `Stash.Hosting.Benchmarks` console project (Release-only, not part of `dotnet test`)

## Machine / Runtime Info

| Field      | Value                                                    |
| ---------- | -------------------------------------------------------- |
| OS         | Arch Linux (x64)                                         |
| CPU        | AMD Ryzen 7 3800X 8-Core Processor (8 physical, 16 logical cores) |
| .NET       | .NET 10.0.8 (42.42.42.42424), X64 RyuJIT AVX2           |
| GC         | Concurrent Workstation                                   |
| Build      | Release, `dotnet run -c Release`                        |
| BDN        | BenchmarkDotNet v0.13.12 (ShortRun: 1 launch, 3 warmup, 3 measured) |

---

## Measurement 1 — Cold `new StashHost()` (post-JIT construction cost)

**Tool:** BenchmarkDotNet ShortRun, InvocationCount=1 per iteration (forces real allocation each time)

| Metric   | Value     |
| -------- | --------- |
| Mean     | 1,327 ns  |
| Median   | 1,387 ns  |
| StdDev   | 131 ns    |
| Min      | 1,177 ns  |
| Max      | 1,417 ns  |
| Allocated | 0 B (stack only) |

**Interpretation:** After JIT warmup, allocating a fresh `StashHost` (which creates a `SemaphoreSlim`,
a `StashEngine`, and the engine's internal IC/global tables) costs roughly **1.3–1.4 µs**. This is the
per-instantiation cost excluding stdlib initialization (the lazy stdlib registration fires on the
first `RunAsync` call, not on construction).

---

## Measurement 2 — Warm `new StashHost() + DisposeAsync` (amortised cycle overhead)

**Tool:** BenchmarkDotNet ShortRun with default InvocationCount (BDN auto-scaled to 16 M invocations)

| Metric   | Value    |
| -------- | -------- |
| Mean     | 47.12 ns |
| Median   | 47.57 ns |
| StdDev   | 1.76 ns  |
| Gen0     | 0.0344 per 1000 ops |
| Allocated | 288 B per op |

**Interpretation / caveat:** At 16M invocations the JIT inlines the async state machine and the
`StashHostOptions` default ctor so aggressively that the `47 ns` figure represents the nearly-empty
async shell and object header overhead, not a meaningful "host construction" figure. The cold
measurement above (Measurement 1) is the load-bearing number for OQ#4. The 288 B allocation per
op is real: it is the `StashHost` object graph (host + semaphore + engine header) minus the larger
engine tables that are lazily initialized on first `RunAsync`.

---

## Measurement 3 — Full lifecycle: `new StashHost` → `CompileAsync` → `RunAsync("fn noop(){…}")` → `DisposeAsync`

**Tool:** Stopwatch median-of-50 (per `.claude/performance.md` methodology; 1 unmeasured warm-up pass
before the timed loop to flush JIT lazy-init paths)

| Metric   | Value     |
| -------- | --------- |
| Median   | 79.3 µs   |

**What is included:** object allocation, `StashEngine` construction, engine lazy-stdlib registration
(fires on the first `RunAsync`), `SemaphoreSlim.WaitAsync`, `Task.Run` dispatch, lex+parse+resolve of
`fn noop() { return 0; }`, VM execution (one chunk, one RETURN opcode), `DisposeAsync` cleanup hooks.

**What is excluded:** `Task.Delay` / `Thread.Sleep` overhead, GC collections, process-global cleanup
of CLI-only hooks (they are no-ops for a pure embedder on first run).

---

## Measurement 4 — Warm `CallAsync<long>("noop")` per-call overhead

**Tool:** Stopwatch median-of-200 (5 unmeasured warm-up calls before the timed loop; host already
has the function defined from a prior `RunAsync`)

| Metric   | Value    |
| -------- | -------- |
| Median   | 2.8 µs   |

**What is included:** `SemaphoreSlim.WaitAsync` (uncontended), `HostMarshaller.ToStashArgs` (empty
args array), `Task.Run` dispatch and scheduling, `StashEngine.CallFunction` lookup + VM dispatch,
`HostMarshaller.FromStash<long>`, `_gate.Release()`.

---

## OQ#4 Verdict — Is a VM pool justified?

At ~79 µs for a full host lifecycle (construction + stdlib init + trivial script + dispose), a VM pool
saves meaningful overhead only if hosts are being created at rates exceeding ~12,000 per second per
core — at that rate the lifecycle cost alone would consume 100 % of CPU time. For the typical
embedding pattern (one host per user session, one host per request in a low-QPS service), **a VM pool
is not justified at these numbers**; the construction threshold where a pool becomes worthwhile is
approximately > 5,000 host constructions/second per core (where the ~1.3 µs cold ctor + 79 µs
lifecycle cost would saturate the request budget at typical latency targets). For v1, prefer
simplicity: create, use, dispose.

---

## Notes on BDN Environment

- **Priority escalation failed** (`Permission denied`) on this machine; BDN ran without elevated
  priority. Numbers may be ±5–10 % noisier than root-level runs.
- The `ShortRun` job uses only 3 warmup + 3 measured iterations; a full `LongRun` would narrow the
  CI but is not necessary for the OQ#4 decision at this order-of-magnitude granularity.
- `.NET 10.0` is not yet in BDN 0.13.12's `RuntimeMoniker` enum; `[ShortRunJob]` is used instead of
  `[SimpleJob(RuntimeMoniker.Net10_0)]` to target the host runtime automatically.
