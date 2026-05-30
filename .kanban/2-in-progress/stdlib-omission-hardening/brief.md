# RFC: Stdlib Omission Hardening

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-05-31
> **Slug:** stdlib-omission-hardening
> **Milestone:** omission-hardening

## Summary

Audit `Stash.Stdlib` (and its companion source generator `Stash.Stdlib.Generators`) end-to-end against the **Construct > Detect > Instruct** doctrine: classify every cross-cutting concern, then resolve each one — either promote it to a stronger level, or record an explicit written justification for keeping it where it is.

The audit itself is the first deliverable. Subsequent phases implement the small, high-confidence promotions the audit identifies, leaving the rest classified with justification.

## Motivation

`Stash.Stdlib` is branded "the single source of truth for all namespaces, functions, constants, members, structs, and enums." Its surface is consumed by the runtime, the analyzer, the LSP, the docs generator, and the playground. If a concern in that surface is silently *Instruct* — "every contributor must remember to add metadata X" — a future namespace can ship missing the participation and nothing fails until a user trips over a hover with no return type or a stability variant that silently behaves as `Cached`.

This unit is the first child of the long-term `omission-hardening` milestone (see `.kanban/milestones/omission-hardening/MILESTONE.md`). The scope-sizer hypothesis — that built-in *registration* is already Construct via the `[StashNamespace]` source generator — is **confirmed** (see Decision Log), so the surface needing real resolution work is small.

## Goals

- Produce, in phase 1, an **exhaustive classified table** of every cross-cutting concern in `Stash.Stdlib` + `Stash.Stdlib.Generators` (both the already-Construct mechanisms and the gaps), each reproduced against the code.
- Promote the high-value Detect/Instruct concerns to Construct where the type system / generator can express the invariant — specifically: the silent-skip edges of the source generator, the non-exhaustive `Stability` handling, and the silent absence of a DataMembers consistency test.
- For each concern the audit recommends *not* promoting (Roslyn throw-body scanning, snapshot/reference tests, possibly the type-label vocabulary), record an explicit written justification in `Cross-Cutting Concerns` rather than churning the architecture.

## Non-Goals

- This unit does NOT rewrite the source generator, change the `[StashNamespace]` / `[StashFn]` / `[StashMember]` surface, or alter wire-format / runtime semantics for built-ins.
- This unit does NOT touch sibling subsystems unless a stdlib concern provably crosses the boundary. In particular it does not duplicate the registry-authz pass (`Stash.Registry`, in-flight elsewhere) and does not pre-empt the planned future `Stash.Bytecode` opcode-coverage pass.
- This unit does NOT introduce new stdlib functions, members, or namespaces; metadata-checklist tests (`Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`, `StandardLibraryReferenceTests`) ship unchanged and stay green.
- This unit does NOT spec the future "make `BuiltInParam.Type`/`NamespaceMember.ReturnType`/`BuiltInField.Type` constructor-required" change as a flagship promotion — the source generator is the sole producer and never emits a null Type today (verified), so the change is defense-in-depth, not gap-closure. We note it in the audit and skip it.

## Design

The intended end state is: every cross-cutting concern in `Stash.Stdlib` is named in a single classified table living in this brief, has a single source of truth, and either fails closed at compile/runtime today or has an explicit written justification for relying on a meta-test (Detect) or convention (Instruct).

### Surface

No user-facing surface changes. Author-facing surface changes are limited to:

- New generator diagnostic `STSG014` (proposed id): `[StashFn]` / `[StashMember]` / `[StashConst]` declared **outside** a `[StashNamespace]`-attributed partial static class. Build error.
- New consistency test class extending `Stash.Tests/Stdlib/StdlibConsistencyTests.cs` to cover DataMembers (`[StashMember]`) — same shape as the existing Functions / Constants / Namespaces pairs.
- Exhaustive `switch` on `Stability` at both interpretation sites, with a `_ => throw new InvalidOperationException(...)` default. A future enum variant becomes a compile-or-launch failure instead of silently behaving as `Cached` at runtime and `Live` at emit.

### Semantics

For each promoted concern, omission moves from "silent" to "fails closed":

- A method annotated `[StashFn]` outside a `[StashNamespace]` class **fails the build** with `STSG014` (today: silently unregistered).
- A `[StashMember]` payload registered in a namespace but not exposed by the runtime, or vice versa, **fails an xUnit assertion** at test time (today: silently inconsistent for the DataMembers kind only).
- A future `Stability` variant added to the enum **fails closed at first invocation / first emit** (today: silently runs the `Cached` path at runtime, the `Live` path at emit).

For each concern the audit leaves un-promoted, the **Cross-Cutting Concerns** table below records the rationale.

### Implementation Path

Stdlib metadata authoring (`[StashFn]`/`[StashMember]`/`[StashConst]`/`[StashNamespace]` on a partial static class)
-> source generator (`Stash.Stdlib.Generators`) discovers, validates, and emits both per-namespace `Define()` and the `GeneratedStdlibRegistry`
-> `StdlibDefinitions.CreateVMGlobals()` aggregates the registry into runtime bindings
-> runtime `StashNamespace` + `NamespaceMemberPayload` dispatch the slots
-> analyzer / LSP / docs read `StdlibRegistry` metadata
-> `Stash.Tests/Stdlib/StdlibConsistencyTests` is the Detect floor.

Every promotion in this unit hardens an edge of that path *without* changing the path itself. No file outside `Stash.Stdlib/**`, `Stash.Stdlib.Generators/**`, or `Stash.Tests/Stdlib/**` needs to move.

### Cross-Cutting Concerns

This is the audit's first-pass classification. Phase 1 reproduces every row against the code; the table may be revised in phase 1's commit (mark revisions in the Decision Log).

| Concern | Single source of truth | Today's level | Verified status | Resolution |
| --- | --- | --- | --- | --- |
| Every `[StashNamespace]` is registered into `GeneratedStdlibRegistry.All()` | `Stash.Stdlib.Generators/StashNamespaceGenerator.cs` (`ForAttributeWithMetadataName`) + `CodeEmitter.EmitRegistry` | **Construct** | Verified — adding a `[StashNamespace]` class is auto-discovered; no hand-list. | Keep. Audit cites it as the milestone's reference example. |
| Capability gating per function/member | Emitted `if ((__caps & X) == X)` guard in `CodeEmitter.EmitFunction`/`EmitMember` | **Construct** | Verified — emitter unconditionally wraps when a `Capability` is set; tested by `StdlibConsistencyTests.Construction_WithNoCapabilities_*`. | Keep. |
| Duplicate Stash-name dedup within a namespace | `seenFnNames` HashSet in `StashNamespaceGenerator.Build` -> `STASH_GEN005` | **Construct** | Verified — emits build error. | Keep. |
| `[StashMember]` <-> `[StashFn]` / `[StashConst]` mutual exclusion | `STASH_MEM001` / `STASH_MEM002` build errors | **Construct** | Verified. | Keep. |
| `<summary>` doc required for `[StashFn]` / `[StashMember]` | `STASH_DOC001` (warning) / `STASH_MEM004` (error) | **Construct (member) / Detect-ish (fn)** | Verified — `STASH_DOC001` is `Warning`; `STASH_MEM004` is `Error`. | Keep as-is. The asymmetry is intentional (fn is the older surface); record but do not change in this unit. |
| `<exception>` / `[StashFn(ThrowsTypes=...)]` coverage on throwing builtins | `Stash.Tests/Stdlib/Wave1ThrowsCoverageTests` + allow-list + fail-path self-test | **Detect (justified)** | Verified — listed in `.claude/language-changes.md` enforcement gate. | Keep Detect. Justification: a build-time Roslyn throw-body walk would be heavy and fragile (heuristic over implementation code); the meta-test asserts the load-bearing property and ships a fail-path self-test and an explicit allow-list pinning legitimate infallible reads. Findings file explicitly flags this to push back on. |
| Public completion surface stability | `Stash.Tests/Stdlib/CompletionSurfaceSnapshotTests` | **Detect (justified)** | Verified. | Keep Detect. Justification: the deliberate "conscious re-baseline" property (re-record snapshot via `STASH_SNAPSHOT_REGEN=1`) is what makes the test valuable — replacing it with a computed assertion erases the intentional human review step. |
| Generated reference docs match `StdlibDefinitions` | `StandardLibraryReferenceTests` + `Stash.Docs/` regenerator | **Detect (justified)** | Verified. | Keep Detect. Same rationale as snapshots. |
| **GAP A — Silent-skip: `[StashFn]`/`[StashMember]`/`[StashConst]` outside any `[StashNamespace]` class** | Source generator predicate (currently filters by `[StashNamespace]` on the *class*; members on a non-namespace class are silently invisible) | **Instruct** today | Verified — generator's `ForAttributeWithMetadataName(NamespaceAttr)` walks only namespace classes; no companion scan for stray fn/member/const attributes elsewhere. | **Promote to Construct.** Add generator diagnostic `STSG014`: a `[StashFn]` / `[StashMember]` / `[StashConst]` whose declaring type is not `[StashNamespace]`-attributed is a build error. Resolved in phase **2**. |
| **GAP B — Non-exhaustive `Stability` handling** | `Stash.Stdlib.Abstractions/Stability.cs` (enum with two variants today) | **Instruct** today | Verified — TWO interpretation sites: `Stash.Stdlib/Models/NamespaceMemberPayload.cs:47` (`if (Stability == Stability.Live) ... else Cached path`) and `Stash.Stdlib.Generators/StashNamespaceGenerator.cs:461-464` (`stab == 0 ? Cached : Live`). A future variant misbehaves silently in both. | **Promote to Construct.** Convert both sites to exhaustive `switch` on `Stability` with a `_ => throw new InvalidOperationException($"unhandled Stability: {stability}")` default. Resolved in phase **3**. |
| **GAP C — DataMembers (`[StashMember]`) lack runtime<->registry consistency tests** | `Stash.Tests/Stdlib/StdlibConsistencyTests.cs` covers Functions / Constants / Namespaces, NOT DataMembers | **Instruct** today | Verified — read the file end-to-end (264 lines): exactly twelve `[Fact]`s, none enumerate `NamespaceMember` slots in either direction. | **Promote to Detect (with explicit pinning).** Add two new facts: `NamespaceMembers_RegistryEntries_HaveRuntimePayload` and `NamespaceMembers_RuntimePayloads_HaveRegistryMetadata` — same shape as the existing Functions/Constants pairs. (A pure Construct promotion via the generator is impractical here: the runtime side is dispatched through `NamespaceMemberPayload` boxed in a slot, not through a compile-time list the generator can enumerate.) Resolved in phase **4**. |
| **GAP D — UFCS type->namespace map not validated against runtime namespaces** | `Stash.Stdlib/Registry/StdlibRegistry.cs:135` — hard-coded `{string->str, array->arr, ...}` dictionary | **Instruct** today | Verified — initialized in static ctor; nothing asserts the target namespaces exist. | **Promote to Construct.** In the same static ctor, validate every value resolves via `NamespaceNames`. A missing namespace throws a `TypeInitializationException` at first load (fail-closed at process start, before any UFCS dispatch). Resolved in phase **3** (small, sits with the Stability fix). |
| **OBSERVATION — Nullable metadata fields (`BuiltInParam.Type`, `NamespaceMember.ReturnType`, `BuiltInField.Type`)** | `Stash.Stdlib/Models/BuiltInParam.cs:4`, `NamespaceMember.cs:8`, `BuiltInField.cs:4` (all `string?`) | **Construct already (via emitter)** | Verified — `CodeEmitter.EmitFunction`/`EmitMember`/`EmitStruct` always emit a non-null type label; no hand-written `NamespaceBuilder` caller exists outside the generator. The `string?` is misleading but harmless. | **No-op.** Flipping to `string` is defense-in-depth, not gap-closure. Recorded in the Decision Log; not implemented. |
| **DEFERRED — Bounded-domain literal scan (stdlib domain)** | Possible: namespace names, type-kind labels, stability labels appearing as inline literals | **Instruct** today | Partially verified — type-label strings are duplicated across `CodeEmitter.ResolveCSharpTypeForLocal`, `StashNamespaceGenerator.InferStashTypeLabel`, and the runtime marshaller. | **Keep Detect, document.** `Stash.Stdlib.Generators` targets `netstandard2.0` and cannot reference runtime enums, so a shared enum across the boundary is impractical. Phase **1's audit** records this and adds a follow-up Detect meta-test only if the audit confirms the type-label set is closed and the duplication has bitten in practice. If neither, leave un-promoted with this justification. |
| **DEFERRED — "Builtin-shaped method missing `[StashFn]`" heuristic diagnostic** | (none — proposal only) | n/a | Not pursued. | **Drop.** Findings file already hedges this as fuzzy / likely noise; a noisy build diagnostic is worse than none. Recorded in Decision Log; not implemented. |

This table is the audit. Phase 1's job is to commit it verified, with any reproducibility deltas folded back into the rows.

## Acceptance Criteria

- The classified table above is committed verified — each row's "today's level" reproducibly matches the code, and each row marked "Resolved in phase N" has a phase that lands the resolution.
- A method annotated `[StashFn]` (or `[StashMember]` / `[StashConst]`) on a class **not** marked `[StashNamespace]` produces build error `STSG014` (proven by an analyzer test that compiles a fixture and asserts the diagnostic id is reported). Today: silently unregistered, no diagnostic.
- A future `Stability` enum variant added (proven by a unit test that constructs `(Stability)99` and invokes both `NamespaceMemberPayload.Invoke` and the generator's stability mapper) throws an `InvalidOperationException` rather than silently selecting the `Cached` or `Live` branch.
- `Stash.Tests/Stdlib/StdlibConsistencyTests` contains two new `[Fact]`s covering DataMember registry-<->-runtime consistency. Disconnecting a registered `[StashMember]` from its runtime payload causes one of them to fail (proven by a fixture or by inspection of the assertion shape against the existing Functions pair).
- `StdlibRegistry`'s UFCS map static ctor throws on the first attempt to load if any UFCS target namespace doesn't exist (proven by a unit test that adds a fixture map entry pointing at a bogus namespace and asserts the ctor throws).
- All language/stdlib checklist gates remain green: `Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`, `StandardLibraryReferenceTests`, plus the standard `dotnet build` and `dotnet test` (filtered for documented flakies).

## Phases

The phase list lives in `plan.yaml`. Summary:

- **Phase 1 — Verify & classify.** Reproduce every row of the Cross-Cutting Concerns table; commit the verified table as the source-of-truth audit deliverable.
- **Phase 2 — Construct: `STSG014` (silent-skip edges).** New generator diagnostic for stray `[StashFn]`/`[StashMember]`/`[StashConst]` outside `[StashNamespace]`. Analyzer test proves the diagnostic fires.
- **Phase 3 — Construct: exhaustive `Stability` + UFCS map validation.** Both `Stability` interpretation sites become exhaustive switches with throwing default; `StdlibRegistry` static ctor validates the UFCS map's targets against `NamespaceNames`.
- **Phase 4 — Detect: DataMembers consistency tests.** New `[Fact]`s in `StdlibConsistencyTests` covering `[StashMember]`-registered slots.

## Open Questions

- Is there appetite, in a *future* unit (not here), to remove the `string?` on `BuiltInParam.Type` / `NamespaceMember.ReturnType` / `BuiltInField.Type` as a hygiene pass? Recorded here so it isn't lost.
- Is `STSG014` the right id, or should it be `STASH_GENXXX`-shaped to match older diagnostics? `STSG010`/`STSG011`/`STSG013` use the shorter form. Phase 2 picks the next id in the established sequence (currently `STSG014`).

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-31 | Scope-sizer hypothesis **confirmed**: registration is already Construct via source generator. | `ForAttributeWithMetadataName(NamespaceAttr)` discovers every `[StashNamespace]`, `EmitRegistry` always yields the registry from the collected models, no hand-list. |
| 2026-05-31 | Skip flipping `BuiltInParam.Type` / `NamespaceMember.ReturnType` / `BuiltInField.Type` to non-null. | `CodeEmitter` is the sole producer (no hand-written `NamespaceBuilder` calls outside the generator) and always emits a non-null Type. The change would be defense-in-depth, not gap-closure. |
| 2026-05-31 | Drop the "builtin-shaped method missing `[StashFn]`" heuristic diagnostic proposal. | Fuzzy by construction; a noisy build diagnostic is worse than none. Findings file pre-hedged it. |
| 2026-05-31 | Keep `Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`, `StandardLibraryReferenceTests` at **Detect**, with justification. | A Roslyn body walk is heavy/fragile; snapshot/reference tests' conscious re-baseline is *the* property that makes them valuable. Findings file explicitly flags these to push back on. |
| 2026-05-31 | Treat `Stability` as **one concern with two participants** (runtime payload + generator mapper), not two. | Single source-of-truth enum; resolving one site and leaving the other is exactly the omission shape this milestone exists to prevent. |
| 2026-05-31 | Keep stdlib type-label vocabulary (`"int"`/`"float"`/`"array"`/...) at **Detect/Instruct**, do not attempt a shared enum. | `Stash.Stdlib.Generators` targets `netstandard2.0` and cannot reference the runtime's types; a shared enum across the boundary is impractical. A Detect meta-test asserting the generator's label set agrees with the runtime's is a possible follow-up but not in this unit. |
