# exports-private-default — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable as `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx` reads the selected section verbatim and dispatches a Resolver.

**Scope reviewed:** commits `e5cda757..3bb35b0a` (5 phase commits) on branch `main`
**Brief:** ./brief.md
**Generated:** 2026-05-19

The implementation faithfully mirrors the brief: `ModuleExports.HasExplicitExports` is gone,
the runtime filter retains only the narrow `chunk.Exports == null` guard for v3 chunks, the
analyzer/LSP guards are gone, the serialized payload no longer carries the byte, and the new
E2E tests (`E2E_ZeroAnnotationModule_ImportFails`, `E2E_NamespaceImport_ExposesOnlyExportedNames`,
`Vm_EmptyExportBlock_AllImportsFail`, `Vm_ImportFromLegacyModule_StillSeesEverything`) pin the
required semantics. Spec and CHANGELOG describe the single rule. Feature-relevant test surface
is 579 pass / 0 fail.

Findings below are all stale-comment / plan-divergence items, no semantic defects.

---

## F01 — [IMPORTANT] final_verify runs unfiltered `dotnet test` — will refuse promotion

**Status:** fixed
**Fixed in:** 2bd4ca2
**Files:** `.kanban/2-in-progress/exports-private-default/plan.yaml:final_verify`
**Phase:** cross-phase / promotion
**Commit:** -

### Observation

`plan.yaml`'s `final_verify` block is:

```yaml
final_verify:
  - dotnet build
  - dotnet test
```

The implementer documented (phase 1D notes in `checkpoint.yaml`) that an unfiltered
`dotnet test` fails on pre-existing environment-flaky test classes unrelated to this feature
(network DNS, fswatch race conditions, shell E2E, `DiffPackageTests`, package installer).
Accordingly, the phase 1D `verify:` block was narrowed to a positive filter and
`checkpoint.yaml` records the divergence. The phase 1D verify and feature-scoped tests pass
clean (579 / 0 / 0 with the filter that I used).

`final_verify` was **not** updated to mirror that narrowing. When `/done exports-private-default`
runs, `promote-done.sh` will execute the unfiltered `dotnet test` and almost certainly refuse
to promote the feature for reasons that have nothing to do with the private-by-default flip.

### Why this matters

`/done` is the last step of the workflow. If it refuses for pre-existing flakies, the user
either has to add a filter under time pressure or override the gate — neither outcome is what
the workflow intends. This is exactly the situation phase 1D's verify narrowing was supposed
to anticipate, but it was only applied at the phase level.

### Suggested fix

Pick one (user's call):

- **(a) Mirror the phase 1D filter into `final_verify`** so promotion uses the same
  feature-relevant subset that all five phases verified against. Add a comment in
  `plan.yaml` explaining why the filter is in place.
- **(b) Keep `final_verify` unfiltered**, accept that promotion will need a `--force`-style
  override, and document the known-flaky list in `.claude/repo.md` as a baseline.

Option (a) matches what every phase actually ran and is the lower-friction choice.

### Verify

```
python3 scripts/checkpoint/promote-done.sh exports-private-default --dry-run
```

(or whatever dry-run / preview switch `promote-done.sh` supports). Confirm the unfiltered
`dotnet test` is the only command that fails and that the filtered variant is green.

---

## F02 — [MINOR] Stale `HasExplicitExports` mention in `ExportBlockStmt` doc comment

**Status:** fixed
**Fixed in:** 8911a54
**Files:** `Stash.Core/Parsing/AST/ExportBlockStmt.cs:13`
**Phase:** 1A (carried from parent feature)
**Commit:** -

### Observation

The XML doc on `ExportBlockStmt` still contains:

```csharp
/// export { };   // valid — HasExplicitExports becomes true with zero names
```

`HasExplicitExports` was removed in phase 1A. The example is now factually wrong — there is
no `HasExplicitExports` property to "become true." `export { }` is still valid; the new
correct phrasing is just that `Names` ends up empty (indistinguishable from no annotation).

### Why this matters

The brief's acceptance criteria say `ModuleExports` no longer has `HasExplicitExports` and the
codebase compiles with `Names` as the only data field. A doc comment referencing the removed
property breaks that single-source-of-truth invariant — future readers (and IDE hover) will be
told a property exists that doesn't.

### Suggested fix

Update the line to:

```csharp
/// export { };   // valid — Names ends up empty, same as a module with no export annotations
```

### Verify

```
grep -rn HasExplicitExports --include='*.cs' --include='*.md'
```

Should return zero hits outside `.kanban/4-done/`.

---

## F03 — [MINOR] Stale test method name `ModuleInfo_Exports_NotNull_WhenModuleHasExplicitExports`

**Status:** fixed
**Fixed in:** 8911a54
**Files:** `Stash.Tests/Analysis/ImportResolverExportTests.cs:377`
**Phase:** 1D
**Commit:** d3aa652

### Observation

```csharp
public void ModuleInfo_Exports_NotNull_WhenModuleHasExplicitExports()
```

The method name references the removed property. Behaviour pinned by the test is still
correct (asserts that a module with `export fn …` produces non-null `Exports`), but the
naming refers to a concept that no longer exists.

### Why this matters

Test method names are documentation. After the flip, the relevant predicate is "module has at
least one `export` annotation", not "module has explicit exports." A reader scanning failure
output will not understand what `HasExplicitExports` means.

### Suggested fix

Rename to e.g. `ModuleInfo_Exports_NotNull_WhenModuleHasAnyExportAnnotation` (or
`…_WhenSourceCompiled`, since every source-compiled module now has a non-null `Exports`).

### Verify

```
dotnet test --filter "FullyQualifiedName~ImportResolverExportTests"
grep -rn HasExplicitExports --include='*.cs'
```

Both should be green / empty outside the kanban folder.

---

## F04 — [MINOR] Stale "legacy modules export everything" wording in `ImportResolver.ModuleInfo` ctor doc

**Status:** fixed
**Fixed in:** 8911a54
**Files:** `Stash.Analysis/Resolvers/ImportResolver.cs:128`
**Phase:** 1B
**Commit:** 890fb30

### Observation

The public ctor's `<param>` for `exports`:

```csharp
/// <param name="exports">
/// The explicit export set, or <see langword="null"/> for legacy modules that export everything.
/// </param>
```

The `Exports` property doc on the same class (lines 86–100) was updated correctly:
> Only names present in `Names` are visible … `null` is reserved for v3 on-disk `.stashc` chunks
> that pre-date the export-set feature.

So the property doc and the ctor `<param>` doc now disagree about what `null` means. Per the
brief: source-compiled modules always have non-null `Exports`; `null` is only the v3 on-disk
fallback. The ctor doc still teaches the pre-flip rule ("legacy modules that export
everything").

### Why this matters

This is the public API surface that an embedder reads when constructing a `ModuleInfo`
directly. The wrong phrasing teaches them the old dual-mode rule.

### Suggested fix

Replace the `<param>` text with the same v3-on-disk explanation already on the property doc:

```csharp
/// <param name="exports">
/// The export set. Always non-null for modules compiled from source (an empty <c>Names</c>
/// means the module exports nothing). <see langword="null"/> is reserved for v3 on-disk
/// <c>.stashc</c> chunks loaded without an export section; the VM exposes their full globals
/// as a legacy fallback.
/// </param>
```

### Verify

`dotnet build` clean; manual read of `ImportResolver.cs:120-160`.

---

## F05 — [NIT] "Legacy modules (no export) are unaffected" comment in `ExportValidationTests`

**Status:** open
**Files:** `Stash.Tests/Analysis/ExportValidationTests.cs:162`
**Phase:** 1D
**Commit:** d3aa652

### Observation

```csharp
// ── Regression: legacy modules (no export) are unaffected ────────────────

[Fact]
public void Validate_LegacyModuleWithLetBindings_NoSA0805()
```

"Legacy module" is the pre-flip term for "file with zero `export` annotations exports
everything." After the flip there is no such category — the test is now just asserting that
SA0805 (cannot export `let`) is not spuriously fired on a file that contains `let`
declarations and no export block at all. The behaviour assertion is still correct; the framing
is stale.

### Why this matters

Minor reader-confusion only. Doesn't affect behaviour or coverage.

### Suggested fix

Rename the comment block to e.g. `// ── Files with no export annotations: SA0805 only fires on
exported lets ──` and rename the test method to `Validate_LetBindingsInUnannotatedFile_NoSA0805`.

### Verify

`dotnet test --filter "FullyQualifiedName~ExportValidationTests"` still green.

---

## F06 — [NIT] Inline comment in `ImportExportRuntimeTests.CompileLegacy` helper says "legacy module"

**Status:** open
**Files:** `Stash.Tests/Bytecode/ImportExportRuntimeTests.cs:40-43`
**Phase:** 1B
**Commit:** 890fb30

### Observation

```csharp
/// Compiles <paramref name="source"/> without running the export builder so that
/// <c>Chunk.Exports</c> is <see langword="null"/> (legacy module behaviour).
private static Chunk CompileLegacy(string source) => CompileSource(source);
```

The helper name and its doc-comment still call this "legacy module behaviour." Post-flip,
**no source file** produces a chunk with `Exports == null` — the builder always runs in the
real pipeline. The only consumer of this null-Exports path is a v3 `.stashc` loaded from disk
(per the brief's "Edge cases" table and `BuildExportedEnvironment`'s guard comment).

The helper still tests something real and useful: it pins what the VM does when
`Chunk.Exports == null`, which simulates the v3-on-disk case without needing to write/read a
v3 binary. But "legacy module" suggests source-compiled files can still hit this path, which
is no longer true.

### Why this matters

Minor reader-confusion only. The test passes and its assertion is correct.

### Suggested fix

Rename the helper to `CompileWithoutExports` (or `CompileV3LikeChunk`) and update the doc to
say "simulates a v3 on-disk chunk where the bytecode reader produces `Exports == null`." Apply
the same rename to the test that uses it (`Vm_ImportFromLegacyModule_StillSeesEverything` →
`Vm_NullExportsChunk_VmExposesFullGlobals` or similar) — that test pins the v3-fallback
branch of `BuildExportedEnvironment`, not "legacy modules."

### Verify

`dotnet test --filter "FullyQualifiedName~ImportExportRuntimeTests"` still green.

---

## F07 — [NIT] CodeActionHandler doc summary still says "legacy modules"

**Status:** open
**Files:** `Stash.Lsp/Handlers/CodeActionHandler.cs:682`
**Phase:** 1B
**Commit:** 890fb30

### Observation

```csharp
/// when the module has explicit exports; falls back to top-level symbol lookup for legacy modules.
```

The body of `ModuleExportsSymbol` was correctly updated (the inline comment on the
`Exports == null` branch now says "v3 on-disk chunk"). The function-level `<summary>` block at
line 682 was missed.

### Why this matters

Minor doc inconsistency on a public-shape API surface. Not load-bearing.

### Suggested fix

Replace "legacy modules" with "v3 on-disk chunks (Exports == null fallback)" in the summary.

### Verify

`grep -n "legacy module" Stash.Lsp/Handlers/CodeActionHandler.cs` returns nothing.
