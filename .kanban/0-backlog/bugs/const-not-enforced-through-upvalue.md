# `const` reassignment is not blocked when the binding is captured as an upvalue

**Status:** Backlog — Bug
**Created:** 2026-06-01
**Discovery context:** Surfaced during the `readonly-modifier` design review (2026-06-01) while verifying Q4 — whether the compiler can re-freeze a `readonly let` rebound through a closure. Tracing the upvalue store path revealed `const` itself is not enforced there.

---

## Problem

Stash blocks reassignment of a `const` binding on the **local** and **global** storage paths, but **not** on the **upvalue** path. When an inner function captures an outer-scope local `const` and assigns to it, the assignment silently succeeds instead of raising. The const contract — "the binding cannot be reassigned" — is violated whenever the const is a captured (nested-local) binding.

The gap is specifically the upvalue store path: top-level `const` (which resolves as a *global*) is correctly guarded, which masks the bug in the most common usage and explains why it went unnoticed.

## Reproduction

```bash
# BUG: nested-local const captured as an upvalue — reassignment is NOT blocked
$ stash -c 'fn outer() { const c = 1; fn g() { c = 2; } g(); io.println(c); } outer();'
# Expected: RuntimeError (cannot assign to constant 'c')
# Actual:   2        <-- reassignment silently succeeded

# CONTRAST 1: top-level const resolves as a GLOBAL — correctly guarded
$ stash -c 'const c = 1; fn g() { c = 2; } g(); io.println(c);'
# RuntimeError: Cannot assign to constant 'c'.   <-- correct

# CONTRAST 2: a nested-local `let` upvalue rebind is legitimate and should succeed
$ stash -c 'fn outer() { let s = 1; fn g() { s = 2; } g(); io.println(s); } outer();'
# 2   <-- correct (let is rebindable)
```

## Blast radius

- **Live, but low-frequency.** Affects any script that declares a `const` inside a function body and mutates it from a nested closure. Idiomatic top-level `const` is unaffected (global path is guarded), so real exposure is modest — but it is a genuine correctness hole, not latent: it misfires today with no special build mode.
- **Becomes load-bearing under `readonly-modifier` (phase 1 of the embedding roadmap).** That feature's Q4 requires re-freezing a `readonly let` value on *every* rebind, including rebinds through an upvalue. The same unguarded upvalue store path is the escape hatch for both. The `readonly-modifier` P4 fix (thread a mutability bit into `UpvalueDescriptor` and guard the upvalue store) closes this const hole as a side effect.
- Does not compound over time on its own.

## Root cause

The upvalue store path carries no mutability metadata and performs no const check:

- `Stash.Bytecode/Runtime/UpvalueDescriptor.cs` — the descriptor is `{ byte Index; bool IsLocal; }`; it has no `IsConst` flag, so the const-ness of the captured binding is lost at capture time.
- `Stash.Bytecode/VM/VirtualMachine.Variables.cs` — `ExecuteSetUpval` writes `frame.Upvalues![idx].Value = …` unconditionally, with no const guard.
- `Stash.Bytecode/Compilation/Compiler.Helpers.cs` — when an assignment resolves to an upvalue, the compiler emits `OpCode.SetUpval` with **no** const check, unlike the local path which checks `CompilerScope.IsLocalConst` and emits a throw, and unlike the global path which is guarded at runtime.

So const is enforced on two of three storage classes; the upvalue class was missed.

## Suggested fix

- **(A) Compile-time guard on the upvalue assignment path.** Mirror the local-path precedent: when resolving an assignment to an upvalue, look up whether the captured binding was declared `const` and emit the same "Assignment to constant variable." throw the local path emits. Requires threading const-ness through `ResolveUpvalue` (it must reach back to the originating scope's `IsLocalConst`). Trade-off: keeps enforcement at compile time (cheapest at runtime) but the resolver must carry declaration metadata across capture frames.
- **(B) Runtime guard via an `IsConst`/mutability bit on `UpvalueDescriptor`.** Add the flag at capture, check it in `ExecuteSetUpval`. Trade-off: a per-store branch on the upvalue hot path, but it is the same plumbing `readonly-modifier` P4 needs anyway (a readonly/mutability bit on the descriptor + a guarded store), so doing it once serves both.

Recommend **(B)**, coordinated with `readonly-modifier` P4 — the descriptor-level mutability bit is required there regardless, and a single guarded upvalue-store path then enforces both `const` (cannot rebind) and `readonly let` (rebind re-freezes). Filing separately so the const fix is tracked as a correctness bug in its own right even if the feature slips.

## Verification

```bash
# Regression test: must FAIL today (prints 2), PASS after the fix (raises).
dotnet test --filter "FullyQualifiedName~ConstUpvalueReassignment"
```

A new test asserting that `fn outer() { const c = 1; fn g() { c = 2; } g(); }` raises a const-assignment `RuntimeError`. Cross-cutting checks that must keep passing: existing `let`-upvalue rebinds still succeed (CONTRAST 2 above), and top-level `const` reassignment still raises (CONTRAST 1).

## Related

- `readonly-modifier` (`.kanban/2-in-progress/readonly-modifier/`) — Q4 / P4; the descriptor mutability-bit fix closes this hole. Brief Decision Log entry 2026-06-01.
- Phase 1 of the embedding roadmap (`reference: project_embedding_roadmap`).
- Same surface: `UpvalueDescriptor.cs`, `VirtualMachine.Variables.cs` (`ExecuteSetUpval`), `Compiler.Helpers.cs`.
