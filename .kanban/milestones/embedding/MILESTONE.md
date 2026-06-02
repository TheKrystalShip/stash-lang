# Milestone: Embed Stash in C# Hosts (Lua `lua_State` Model)

> **Status:** Active
> **Created:** 2026-06-01
> **Slug:** embedding

A living charter for making Stash embeddable: one hermetic `StashEngine` per host, in the
spirit of Lua's `lua_State`. The destination is fixed (below); the route is a fixed 3-phase
sequence whose later phases are specced only after the earlier ones ship. Run
`/milestone embedding` for the derived ledger.

---

## Charter (living — edit freely)

### Vision

Make Stash embeddable inside .NET applications as a scripting / config / policy language, on
the **Lua `lua_State` model**: a single `StashEngine` *is* the world. Constructing two engines
in one process must yield two universes that share **nothing observable** — distinct globals,
distinct cwd/env view, distinct signal handlers, no cross-engine state bleed.

This matters for two reasons. The **destination** is the host SDK (`Stash.Hosting`) that lets a
C# app spin up isolated Stash engines, pass host objects in, and call Stash functions out. But
the **road there fixes live correctness hazards that exist today regardless of embedding**:
process-global leaks (`env.set`/`env.chdir` mutate the real `System.Environment`, so engine 2
sees engine 1's cwd/env), an async-child global-sharing race, and silent inline-cache
corruption on `Chunk`s shared across concurrently-running VMs. The VM is ~80% isolated today;
this milestone closes the remaining 20%.

### Definition of Done (finite & checkable)

All three roadmap phases landed in `.kanban/4-done/` and tagged `milestone: embedding`:

1. **`readonly` modifier** — deep/transitive value immutability (the freeze primitive:
   `IsFrozen` surface + `DeepFreeze` walker).
2. **Hermetic VM** — engine↔engine and parent↔async-child isolation, proven by a **multi-engine
   isolation acceptance suite** (two-engine cwd isolation, two-engine env isolation, async-race
   stress with call-local mutation, IC-slot correctness under concurrent load, SIGTERM
   multiplex — design note §8).
3. **`Stash.Hosting` host SDK** — `StashEngine` facade, host-objects-by-reference, marshalling
   hybrid, `InvokeAsync` bridging `StashFuture`, `IAsyncDisposable`.

The program **converges** — it is the three phases above, not "make embedding better" forever.

### Unit Definition of Done

One roadmap phase is complete when its child `/spec` feature is in `4-done/` with the full
language-change checklist satisfied (`.claude/language-changes.md`): spec + generated stdlib
docs updated, the tooling-compatibility matrix (LSP / DAP / Playground / VS Code / Analysis)
**explicitly verified**, example script(s) under `examples/`, and a green `final_verify` (full
`dotnet test`). For the **hermetic** phase specifically, the isolation acceptance suite (DoD §2
above) must be green — isolation is the deliverable, so it must be *tested*, not asserted.

### Rough order & next up

A fixed dependency chain (unlike a rolling-wave milestone, the order here is a real contract —
each phase consumes the prior phase's artifacts). Detail the current phase; the rest is sketched.

- **Phase 1 — `readonly` modifier · DONE (2026-06-01, merged to `main` @ `822e6146`).** Shipped
  the freeze primitive (`IsFrozen` / `DeepFreeze`, cycle-safe traversal) that phase 2's
  async-child isolation reuses. In `.kanban/4-done/readonly-modifier/`. Derived ledger:
  `done:1, in-flight:0`.

- **Phase 2 — Hermetic VM · DONE (2026-06-02, in `4-done/` on `feature/hermetic-vm`; awaiting
  `worktree-finish` merge to `main`).** 12 phases (2A live-hazard fixes, 2B process-global
  virtualization, 2C multiplexed signals + no-Console-embedded, 2D acceptance suite), driven by the
  second `/autopilot` run. Delivered: per-VM IC-slot clone, async-child freeze-or-clone
  (`BuildChildGlobals`/`DeepClone`/`SnapshotUpvalues`), `_importStack` isolation, `VMContext`
  cwd/env overlay SoT (`NoProcessGlobalLeak` guard at 0), multiplexed signals, two omission guards,
  and the multi-engine isolation acceptance suite. **One design ruling** (user): background-thread
  callbacks (`fs.watch`/signal/timer) are isolated/call-local like async spawns — the principled
  marshaling-to-VM-thread fix is backlogged at
  `.kanban/0-backlog/language/callback-vm-thread-marshaling.md`. Review 1C/1H/1M/1L fixed across two
  passes (pass 2 clean). final_verify green (12954 passed / 0 failed). Derived ledger:
  `done:2, in-flight:0`.

- **Phase 3 — `Stash.Hosting` host SDK · NEXT (unblocked once phase 2 merges to `main`).** Consumes
  a hermetic engine: `StashEngine` facade, host-objects-by-reference, marshalling hybrid,
  `InvokeAsync` bridging `StashFuture`, `IAsyncDisposable`. **Do NOT `/spec` until `hermetic-vm`
  is merged to `main`** (it's promoted on the branch but not yet integrated — run
  `scripts/checkpoint/checkpoint.stash worktree-finish hermetic-vm` from main first). Prior analysis:
  `.kanban/0-backlog/tools/Stash Embedding API — Host SDK Design Analysis.md` — read through the
  supersessions in the phase-2 note §5 (snapshot model, pool demotion, async-is-built). **Strong
  candidate to fold in:** the callback-marshaling event-loop backlog item (the VM pool, if any,
  also lives here per phase-2 note §9).

### Decisions & learnings (append as you go)

| Date | Decision / learning | Why it changed the plan |
| --- | --- | --- |
| 2026-06-02 | **Phase 2 (hermetic VM) DONE** (promoted on `feature/hermetic-vm`, awaiting merge). **Design ruling:** background-thread callbacks (`fs.watch`/signal/timer, via `InvokeCallbackDirect`'s pool-thread branch) are **isolated/call-local** like async spawns — Stash has no event-loop to marshal them onto the VM thread, so "share freely" (JS/Lua model) isn't available without building one. The marshaling event-loop is the principled long-term fix, backlogged at `.kanban/0-backlog/language/callback-vm-thread-marshaling.md` — a strong phase-3 candidate. | The autopilot hit a question the plan didn't anticipate: isolating async-child state (2A-2/2A-3) silently changed callback semantics (callbacks can no longer mutate outer state). Resolved by ruling rather than shipping a half-isolated race. Also exposed (and fixed) a parallel-task data race in `BuildChildGlobals` and an upvalue-isolation gap on the callback path — both full-suite-only, invisible to filtered per-phase verifies. |
| 2026-06-01 | **Phase 1 (`readonly` modifier) DONE** — driven by the first real `/autopilot` run (6 impl phases + 8 review findings across two passes), merged `--no-ff` to `main` @ `822e6146` with green final_verify. Phase 2 (hermetic VM) is now **unblocked** and `/spec`-able. | Closes the strict-sequencing gate. Phase 2's async-child freeze-or-clone rule can now consume the concrete `IsFrozen`/`DeepFreeze` API instead of guessing against types that didn't exist. |
| 2026-06-01 | Milestone created. User chose **strict sequencing** (readonly → *full* hermetic → host SDK) over splitting the readonly-independent hermetic slice (IC-slot cloning + cwd/env/signal virtualization) out as an early standalone spec. | Keeps phase 2 a single coherent spec and honors the design note's original "wait for phase 1" guidance. Accepted trade-off: the engine↔engine spilling fix **and** the *Critical* IC-slot corruption fix wait behind `readonly`'s 6 phases. (If the async race or IC corruption starts biting before readonly lands, revisit — the Scope-A slice is genuinely readonly-independent and could be pulled forward.) |
| 2026-05-30 | Sharing keys off **runtime frozen-ness** (`readonly`/`freeze`), NOT `const`. `const` stays JS-style binding-only, unchanged. | Stash `const` doesn't freeze *values*, so it can't carry the share-vs-clone contract. Supersedes the old embedding doc's §14.5 `const`/`let` snapshot model. |
| 2026-05-30 | Async is **fully built** and genuinely threaded (`StashFuture` wraps `Task<object?>`; `await` blocks the VM thread). Phase 2 **isolates** it, does not build it. | Corrects the old embedding doc, which called the `await` infrastructure "unfinished." |
| 2026-05-30 | VM pool **demoted** from foundational to an optional phase-3 concern. | Core guarantee is "one engine is single-threaded; parallelism comes from multiple engines or Stash's own async." Cheap engine construction may make a pool unnecessary. |

### Open questions

Deferred to the phase-2 (hermetic) architect — from the design note §6:

- **Deep-clone cycle policy:** detect-and-fail (visited set, throw on cycle) for v1, or
  cycle-preserving clone? (Lean detect-and-fail.)
- **Clone eagerness:** clone all non-frozen globals every async spawn (simple), copy-on-write,
  or only what the child captures (not statically knowable)? (Lean eager + `freeze` to amortize.)
- **IC-slot isolation shape:** per-VM `Array.Copy` clone on chunk load vs moving IC slots into
  the VM (`vm._icSlotsByChunkId`)? (Clone is the smaller change.)
- **Engine-construction cost:** how cheap can a fresh engine be? Drives whether a pool is ever
  needed (phase 3).
- **Spec sizing:** does phase 2 split into 2a (live-hazard fixes) and 2b (process-global
  virtualization)?

---

## Ledger (DERIVED — do not edit by hand)

Completion is computed from feature dirs, not asserted here. Each child feature's `plan.yaml`
carries `milestone: embedding`; the status script groups them across all git worktrees:

```bash
stash scripts/checkpoint/checkpoint.stash milestone-status embedding
```

- **Done** = features in `.kanban/4-done/` tagged with this milestone.
- **In-flight** = features in `.kanban/2-in-progress/` tagged with this milestone.

Currently tagged: `readonly-modifier` (phase 1, **done** in `4-done/`). Phases 2 and 3 are not
yet specced — they appear here once their `/spec` runs and tags their `plan.yaml`. If anything
written elsewhere in this doc disagrees with the command above, the command wins.
