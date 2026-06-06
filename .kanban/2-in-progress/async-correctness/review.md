# async-correctness — Review (Pass 2 / Final)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `c2ce0830..ea4174c4` on branch `feature/async-correctness`
**Pass-2 diff range (the five post-pass-1 fix + chore commits):** `cae1dcc0..ea4174c4`
**Brief:** ../brief.md
**Generated:** 2026-06-06

---

## Summary

**Zero findings.** The pass-1 fixes (F01 through F05) are each scrutinized below and judged correct, complete, and regression-free; the locked async contract (D1–D5) remains end-to-end delivered.

**By severity:**

- CRITICAL: 0
- IMPORTANT: 0
- MINOR: 0

**Overall assessment.** The five pass-1 fixes are surgical and do not regress the contract.

- **F01 (`16db172a`)** removes only the two unreachable `<exception cref="StateError">` tags on `process.pid` (line 458) and `process.detach` (line 535-536) and regenerates the reference. Cross-checked: the other eleven StateError tags (lines 309/363/411/438/471/571/605/650/679/798/854) all guard functions that reach `ResolveTrackedProcess` at `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs:1054` — including `IsAlive` (line 445), the function whose name reads like a non-consumer but whose body does call `ResolveTrackedProcess` and so genuinely throws `StateError` on a cross-task handle. The pid/detach removal is complete and the kept tags are reachable.
- **F02+F03 (`b602d4d2`)** hoists the gate into `UnobservedFaultReporter.Report(registry, writer, bool embeddedMode = false)` (`Stash.Bytecode/UnobservedFaultReporter.cs:36`) and threads `vm.EmbeddedMode` from the single CLI call-site (`Stash.Cli/Program.cs:1296`). The optional default `embeddedMode = false` preserves every existing 2-arg caller (five test sites + the gotcha test) at the pre-fix "report fires" behaviour, so no incidental regression. The replacement `EmbeddedModeGateTests` directly exercises the gate (true → 0, false → fires); the F03 `Stash.Tests/Cli/UnobservedAsyncCliWiringTests.cs` spawns the real CLI binary as a subprocess for done_when #1 (exit 0 + warning) and done_when #7 (top-level error + warning + exit 70), reads stdout/stderr concurrently to avoid pipe-buffer deadlock, asserts primary-error-before-warning ordering, and needs no `[Collection]` (each spawns a fresh child process — no shared `Console` capture). Wait timeout is generous (30 s); the 300 ms `time.sleep(0.3)` between spawn and exit is comfortably above the microsecond-class scheduling latency for a synchronous `throw` in a Task.Run body, so the test is not racy in practice.
- **F04 (`3025b900`)** — the highest-risk change — flips `VMContext.SpawnedFutures` from `SpawnedFutureRegistry?` to non-nullable `SpawnedFutureRegistry` field-initialized to `new SpawnedFutureRegistry()` (`Stash.Bytecode/Runtime/VMContext.cs:440`), simplifies `RegisterFuture` from `?.Register` to `.Register` (line 723), drops the `if (SpawnedFutures != null)` conditional in `InvokeCallbackDirect`, and ships `SpawnedFuturePropagationMetaTests` (`Stash.Tests/Bytecode/`). The "default-fresh registry" carries the same end-state failure mode as the old nullable when a child site forgets to propagate (child's futures escape D1) — neither worse nor better than the prior null no-op — but the meta-test (1) confirms all five real propagation sites (`Async.cs:101`, `VMContext.cs:384` Fork, `VMContext.cs:688` InvokeCallbackDirect, `Modules.cs:114`, `VirtualMachine.cs:145` root-VM-seeding-its-context) classify as propagated; (2) confirms the two `PinnedExemptions` (`StashEngine.cs` engine-root, `Runtime/VMTemplateEvaluator.cs` same-thread template evaluator) are genuinely exempt (engine root has no parent registry to inherit from; the template evaluator's child VM runs a single flattened-scope expression synchronously and cannot spawn futures); (3) carries two fail-path self-tests (`Scanner_MissingPropagation_IsDetectedAsViolation`, `Scanner_OrphanRegistry_IsDetectedAsViolation`) with embedded-resource `.txt` fixtures (kept as `.txt` to dodge the `.cs` SDK glob and avoid an internal-access build break) proving teeth; (4) carries a `PinnedExemptions_MatchActualNonPropagatedConstructions` floor that forces a deliberate test edit when the exempt set drifts; and (5) sets `MinConstructionCount = 4` (actual count is 7) guarding against a vacuous-pass file-discovery regression. The brief §Cross-Cutting Concerns row 2 and Decision Log are reworded to honestly describe the delivered mechanism as "Detect (with teeth)" rather than the original Construct claim — wording change is accurate to the code. (Narrow residual: `EnclosingMethodContainsPropagationStatement` checks the *enclosing method* for any `.SpawnedFutures =` assignment, so a hypothetical second `new VirtualMachine` added inside `InvokeCallbackDirect` would inherit the existing statement's pass. Today each propagation site lives in its own method, so the gap is unexploited; flagging it would be padding rather than a finding.)
- **F05 (`ea4174c4`)** is doc-only: chore commit recording the resolved status of an out-of-scope deferral with the bug-template-shaped backlog stub `0-backlog/bugs/analyzer-event-namespace-unknown-sa0202.md` already filed; the review.md `**Resolution:**` line correctly distinguishes "out-of-scope deferral with required deliverable complete" from an `/accept` (which CRITICAL findings cannot use anyway).

**Core-contract sanity (the shared files moved beneath F01–F04).** D1's chokepoint — `StashFuture.MarkObserved()` / `Observed` — is intact (`Stash.Core/Runtime/Types/StashFuture.cs:22,29`); `IsAsyncChild`-discriminated cancellation propagation in `VirtualMachine.Dispatch.cs:92,114` is untouched by F04 (the non-nullable flip is a property-type change, not a dispatch change); the registry is correctly seeded at every real child-VM site under the new non-nullable contract; the `VirtualMachine.SpawnedFutures` property setter at `Stash.Bytecode/VM/VirtualMachine.cs:90-101` still re-syncs `_context.SpawnedFutures` on assignment, so even though the field-initializer runs first and stamps an orphan registry into both the VM and its context, the subsequent object-initializer `SpawnedFutures = capturedRegistry` / `SpawnedFutures = SpawnedFutures` propagates atomically into both. Build succeeds clean (`dotnet build Stash.Bytecode/Stash.Bytecode.csproj` → 0 warnings / 0 errors).

The contract is ready for `/done`.
