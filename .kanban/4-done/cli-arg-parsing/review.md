# cli-arg-parsing — Review

> Produced by `/feature-review`. One finding per H2 section.
> `/resolve cli-arg-parsing Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `b72da26..f69e050` on `main` (11 phase commits P1–P11)
**Brief:** ./brief.md
**Generated:** 2026-05-22

---

## Summary

- **Total findings:** 6 (1 CRITICAL, 4 IMPORTANT, 2 MINOR)
- **`main` is RED** at HEAD (8851 passed / 43 failed / 108 skipped). Classification:
  - **Intra-feature cleanup misses (blocking):** stale `args` entries in `Wave4ThrowsCoverageTests` (3 cases) and `StdlibConsistencyTests` (1 case) — these are stdlib **metadata** tests, not in-tree callers. P8 deleted the namespace without updating the metadata-iteration drivers. → F02.
  - **Acceptable-deferred per P8 non-goal (not blocking this feature):** `TypeInferenceTests.InfersType_ArgsParseReturnsDict`, `DapIntegrationTests.Integration_ScriptArgs_Accessible`, `AliasPersistenceTests.Load_TagsNewAliasesAsSaved`, `AliasHooksTests.HookRecursion_BeforeInvokesSameAlias_CycleDetected`. These tests exercise *other* features that incidentally used `args.*` as fixture/source. The brief and P8 `non_goals` explicitly defer in-tree caller migration to a follow-up spec. → F03.
  - **Pre-existing, unrelated:** ~30 `DiffPackageTests` failures (`Module does not export 'MARKER_INSERT'`) — caused by the `exports-private-default` feature's default flip; the `@stash/diff` package was never updated. Already documented in `.claude/repo.md`. Out of scope for this review.

---

## F01 — [CRITICAL] cli.positional / cli.option / cli.schema raise ValueError instead of CliSchemaError for unknown type tags

**Status:** fixed
**Fixed in:** 9578a96
**Files:** `Stash.Stdlib/BuiltIns/CliBuiltIns.cs:168`, `Stash.Stdlib/BuiltIns/CliBuiltIns.cs:205`, `Stash.Stdlib/BuiltIns/CliBuiltIns.cs:298`, `Stash.Stdlib/BuiltIns/CliBuiltIns.cs:369-374`, `Stash.Tests/Stdlib/CliSchemaBuilderTests.cs:44`, `Stash.Tests/Stdlib/CliSchemaBuilderTests.cs:92`, `Stash.Tests/Stdlib/CliSchemaBuilderTests.cs:280-284`, `docs/Stash — Standard Library Reference.md:2683-2700`
**Phase:** P1/P2 (regression of P2's done_when)
**Commit:** d618184 (P1) + e6e054e (P2) + f69e050 (P11)

### Observation

`ValidateTypeTag` in `CliBuiltIns.cs:369-374` throws `ValueError` for unknown type tags. This is the placeholder error that P1's plan explicitly says should be re-typed to `CliSchemaError` in P2:

> P1 done_when: "P2 — schema-time errors throw a placeholder ValueError until P2 lands; rewire to CliSchemaError in P2."
> P2 done_when: "P1 schema-time validation now raises CliSchemaError (not the placeholder)."

The re-typing happened in `CliBuiltIns.Schema.cs` (duplicate-short, default conversion, help shadowing, repeated-positional-not-last — all `CliSchemaError`) but was missed in three call sites in `CliBuiltIns.cs`:

- `cli.positional("notatype")` → `ValueError` (should be `CliSchemaError`)
- `cli.option("notatype")` → `ValueError` (should be `CliSchemaError`)
- The XML `<exception cref="ValueError">` doc comments on `Positional` (line 168), `Option` (line 205), and `Schema` (line 298) propagated into `docs/Stash — Standard Library Reference.md` as `Throws: ValueError` for the public `cli.positional` / `cli.option` / `cli.schema` API.

The miss was codified by tests `CliPositional_UnknownTypeTag_ThrowsValueError` (line 44), `CliOption_UnknownTypeTag_ThrowsValueError` (line 92), and `CliSchema_UnknownTypeTagInOption_ThrowsValueError` (line 280) which assert `Assert.IsType<ValueError>`. The comment at `CliSchemaBuilderTests.cs:11` says "All schema-time validation failures throw ValueError in P1 (rewired to CliSchemaError in P2)" — but the test file was not updated when P2 landed.

### Why this matters

Brief acceptance criterion #8 is the load-bearing parity gate for this feature's error model:

> "cli.schema({ a: cli.option("notatype") }) raises CliSchemaError at construction."

Today it raises `ValueError`. A user writing `try { ... } catch CliSchemaError as e { ... }` per the brief's documented pattern will silently miss the unknown-type-tag failure. The public stdlib reference advertises the wrong error type, so users following the docs will write the wrong catch clause. This is a three-layer parity break (runtime, tests, regenerated docs).

### Suggested fix

1. **Runtime:** In `CliBuiltIns.cs:369-374`, change `throw new ValueError(...)` to `throw new CliSchemaError("typeTag", $"unknown type tag \"{tag}\". Supported tags: ...")`. Also audit the two arg-count guards on lines 174 and 211 — those are `TypeError`-shaped arity failures and can stay as `ValueError`/`TypeError`, but the unknown-tag path is schema-time validation per the brief.
2. **XML metadata:** Change `<exception cref="ValueError">` to `<exception cref="CliSchemaError">` on `Positional` (line 168), `Option` (line 205), `Schema` (line 298, the "any schema-time validation rule" exception line — it already throws `CliSchemaError`/`TypeError` from `BuildSchema`, never `ValueError`).
3. **Tests:** Update the three test assertions in `CliSchemaBuilderTests.cs` (lines 44, 92, 280) to `Assert.IsType<CliSchemaError>(ex)`; rename the methods from `_ThrowsValueError` → `_ThrowsCliSchemaError`; remove or update the misleading file-header comment at line 11.
4. **Docs regeneration:** Re-run `dotnet run --project Stash.Docs/` so the `Throws: ValueError` lines in the stdlib reference update.

### Verify

```bash
dotnet test Stash.Tests --filter "FullyQualifiedName~CliSchemaBuilderTests"
dotnet run --project Stash.Docs/
grep -n "Throws:" "docs/Stash — Standard Library Reference.md" | grep -A0 -B0 "cli.option\|cli.positional\|cli.schema"
```

---

## F02 — [IMPORTANT] Stdlib metadata tests still iterate the removed `args` namespace

**Status:** fixed
**Fixed in:** dba290e
**Files:** `Stash.Tests/Stdlib/SourceGenerator/Wave4ThrowsCoverageTests.cs:26`, `Stash.Tests/Stdlib/StdlibConsistencyTests.cs:249`
**Phase:** P8
**Commit:** 288f7f4

### Observation

P8 deleted `ArgsBuiltIns.cs` and removed the `args` namespace from the runtime registry. But two stdlib **metadata-iteration** tests still drive themselves off a hard-coded list that includes `"args"`:

`Wave4ThrowsCoverageTests.cs:22-31` declares
```csharp
private static readonly Dictionary<string, HashSet<string>> NoThrowAllowList = new()
{
    ...
    ["args"] = new() { "list", "count" },
    ...
};
```
This drives three xUnit cases that all fail at HEAD:
- `Wave4_EveryFunctionHasThrowsOrIsAllowlisted(namespaceName: "args")` — calls `StdlibDefinitions.Namespaces.FirstOrDefault(n => n.Name == "args")`, which now returns `null`, and the `Assert.NotNull(ns)` fails.
- `Wave4_AllFunctionsTagged_CoverageCheckPasses` — same lookup with `.First(...)` throws.
- `Wave4_TaggedThrows_ReferenceKnownErrorTypes` — same lookup with `.First(...)` throws.

`StdlibConsistencyTests.cs:249` declares
```csharp
string[] gated = ["env", "fs", "http", "ssh", "sftp", "process", "args", "pkg"];
```
This drives `Construction_WithNoCapabilities_ExcludesCapabilityGatedNamespaces` to check that `args` is absent when the `Process` capability is denied. With `args` no longer registered at any capability level, the assertion `Assert.False(definedNamespaces.Contains("args"))` still passes — but the test now also has a stale assumption ("there exists a gated namespace called args"). Worth removing the entry for documentation hygiene even though it currently passes (re-verified locally).

These are **stdlib metadata** tests (the test bodies iterate the namespace registry), not in-tree `args.*` callers. P8's non-goal explicitly carves out "in-tree args.* callers" — but the source-generator coverage tests are part of the stdlib build hygiene, not a caller, and they should have been updated when the namespace was deleted.

### Why this matters

`main` is red. The Wave4 failures are inside this feature's scope (the deletion that broke them happened in P8 of this feature). They are not caller migration, which is what the P8 non-goal defers. Leaving `main` red because of the namespace this feature deleted blocks `/done` (`final_verify` would have to be filtered around these failures, which goes against the project's "fix or skip with documented reason" policy).

### Suggested fix

In `Wave4ThrowsCoverageTests.cs:26`, remove the `["args"] = new() { "list", "count" },` entry from `NoThrowAllowList`. No other code in the file depends on `"args"` being present.

In `StdlibConsistencyTests.cs:249`, remove `"args"` from the `gated` array.

Both are mechanical one-line edits scoped to test files.

### Verify

```bash
dotnet test Stash.Tests --filter "FullyQualifiedName~Wave4ThrowsCoverageTests|FullyQualifiedName~StdlibConsistencyTests"
```

---

## F03 — [IMPORTANT] Four pre-existing in-tree `args.*` call sites still fail at HEAD (acceptable-deferred per P8 non-goal, but should be annotated/skipped to keep `main` green)

**Status:** fixed
**Fixed in:** dba290e
**Files:** `Stash.Tests/Analysis/TypeInferenceTests.cs` (InfersType_ArgsParseReturnsDict), `Stash.Tests/Dap/DapIntegrationTests.cs` (Integration_ScriptArgs_Accessible), `Stash.Tests/Interpreting/AliasPersistenceTests.cs` (Load_TagsNewAliasesAsSaved), `Stash.Tests/Interpreting/AliasHooksTests.cs` (HookRecursion_BeforeInvokesSameAlias_CycleDetected)
**Phase:** P8
**Commit:** 288f7f4

### Observation

P8 carved out `non_goals: "Do not migrate in-tree args.* callers"` and listed the four old `Stash.Tests/Interpreting/Args*Tests.cs` files plus `CliExecutionTests.cs` as in-scope for the `[Fact(Skip = ...)]` bridge. But four additional in-tree callers were missed because they live in unrelated test files and use `args.*` only as fixture/source content:

- `TypeInferenceTests.InfersType_ArgsParseReturnsDict` — script source: `let parsed = args.parse({...})`. Now fails because `args.parse` is undefined.
- `DapIntegrationTests.Integration_ScriptArgs_Accessible` — uses `args` as a global in a debug fixture script.
- `AliasPersistenceTests.Load_TagsNewAliasesAsSaved` — uses `args` global inside an alias body.
- `AliasHooksTests.HookRecursion_BeforeInvokesSameAlias_CycleDetected` — same pattern.

These are genuinely "in-tree args.* callers" exercising the **DAP**, **alias**, and **type-inference** subsystems. Migrating them is the follow-up spec's job per the brief's "No migration of in-tree callers" non-goal. But P8's done_when only enumerated the `Args*Tests.cs` files for the skip-annotation bridge — these four tests slipped through and now fail unconditionally on `main`.

### Why this matters

`main` is red on HEAD. The fix is the same one-line `[Fact(Skip = "...")]` bridge that P8 applied to the explicit list. Leaving them as silent failures means the follow-up migration spec inherits a four-failure backlog with no marker that these are deferred-by-design. `/done` cannot run an unfiltered `final_verify` until these are either skipped or migrated.

### Suggested fix

Apply the same skip annotation P8 used for the explicit list, with the same reason string:

```csharp
[Fact(Skip = "args namespace removed in cli-arg-parsing; migrated by follow-up spec")]
```

…on each of the four test methods. The four tests can be migrated to `cli.argv()` / `cli.parse` in the follow-up spec without losing coverage — the skip is the bridge, not the destination.

Alternative: convert each to `cli.argv()` / `cli.parse` directly if the test intent is preserved (DAP and alias tests in particular use `args` as a black box; the mechanical rename is similar to what P8 did to `CliExecutionTests.cs`). This is a judgment call between the orchestrator and the follow-up spec.

### Verify

```bash
dotnet test Stash.Tests --filter "FullyQualifiedName~TypeInferenceTests.InfersType_ArgsParseReturnsDict|FullyQualifiedName~DapIntegrationTests.Integration_ScriptArgs_Accessible|FullyQualifiedName~AliasPersistenceTests.Load_TagsNewAliasesAsSaved|FullyQualifiedName~AliasHooksTests.HookRecursion_BeforeInvokesSameAlias_CycleDetected"
```

---

## F04 — [IMPORTANT] 1158 LOC of orphan code in Stash.Core/Common (ArgumentParser.cs + ArgumentBuilder.cs)

**Status:** fixed
**Fixed in:** 1830b27
**Files:** `Stash.Core/Common/ArgumentParser.cs` (927 lines), `Stash.Core/Common/ArgumentBuilder.cs` (231 lines)
**Phase:** P8
**Commit:** 288f7f4

### Observation

`ArgumentParser` (`Stash.Core/Common/ArgumentParser.cs:10`) and `ArgumentBuilder` (`Stash.Core/Common/ArgumentBuilder.cs:9`) were the implementation engines behind the now-deleted `args.parse` and `args.build` built-ins. Their XML doc comments still reference the deleted API:

- `ArgumentParser.cs:29-30`: "Implements the args.parse() built-in function."
- `ArgumentBuilder.cs:8`: "Builds CLI argument token lists from a dict specification and a values dict — the reverse of ArgumentParser."

A repo-wide search confirms **zero** callers outside their own files (and the `.lscache`):
```
grep -rn "ArgumentParser\|ArgumentBuilder" Stash.Core/ Stash.Stdlib/ Stash.Cli/
→ only declarations + comments + .lscache; no `new ArgumentParser(...)` or `ArgumentBuilder.Build(...)` calls anywhere.
```

The P8 commit body explicitly flagged this:
> "Stash.Core/Common/ArgumentParser.cs and ArgumentBuilder.cs are now orphan helpers (no callers outside the deleted ArgsBuiltIns.cs). Left in place per P8 non-goal."

P8's stated non-goal is "Do not migrate in-tree args.* callers" — but these files are **not** callers. They are the **implementation** of `args.parse` / `args.build`. With the namespace deleted and no other caller, they are dead code by the end of this feature. The follow-up migration spec has nothing to migrate them *to*; their entire purpose is subsumed by `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs` and `CliBuiltIns.Build.cs`.

### Why this matters

1158 LOC of dead C# in `Stash.Core/Common/` — the foundation layer with the strictest "no dependencies, careful changes" rule per `Stash.Core/CLAUDE.md`. Future readers will assume the code is reachable. Maintenance debt with zero offsetting benefit, in the most blast-radius-sensitive directory in the repo. The brief says (Decision Log, 2026-05-18): "the @stash/cli user-space package will be **deleted wholesale**, not migrated" — same logic applies to these two helpers.

### Suggested fix

Delete both files:
```bash
git rm Stash.Core/Common/ArgumentParser.cs Stash.Core/Common/ArgumentBuilder.cs
```

Confirm no broken refs:
```bash
dotnet build Stash.sln
```

If the build fails on a stray reference, that reference itself is the bug — the helpers have no compile-time callers in the diff.

### Verify

```bash
grep -rn "ArgumentParser\|ArgumentBuilder" Stash.Core/ Stash.Stdlib/ Stash.Cli/ Stash.Tests/
# Should match zero non-.lscache lines.
dotnet build Stash.sln
```

---

## F05 — [MINOR] Dead `case "command":` branch in LiteralSchemaBuilder.EvalBuilderCall

**Status:** fixed
**Fixed in:** 1830b27
**Files:** `Stash.Analysis/Cli/LiteralSchemaBuilder.cs:522-580` (the `case "command":` block inside `EvalBuilderCall`)
**Phase:** P10
**Commit:** 2c3e9de

### Observation

`LiteralSchemaBuilder.EvalBuilderCall` (line 428) has a `case "command":` branch (line 522) that attempts to handle `cli.command({...})` calls inside a literal schema. The branch is unreachable by design — the literal-ness predicate filters all `CallExpr` nodes before reaching this switch:

1. `IsLiteralSchemaDict` (line 239) iterates schema-dict values and calls `IsLiteralBuilderCallExpr` (line 259) on each.
2. `IsLiteralBuilderCallExpr` (line 268-274) requires **every argument** of the builder call to satisfy `IsLiteralExpr` (line 297).
3. `IsLiteralExpr` only admits literal kinds (literals, arrays, dicts, `UnaryExpr(Minus, numeric)`) — `CallExpr` is never admitted.
4. `cli.command({...})` passes a `DictLiteralExpr` whose entry **values** are `CallExpr` (the inner `cli.schema(...)` calls), so `IsLiteralExpr(DictLiteralExpr)` returns false on the first inner `cli.schema(...)` call.
5. Therefore any `cli.command({...})` inside a literal schema causes the whole schema to be classified non-literal and the static help mode falls back to the generic message — the `case "command":` branch in `EvalBuilderCall` cannot fire.

The P10 commit body explicitly acknowledges this:
> "case `command:` branch in `LiteralSchemaBuilder.EvalBuilderCall` is dead code by design — the predicate rejects nested `cli.command({...})` (CallExpr inside a literal). Implementer flagged this explicitly."

### Why this matters

~60 LOC of dead branch in an analyzer file. Future maintainers will assume the branch is exercised and may attempt to extend or refactor it, only to find that no test fixture can reach it. The code also includes a comment ("For simplicity in literal mode, we skip nested subcommand building...") that misleadingly implies subcommand literals are partially supported.

### Suggested fix

Either:
1. **Remove** the `case "command":` block entirely. If the predicate is ever relaxed in a follow-up spec to admit nested builder calls, the branch can be re-added then with proper test coverage.
2. **Replace** the branch body with `throw new InvalidOperationException("unreachable: literal predicate rejects cli.command nested CallExpr; see IsLiteralExpr");`.

Option 1 is cleaner and matches the brief's "literal-only stub" framing for P10.

Either way, also remove `"command"` from the `BuilderNames` set on `LiteralSchemaBuilder.cs:61` so the predicate is consistent with the implementation.

### Verify

```bash
dotnet build Stash.Analysis
dotnet test Stash.Tests --filter "FullyQualifiedName~LiteralSchemaPredicateTests|FullyQualifiedName~StaticHelpModeTests"
# Existing subcommand-fallback tests must still hit the FallbackMessage branch.
```

---

## F06 — [MINOR] InternalsVisibleTo exposes Stash.Stdlib internals to Stash.Analysis and Stash.Cli

**Status:** fixed
**Fixed in:** 257cd41
**Resolution:** Option 3 — kept InternalsVisibleTo, added `<remarks>` doc comments on the three entry points (`MakeArgSpecInstance`, `BuildSchema`, `RenderHelp`) flagging them as cross-assembly contracts and naming the dependent files.
**Files:** `Stash.Stdlib/Stash.Stdlib.csproj:15-16`
**Phase:** P10
**Commit:** 2c3e9de

### Observation

P10 added two new `InternalsVisibleTo` entries to `Stash.Stdlib.csproj`:

```xml
<InternalsVisibleTo Include="Stash.Analysis" />
<InternalsVisibleTo Include="Stash" />
```

This was done so `LiteralSchemaBuilder` (in Stash.Analysis) and `StaticHelpMode` (in Stash.Cli, asm name "Stash") can call the internal `CliBuiltIns.MakeArgSpecInstance` / `BuildSchema` / `RenderHelp` helpers.

The P10 commit body flags it for review:
> "InternalsVisibleTo added to Stash.Stdlib.csproj for Stash.Analysis and Stash (Stash.Cli) so LiteralSchemaBuilder can call internal stdlib methods. Worth flagging as an architectural coupling for the reviewer to evaluate."

### Why this matters

Until P10, `Stash.Stdlib` exposed only its public StashFn surface to the rest of the toolchain. The new IVT entries create two reverse-direction couplings:
- The analysis layer (which previously only read AST metadata) now constructs `StashInstance`s through internal stdlib factories.
- The CLI layer (which previously orchestrated the VM through public interfaces) now calls `CliBuiltIns.RenderHelp` directly.

The coupling is load-bearing for the literal static-help path: the analyzer needs to build a real `CliSchema` instance the help renderer recognizes. The two reasonable alternatives are:
1. Promote `CliBuiltIns.MakeArgSpecInstance` / `BuildSchema` / `RenderHelp` to `public` (formalizes the contract; matches what IVT already grants).
2. Move the literal-build + render-help glue into Stash.Stdlib itself, with the analyzer only providing the parsed AST snippet.

Either is more durable than IVT, which is invisible at the consumer call site and easy to entangle further in follow-ups.

### Suggested fix

Decide between (1) and (2) above. If kept as IVT, at minimum add an XML doc comment on each of the three internal entry points (`CliBuiltIns.MakeArgSpecInstance`, `CliBuiltIns.BuildSchema`, `CliBuiltIns.RenderHelp`) that says: "Treated as a public contract via InternalsVisibleTo to Stash.Analysis and Stash.Cli for the static `--help` path. Signature changes require updating LiteralSchemaBuilder and StaticHelpMode in lockstep."

This finding can be downgraded to "acceptable with the comment" by the orchestrator if the team prefers IVT over either alternative.

### Verify

```bash
# After applying the chosen fix:
dotnet build Stash.sln
dotnet test Stash.Tests --filter "FullyQualifiedName~StaticHelpModeTests|FullyQualifiedName~LiteralSchemaPredicateTests|FullyQualifiedName~CliSchemaLspTests"
```
