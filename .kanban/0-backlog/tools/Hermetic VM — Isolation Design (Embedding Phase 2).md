# Hermetic VM — Isolation Design (Embedding Roadmap, Phase 2)

> **Status:** Design note — NOT yet a spec. Do **not** bootstrap `/spec` until the `readonly`
> modifier (phase 1) ships, because the async-child model here depends on the concrete freeze API
> that phase produces (see §3).
> **Tracked under:** the `embedding` milestone (`.kanban/milestones/embedding/MILESTONE.md`) as
> **phase 2**. The milestone records the strict-sequencing decision and the spec-time obligation
> to re-verify §4's leak inventory against current source before writing `plan.yaml`.
> **Created:** 2026-05-30
> **Type:** Design note — captures fresh design context so the future architect inherits it.
> **Discovery context:** Produced during the from-scratch embedding design discussion (2026-05-30)
> that also produced the `readonly` modifier spec. This is **phase 2** of a 3-phase roadmap:
> `readonly` modifier → **hermetic VM** → `Stash.Hosting` host SDK.
> **Relationship to prior art:** Reaffirms and partly **supersedes** the older
> `0-backlog/tools/Stash Embedding API — Host SDK Design Analysis.md` (§13/§14). See §6 for exactly
> what is reaffirmed vs changed.

---

## 1. What "hermetic" means here

The goal of the embedding initiative is one self-contained, isolated `StashEngine` per host, in the
spirit of Lua's `lua_State`: constructing two engines in one process yields two universes that share
nothing observable. "Hermetically sealing the VM" is the work that makes that true.

Isolation splits into **two distinct scopes that must be solved separately.** Conflating them is the
main trap.

- **Scope A — engine ↔ engine (process-global state).** Two independent `VirtualMachine` instances
  in the same process must not leak into each other. Today they do, via stdlib operations that reach
  past the instance into `System.Environment`, process-wide static registries, and mutable shared
  `Chunk` state.
- **Scope B — parent ↔ async-child (within one engine).** `async fn` already forks a **child VM on a
  thread-pool thread** and shares reference-typed globals with the parent by reference. This is a
  **live correctness hazard today**, independent of embedding.

These have different fixes. Scope A is *virtualization* of process-global resources. Scope B is
*value-graph isolation* of shared globals. A single spec may cover both, or they may be split (§7).

## 2. Scope B — the live hazard (parent ↔ async-child)

`VirtualMachine.SpawnAsyncFunction` (`Stash.Bytecode/VM/VirtualMachine.Async.cs`) forks a child VM
via `Task.Run` and captures parent state. The relevant facts (read from current source):

- **Globals are shallow-copied:** `new Dictionary<string, StashValue>(_globals)`. A new dictionary,
  but `StashValue` is a struct holding an `_obj` reference, so every reference-typed global (dict,
  array, `StashInstance`, `StashError`) is **shared by reference** across the parent and the child
  running on a different thread. The runtime value types are not thread-safe → concurrent mutation
  of a shared global collection races and can corrupt internal storage.
- **`_importStack` is shared by reference** — a plain `List` handed to the child (`capturedImportStack
  = _importStack`), read/written across threads during `import`.
- **IO is already hardened:** the parent's `Output`/`ErrorOutput` are upgraded to
  `SynchronizedTextWriter` before the fork — evidence the authors knew about cross-thread sharing but
  only closed the IO axis.
- `ModuleCache` / `ModuleLocks` are `ConcurrentDictionary` (already safe). `Chunk` is shared (see the
  IC-slot leak in §5).

**Target model (decided):** when forking a child, build its globals **per entry**:
- **Frozen value** (`readonly`/`freeze`) → **share by reference** (safe: immutable can't race).
- **Non-frozen reference value** → **deep-clone** (cycle-safe) into the child so each task gets a
  private copy.
- Primitives copy by value already (struct semantics) — no work.

`_importStack` must be snapshotted/isolated per child, not shared. IO stays `SynchronizedTextWriter`.

**Author contract:** async bodies get isolated mutable state; they communicate results via **return
values** (`await` / `task.all`), not by mutating shared globals. Mutating a non-frozen global from an
async body becomes call-local (hits the clone), not a cross-thread write. This is a behavior change
from today's (racy) shared-mutation and warrants a migration note.

## 3. Hard dependency on phase 1 (`readonly`/`freeze`) — why we wait to spec

Scope B's core check is *"is this global value frozen? share it : deep-clone it."* That rests on
artifacts the `readonly` spec **explicitly defers to its own implementers**:

- **`readonly-modifier` Q1 (array carrier).** Whether plain arrays carry the `IsFrozen` flag via a
  wrapper class or an extended carrier is left to the P3 implementer. The uniform "is this value
  frozen?" surface and the `DeepFreeze` walker are P3 outputs. Phase 2's deep-**clone** walker should
  **reuse P3's cycle-safe traversal** rather than reinvent it.
- **Q2 (freeze capability gate)** and the final `IsFrozen` surface across dict/array/struct/`StashError`.

Writing phase 2's `plan.yaml` (`files`, `verify`, `done_when`) before that API is concrete means
guessing against types that don't exist yet. Hence: **design now (this note), spec after phase 1 ships.**

## 4. Scope A — engine ↔ engine process-global leak inventory

The older §13 audit (2026-05-01) enumerated the process-global leaks. Treat the list below as the
**starting inventory to re-verify against current code at spec time**, not gospel — files move.
Each leak's fix is virtualization onto per-VM state.

| Leak | Today | Fix |
| --- | --- | --- |
| **Inline-cache writes on shared `Chunk`** (`Bytecode/Chunk.cs` `ICSlots[]`, `ICSlot.cs`) | Mutable struct array shared across VMs running the same compiled chunk → concurrent IC writes silently corrupt cached field offsets. **Critical**, and already live under async (parent + child run the same chunk). | **Per-VM IC-slot clone on chunk load** (`Array.Copy` of the fixed-size struct array). Rest of `Chunk` stays shared + immutable. `.stashc` format unaffected (IC slots are runtime-only). |
| **`System.Environment.CurrentDirectory`** via `env.chdir`/`popDir`/`dirStack` (`CurrentProcessImpl.cs`) | Mutates real process cwd; engine 2's relative paths resolve against engine 1's cwd. | Virtual **`VMContext.WorkingDirectory`**; all stdlib relative-path resolution (`fs.*`, `path.resolve`, `process.spawn`, `io.*`, `tpl.renderFile`, glob) routes through it. Init from `Environment.CurrentDirectory` at construction only. |
| **`System.Environment` env vars** via `env.set`/`get`/`unset` (`EnvBuiltIns.cs`) | Process-wide. | Virtual **`VMContext.EnvVars`** overlay (`null` = unset); `env.get` consults overlay then falls back. |
| **`process.spawn`** | Inherits process cwd/env. | Set `ProcessStartInfo.WorkingDirectory = ctx.WorkingDirectory` and merge `ctx.EnvVars` — spawned children inherit the **VM's** view (what scripts already expect). |
| **Signal handler registry** (`SignalImpl.cs` static dict) + POSIX regs on `VMContext` (`_sigtermReg`/`_sighupReg`) | Last-writer-wins; SIGTERM runs only one VM's defer/lock cleanup → leaked file locks. | **Multiplex:** `ConcurrentDictionary<string, List<(IInterpreterContext, IStashCallable)>>`; on signal, invoke every handler in registration order, each `try/catch`-wrapped; `signal.off(name)` removes the calling VM's handlers (by `IInterpreterContext` identity). |
| **`Console.Out`/`Error`/`In` defaults** (`VMContext.cs`) | Default `Output` falls through to `Console.Out` → interleaved cross-engine output. | Ensure each engine has explicit per-engine writers; no `Console` fallthrough in embedded mode. |
| **`HttpClient` singleton** (`HttpBuiltIns.cs`) | Thread-safe but shares cookies / connection pool across engines. | Low severity — note; optionally per-engine client. |
| **`GlobalSlotAllocator`** (`Compilation/GlobalSlotAllocator.cs`) | Unsynchronized if shared across parallel compilers. | Only matters if compilation is parallelized — note. |
| **`Random.Shared`** (`MathBuiltIns.cs`) | ThreadLocal — safe across threads, but two VMs on one thread share. | Low — optional per-VM seed. |
| **`PromptBuiltIns` / `ProcessBuiltIns` static delegate slots** | Cross-VM bleed of REPL prompt/history state. | High for CLI **test isolation**, low for embedded; address if cheap. |

Orthogonal note: even with the VM isolated, `StashDictionary`/array/`StashInstance` instances are not
thread-safe. The Scope B freeze-or-clone model sidesteps this for shared globals; a value captured in
a closure that escapes to multiple threads by other means remains the author's responsibility.

## 5. Relationship to the old embedding doc (§13/§14)

**Reaffirmed (carry forward):**
- The §13 leak audit (§4 above is its distillation).
- Virtual cwd/env overlay and multiplexed signals (§14.3).
- Per-VM IC-slot clone on chunk load (§14.2).
- Full stdlib available; hosts opt out via `StashCapabilities` — no "embedded profile" presets (§14.4).

**Superseded / changed by the 2026-05-30 decisions:**
- **Snapshot model (§14.5) is REPLACED.** The old doc keyed sharing on `const` (frozen-shared) vs
  `let` (deep-clone). We rejected that: Stash's `const` is JS-style **binding-only** and does not
  freeze values, so it can't carry the contract. Sharing now keys off **runtime frozen-ness**
  (`readonly`/`freeze`), independent of `const`/`let`. `const` semantics stay unchanged.
- **Concurrency/pool model (§5, §14.1) is DEMOTED.** The VM pool is **not foundational** — it's an
  optional userland/`Stash.Hosting` (phase 3) concern. The core guarantee is "one engine is
  single-threaded; parallelism comes from multiple engines or from Stash's own async." Making engine
  construction cheap (share immutable stdlib definitions + `Chunk`s, clone only mutable bits) may make
  a pool unnecessary; that's a phase-3 question.
- **Async is NOT "unfinished."** The old doc called `await` infrastructure incomplete. It is fully
  built and genuinely parallel (`StashFuture` wraps `Task<object?>`; `await` blocks the VM thread).
  Phase 2's job is **isolating** the existing threaded async, not building async.

## 6. Open sub-decisions (NOT yet made — for the future architect)

- **Deep-clone cycle policy:** detect-and-fail (clone with a visited set, throw on cycle) for v1, or
  cycle-preserving clone. Lean detect-and-fail first.
- **Clone eagerness:** clone all non-frozen globals on every async spawn (simple, cost ∝ mutable
  state), copy-on-write, or only what the child captures (can't be known statically). Lean eager +
  `freeze` to amortize the immutable bulk.
- **IC-slot isolation shape:** per-VM `Array.Copy` clone on load vs moving IC slots into the VM
  (`vm._icSlotsByChunkId`). Clone is the smaller change; in-VM is the fallback if memory bites.
- **Engine-construction cost:** how cheap can a fresh engine be? Drives whether a pool is ever needed.
- **Spec sizing / split:** this is large. It may warrant splitting into e.g. **2a** (per-VM IC-slot
  isolation + async-child freeze-or-clone globals + `_importStack` isolation — the live-hazard fixes)
  and **2b** (process-global virtualization: cwd/env/signals/Console/etc.). The architect should size
  this when phase 1 lands.

## 7. Tentative phase sketch (a sketch, not a plan — the architect owns the real one)

1. **Per-VM IC-slot isolation** on shared `Chunk`s (clone on load). Closes the critical silent-
   corruption leak; benefits async correctness immediately.
2. **Async-child global isolation** — freeze-or-clone in `SpawnAsyncFunction` (depends on phase-1
   `IsFrozen`/`DeepFreeze`; reuse the cycle-safe walker) + `_importStack` isolation.
3. **Virtual cwd/env** — `VMContext.WorkingDirectory` + `EnvVars`; route all path/env stdlib through
   them; `process.spawn` inherits the VM view.
4. **Multiplexed signals** — registry + per-VM `signal.off`; SIGTERM runs every VM's cleanup.
5. **Residual leaks + isolation test harness** — Console defaults, HttpClient, prompt/process statics;
   a multi-engine isolation test suite (§8).

## 8. Verification ideas (seed for the eventual acceptance criteria)

- **Two-engine cwd isolation:** engine A `env.chdir("/tmp")`; engine B's `fs.realpath(".")` is
  unaffected.
- **Two-engine env isolation:** A `env.set("X","1")`; B's `env.get("X")` is null.
- **Async race stress:** spawn N async tasks mutating a non-frozen captured collection; assert no
  corruption and that mutations are call-local (parent's copy unchanged). Repeat with a `freeze`d
  collection asserting it's shared and immutable (writes throw).
- **IC-slot correctness under load:** run the same field-accessing chunk concurrently across two
  engines; assert no wrong-field reads.
- **SIGTERM multiplex:** two engines each register cleanup; SIGTERM runs both.
- Optional debug-mode **global-drift checksum** across an async call boundary.

## 9. Forward pointer

Phase 3 (`Stash.Hosting` host SDK) consumes a hermetic engine: `StashEngine` as a `lua_State`,
host-objects-by-reference via a reflection-free core interface + JIT-only reflection proxy (CLI
Native AOT preserved), marshalling hybrid, `InvokeAsync` bridging `StashFuture`, `IAsyncDisposable`,
typed `StashException` out / host exceptions catchable-in-Stash. The optional VM pool, if any, lives
here. See `Stash Embedding API — Host SDK Design Analysis.md` (§4 onward) for prior analysis, read
through the supersessions in §5 above.
