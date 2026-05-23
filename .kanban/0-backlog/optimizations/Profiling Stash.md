# Profiling Stash

## Layer 1: Instrumented Opcode Counters (5 minutes, no tools needed)

Before reaching for profilers, add **opcode frequency + time counters** directly into the dispatch loop. This tells you _what the VM spends its time on_ without any external tooling.

Add this to `VirtualMachine.Dispatch.cs` behind a `#if DEBUG` or a compile-time flag:

```csharp
// At class level:
#if PROFILE_DISPATCH
private readonly long[] _opcodeCounts = new long[256];
private readonly long[] _opcodeNanos = new long[256];
#endif

// In RunInner(), wrapping the switch:
#if PROFILE_DISPATCH
long startTick = System.Diagnostics.Stopwatch.GetTimestamp();
#endif

switch ((OpCode)instruction) { /* ... existing cases ... */ }

#if PROFILE_DISPATCH
long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - startTick;
_opcodeCounts[instruction]++;
_opcodeNanos[instruction] += elapsed;
#endif
```

Then at program exit (or in a `DumpProfile()` method), print:

```csharp
var freq = Stopwatch.Frequency;
for (int i = 0; i < 84; i++)
{
    if (_opcodeCounts[i] > 0)
        Console.Error.WriteLine($"{(OpCode)i,-20} count={_opcodeCounts[i],12:N0}  time={_opcodeNanos[i] * 1000.0 / freq,10:F2}ms");
}
```

**What this tells you:** Which opcodes dominate wall-clock time. If `LoadGlobal` is 40% of time, that's your bottleneck. If `Call` is 30%, function dispatch is the problem. This is the single most valuable measurement you can make.

**Cost:** ~5-10% overhead from `Stopwatch.GetTimestamp()` per instruction, but the relative proportions remain accurate.

---

## Layer 2: `dotnet-trace` + Speedscope (15 minutes)

This gives you **CPU sampling flame graphs** — which C# methods consume the most CPU time.

### Install:

```bash
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-counters
```

### Capture a trace:

```bash
# Run benchmark in one terminal (NOT AOT — use managed build for tracing)
dotnet run --project Stash.Cli/ -- benchmarks/bench_algorithms.stash &
PID=$!

# Attach trace (CPU sampling at 1000Hz)
dotnet-trace collect -p $PID --profile cpu-sampling --duration 00:00:30
```

Or simpler — launch with tracing from the start:

```bash
dotnet-trace collect -- dotnet run --project Stash.Cli/ -- benchmarks/bench_algorithms.stash
```

This produces a `.nettrace` file.

### View in VS Code:

1. Install the **.NET Runtime Events** extension (if available) or use **Speedscope**:
   ```bash
   dotnet-trace convert trace.nettrace --format speedscope
   ```
2. Open [speedscope.app](https://www.speedscope.app/) in your browser, drag-drop the `.speedscope.json` file
3. You get a flame graph showing exactly which methods consume CPU time

### What to look for:

- Is `RunInner` >80% of execution time? (If not, startup/compilation is a factor)
- Within `RunInner`, which `Execute*` methods dominate?
- Is `StashValue.FromObject` / `ToObject` still showing up? (Boxing residue)
- Is `Dictionary.TryGetValue` visible? (Global variable lookup cost)
- Is GC visible in the trace? (`GC.Collect`, `SGen`, etc.)

---

## Layer 3: `dotnet-counters` for GC Pressure (2 minutes)

```bash
dotnet-counters monitor -p $PID --counters System.Runtime
```

Key metrics to watch during benchmark execution:

| Counter          | What it means       | Red flag                                        |
| ---------------- | ------------------- | ----------------------------------------------- |
| `gc-heap-size`   | Total managed heap  | Growing continuously = leak                     |
| `gen-0-gc-count` | Gen0 collections    | >50/sec during benchmark = excessive allocation |
| `gen-2-gc-count` | Gen2 collections    | Any during benchmark = stop-the-world pause     |
| `alloc-rate`     | Bytes/sec allocated | >100MB/s during tight loop = boxing problem     |
| `time-in-gc`     | % time in GC        | >5% = GC is a real bottleneck                   |

If `alloc-rate` is high during `bench_algorithms.stash`, closures or collection creation is causing GC pressure. If `time-in-gc` is <1%, GC isn't your problem — it's pure dispatch overhead.

---

## Layer 4: `perf` for Hardware-Level Analysis (Linux)

This is where it gets interesting. `perf` can tell you about **branch mispredictions**, **cache misses**, and **instruction-level hotspots** — things invisible to managed profilers.

### Install:

```bash
sudo apt install linux-tools-$(uname -r) linux-tools-common
```

### Profile the AOT binary:

```bash
# Build release AOT
dotnet publish Stash.Cli/ -c Release -r linux-x64

# Run with perf stat (hardware counters summary)
perf stat ./Stash.Cli/bin/Release/net10.0/linux-x64/publish/Stash benchmarks/bench_algorithms.stash
```

This prints:

```
 Performance counter stats for './Stash ...'

    1,234,567,890  instructions              #    1.23 IPC
      456,789,012  cycles
       12,345,678  branch-misses             #    3.45% of all branches
        1,234,567  cache-misses              #    0.12% of all cache refs
```

**Critical numbers:**

- **IPC (Instructions Per Cycle):** Modern CPUs can do 3-5 IPC. If you're at 0.5-1.0, you're stalling (cache misses or branch mispredicts)
- **branch-misses >5%:** The switch dispatch is being mispredicted. This is where computed goto (C/Rust) would help.
- **cache-misses >1%:** Data layout is causing L1/L2 misses. `StashValue` at 24 bytes means ~2.6 values per 64-byte cache line — not terrible but not great.

### Annotated hotspot analysis:

```bash
# Record detailed samples
perf record -g ./Stash.Cli/bin/.../Stash benchmarks/bench_algorithms.stash

# View hotspot report
perf report
```

This shows you which _assembly instructions_ are hottest. In the AOT binary, you can see the actual machine code for your switch dispatch and identify whether the JIT generated a jump table, a binary search, or a linear scan.

---

## Layer 5: BenchmarkDotNet for Micro-Benchmarks

For isolating specific operations (e.g., "how expensive is `StashValue.FromObject` vs `StashValue.FromInt`?"), add a BenchmarkDotNet project:

```csharp
[MemoryDiagnoser]   // Shows allocations
[DisassemblyDiagnoser(maxDepth: 3)]  // Shows JIT-generated assembly
public class DispatchBenchmarks
{
    private StashValue[] _stack = new StashValue[1024];
    private int _sp = 0;

    [Benchmark]
    public StashValue StackPushPop()
    {
        _stack[_sp++] = StashValue.FromInt(42);
        return _stack[--_sp];
    }

    [Benchmark]
    public long LoadLocal()
    {
        return _stack[100 + 3].AsInt;  // frame.BaseSlot + slot
    }

    [Benchmark]
    public object? GlobalLookup()
    {
        return _globals.TryGetValue("x", out var v) ? v : null;
    }
}
```

The `[DisassemblyDiagnoser]` output is gold — it shows you the exact x86 assembly the JIT generates for each operation. You can see whether `StashValue.AsInt` actually inlines or if there's a method call overhead.

---

## What I Expect You'll Find

Based on the benchmark numbers and the code I've reviewed, here's my prediction of where the time goes — your profiling will confirm or refute this:

| Suspected bottleneck             | Evidence                                                                                                              | Expected share                                                 |
| -------------------------------- | --------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| **Switch dispatch overhead**     | 84 opcodes in one switch; CPython uses computed goto                                                                  | 20-30% — each instruction pays a branch prediction penalty     |
| **Global variable lookup**       | `Dictionary<string, object?>.TryGetValue` per access + `FromObject()` boxing on load + `ToObject()` unboxing on store | 15-25% — globals are still in `object?` land, not `StashValue` |
| **Function call frame setup**    | `PushFrame` writes 6 fields × 600K calls in function benchmark                                                        | 10-15%                                                         |
| **`StashValue` size (24 bytes)** | Cache line holds 2.6 values; Lua uses 16-byte tagged values, CPython uses 8-byte pointers                             | 10% — poor cache utilization on stack-heavy code               |
| **Upvalue `IsOpen` branch**      | Every closure variable access checks a bool                                                                           | 5% — highly predictable but still a branch                     |
| **GC pauses**                    | Closure creation allocates `VMFunction` and `Upvalue[]` per call                                                      | 5-10% if allocation-heavy                                      |

**The biggest quick win will probably be globalizing the globals** — converting `_globals` from `Dictionary<string, object?>` to `StashValue[]` indexed by global slot number (like locals), eliminating the string hash lookup + boxing round-trip on every global access. The compiler already knows global names at compile time — it could assign them numeric slots.

Start with **Layer 1** (opcode counters). That alone will tell you where 80% of the optimization opportunity sits, and it takes 5 minutes to implement. Then we can write targeted specs for whatever the profiling reveals.
