# Compiling Stash to .NET IL — Feasibility Analysis

> **Status:** Exploratory analysis (backlog)
> **Created:** 2026-05-01
> **Type:** Architectural option study — no implementation commitment
> **Question:** _How impossible is it to compile Stash to .NET IL so that the .NET backend can run Stash, removing the need for our custom VM?_

---

## TL;DR

**Possible? Yes.** **Worth it? Almost certainly no, in the form the question implies.**

Compiling Stash directly to IL and dropping the custom VM would:

- Cost roughly as much code as the current VM (~5–8k SLOC of IL emission to replace ~3k SLOC of dispatch loop, plus a runtime support library that ends up being most of what `Stash.Bytecode` already does).
- Either **kill Native AOT** (if we go the JIT/`Reflection.Emit` route) or **kill the REPL and fast script startup** (if we go the static-AOT route).
- Deliver a **modest** performance improvement at best — likely 1.2×–2× on tight numeric loops, and **near-zero** on shell scripts, dict-heavy workloads, and stdlib-bound code, which is most real Stash code.
- Force a rewrite of the DAP, the `.stashc` cache format, and the REPL's incremental compilation model.

The dominant cost in Stash execution is **virtual dispatch through the `IVM*` protocol interfaces and managed allocation of `StashValue.Obj` heap references**, not the bytecode dispatch loop. IL compilation does not help with either, because both are inherent to Stash's dynamic, tagged-union value model.

There is, however, a **narrow viable experiment** worth running (see §10): a static-AOT-only "compile script to assembly" mode, kept side-by-side with the VM. This is a small bet with bounded scope.

---

## 1. What "compile to IL" actually means

The phrase is ambiguous. There are at least four distinct paths, with very different consequences:

| Option                                 | Description                                                    | Native AOT?                          | REPL?  | Fast startup?                      |
| -------------------------------------- | -------------------------------------------------------------- | ------------------------------------ | ------ | ---------------------------------- |
| **A. Static AOT-to-IL**                | `stash build foo.stash → foo.dll`; CLI launches the assembly.  | ✅ Yes (ILC bundles)                 | ❌ No  | ❌ No (assembly load cost)         |
| **B. Runtime JIT (`Reflection.Emit`)** | At script load time, compile chunks to IL via `DynamicMethod`. | ❌ **No** (RE not supported in NAOT) | ✅ Yes | ❌ No (JIT cost on every run)      |
| **C. C# source generation**            | Transpile `.stash` → `.cs` at build time, compile via Roslyn.  | ✅ Yes                               | ❌ No  | ❌ No (build step + assembly load) |
| **D. Hybrid tiered JIT**               | Keep VM; promote hot methods to IL via `DynamicMethod`.        | ❌ **No**                            | ✅ Yes | ✅ Yes                             |

**The Native AOT constraint is the single biggest factor.** `Stash.Cli` ships as a Native AOT binary today (see [Stash.Cli/Stash.Cli.csproj](Stash.Cli/Stash.Cli.csproj)) precisely because we want a single-file, fast-startup, no-runtime-required executable for sysadmin use cases. Any IL-emission-at-runtime path (B, D) silently demotes the CLI to a regular self-contained .NET app, which:

- Adds ~30 MB of JIT/runtime baggage.
- Adds 50–200 ms of cold-start overhead.
- Re-introduces the dependency on `System.Reflection.Emit`, which the NAOT compiler will refuse to publish.
- Breaks the existing deployment story (`stash` as a copy-and-run binary).

So Options B and D are essentially off the table unless we're willing to abandon NAOT — which we shouldn't be, because fast cold start is one of the things that makes Stash competitive with bash for system administration.

That leaves **A** and **C** as the only paths that preserve the deployment model. Both require giving up the REPL, or running it through a separate (non-AOT) interpreter binary.

---

## 2. What the VM actually does (and what IL can't replace)

The custom VM is often imagined as "just a dispatch loop over opcodes." It isn't. The VM is a thin layer (`VirtualMachine.Dispatch.cs`) sitting on top of a much larger **runtime support library**. Removing the dispatch loop does **not** remove the runtime.

### What's in the dispatch loop (~3k SLOC, replaceable by IL)

- The big `switch` over 99 opcodes
- IP/frame management
- Register read/write through `_stack[BaseSlot + r]`
- Inline cache slot lookup (`CallBuiltIn`, `GetFieldIC`)

### What's _outside_ the dispatch loop (the real runtime, ~25–30k SLOC, **NOT** replaceable by IL)

- `StashValue` tagged union (`Stash.Core/Runtime/StashValue.cs`)
- All 13 `IVM*` protocol implementations across every domain type (`Stash.Core/Runtime/Protocols/`)
- All 35 stdlib namespaces (`Stash.Stdlib/BuiltIns/*.cs`)
- `StashStruct` / `StashEnum` / `StashError` / `VMFunction` / `VMBoundMethod` / closures / upvalues
- Defer stack management
- Lock handles + signal-driven cleanup (`FileLockHandle`)
- Shell pipeline execution (`VirtualMachine.Process.cs`)
- Try-expression value semantics
- Retry / timeout / elevate orchestration
- Bytecode serialization

**An IL backend has to call into all of this exactly the way the VM does today.** The win is not "deleting the runtime" — there is no runtime to delete. The win is at most "deleting the dispatch overhead." That overhead is real but small: my rough estimate is **5–15 % of total runtime** on dynamic code, much less on stdlib-bound workloads.

---

## 3. Where the time actually goes

A representative Stash hot loop:

```stash
let total = 0;
for (let i = 0; i < 1000000; i = i + 1) {
    total = total + i * 2;
}
```

Current VM cost per iteration (rough):

1. Dispatch (switch + IP) → ~3 ns
2. `Add` opcode → calls `IVMArithmetic.VMAdd(StashValue, StashValue)` → tag check → integer add → wrap result in `StashValue.FromInt` → ~5 ns
3. Same for `Mul` → ~5 ns
4. `Lt` comparison → ~3 ns
5. `JmpFalse` → ~2 ns

Total: ~18 ns/iter, of which dispatch is ~3 ns (~17 %). IL compilation removes the 3 ns. The other 15 ns are inherent to the value representation and the `IVM*` virtual calls.

The Post-Quickening Strategy spec (`.kanban/0-backlog/VM Performance — Post-Quickening Strategy.md`) targets the **15 ns**, not the 3 ns, by specializing operations on observed types. **That's the right battle to fight.** A `AddII` opcode that knows both operands are integers can do the work in ~2 ns — better than what naive IL compilation would produce, because the `IVMArithmetic.VMAdd` virtual call is still there in the IL version unless we duplicate the same monomorphization work.

**Lesson from prior art:**

- **IronPython / IronRuby** both compiled to IL via the DLR. Both were eventually outpaced (or matched) by the interpreter-based CPython/MRI on most workloads. Microsoft effectively shelved the DLR. The IL backend did not save them; aggressive type specialization in the interpreter (PyPy, TruffleRuby) did.
- **Roslyn scripting** compiles C# to IL on every invocation. Hello-world cold start is ~300–500 ms. We don't want that.
- **LuaJIT** is the closest "compile dynamic language to native" success. It does NOT use the .NET JIT — it has its own custom trace-based JIT specifically because general-purpose JITs can't see through tagged unions efficiently.

The pattern is clear: **for highly dynamic languages, the dispatch loop is rarely the bottleneck; the value representation and call protocol are.** Compiling to a general-purpose IL doesn't fix those.

---

## 4. Feature-by-feature translation cost

| Stash feature                      | IL translation feasibility         | Notes                                                                                                                                                                                                                                          |
| ---------------------------------- | ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| Arithmetic, comparisons, jumps     | Easy                               | Direct IL `add`, `sub`, `brfalse` after tag check helper.                                                                                                                                                                                      |
| Local variables / register windows | Easy                               | Map to IL locals; lifetime analysis trivial after compiler hands off.                                                                                                                                                                          |
| Function calls                     | Easy → Medium                      | Direct `call` for known callees; `callvirt` on `IStashCallable` otherwise.                                                                                                                                                                     |
| Closures / upvalues                | Medium                             | IL has no closures; emit a class per closure with captured fields (what C# does for lambdas). Open/closed upvalue distinction maps awkwardly.                                                                                                  |
| Field access on `StashStruct`      | Medium                             | Still needs runtime lookup unless we monomorphize. IL `callvirt` to `IVMFieldAccessible.VMGetField(string)` — same as VM.                                                                                                                      |
| Namespace built-in calls           | Easy                               | Direct `call` to `BuiltInFunction` delegate; **better than VM here** (no IC slot lookup). Modest win.                                                                                                                                          |
| `try expr` returning null          | Medium                             | IL `try`/`catch` blocks around an expression; need helper to suppress and return null. Doable but verbose.                                                                                                                                     |
| `defer` LIFO                       | **Hard**                           | IL `finally` is single-block, not a LIFO stack. Requires explicit defer-list per frame, executed before return — basically a port of `RunFrameDefers`. No help from IL here.                                                                   |
| `lock { }` blocks                  | Medium-Hard                        | Full `FileLockHandle` runtime + signal handler registration must be called from IL. Same code as VM, just invoked from generated IL.                                                                                                           |
| `elevate { }`                      | Medium                             | Sets context flag; subsequent shell commands check it. IL just calls into the same runtime.                                                                                                                                                    |
| `retry { } until ...`              | Medium                             | Loop in IL with closure for body. Doable but the runtime helper does most of the work today.                                                                                                                                                   |
| `timeout(d) { }`                   | Medium                             | Same as retry; uses `CancellationToken` plumbing.                                                                                                                                                                                              |
| `await`                            | Hard                               | Stash async is incomplete. Mapping to .NET `Task` ABI is a significant design exercise unrelated to "compile to IL."                                                                                                                           |
| `$(cmd                             | cmd)`                              | Medium                                                                                                                                                                                                                                         | Pipeline runtime exists; IL just calls `VirtualMachine.RunPassthroughPipeline`. |
| `is` checks                        | Easy                               | Tag check + cast.                                                                                                                                                                                                                              |
| Pattern destructuring              | Medium                             | IL sequence of field/index reads + assigns.                                                                                                                                                                                                    |
| Bytecode caching (`.stashc`)       | Replaced                           | Becomes assembly caching (`.dll`). Different file format, different headers, no longer self-describing.                                                                                                                                        |
| REPL incremental compilation       | **Very hard / impossible in NAOT** | Requires `Reflection.Emit` or per-input assembly load + unloading. Not viable under NAOT.                                                                                                                                                      |
| DAP debugger                       | **Hard**                           | Current DAP uses bytecode + sequence points (`Stash.Bytecode/Debugging/`). IL would need either PDB emission with mapped sequence points (doable but new) or a parallel debug-mode interpreter. Expect to rewrite roughly half of `Stash.Dap`. |
| Static analysis                    | Unchanged                          | AST-based, doesn't touch bytecode or IL.                                                                                                                                                                                                       |
| LSP                                | Unchanged                          | Same.                                                                                                                                                                                                                                          |

**Estimate of new code required**: 6–10k SLOC for the IL emitter (one IL emission method per AST node × visitor pattern × edge cases) + 1–2k SLOC of runtime helpers + 2–3k SLOC of debugger glue = **~10–15k SLOC of new code**, replacing ~3k SLOC of dispatch loop.

---

## 5. Honest cost-benefit ledger

### What we'd lose (Option A — static AOT)

- The REPL, unless we ship a separate interpreter binary
- `.stashc` (bytecode caches), replaced by `.dll`
- Fast script startup for one-shot scripts (every script runs through assembly load)
- The shell mode (which depends on REPL incremental compilation)
- `eval`-style flexibility (we don't have one yet, but this closes the door permanently)
- Maintenance simplicity: instead of one execution path, we'd have static-compiled-Stash + interpreted-REPL-Stash, and they must stay semantically identical
- Several thousand existing test-suite scenarios that exercise the VM directly (would need re-targeting)

### What we'd lose (Option B/D — runtime JIT)

- Native AOT entirely
- The single-binary deployment story
- Cold start performance (the actual reason sysadmins might pick Stash over Python)
- Compatibility with locked-down environments that don't allow JIT

### What we'd gain

- A **modest** perf win on tight loops (estimate: 1.2–2× on synthetic numeric benchmarks; ≤1.2× on real shell scripts)
- Standard .NET tooling can profile generated IL (dotnet-trace works on the VM today too — minor win)
- One conceptual model ("Stash is just .NET") that some users might find appealing
- Possibly easier to embed Stash in C# applications (call the generated assemblies) — but this is also achievable today via `VirtualMachine.Run`

### What stays the same

- Stdlib (still C#)
- Cross-platform behavior (still C#'s problem)
- Static analysis quality
- LSP feature set
- Memory footprint of running scripts (`StashValue` allocation pattern unchanged)

### What gets worse

- Maintenance burden (more code, more layers, two execution semantics to keep aligned)
- Debugger complexity
- Cold start (in every option)
- Build time of `Stash.Cli` (IL emission adds dependencies; Roslyn for Option C is huge)

---

## 6. Comparable systems and what they teach us

| System                             | Approach                                               | Outcome                                                                                                |
| ---------------------------------- | ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------ |
| **IronPython**                     | Compile Python → IL via DLR                            | Slower than CPython on most workloads; project effectively dormant.                                    |
| **IronRuby**                       | Compile Ruby → IL via DLR                              | Discontinued. JRuby (JVM) outperformed it.                                                             |
| **DLR (Dynamic Language Runtime)** | MS framework for dynamic→IL                            | De facto abandoned; demonstrates that "compile dynamic language to .NET IL" is not a free lunch.       |
| **Roslyn scripting** (`csi`)       | Compile C# scripts via Roslyn → IL                     | Works, but cold start is hundreds of ms; not a model for a sysadmin tool.                              |
| **F# Interactive**                 | Static-typed dynamic compilation                       | Works because F# is statically typed; the IL win comes from typing, not from .NET.                     |
| **PowerShell**                     | Tree-walking interpreter + ad-hoc IL via DynamicMethod | Hot paths use DynamicMethod; cold start is famously slow (~300–800 ms); requires JIT runtime, no NAOT. |
| **LuaJIT**                         | Custom trace-based JIT, not host JIT                   | Massive perf wins because it fully owns the JIT and value representation.                              |
| **Boo / Nemerle**                  | Static .NET languages → IL                             | Successful precisely because they're statically typed.                                                 |

**The pattern**: dynamic languages on host JITs (DLR, IronPython, IronRuby) consistently underperform their dedicated interpreters/JITs. Static languages on host JITs (F#, Boo) succeed. **Stash is closer to the first group than the second.**

PowerShell is the most directly comparable system — a sysadmin-targeted dynamic language on .NET. Its cold-start problem is one of its most-cited pain points. Stash beats it today substantially because of NAOT. Throwing that advantage away to chase a marginal interpreter perf win would be strategically backwards.

---

## 7. Decision framework

Before pursuing any IL strategy, we should ask:

1. **Is the VM dispatch loop actually the bottleneck for our users?**
   - Profile real Stash scripts (sysadmin workloads — log parsing, file ops, shell pipelines, JSON munging)
   - If dispatch is <10 % of runtime, IL compilation is unjustifiable on perf grounds
   - Existing benchmarks in `benchmarks/` mostly measure dispatch-heavy synthetic loops; they over-state the potential win

2. **Is fast cold start more valuable to our users than peak throughput?**
   - For sysadmin scripts, almost always yes
   - For long-running daemons (rare in our target), maybe not
   - Users running `stash backup.stash` from cron care about start-up; users running 24h batch jobs care about throughput

3. **Can the same perf gains be had inside the VM?**
   - Yes — the Post-Quickening Strategy spec already targets the dominant costs (type specialization, monomorphization, `AddII`-style fused ops)
   - This work happens inside the existing dispatch loop, preserves NAOT, and improves stdlib-bound code too

If the answers are "no, yes, yes" — which I believe they are — then **the right move is to pursue the post-quickening strategy and not pursue IL compilation**.

---

## 8. The narrow case where IL _might_ win

There is one realistic scenario worth keeping in mind:

**"Build mode" for production scripts.** A user writes `myscript.stash`, then runs `stash build myscript.stash` once, getting `myscript.dll` (or `myscript` native binary via NAOT/ILC). The build is slow; the resulting binary starts faster than even the current VM-on-NAOT path because there's no bytecode load step.

This is **Option A, opt-in, side-by-side with the existing VM**. It would:

- Preserve REPL and fast-script-mode (which still use the VM)
- Be limited to scripts that don't rely on `eval`/REPL semantics (already the case — we have no `eval`)
- Be primarily a deployment convenience, not a perf play
- Cost roughly the same as Option A standalone (~10–15k SLOC) but without forcing every script through it

**Even this is debatable.** The existing path (`stash myscript.stash`) is already a single binary launch; the win is at most "no bytecode loading" — measured in microseconds. The conceptual win ("I get a `.dll` I can drop into a C# app") may be more valuable than the perf win.

If we ever do this, it should be **after** the post-quickening work, **after** we have benchmarks that show users would benefit, and **explicitly framed as a deployment feature, not a performance feature**.

---

## 9. Risks of pursuing IL compilation

| Risk                                   | Severity     | Notes                                                                                                                      |
| -------------------------------------- | ------------ | -------------------------------------------------------------------------------------------------------------------------- |
| Loss of Native AOT                     | **Critical** | Drops one of Stash's strongest differentiators.                                                                            |
| REPL regression / loss                 | **High**     | The shell mode + REPL is core to interactive sysadmin use.                                                                 |
| Two execution paths drift in semantics | **High**     | Subtle bugs where compiled-IL Stash behaves differently from interpreted Stash. Test surface doubles.                      |
| Debugger rewrite                       | **Medium**   | DAP currently uses bytecode + IP sequence points. IL needs PDBs, sequence point mapping, locals scopes — significant work. |
| Marginal perf gain disappoints         | **Medium**   | If we ship IL backend and users see <2× improvement on their actual scripts, we've spent months for little user benefit.   |
| Build system complexity                | **Medium**   | New artifact type (`.dll` or per-platform binary), new caching, new versioning.                                            |
| .stashc format becomes legacy          | **Low**      | Can keep both, but ongoing burden.                                                                                         |

---

## 10. Recommendation

**Do not pursue IL compilation as a replacement for the VM.** The cost-benefit math doesn't work, the prior art (IronPython, IronRuby, DLR) is uniformly discouraging, and the most valuable target audience (sysadmins running short scripts) cares more about cold start than throughput — which is the opposite of what IL compilation optimizes.

**Do pursue:**

1. **Profile real workloads first** (per `.kanban/0-backlog/Profiling Stash.md`). Confirm where time actually goes.
2. **Execute the Post-Quickening Strategy** (`.kanban/0-backlog/VM Performance — Post-Quickening Strategy.md`). This targets the dominant costs (virtual dispatch through `IVM*`, allocation of boxed values) inside the existing architecture. Preserves NAOT, REPL, shell mode, and the deployment story.
3. **AOT-Compatible Adaptive Optimization** (`.kanban/0-backlog/AOT-Compatible Adaptive Optimization — Analysis.md`) is the right model: monomorphization, inline caching, and quickening — none of which require IL emission.

**Optional small experiment, if curiosity demands:**

- Spend 1–3 days using `System.Reflection.Emit` to compile **one** hand-picked Stash function (e.g., a tight numeric loop) to an `IL DynamicMethod`, calling into the existing runtime for everything else
- Benchmark against the same function in the VM
- If the speedup is >3× on a realistic workload (not just a synthetic micro-benchmark), reopen this discussion
- Be aware that this experiment requires temporarily disabling NAOT for the test binary — that's fine for an experiment but proves the broader incompatibility

The expected outcome: 1.2×–1.8× on a numeric loop, ≤1.1× on anything stdlib-bound. That result alone is worth knowing and would close the question.

---

## 11. Decisions Recorded

> **Decision:** Reject IL compilation as a replacement for the bytecode VM.
> **Alternatives considered:** Static AOT-to-IL (Option A); Runtime JIT via `Reflection.Emit` (Option B); C# source generation (Option C); Hybrid tiered JIT (Option D).
> **Rationale:** Options B and D break Native AOT, the deployment property that makes Stash competitive in the sysadmin niche. Options A and C kill the REPL and fast script startup. None deliver enough perf to justify the cost, because the dominant runtime cost in Stash is virtual dispatch through `IVM*` protocols and allocation of `StashValue.Obj`, neither of which IL emission addresses. Comparable dynamic-language-to-IL projects (IronPython, IronRuby, DLR) failed to deliver on the same promise.
> **Risks of reversal:** If real-world profiling later shows the dispatch loop is the dominant cost (>30 % of runtime) and post-quickening fails to recover the gap, a focused Option A "build mode" could be revisited as a deployment feature, not a perf play.

> **Decision:** Continue investing in the post-quickening / adaptive optimization strategy inside the existing VM.
> **Alternatives considered:** Skipping perf work entirely; rewriting in Rust for speed; IL compilation.
> **Rationale:** Specialization inside the VM addresses the actual dominant costs, preserves NAOT and REPL, and has direct precedent in successful dynamic-language runtimes (V8, LuaJIT, PyPy all win primarily through type specialization, not through host JIT compilation).
> **Risks of reversal:** None obvious; this is the conservative path.

---

## 12. Open questions (worth resolving even without IL work)

- **What is the actual dispatch overhead as a fraction of total runtime?** Needs profiling on representative workloads.
- **Could we expose Stash as a callable .NET library more cleanly?** This is sometimes the _real_ reason people ask "can it compile to IL?" — and is answerable independently by improving the embedding API around `VirtualMachine`.
- **Is there a path to Native AOT for `Stash.Lsp` and `Stash.Dap`?** Currently blocked by OmniSharp/DryIoc reflection. Solving this would be more impactful than IL compilation, since it cuts another layer of "you need .NET runtime to use the LSP."

These questions are worth tracking separately from the IL question.
