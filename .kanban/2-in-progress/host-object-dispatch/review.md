# host-object-dispatch — Review (pass 2)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `116f12066ed3d2dbb4d42dac8143e60286f922b6..5cf4ff5f39619eec956d92aa107b0a4068b1f5f7` on branch `feature/host-object-dispatch`
**Brief:** ../brief.md
**Generated:** 2026-06-05
**Pass:** 2 (re-review after F01/F02/F03 fixes — weighted toward fix-commit regression risk)

---

## Summary — Clean bill

**Severity counts:** 0 CRITICAL · 0 IMPORTANT · 0 MINOR

All three pass-1 findings are resolved with no observable regressions. The structural
restructuring in F03 — the riskiest change — preserves every error class with byte-identical
messages, and the F01 nested-`observedTargets` forwarding does not over-observe non-host
values. The benchmark project still compiles, the F02 re-run produced internally consistent
numbers (1.30 / 1.33 / 1.45 μs medians; baseline ≤ property-read < method-call as expected),
and the multimodality warning is acknowledged in the interpretation.

### F01 — fixed (c592018e) — confirmed correct, no regression

- `HostMarshaller.cs` lines 100/110/120 now forward `observedTargets` into the three
  recursive `ToStash` branches (dict / anon / enumerable). The forward is benign for
  non-host values: `observedTargets?.TryAdd` only fires inside the `HostHandle` constructor
  (`HostHandle.cs:70`), which is reached only by the registered-host-type branch
  (`HostMarshaller.cs:88-93`). Primitives, raw dicts, anonymous projections, and
  enumerables of non-host values never construct a `HostHandle`, so no over-observation
  occurs.
- Double-observation is impossible: `ConditionalWeakTable<,>.TryAdd` is idempotent by key
  reference. The same target observed via SetGlobal + nested-dict-return + multiple direct
  returns still adds exactly one entry. The `OnRelease_NotFiredTwice_WhenSameInstanceObservedMultipleTimes`
  test (HostObjectLifetimeTests:108) re-asserts this through `r.self()` → same instance →
  one release.
- Async path is also fixed-by-symmetry: `InvokeHostDelegate.InvokeAsyncMethod`'s bridge-task
  continuation at line 288 calls `HostMarshaller.ToStash(rawResult, allRegistrations,
  observedTargets)` — so a nested host instance returned from an async handler is now
  observed via the same forwarding.
- New regression test `OnRelease_FiresForHostInstance_ReturnedNestedInDict`
  (HostObjectLifetimeTests:239) covers the exact case described in F01 (inner Resource
  has no SetGlobal binding; its only path into observation is the nested dict). Passes.
- "Never-for-unobserved" guarantee preserved: `OnRelease_NotFiredForUnobservedInstances`
  (line 80) still passes.

### F03 — fixed (982383dc) — sync restructure preserves all three error classes

The riskiest change. Traced byte-by-byte:

- **Sync `DynamicInvoke` is now structurally single-sited.** `grep DynamicInvoke
  Stash.Hosting/` finds exactly two source call sites: `InvokeHostDelegate.cs:166` (sync
  chokepoint) and `HostTypeBuilder.cs:342` (inside the async closure — acceptable per the
  brief's softened wording, "convention-enforced for async/getter/setter"). The
  `HostMethodInvoker` closure on `HostTypeBuilder.cs:189-214` now marshals args and
  returns `(clrArgs, handler)` without invoking; `InvokeHostDelegate.InvokeMethod`
  (line 159 + 166) is the sole sync invoke site.
- **Arity-mismatch message preserved.** `HostBoundMethod.CallDirect` still throws the
  arity-mismatch `HostError("<TypeName>.<name> expects N argument(s), got M")` BEFORE
  the closure runs. Verified by `Method_ArityMismatch_TooManyArgs_ThrowsHostError_ExactMessage`
  and `Method_ArityMismatch_TooFewArgs_ThrowsHostError_ExactMessage`
  (HostObjectMethodTests:120, 140) — both green.
- **Per-arg marshalling message preserved.** Closure still throws
  `HostError("arg {i+1} to {typeName}.{memberName}: {ex.Message}")` from inside the
  marshalling loop (HostTypeBuilder.cs:202-206). This throw happens INSIDE the closure
  call at `InvokeHostDelegate.cs:159`, BEFORE the new `DynamicInvoke` try/catch — the
  `RuntimeError` propagates up through the closure unchanged. Verified by
  `Method_ArgTypeMismatch_ThrowsHostError_PerArgMessage` (line 162) — green;
  assertion `Assert.StartsWith("arg 1 to Player.attack:", ex.Error.Message)`.
- **CLR-exception-in-handler message preserved via `TargetInvocationException` unwrap.**
  `InvokeHostDelegate.cs:174-181` unwraps `TargetInvocationException` at the new
  `DynamicInvoke` site and re-throws as `HostError(inner.Message, span)`. This is the
  critical detail: `Delegate.DynamicInvoke` always wraps handler exceptions in TIE, and
  the unwrap lives at the new structural site, not orphaned at the old location.
  Verified by `Method_ThrowingCLR_SurfacesHostErrorViaCallAsync` (line 184) and
  `Method_ThrowingCLR_SurfacesHostErrorViaTryCallAsync` (line 202) — both green; both
  assert `Assert.Contains("clr-boom", ex.Error.Message)` where `Bad()` throws
  `InvalidOperationException("clr-boom")`. The inner message survives the unwrap.
- **`RuntimeError` from handler body still propagates structured.** `catch (RuntimeError)
  { throw; }` at line 168 ensures a HostError or ReadOnlyError thrown by the handler
  body keeps its shape.
- **Return-value marshalling deliberately outside the try/catch.** The unregistered-return-type
  contract still holds — `Method_ReturnValue_UnregisteredType_SurfacesMarshallerError`
  (line 220) asserts the marshaller's `ArgumentException` surfaces as a generic
  RuntimeError, NOT HostError. Line 187-197 comment on InvokeHostDelegate makes the
  intent explicit and the structural placement of `ToStash` outside the try/catch
  preserves it.
- **Async path genuinely unchanged.** Diff confirms `HostAsyncMethodInvoker` body is
  identical to pre-F03 (only the `<remarks>` block was added to its delegate-type
  doc-comment). The closure still performs DynamicInvoke + await + CT-bridge internally
  at HostTypeBuilder.cs:342. All 14 async tests still pass.
- **Brief wording is now accurate.** The cross-cutting concern row and the ADR entry
  honestly state "Structural for sync method dispatch ... Convention-enforced for async
  method, getter, and setter dispatch" — matching the codebase exactly. No mixed
  sync-structural/async-convention claim is now misrepresented.

### F02 — fixed (acf41c1a + a4ef0888) — benchmark coherent

- `[ShortRunJob]` → `[SimpleJob(warmupCount: 10, iterationCount: 30)]` applied to
  `HostMemberAccessBenchmarks` only (HostConstructionBenchmarks retains ShortRun, with
  doc rationale).
- `dotnet build Stash.Hosting.Benchmarks` succeeds (0 warnings, 0 errors).
- New medians (1.304 / 1.331 / 1.452 μs) match expected work profile: baseline ≤
  property-read < method-call. No more "15 μs property-read" anomaly, no more
  "low-variance and jitter" self-contradiction.
- BDN multimodality warning on `WarmPropertyRead` (mValue=2.86, 4 outliers) acknowledged
  in the interpretation per F02's own statistical-honesty standard — a4ef0888 explicitly
  added "BDN flagged a mild multimodal distribution for `WarmPropertyRead` (mValue =
  2.86, 4 outliers removed); the median and 0.061 μs StdDev keep it well within baseline
  noise."
- No `BenchmarkDotNet.Artifacts/` paths committed (`git ls-files | grep BenchmarkDotNet.Artifacts`
  is empty).
- The Notes section (line 67-70) accurately reflects the mixed regime: `HostMemberAccessBenchmarks`
  uses SimpleJob 10+30, `HostConstructionBenchmarks` retains ShortRun for its coarser
  verdict.
- Numeric arithmetic in the table is internally consistent: 1.452 / 1.304 ≈ 1.114 → Ratio
  reported as 1.12× (rounded); 1.331 / 1.304 ≈ 1.021 → Ratio reported as 1.01× (rounded).
  Both reasonable.

### Still-applies invariants re-confirmed

- **Zero `Stash.Bytecode/` touch.** `git diff --stat 116f1206..5cf4ff5f -- 'Stash.Bytecode/**' 'Stash.Core/**'`
  reports only `Stash.Core/Runtime/Errors/HostError.cs | 10 ++++++++++` — the single sanctioned core touch.
- **No-magic-strings.** `grep "\"HostError\"" Stash.Hosting/` finds only the `KindHostError`
  const definition (StashError.cs:51) plus XML `<see cref>` doc comments — no bare load-bearing
  string literal. The F03 restructure moved code but introduced none.
- **Sync `DynamicInvoke` chokepoint structural.** Confirmed above — sole source call site
  at `InvokeHostDelegate.cs:166`.
- **F01 nested-collection forward.** Confirmed for both sync (line 197) and async
  (line 288) marshaller call sites.

### Test results

- `dotnet test --filter "FullyQualifiedName~Stash.Tests.Embedding"`: 138/138 passed
  (matches reviewer baseline 138 Embedding tests).
- `dotnet test --filter "FullyQualifiedName~Stash.Tests.Embedding.HostObjectMethodTests"`:
  14/14 passed — sync error-class messages verified preserved.
- `dotnet test --filter "FullyQualifiedName~Stash.Tests.Embedding.HostObject"`:
  57/57 passed — full host-object surface.
- Pass-2 baseline: 13600/13606 passed (0 failed, 6 honest skips). Matches expected green.

---
