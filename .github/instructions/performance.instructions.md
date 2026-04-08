---
description: "Use when: optimizing performance of the compiler or bytecode VM, profiling hot paths, running benchmarks, analyzing perf/dotnet-trace output, verifying that a performance improvement actually worked, or discussing runtime bottlenecks."
---

# Performance Work

## Mandatory Verification Workflow

Every performance change **must** be verified with before/after measurements. Do not merge or consider a performance optimization complete without empirical evidence that it improved performance.

### Step 1 — Baseline

Run the relevant benchmarks **before** making any changes and record the median results:

```bash
cd benchmarks/
./run_all_benchmarks.sh        # Full cross-language comparison (median of 3 runs)
```

For targeted work, run individual Stash benchmarks directly:

```bash
dotnet run --project ../Stash.Cli/ -c Release -- bench_algorithms.stash
dotnet run --project ../Stash.Cli/ -c Release -- bench_function_calls.stash
dotnet run --project ../Stash.Cli/ -c Release -- bench_lexer_heavy.stash
dotnet run --project ../Stash.Cli/ -c Release -- bench_namespace_calls.stash
dotnet run --project ../Stash.Cli/ -c Release -- bench_scope_lookup.stash
dotnet run --project ../Stash.Cli/ -c Release -- bench_numeric.stash
dotnet run --project ../Stash.Cli/ -c Release -- bench_env_stress.stash
```

### Step 2 — Profile

Use the system profiling tools to identify the actual bottleneck **before** writing optimization code:

```bash
# CPU sampling with perf (AOT binary — best signal)
dotnet publish ../Stash.Cli/ -c Release -o /tmp/stash-perf
perf record -g /tmp/stash-perf/stash benchmarks/bench_algorithms.stash
perf report --sort=dso,symbol

# Quick stats (cycles, instructions, branch misses)
perf stat /tmp/stash-perf/stash benchmarks/bench_algorithms.stash

# Managed profiling with dotnet-trace (when AOT profiling is insufficient)
dotnet-trace collect -- dotnet run --project ../Stash.Cli/ -c Release -- benchmarks/bench_algorithms.stash
# Open the .nettrace in a speedscope viewer or VS diagnostics

# GC and allocation analysis
dotnet-counters collect -- dotnet run --project ../Stash.Cli/ -c Release -- benchmarks/bench_algorithms.stash
```

### Step 3 — Implement

Make the change. Keep optimizations focused and isolated — one bottleneck per PR.

### Step 4 — Verify

Re-run the **same benchmarks** from Step 1 and compare. A valid improvement should show:

- Consistent improvement across multiple runs (not noise)
- No regressions in unrelated benchmarks
- Profile data (perf/dotnet-trace) confirming the targeted hotspot shrank

If the numbers don't improve, or other benchmarks regress, investigate before proceeding.

## Available Benchmarks

| File                          | What it stresses                                         |
| ----------------------------- | -------------------------------------------------------- |
| `bench_algorithms.stash`      | Recursion, iteration, array manipulation, struct usage   |
| `bench_function_calls.stash`  | Call overhead, parameter binding, return values          |
| `bench_lexer_heavy.stash`     | Expression parsing and evaluation throughput             |
| `bench_namespace_calls.stash` | Built-in namespace dispatch (math._, str._, conv.\*)     |
| `bench_scope_lookup.stash`    | Variable lookup, scope chain walking, closures           |
| `bench_numeric.stash`         | Boxing/unboxing, arithmetic fast paths, numeric dispatch |
| `bench_env_stress.stash`      | Environment chain stress, deep scopes, closure capture   |

Each benchmark has equivalents in Python, Node.js, Ruby, Perl, Lua, and Bash for cross-language comparison via `run_all_benchmarks.sh`.

## Bytecode Inspection

Use `--disassemble` to print the bytecode the compiler produces for a script without executing it. This is useful for confirming that optimizations (such as superinstruction fusion) are firing, and for understanding what the VM actually executes in hot loops:

```bash
# Show optimized bytecode (default)
stash --disassemble bench_algorithms.stash

# Show unoptimized bytecode for comparison
stash --no-optimize --disassemble bench_algorithms.stash

# Works with inline code too
stash --disassemble -c 'let x = 1 + 2;'
```

The output includes the main script chunk and all nested function chunks, with opcode names, operand values, constant annotations, and jump targets. Compare optimized vs unoptimized output to verify that fused opcodes (`LL_Add`, `LC_LessThan`, `DupStoreLocalPop`, etc.) and specializations (`LoadLocal0`–`LoadLocal3`, `Call0`–`Call2`) are being emitted where expected.

## Profiling Tips

- **Always profile the AOT binary** (`dotnet publish -c Release`) for `perf` — the JIT binary has different characteristics.
- **Use `perf stat`** first for a quick overview (IPC, branch miss rate, cache misses) before diving into `perf record`.
- **Watch for GC write barriers** (`RhpAssignRef*`) — these indicate boxing/allocation pressure in the VM hot loop.
- **Dictionary.FindValue** in profiles means variable access is hitting hash lookups — consider slot-based access.
- **Run benchmarks 3+ times** and use the median. Single runs are unreliable.
- When profiling specific opcodes or dispatch paths, `bench_numeric.stash` and `bench_env_stress.stash` are the most targeted Stash-only benchmarks.
