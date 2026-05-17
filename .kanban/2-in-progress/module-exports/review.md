# module-exports — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve module-exports Fxx` reads exactly one section and dispatches a Resolver.

**Scope reviewed:** commits `9df0387^..04c1638` on branch `main`
**Spec:** ../spec.md
**Generated:** 2026-05-16

---

## F01 — [CRITICAL] Runtime export enforcement is dead code on the production module load path

**Status:** fixed
**Fixed in:** deb876b
**Files:** `Stash.Bytecode/VM/VirtualMachine.Modules.cs:111`, `Stash.Cli/Program.cs:447`, `Stash.Cli/Program.cs:535`, `Stash.Cli/Program.cs:617`, `Stash.Cli/Program.cs:691`, `Stash.Cli/Program.cs:777`, `Stash.Cli/Program.cs:1080`, `Stash.Cli/Program.cs:1196`, `Stash.Cli/Program.cs:1285`, `Stash.Cli/Shell/ShellRunner.cs:323`, `Stash.Bytecode/StashEngine.cs:238`, `Stash.Bytecode/StashEngine.cs:365`, `Stash.Bytecode/StashCompilationPipeline.cs:85`, `Stash.Bytecode/Runtime/VMTemplateEvaluator.cs:95`, `Stash.Dap/DebugSession.cs:305`, `Stash.Dap/DebugSession.cs:352`
**Phase:** 1D / 1E (cross-phase)
**Commit:** 6c8792d, 74fa9a4

### Observation

`Compiler.Compile()` accepts an optional `ModuleExports? exports` argument (Stash.Bytecode/Compilation/Compiler.cs:84, :100) and assigns it to `Chunk.Exports`. The VM's `BuildExportedEnvironment` (Stash.Bytecode/VM/VirtualMachine.Modules.cs:154) is keyed off `moduleChunk.Exports`. However, **no real call site in the production codebase ever invokes `ModuleExportsBuilder.Build()` or passes the result to `Compiler.Compile()`**. Specifically:

- `VirtualMachine.LoadModule`'s built-in file loader (Stash.Bytecode/VM/VirtualMachine.Modules.cs:111) calls `Compiler.Compile(stmts)` with no exports argument.
- Every entry point in `Stash.Cli/Program.cs` (run, compile, repl bootstrap, eval, REPL ingest, doc, check, registry) calls `Compiler.Compile(statements)` plain.
- `StashEngine.cs:238`/`:365`, `StashCompilationPipeline.cs:85`, `ShellRunner.cs:323`, `VMTemplateEvaluator.cs:95`, `DebugSession.cs:305`/`:352` likewise.

As a result, `Chunk.Exports` is always `null` for chunks compiled at runtime, `BuildExportedEnvironment` falls into its `exports == null → return globals unchanged` branch (line 159–160), and **module-private symbols are silently importable from any consumer**.

Reproduction (confirmed against current `main`, all phases committed):

```
$ cat /tmp/mod.stash
export fn pub() -> int { return 1; }
fn priv() -> int { return 99; }

$ cat /tmp/main.stash
import { priv } from "./mod.stash";
io.println(priv());

$ dotnet run --project Stash.Cli/ -- /tmp/main.stash
99
```

Expected: a runtime error `Module does not export 'priv'.` Actual: prints `99`.

The only paths where `Chunk.Exports` is populated today are:
1. Test helpers (`ImportExportRuntimeTests.CompileWithExports`, `ExportSerializationTests`) that explicitly thread `exports` through.
2. `BytecodeReader` deserializing a `.stashc` file — but that file can only have been produced by something that already attached `exports` at compile time, which nothing in production does.

### Why this matters

This is the central runtime guarantee of the feature: "When a module uses any `export` annotation, only the annotated symbols are visible to importers" (spec §2 Goals, §3.2.4). The entire VM-side machinery (phase 1E) is wired correctly but receives `null` from upstream because no compilation pipeline produces a populated `Chunk.Exports`. Every script run, package import, REPL session, embedded engine, DAP debug, and template evaluation silently bypasses export filtering.

The static analyzer (phase 1F) does flag bad imports during LSP / `stash check`, so the *editor* experience matches the spec — but at runtime, the back-compat fallback path is the only path. The feature ships as "lint-only," which is not what the spec specifies.

### Suggested fix

Make `Compiler.Compile(stmts)` build the export set itself, so every caller benefits without ceremony. The cleanest place is the existing `Compiler.Compile(List<Stmt>, …, ModuleExports? exports = null)` overload — when `exports` is null, call `ModuleExportsBuilder.Build(stmts, _)` and use its result. This requires `Stash.Bytecode` to reference `Stash.Analysis`, which is forbidden by the project layering rule (see plan.yaml phase 1D notes).

Therefore the correct fix is to update every compilation entry point that loads user source to call `ModuleExportsBuilder.Build` first. Concretely:

1. `VirtualMachine.Modules.cs:111` (the built-in file loader inside `LoadModule`):
   ```csharp
   var exportDiagnostics = new List<SemanticDiagnostic>();
   var moduleExports = ModuleExportsBuilder.Build(stmts, exportDiagnostics);
   // Discard exportDiagnostics — they will surface again during static analysis;
   // at runtime we only need the export set itself.
   moduleChunk = Compiler.Compile(stmts, exports: moduleExports);
   ```
   This file already references Stash.Analysis indirectly? It does not — VirtualMachine is in Stash.Bytecode, which cannot reference Stash.Analysis. The fix needs to thread the builder through a delegate or factor a thin "compile-with-exports" helper into a layer that owns both deps. The cleanest option is a new helper `Stash.Cli.Shared` / `Stash.Bytecode.StashCompilationPipeline` (which already references both) exposing `CompileModule(string source, string path) → Chunk` that the VM's file loader uses by default. See spec §3.2.3 and plan.yaml phase 1D "Compiler.Compile" note for the dependency-graph rationale.

2. Every `Compiler.Compile(stmts)` call in `Stash.Cli/Program.cs`, `Stash.Cli/Shell/ShellRunner.cs`, `Stash.Bytecode/StashEngine.cs`, `Stash.Bytecode/StashCompilationPipeline.cs`, `Stash.Bytecode/Runtime/VMTemplateEvaluator.cs`, `Stash.Dap/DebugSession.cs` must be migrated to the new helper.

3. The REPL is a special case: per spec §3.3.5 it should accept `export` syntax with no operational effect. The simplest treatment is to call the builder but not pass the result to `Compile` (or pass it and have `BuildExportedEnvironment` only kick in on module loads — which is already the case since the REPL globals aren't loaded via `LoadModule`).

Add a runtime regression test that exercises the file-loader path (not just the `ModuleLoader` delegate path used by `ImportExportRuntimeTests`):

```csharp
[Fact]
public void Vm_FileLoader_PrivateNameIsHidden()
{
    // Write a temp module with export + private fn, write a main that imports the private,
    // run with the default file loader, assert RuntimeError "Module does not export 'priv'".
}
```

### Verify

```
# 1. Smoke: the repro above should now error out.
echo 'export fn pub() -> int { return 1; }
fn priv() -> int { return 99; }' > /tmp/mod.stash
echo 'import { priv } from "./mod.stash";
io.println(priv());' > /tmp/main.stash
dotnet run --project Stash.Cli/ -- /tmp/main.stash
# Expected: non-zero exit, "Module does not export 'priv'."

# 2. Full test sweep.
dotnet test Stash.Tests --filter "FullyQualifiedName~Export"
dotnet test Stash.Tests
```

---

## F02 — [IMPORTANT] `ModuleExportsBuilder` is a thin shim — Build logic still lives on the Analysis-layer `ModuleExports` class

**Status:** fixed
**Fixed in:** 908d1c8
**Files:** `Stash.Analysis/Models/ModuleExports.cs:56-98`, `Stash.Analysis/Models/ModuleExportsBuilder.cs:37-51`
**Phase:** 1D
**Commit:** 6c8792d

### Observation

Plan.yaml phase 1D explicitly mandates the split:

> **What stays in Stash.Analysis** — Delete `Stash.Analysis/Models/ModuleExports.cs` (the old file — now superseded by the Core type). Create `Stash.Analysis/Models/ModuleExportsBuilder.cs` with the builder logic extracted from the old `Build()` static method.

The implemented layout keeps both classes:

- `Stash.Analysis/Models/ModuleExports.cs` still exists and still contains the full `Build(IReadOnlyList<Stmt>, ScopeTree, List<SemanticDiagnostic>)` method, plus the rich `ExportEntry` record, the `Names: IReadOnlyDictionary<string, ExportEntry>` shape, and all SA0805–SA0808 emission.
- `Stash.Analysis/Models/ModuleExportsBuilder.cs:42` just delegates to that method:
  ```csharp
  var analysisExports = ModuleExports.Build(topLevel, null!, diagnostics);
  ```
  and then strips the dictionary down to a `HashSet<string>` for the Core type.

Concrete consequences:

1. There are now **two** types named `ModuleExports` in the codebase: `Stash.Analysis.ModuleExports` (rich, dict-of-entries) and `Stash.Core.Resolution.ModuleExports` (Core, set-of-names). `ImportResolver.ModuleInfo.Exports` exposes the Analysis flavor (`Stash.Analysis/Resolvers/ImportResolver.cs:86`), so the LSP / analyzer code paths use a different runtime type than the bytecode/VM path. The compiler's `Compiler.Compile(…, ModuleExports? exports = null)` parameter is the Core type. The Analysis-layer one cannot be passed to the compiler directly — every consumer that has the Analysis form first has to translate.
2. `ModuleExportsBuilder.Build` does not actually contain "the builder logic" — it is a 5-line wrapper. The duplication risk the plan was trying to remove (`SemanticValidator` calling Build twice, once for diagnostics, once for the Chunk) is still present: `SemanticValidator.cs:99` and `AnalysisEngine.cs:411` each call into the Analysis-layer `Build` with their own diagnostic lists.
3. The Analysis-layer `Build` method has a `ScopeTree scopeTree` parameter that is **declared but never read** in the method body. `ModuleExportsBuilder.Build` passes `null!` for it (line 42). Dead parameter → dead-code smell → easy to misuse.

### Why this matters

Spec parity: phase 1D's notes are explicit about the file layout and the dependency graph rationale (Bytecode and Analysis are siblings, Core sits below both). Today's shape has the Analysis-layer `ModuleExports` doing double duty as both a data model and a builder, with diagnostic emission entangled in the data type. The Core data model is correctly defined as a set-of-names; the Analysis data model should either (a) be the same Core type re-exported, or (b) contain only data with no `Build` static.

Maintainability: a future change to the export-build rules now has to be made in one place that pretends to be data (`Stash.Analysis.ModuleExports.Build`) and a separate place that pretends to be a builder (`ModuleExportsBuilder.Build`). The naming collision (`ModuleExports` in two namespaces, both used in the same files via `using` aliases) is also a readability hazard.

### Suggested fix

Follow plan.yaml phase 1D as written:

1. Extract the body of `Stash.Analysis.ModuleExports.Build(…)` into `ModuleExportsBuilder.Build(IReadOnlyList<Stmt>, List<SemanticDiagnostic>)`. Return `Stash.Core.Resolution.ModuleExports.Create(true, ImmutableHashSet.CreateRange(names.Keys))` directly.
2. Delete `Stash.Analysis/Models/ModuleExports.cs`. Replace every consumer reference (`ImportResolver.ModuleInfo.Exports`, `SemanticValidator`, `AnalysisEngine.ParseModule`) with the Core type. The `ExportEntry` record (with kind / spans) is currently only used internally by `Build` itself — drop it or move it inside `ModuleExportsBuilder` as a private helper.
3. Remove the unused `ScopeTree scopeTree` parameter.
4. Update `ImportResolver.ResolveSelectiveImport` (Stash.Analysis/Resolvers/ImportResolver.cs:216) and `BuildFilteredModuleInfo` (line 331) which currently use `Exports.Names.ContainsKey(lexeme)` — switch to `Exports.Names.Contains(lexeme)` against the Core type's `IReadOnlySet<string>`.

### Verify

```
dotnet build Stash.sln
dotnet test Stash.Tests --filter "FullyQualifiedName~Export"
dotnet test Stash.Tests --filter "FullyQualifiedName~ImportResolver"
```

---

## F03 — [MINOR] Unused `ScopeTree` parameter on `Stash.Analysis.ModuleExports.Build`

**Status:** fixed
**Fixed in:** 908d1c8
**Files:** `Stash.Analysis/Models/ModuleExports.cs:48-58`
**Phase:** 1C
**Commit:** 4354a99

### Observation

The signature

```csharp
public static ModuleExports Build(
    IReadOnlyList<Stmt> topLevel,
    ScopeTree scopeTree,
    List<SemanticDiagnostic> diagnostics)
```

declares `scopeTree` and documents it ("The scope tree produced by SymbolCollector."), but the method body never references it — it builds its own `topLevelIndex` from the AST directly (lines 79, 106–149). `ModuleExportsBuilder` passes `null!` for this parameter (Stash.Analysis/Models/ModuleExportsBuilder.cs:42); `AnalysisEngine.ParseModule` passes a real scope tree (Stash.Analysis/Engines/AnalysisEngine.cs:411).

### Why this matters

A `null!` argument that "works" because the parameter is unread is a latent NRE waiting for a future maintainer who decides to actually use the scope tree. The parameter also implies a dependency on `SymbolCollector` running first, which is not actually true. This is cheap to remove and will be obviated by F02 if that finding is taken.

### Suggested fix

Remove the `ScopeTree scopeTree` parameter from `ModuleExports.Build` and from `ModuleExportsBuilder.Build`'s call site (drop the `null!`). Update `AnalysisEngine.ParseModule:411` and `SemanticValidator.cs:99` accordingly. If F02 is being addressed in the same change, fold this fix into it.

### Verify

```
dotnet build Stash.Analysis
dotnet test Stash.Tests --filter "FullyQualifiedName~ExportSetBuilderTests|FullyQualifiedName~ExportValidationTests"
```

---

## F04 — [MINOR] `SemanticTokenWalker` emits export-block names as `Variable` regardless of underlying symbol kind

**Status:** fixed
**Fixed in:** 7df13d2
**Files:** `Stash.Analysis/Visitors/SemanticTokenWalker.cs:613-621`
**Phase:** 1G
**Commit:** c42cf65

### Observation

```csharp
public int VisitExportBlockStmt(ExportBlockStmt stmt)
{
    EmitFromToken(stmt.ExportKeyword, TokenTypeKeyword, 0);
    foreach (var name in stmt.Names)
    {
        EmitFromToken(name, TokenTypeVariable, 0);
    }
    return 0;
}
```

Every identifier inside `export { … }` is emitted as `TokenTypeVariable`. In practice the names refer to functions, structs, enums, interfaces, or consts — never `let` bindings (which would be SA0805). The hover / go-to-definition path already resolves correctly (via `ImportResolver`), but the syntax highlight colors the names as if they were locals.

### Why this matters

Minor visual inconsistency — in `export { greet, VERSION, Point, Direction }`, all four are colored as variables even though `greet` is a function, `VERSION` is a constant, `Point` is a struct, and `Direction` is an enum. The walker has the top-level scope tree available; it could look each name up and emit the matching token type.

### Suggested fix

In `VisitExportBlockStmt`, before emitting each name token, look it up in `_result.Symbols.FindDefinition(name.Lexeme, name.Span.StartLine, name.Span.StartColumn)` (the same pattern used by `VisitImportStmt` at line 627–629) and dispatch on the resolved `SymbolKind`:

```csharp
foreach (var name in stmt.Names)
{
    var def = _result.Symbols.FindDefinition(name.Lexeme, name.Span.StartLine, name.Span.StartColumn);
    int tokenType = def?.Kind switch
    {
        SymbolKind.Function => TokenTypeFunction,
        SymbolKind.Struct or SymbolKind.Enum or SymbolKind.Interface => TokenTypeType,
        SymbolKind.Constant => TokenTypeVariable,    // with readonly modifier if available
        _ => TokenTypeVariable,
    };
    EmitFromToken(name, tokenType, 0);
}
```

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~ExportLspTests"
```

---
