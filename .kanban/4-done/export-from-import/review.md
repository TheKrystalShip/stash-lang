# export-from-import — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `10d1f2d..ffa0464` on branch `main`
**Brief:** ./brief.md
**Generated:** 2026-05-18

---

## F01 — [IMPORTANT] `ResolveSelectiveImport` emits duplicate / code-less diagnostic and short-circuits SA0809 hint

**Status:** fixed
**Fixed in:** 100ea6b
**Files:** `Stash.Analysis/Resolvers/ImportResolver.cs:676-704`
**Phase:** cross-phase (pre-existing parent-feature code, exercised by 2F's SA0825 design)
**Commit:** —

### Observation

`ResolveSelectiveImport` adds a hand-coded `SemanticDiagnostic` (no SA-code, level `Error`) for every missing-name case, and then *additionally* adds `SA0809` only when a private top-level symbol with that name exists. Two distinct issues:

1. The first diagnostic has no `Code` — directly violates `Stash.Analysis/CLAUDE.md`'s rule: "never construct `SemanticDiagnostic` by hand-coding its code/message/level."
2. When the name *is* a private symbol the user sees **two** diagnostics for the same token: the code-less generic error plus `SA0809`.

This pre-dates this feature but interacts with re-exports: an `export { foo } from "lib/x.stash"` where `foo` is module-private now reaches `ResolveSelectiveImport` only indirectly (the new path resolves via `ResolveExportFrom` and emits `SA0825`). Importers of the *re-exporting* module that try `import { foo } from "index.stash"` will still trigger the buggy double-emit if the symbol is private. The brief explicitly defines `SA0825` (and parent-feature `SA0809`) as the canonical codes.

### Why this matters

Double-emit and code-less diagnostics confuse users (and break suppression-by-code in `.stashcheck`). The hand-coded path is also a latent bug-vector for future renumbering — moving SA0809's meaning, level, or message will desync the spec from the message users see.

### Suggested fix

Drop the hand-coded `SemanticDiagnostic` branch in `ResolveSelectiveImport`; emit `SA0825` (canonical "module does not export 'X'") for the not-in-export-set case and `SA0809` (private-name hint) as a *related* diagnostic when applicable. Mirror the equivalent logic added in `ResolveExportFrom`.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ImportResolverExportTests|FullyQualifiedName~ReexportResolverTests"
```

---

## F02 — [MINOR] AST node XML docs reference stale diagnostic codes (SA0812 / SA0813)

**Status:** fixed
**Fixed in:** bc88631
**Files:** `Stash.Core/Parsing/AST/ExportFromStmt.cs:19,23`, `Stash.Core/Parsing/AST/ExportModuleAsStmt.cs:21`
**Phase:** 2A
**Commit:** 82993d4

### Observation

`ExportFromStmt.cs` mentions `SA0812` (empty list) in two XML-doc comments, and `ExportModuleAsStmt.cs` mentions `SA0813` (alias collision). The `chore: renumber SA0811-SA0813 → SA0822-SA0824` commit (`248f7dc`) updated emission sites, the descriptor registry, the validator, and tests, but missed the XML doc-comments on the two AST node files. The correct codes are `SA0823` and `SA0824`.

### Why this matters

These XML comments surface in IDE tooltips for anyone reading the AST API (LSP `Stash.Core` hover, Roslyn quick-info). They are documentation-quality drift, not a runtime defect, but the project explicitly treats doc-code consistency as a single-source-of-truth concern.

### Suggested fix

Replace `SA0812` → `SA0823` (two occurrences in `ExportFromStmt.cs`) and `SA0813` → `SA0824` (one occurrence in `ExportModuleAsStmt.cs`).

### Verify

```
grep -rn "SA081[0-9]" Stash.Core/Parsing/AST/ExportFromStmt.cs Stash.Core/Parsing/AST/ExportModuleAsStmt.cs
# Expect zero matches.
dotnet build Stash.Core
```

---

## F03 — [IMPORTANT] SA0823 level disagrees with brief (Warning vs. Error)

**Status:** fixed
**Fixed in:** 100ea6b
**Files:** `Stash.Analysis/Models/DiagnosticDescriptors.cs:188-192`, `Stash.Tests/Analysis/ExportFromValidationTests.cs:36`, `docs/Stash — Language Specification.md:439,508`
**Phase:** 2C
**Commit:** eefd183

### Observation

The brief's diagnostics table (brief.md line 233) specifies `SA0823 | Error | export {} from "path";`. The implementation registers `SA0823` at `DiagnosticLevel.Warning`, the test asserts `Level == DiagnosticLevel.Warning`, and the language spec text says "produces a warning (SA0823)". Acceptance #6 of the brief reads `export {} from "lib/x.stash"` produces SA0823 — doesn't constrain level explicitly, but the descriptors table in the design section is explicit.

### Why this matters

Brief parity. Either the brief is the source of truth (then the level needs to be `Error`) or the design decision to demote was made silently during implementation and the brief should be amended. Today the three pieces (brief, spec, descriptor) are inconsistent across two of the three.

### Suggested fix

Pick one: (a) change `SA0823` to `DiagnosticLevel.Error`, update the test assertion and spec table; or (b) update brief.md's design table to read `Warning`, with a one-line rationale (empty list is benign — the import still loads). Both ends should match.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ExportFromValidationTests"
grep -n "SA0823" docs/Stash\ —\ Language\ Specification.md .kanban/2-in-progress/export-from-import/brief.md
```

---

## F04 — [IMPORTANT] `export "p" as alias;` does not propagate an OriginPath; LSP hover/go-to-def cannot follow namespace re-export chains

**Status:** fixed
**Fixed in:** 100ea6b
**Files:** `Stash.Analysis/Models/ModuleExportsBuilder.cs:250-270` (`ProcessExportModuleAs`)
**Phase:** 2F
**Commit:** 128ea50

### Observation

`ProcessExportModuleAs` creates the `ExportEntry` for the alias *without* setting `OriginPath`. Compare to `ProcessExportFrom` which does set `OriginPath = (exportFrom.Path as LiteralExpr)?.Value as string;`. Consequence: when module `index.stash` contains `export "lib/data.stash" as data;` and module `main.stash` does `import { data } from "index.stash";`, `HoverHandler.ResolveReExportChain` reaches `index.stash`'s `ExportEntries["data"]`, sees `OriginPath == null`, and treats `data` as locally declared in index.stash. Hover/go-to-def therefore lands on the `export "lib/data.stash" as data;` line inside `index.stash` rather than on the source module (or its top-level), breaking the brief's "original declaration" promise (acceptance #8) for the namespace form.

The implementer's 2F report flagged this as a known gap; it does not appear in the brief's non-goals.

### Why this matters

Brief parity (acceptance #8) for the namespace re-export form. It also creates an asymmetry: selective re-exports follow the chain, namespace re-exports stop one hop short. Power users authoring barrel files will notice.

### Suggested fix

In `ProcessExportModuleAs`, capture the path string the same way `ProcessExportFrom` does and pass it through to the `ExportEntry`:

```csharp
var originPath = (exportModuleAs.Path as LiteralExpr)?.Value as string;
names[aliasLexeme] = new ExportEntry(SymbolKind.Namespace, exportModuleAs.Alias.Span, exportSpan, originPath);
```

`HoverHandler.ResolveReExportChain` will then walk to the namespace target's `ModuleInfo` correctly. Add a `ReexportLspTests` case that hovers a re-exported namespace's member and asserts the source module's URI.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ReexportLspTests"
```

---

## F05 — [MINOR] `FindReExportSpanForPath` uses suffix match — wrong span on basename collisions

**Status:** fixed
**Fixed in:** 3598f3a
**Files:** `Stash.Analysis/Resolvers/ImportResolver.cs:601-624`
**Phase:** 2F
**Commit:** 128ea50

### Observation

`FindReExportSpanForPath` compares `targetAbsPath.EndsWith(pathValue)` and also falls back to `Path.GetFileName(targetAbsPath) == Path.GetFileName(pathValue)`. Both are insufficient for disambiguation when two `.stash` files share a basename (e.g. `lib/a/types.stash` and `lib/b/types.stash`). The result is that on a transitive cycle whose closing edge happens to share its basename with another statically resolvable re-export target in the same module, SA0826's diagnostic span lands on the wrong statement.

### Why this matters

Misattributed diagnostic spans cost users debugging time. The fact that this only fires inside an already-detected cycle limits the blast radius to "user is already chasing a multi-file bug." Still suboptimal.

### Suggested fix

Resolve the statement's path to an absolute path (call `ResolveImportToAbsolutePathSilent(pathValue, documentDir)`) and compare with `string.Equals(... StringComparison.OrdinalIgnoreCase)`. The `documentDir` is available at the call site in `DetectReExportCycles` if threaded through.

### Verify

Add a `ReexportResolverTests` case with two cycle paths sharing a basename and assert the span points at the correct statement.

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ReexportResolverTests"
```

---

## F06 — [MINOR] `ImportResolver._loadingModules` and `EnsureModuleLoaded` thread-safety

**Status:** fixed
**Fixed in:** 3598f3a
**Files:** `Stash.Analysis/Resolvers/ImportResolver.cs:45,903`, `Stash.Analysis/Engines/AnalysisEngine.cs:255-279`
**Phase:** 2G (out-of-plan-scope `EnsureModuleLoaded` addition)
**Commit:** 9924510

### Observation

`_loadingModules` is a plain `HashSet<string>`; `LoadModule` mutates it without synchronization (lines 903, 926). `EnsureModuleLoaded` calls `ParseModule`, which constructs a fresh `ModuleInfo` and inserts it into `_moduleCache`. Two concurrent LSP requests — say, hover on two different documents that both re-export `lib/types.stash` for the first time — both reach `_loadingModules.Add(absolutePath)`. Race conditions: torn HashSet state, both threads parsing the same file, or a `ModuleInfo` getting overwritten in `_moduleCache`. The first two are HashSet-internal hazards; the last is a wasted re-parse plus the dependency-tracking side-effects double-firing.

The LSP request loop is generally async — DocumentManager and AnalysisEngine handlers are not visibly serialized.

### Why this matters

The LSP server tolerates some redundant work, but a corrupted `HashSet` can throw `InvalidOperationException` or yield a permanently stuck "loading" entry. This bug is pre-existing in `LoadModule`; phase 2G's `EnsureModuleLoaded` widens its blast radius from "internal to single resolver call" to "any external LSP handler."

### Suggested fix

Lock `_loadingModules` (or convert to `ConcurrentDictionary<string, byte>`). Document that `LoadModule`/`EnsureModuleLoaded` are safe for concurrent calls. Consider de-duplicating concurrent parses with a `Lazy<ModuleInfo>`-per-path cache, but a simple lock is sufficient for now.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ImportResolverTests"
# Add a multi-threaded stress test that calls EnsureModuleLoaded from N tasks on the same path.
```

---

## F07 — [MINOR] No same-module LSP coverage for re-exported names (D-12 IDE gap)

**Status:** fixed
**Fixed in:** 3598f3a
**Files:** `Stash.Analysis/Visitors/SymbolCollector.cs:996-999`, `Stash.Tests/Lsp/ReexportLspTests.cs`
**Phase:** 2G
**Commit:** 9924510

### Observation

`SymbolCollector.VisitExportFromStmt` adds a placeholder `SymbolKind.Variable` symbol for each re-exported name (`detail = "re-exported from <path>"`) with no `SourceUri`. Unlike `VisitImportStmt`, `ImportResolver.ResolveExportFrom` never replaces the placeholder via `resolution.ResolvedSymbols.Add(...)` — it only tracks names for SA0827 detection. Consequence: in the *re-exporting* file itself (e.g. inside `index.stash`), hover or go-to-definition on a re-exported name `foo` shows the placeholder ("re-exported from <path>", kind Variable, no SourceUri) instead of the actual function/struct/enum signature from the source module.

Decision D-12 in the brief promises feature parity with `import` for same-module use. Acceptance #11/#12 cover *runtime* same-module use (and the VM tests pass). LSP same-module behaviour is not asserted by any test in `ReexportLspTests`.

### Why this matters

Authors editing a barrel `index.stash` lose hover/go-to-def for the very symbols they just re-exported, even though the desugaring guarantees the binding is identical to `import + export {}`. Selective re-export tooling parity with `import` is incomplete.

### Suggested fix

In `ImportResolver.ResolveExportFrom`, after the SA0825 check, also push a fully-resolved `SymbolInfo` (with `SourceUri`, real Kind, FullSpan, Detail) into `resolution.ResolvedSymbols` — mirroring the loop in `ResolveSelectiveImport` (lines 694-737). The downstream wiring that replaces the SymbolCollector placeholder with the resolved version is the same path used for `import { foo } from "p";` today.

Add an LSP test: re-exporting file `index.stash` contains `export { Color } from "lib/types.stash";` then `let c = Color.Red;` on the next line — hover on `Color` should report `Color` as the source-module enum.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ReexportLspTests"
```

---

## F08 — [MINOR] Diagnostic-code drift: brief uses SA0809/SA0810/SA0814 throughout; implementation uses SA0825/SA0826/SA0827

**Status:** fixed
**Fixed in:** bc88631
**Files:** `.kanban/2-in-progress/export-from-import/brief.md:51-52,166,227-236,313-316,353`, `.kanban/2-in-progress/export-from-import/plan.yaml:213-235`
**Phase:** cross-phase (renumbering 248f7dc only renamed SA0811-13 → SA0822-24; the other group was renumbered to SA0825-27 without the brief being amended)
**Commit:** 248f7dc

### Observation

Brief acceptance criteria #4 and #7 reference `SA0809` and `SA0810`. Brief design table at line 227-236 names the codes `SA0809` (source-export check), `SA0810` (cycles), `SA0814` (redundant-pair hint). Phase 2F in plan.yaml repeats those numbers. The implementation, the language spec, the descriptor registry, and all tests use `SA0825` / `SA0826` / `SA0827` instead. Note that the parent feature has already claimed `SA0809` with a different meaning ("importer references private name"; see spec line 392), so the renumber was necessary — but the brief was never reconciled.

This is **documentation-only drift**: the *code* is consistent (spec, descriptor, tests, emission sites all agree on the 25/26/27 numbers). The brief and plan.yaml are stale.

### Why this matters

Future readers chasing acceptance criterion #4 or #7 will fail to find the corresponding code. The brief is referenced by `/done` and by historical search. Same kind of drift the renumbering chore was supposed to settle.

### Suggested fix

Update `brief.md` and `plan.yaml` 2F entry to use the final codes (SA0825, SA0826, SA0827). Add a one-line "Renumbered from SA0809/SA0810/SA0814 to avoid conflict with parent feature's SA0809" note in the brief's Decision Log.

### Verify

```
grep -n "SA0809\|SA0810\|SA0814" .kanban/2-in-progress/export-from-import/brief.md .kanban/2-in-progress/export-from-import/plan.yaml
# Only matches should be the renumber note in the Decision Log.
```

---

## F09 — [MINOR] `examples/reexport_barrel.stash` and spec rely on `OriginPath`'s relative-path resolution but never exercise bare-specifier (`@scope/pkg`) re-exports

**Status:** fixed
**Fixed in:** 3598f3a
**Files:** `examples/reexport_barrel.stash`, `Stash.Lsp/Handlers/HoverHandler.cs:484-495` (`ResolveOriginPath`)
**Phase:** 2G, 2J
**Commit:** 9924510, ffa0464

### Observation

`HoverHandler.ResolveOriginPath` resolves an `OriginPath` first via `Path.GetFullPath(originPath, moduleDir)` and falls back to `ModuleResolver.ResolvePackageImport` for bare specifiers (`@scope/pkg/x.stash`). No test or example exercises the bare-specifier branch. The brief's acceptance section doesn't enumerate it, but the brief's D-9 ("any expression `import` accepts") implies parity.

### Why this matters

Barrel files in published Stash packages routinely re-export from sibling packages (`export { Http } from "@stash/net";`). LSP hover chain following in that case rides on the bare-specifier fallback — untested. A package layout where the package's own files relocate (e.g. `lib/` → `src/`) could leave `OriginPath` resolution dangling.

### Suggested fix

Add a `ReexportLspTests` case that re-exports a name from a `@scope/pkg` bare specifier and asserts hover lands in the package's actual source file. Optionally extend `examples/reexport_barrel.stash` (or add a second example) to demonstrate cross-package re-export.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ReexportLspTests"
```

---

## F10 — [MINOR] tmLanguage re-export rule ordering is load-bearing and undocumented

**Status:** fixed
**Fixed in:** bc88631
**Files:** `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json:100-115`
**Phase:** 2I
**Commit:** a2b83f9

### Observation

The new re-export rule (`export { … } from`) was inserted *before* the existing `export { … }` block pattern. The two patterns are siblings under `export-declaration`, and tmLanguage tries them in source order; the broader block pattern (no `from`) would otherwise swallow `export { a } from "p";` and lose the `from` keyword highlighting. There is no comment in the file explaining that ordering is required; a future maintainer alphabetising or refactoring the rules could break highlighting silently.

### Why this matters

Low-impact: only affects VS Code syntax highlighting. But silent regressions in grammar files are typically caught only by user reports.

### Suggested fix

Add a `comment` field on the broader `export { … }` rule (the second one) noting "Must appear *after* the `export { … } from` rule — otherwise the from-clause is not highlighted." This costs one line, prevents one entire class of accidental edit damage.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ReexportGrammarTests"
```
