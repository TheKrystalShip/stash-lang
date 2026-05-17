# Module Exports — explicit export keyword — Design Spec

> **Status:** draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-16
> **Slug:** module-exports

## 1. Motivation

Stash modules currently expose **every** top-level binding to importers. There is no way for a module author to mark a symbol as module-private. Consequences observed in `examples/packages/` and downstream registries:

- `import { ... } from "pkg"` autocompletion lists internal helpers, sentinels, and one-off cache state as if they were API.
- Library authors hand-roll a `_underscore_prefix` convention. Tooling treats those names like any other export.
- Refactoring is hazardous: any rename of a top-level helper is a breaking change for unknown downstream consumers.
- Package index files (`index.stash`) have no idiom for "expose only the public surface" other than wrapping everything in a hand-built namespace.

A real example, `examples/packages/diff/lib/constants.stash:46`:

```stash
// ── Internal sentinels (do not export beyond the package) ────────────────
```

The comment is documentation only; nothing enforces it.

The cost of doing nothing scales with the package ecosystem: each new package is one more module whose entire internal surface is part of its de-facto contract.

## 2. Goals & Non-Goals

**Goals**

- Add explicit `export` annotations at declaration sites and as a standalone `export { ... }` block.
- When a module uses any `export` annotation, only the annotated symbols are visible to importers; all other top-level symbols become module-private.
- Preserve backwards compatibility: modules with **zero** `export` annotations behave as today (everything exported).
- Enforce the export set uniformly across the runtime importer, the static analysis import resolver, and the LSP (completion, go-to-definition, hover).
- Reject `export let` and any other attempt to export a mutable binding with a clear compile-time diagnostic.
- Introduce `export` as a **soft (contextual) keyword** so existing code that uses `export` as a function name (`examples/packages/docker/lib/containers.stash:86 fn export(...)`) does not break.

**Non-Goals**

- No `export default` (TypeScript's default-export concept). Every export is named.
- No `export ... from "other.stash"` re-export forms in this feature. (Future work — see Open Questions.)
- No renaming-on-export (`export { foo as bar }`). Phase 1 ships names-as-declared only.
- No new visibility levels (`internal`, `package`, `protected`). Just exported vs. module-private.
- No changes to namespace `import "pkg" as ns` semantics beyond filtering — the import path resolution, package lookup, and circular-import handling are unchanged.
- No introduction of pragmas; the user explicitly excluded that mechanism.
- No `export let` (hard error). Shared mutable state must go through accessor functions.

## 3. Design

### 3.1 Surface (syntax / API / CLI / config)

Stash grammar additions (BNF fragment, mirroring the spec's existing convention):

```
statement           = ... existing forms ...
                    | exportDecl
                    | exportBlock ;

exportDecl          = "export" decoratedDecl ;

decoratedDecl       = fnDecl
                    | asyncFnDecl
                    | constDecl
                    | structDecl
                    | enumDecl
                    | interfaceDecl ;

exportBlock         = "export" "{" exportName ("," exportName)* ","? "}" ";" ;

exportName          = identifier ;          // must resolve to a top-level symbol
```

Examples:

```stash
// Declaration-site form
export fn diff(a, b) { ... }
export async fn fetch(url) { ... }
export const VERSION: str = "1.0.0";
export struct Point { x: int, y: int }
export enum Status { Ok, Err }
export interface Closer { fn close() }

// Block form — may appear anywhere at the top level
fn helper() { ... }
fn diff(a, b) { ... }
const VERSION = "1.0.0";

export { diff, VERSION };

// Mixed forms in one file are allowed
export fn diff(a, b) { ... }
fn _internal() { ... }
const VERSION = "1.0.0";
export { VERSION };
```

Disallowed forms (compile-time error):

```stash
export let counter = 0;            // SX001 export let
export { counter };                // SX001 if counter is a `let` binding
export extend string { ... }       // SX002 extend cannot be exported
export import "x.stash" as x;      // SX003 import cannot be exported
export { undefined_name };         // SX004 unknown export name
export fn foo() {}                 // OK
export { foo };                    //   ... but combined with above: SX005 duplicate
```

### 3.2 Semantics

#### 3.2.1 Lexer

`export` is added to `Stash.Lexing.Keywords.SoftKeywords`. It is **not** promoted to a dedicated `TokenType`; the lexer emits `TokenType.Identifier` with lexeme `"export"`. This preserves backward compatibility with `fn export(...)` (an `Identifier` in name position is fine) and call sites `export(...)`.

#### 3.2.2 Parser

At statement entry (`Parser.Declaration`), recognize the soft keyword `export` only when the next token is one of: `Fn`, `Const`, `Struct`, `Enum`, `Interface`, `LeftBrace`, or `Identifier` with lexeme `async` followed by `Fn`. Otherwise, fall through to existing identifier handling. This is exactly the pattern already used for `async fn` (`Parser.cs:171–178`).

Special case: when the parser sees `export` followed by `Let`, it raises **SX001** before attempting to parse the inner declaration. This produces a clean "exporting mutable bindings is not allowed" diagnostic instead of a generic "unexpected `let`".

Two new AST node types:

- `ExportDeclStmt(Token ExportKeyword, Stmt Inner, SourceSpan Span)` — wraps an underlying declaration. `Inner` is one of `FnDeclStmt`, `ConstDeclStmt`, `StructDeclStmt`, `EnumDeclStmt`, `InterfaceDeclStmt`.
- `ExportBlockStmt(Token ExportKeyword, List<Token> Names, SourceSpan Span)` — bare list of identifiers.

Both extend `Stmt` and dispatch through `IStmtVisitor` (six implementors per the rule in `Stash.Core/CLAUDE.md`).

#### 3.2.3 Export set construction

After parsing, a new pass — `ExportSetBuilder` — walks the program's top-level statement list and produces a `ModuleExports` record:

```
ModuleExports {
  bool HasExplicitExports;            // true if any export annotation seen
  IReadOnlyDictionary<string, ExportEntry> Names;
}

ExportEntry { SymbolKind Kind; SourceSpan DeclSpan; SourceSpan ExportSpan; }
```

Rules:

1. If the file contains zero `ExportDeclStmt` and zero `ExportBlockStmt`, `HasExplicitExports = false` and `Names` is empty. The runtime and analyzer fall back to today's "everything is exported" behavior (Section 3.3.1).
2. Otherwise `HasExplicitExports = true`. The exact export set:
   - Every `ExportDeclStmt` contributes the declared name of `Inner`.
   - Every `ExportBlockStmt` contributes each name in its list. Each name must resolve to a top-level `fn`, `const`, `struct`, `enum`, or `interface` declaration in the same file. `let` bindings → SX001. Imports → SX003. Unknown → SX004.
   - Duplicates → SX005.
3. The built `ModuleExports` record is attached to the compiled `Chunk` (top-level chunk only) as a new field, and to the analyzer's `ModuleInfo` (Section 3.3.2).

#### 3.2.4 Runtime enforcement (bytecode VM)

`Chunk` gains one new field: `ModuleExports? Exports`. Populated by the compiler from the `ExportSetBuilder` result for the **top-level** chunk only. Nested function chunks always have `Exports == null`.

`VirtualMachine.Modules.cs` enforcement points:

1. **`LoadModule`** (current line 20) — after `moduleVM.Execute(moduleChunk)` (line 140), wrap `moduleVM.Globals` in a filtered view before caching/returning. The new helper `BuildExportedEnvironment(moduleVM.Globals, moduleChunk.Exports)`:
   - If `Exports == null` (no explicit exports), returns the full globals dict unchanged (back-compat).
   - Otherwise, returns a new `Dictionary<string, StashValue>` containing only the entries whose key is in `Exports.Names`, **plus** all built-in namespaces (the existing carve-out for `io`, `fs`, `str`, ... that comes in via the parent-copy `_globals = new(_globals)`).
   - The cache key (`ModuleCache[resolvedPath]`) stores the filtered dict, so subsequent imports of the same module reuse the filtered view.
2. **`ExecuteImport`** (current line 301) — the existing lookup `moduleEnv.TryGetValue(importName, ...)` already raises `"Module does not export 'X'"` on missing names. With filtering, an internal symbol becomes "not exported" and the existing error fires with no code change. The error message is reused verbatim — it now means what it says.
3. **`ExecuteImportAs`** (current line 326) — already iterates `moduleEnv` to build the `StashNamespace`. With `moduleEnv` already filtered, the namespace alias exposes only the export set. The existing `IsBuiltIn` carve-out (line 344) still applies and is unchanged.

This is the **only** runtime change. The interpreter, the compiler's other passes, and the VM dispatch loop are untouched. **No new opcodes** — preserves the dispatch-loop size limit called out in `Stash.Bytecode/CLAUDE.md`.

#### 3.2.5 Static analysis enforcement

`Stash.Analysis/Resolvers/ImportResolver.cs:ModuleInfo` gains one new field `ModuleExports? Exports`. The `ModuleParser` delegate (the `AnalysisEngine`-provided callback that parses a file and returns its `ModuleInfo`) runs the `ExportSetBuilder` pass on the parsed AST and populates this field.

`ResolveSelectiveImport` (around `ImportResolver.cs:193`) currently does:

```csharp
var exportedSymbol = moduleInfo.Symbols.GetTopLevel()
    .FirstOrDefault(s => s.Name == nameToken.Lexeme);
if (exportedSymbol == null) { /* error "does not export X" */ }
```

This is updated to consult `moduleInfo.Exports` first:

- If `Exports != null` and `nameToken.Lexeme` is **not** in `Exports.Names`, raise the existing "does not export 'X'" diagnostic. The symbol may still exist in `Symbols.GetTopLevel()` (it's a module-private declaration); we treat it as if it weren't there. If the name *is* in the top-level symbols but missing from the export set, we additionally surface **SX-W001** as an information-level diagnostic to hint the author may have forgotten to export it.
- Otherwise existing behavior is unchanged.

For `import "pkg" as ns` (namespace imports), `ResolveNamespaceImport` constructs the alias completion set from `moduleInfo.Symbols`. It is updated to skip symbols not in `Exports.Names` when `Exports != null`. This keeps LSP completion on `ns.<cursor>` consistent with what the runtime will actually expose.

#### 3.2.6 LSP / completion side effects

Because the analyzer's `ImportResolution` already drives completions, hovers, and go-to-definition, no separate handler changes are required for completion/hover. Three behaviors emerge for free:

- `import { <cursor> } from "pkg"` — completion proposes only exported names.
- `ns.<cursor>` after `import "pkg" as ns` — completion proposes only exported names.
- Go-to-definition on an export name jumps to the underlying declaration, not the `export { ... }` block. Hover shows the declaration's doc comment.

Explicit handler changes required:

- `Stash.Lsp/Handlers/CodeActionHandler.cs:506–543` — the "add missing import" code action consults the same `ModuleInfo.Symbols`. It must be updated to filter by `Exports` so the action only proposes truly-exported names.
- `Stash.Analysis/Visitors/SemanticTokenWalker.cs` — emit `keyword` token for the `export` keyword inside `ExportDeclStmt` and `ExportBlockStmt`.

#### 3.2.7 Edge cases (must be specified)

| Case | Behavior |
| --- | --- |
| File with zero declarations and zero exports | Back-compat path; no-op file. |
| File with `export { }` (empty block) | Valid. `HasExplicitExports = true` with empty `Names`. Module exposes zero symbols. (Useful for "main script" files where nothing is meant to be importable.) |
| File with only `extend Foo { ... }` and no other declarations | No exports needed — `extend` is a global side effect at module-load time. `HasExplicitExports = false`. The module is "imported for side effects." Importing `{ name }` from such a file errors as today. |
| `import "pkg" as ns` where `pkg/index.stash` re-imports a submodule (`import "sub.stash" as sub;`) | If `index.stash` has `HasExplicitExports = true` and does not export `sub`, then `ns.sub` is hidden. To re-expose, the user writes `export { sub };` in `index.stash`. (See D-2.) |
| Doc-comment `///` immediately above `export fn foo` | Bound to `foo` exactly as today. No change. |
| Anonymous declarations (lambdas assigned to `const`) | `export const f = (x) => x;` is fine — it exports the `const` binding. |
| `let` already in file, then author adds `export {}` | All `let` bindings become module-private. They are still visible inside the module's own runtime execution. Importers cannot see them. |
| `export(...)` at top level (call expression) | Parses as a call, not an export. Soft-keyword follow-set rules it out as a declaration. |
| `let export = 5;` | Parses as a `let` binding named `export`. Soft keyword allows the identifier in non-statement-start position. |

### 3.3 Interaction with existing features

#### 3.3.1 Back-compat for legacy modules

Legacy modules (no `export` keyword anywhere) behave identically to today. This is the single most important interaction: every existing example, test, and downstream package continues to work without modification. The "have any export annotations?" check happens once per module load.

#### 3.3.2 Interaction with `extend`

`extend Type { fn method() { ... } }` registers methods on the receiver type globally at module-load time. Whether the module exports the extension or not is **irrelevant** — `extend` is not a named symbol. We forbid `export extend` (SX002) to make this explicit. Side effect: a module loaded only for its extensions does not need to export anything.

Cross-reference: `Stash.Bytecode/Runtime/ExtensionRegistry.cs`. No code change required there.

#### 3.3.3 Interaction with `interface`

Interface declarations are types. They participate in the export set. The interface's method **signatures** are part of the interface (they are not separate top-level functions); exporting the interface exports those signatures.

#### 3.3.4 Interaction with UFCS / Extension methods

UFCS dispatch (`Stash.Bytecode/Runtime/ExtensionRegistry.cs`) is unaffected — it operates on the receiver's runtime type, not on the importer's name table. A module-private helper function is not discoverable via UFCS in another module (UFCS resolution is also name-table-based).

#### 3.3.5 Interaction with the REPL

REPL input is evaluated as a top-level script in a single VM. `export` annotations at the REPL are silently allowed but operationally inert: there is no importing module. The parser still enforces SX001–SX005. (No special REPL carve-out — keep semantics uniform.)

#### 3.3.6 Interaction with the formatter

The `StashFormatter` (`Stash.Analysis/Visitors/StashFormatter.cs`) must learn to print `ExportDeclStmt` and `ExportBlockStmt`. Layout decisions:

- `ExportDeclStmt`: prints `export ` followed by the inner declaration's printed form. Doc comments stay attached to the inner declaration, not to the `export` keyword.
- `ExportBlockStmt`: same convention as the existing destructured `import {}` printer — single-line if it fits, otherwise broken across lines with one identifier per line.
- The `ImportSorter` (`Stash.Analysis/Formatting/ImportSorter.cs`) is **not** repurposed. Export blocks retain authorial ordering.

#### 3.3.7 Interaction with `.stashc` bytecode serialization

The new `Chunk.Exports` field is serialized as part of the top-level chunk. `BytecodeWriter.cs` / `BytecodeReader.cs` need a new metadata kind (call it `ExportSetMetadata`) and the format-version byte is bumped. The OpCode-table hash is unaffected (no new opcodes), so existing `.stashc` files are invalidated only by the version bump, which is already the documented escape hatch in the `BytecodeWriter.cs` header comment.

### 3.4 Cross-platform considerations

None. This is a purely textual / semantic feature. File-system path resolution is unchanged. No platform-specific behavior introduced.

## 4. Implementation Surface

- [x] Lexer / Parser / AST — add `export` to soft keywords; add `ExportDeclStmt` / `ExportBlockStmt`; parser changes at statement entry; 6-visitor pattern updates.
- [x] Compiler / Bytecode / Opcodes — no new opcodes. Compiler gains an `ExportSetBuilder` pass and a new `Chunk.Exports` field.
- [x] VM / Execution — `VirtualMachine.Modules.cs` filtering at module-load time.
- [x] Stdlib — no changes.
- [x] Static analysis (Stash.Analysis) — `ImportResolver.ModuleInfo` gains `Exports`; `ResolveSelectiveImport` and `ResolveNamespaceImport` consult it; `SemanticValidator` raises SX001–SX005.
- [x] LSP / DAP — `CodeActionHandler` "missing import" filter; semantic-token highlighting for the `export` keyword; otherwise free.
- [x] Playground / VS Code grammar — Monarch tokenizer (`Stash.Playground/wwwroot/js/stash-language.js`) and tmLanguage (`.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`) gain `export` as a keyword.
- [x] Docs — language spec gets a new "Exports" subsection under "Source Files and Modules"; contextual-keyword table updated (add `export`); examples updated.
- [x] Tests — parser, resolver, runtime, analyzer, formatter, LSP completion. Detailed in Section 6.

## 5. Decision Log

| Decision | Alternatives considered | Rationale | Risks |
| --- | --- | --- | --- |
| D-1: `export` is a **soft (contextual) keyword**, not a hard keyword. | Make it a hard keyword like `import`. | A grep of the tree shows `fn export(container, output_path)` in `examples/packages/docker/lib/containers.stash:86`. Promoting `export` to a hard keyword breaks user code without a deprecation window. Soft keywords already exist in Stash (`async`, `await`, `defer`, ...); using the same machinery keeps the design lean. | Soft-keyword parsing requires distinguishing `export fn` (declaration) from `export(...)` (call) and `let x = export;` (identifier). The follow-set is narrow (`fn`, `async`, `const`, `struct`, `enum`, `interface`, `let`, `{`) so disambiguation is straightforward. |
| D-2: "Private by default" activates only when the file has at least one `export` annotation. | (a) Always private by default. (b) Always public by default. | (a) breaks every existing script and package overnight. (b) is the status quo and what users escape from. The hybrid is the same opt-in model that, e.g., Go's `internal` packages and TypeScript's `--isolatedModules` use: feature is dormant until you touch it. Once a file has any `export`, the author has clearly signaled intent. | A subtle footgun: adding the *first* `export` to a file silently hides all previously-public symbols. We mitigate with the analyzer hint SX-W001 the first time `import { X } from "file.stash"` references a name that exists in `file.stash` as a top-level decl but is not in the export set. |
| D-3: `export let` is a hard error (SX001), not a warning. | Allow it; warn; silently ignore the `let`. | The user explicitly required this. Mutable cross-module bindings create live-binding aliasing — a class of bugs (ES Modules `let` exports) that Stash should not inherit. Hard error is the only enforcement that survives across translations and round-trips. | If a user has a real need for "shared mutable state," they must write an accessor function. Verbose but explicit, which matches Stash's overall philosophy. |
| D-4: No `export default`. Every export is named. | TypeScript / ES-modules-style anonymous default export. | Anonymous defaults add a parallel resolution path (`import x from "..."` vs. `import { x } from "..."`) and complicate tooling. Stash has no demonstrated need; deferral is cheap. | If users ask for it later, we can layer it on without breaking the named-export model. |
| D-5: No re-export syntax (`export { x } from "other.stash"`) in this feature. | Include it. | The runtime change is non-trivial: a re-export must be evaluated at module-load time so that `ModuleCache[parent]` ends up holding the re-exported value. It also interacts with circular imports in subtle ways. Out of scope for v1; tracked as a follow-up. | If users adopt the package model heavily, re-exports become the natural ergonomic pattern. We will likely need this within 1–2 releases. |
| D-6: No `export {}` renaming (`export { foo as bar }`). | Include it. | Stash has no existing rename-on-import either. Symmetry favors leaving both off in v1. | Same as D-5 — straightforward follow-up. |
| D-7: `export extend ...` is forbidden (SX002). | Treat `export extend` as a no-op / synonym for `extend`. | `extend` blocks are not named symbols. The `export` keyword applied to them has no operational meaning. Forbidding is clearer than silently accepting. | None. |
| D-8: Export filtering happens once at `LoadModule` return, not inside `ExecuteImport`. | Filter inside each import opcode. | The filtered dict is the same for every importer; computing it once at module-load and caching saves work and lets the existing "Module does not export 'X'" error fire naturally. Cache contract stays simple: `ModuleCache[path] = filteredGlobals`. | The filtered dict is observably different from `moduleVM.Globals`. We must be careful that no other code path reads `_globals` directly across module boundaries (audit confirms it does not). |
| D-9: `Chunk.Exports` lives on the top-level chunk and is `null` for nested chunks. | Pass `ModuleExports` out-of-band from the compiler. | The chunk is already the unit of bytecode caching and serialization. Attaching exports to it makes the contract uniform: a `Chunk` fully describes a module. Required for `.stashc` round-trip. | Bumps the serialization format version. Existing `.stashc` files are re-compiled on next run. Acceptable per the documented escape hatch in `BytecodeWriter.cs`. |
| D-10: SX-W001 ("import refers to declared-but-unexported name") is an analyzer-only diagnostic, not a runtime error. | Make the runtime error message itself say "did you forget...". | The runtime cannot easily know whether the missing name exists as a private declaration without re-scanning the chunk. The analyzer already has the full `ModuleInfo`; it is the right place. | Users running scripts outside the analyzer (no LSP, no `stash-check`) get only the generic "does not export X" message. Acceptable. |

## 6. Test Plan

Scenario list (per-phase tests are scoped in `plan.yaml`):

**Parser / AST**

- `Parse_ExportFn_ProducesExportDeclStmt`
- `Parse_ExportConst_ProducesExportDeclStmt`
- `Parse_ExportStruct/Enum/Interface_ProducesExportDeclStmt`
- `Parse_ExportAsyncFn_ProducesExportDeclStmtWithAsyncInner`
- `Parse_ExportBlock_Single_ProducesExportBlockStmt`
- `Parse_ExportBlock_Multi_TrailingComma_OK`
- `Parse_ExportLet_RaisesSX001`
- `Parse_ExportExtend_RaisesSX002`
- `Parse_ExportImport_RaisesSX003`
- `Parse_ExportCall_AtCallPosition_StillCalls` (`export(foo);` parses as a call, not a declaration)
- `Parse_FnExport_AsName_StillWorks` (`fn export() {}` is a function named `export`)
- `Parse_LetExportEquals_StillWorks` (`let export = 5;` declares identifier `export`)

**ExportSetBuilder / SemanticValidator**

- `Build_NoAnnotations_HasExplicitExportsFalse`
- `Build_EmptyExportBlock_HasExplicitExportsTrueZeroNames`
- `Build_DeclSiteAndBlock_MergedAsUnion`
- `Build_DuplicateExports_RaisesSX005`
- `Build_UnknownNameInBlock_RaisesSX004`
- `Build_NameOfLetBindingInBlock_RaisesSX001`
- `Build_NameOfImportInBlock_RaisesSX003`

**Compiler / Chunk**

- `Compile_ExportFn_AttachesToTopChunkExports`
- `Compile_NestedFn_ChunkExportsIsNull`
- `Serialization_RoundTripsExportSet` (.stashc write/read)

**VM / Runtime**

- `Vm_ImportFromExplicitExportModule_OnlySeesExports`
- `Vm_ImportFromLegacyModule_StillSeesEverything`
- `Vm_ImportMissingExport_RaisesDoesNotExport`
- `Vm_ImportAsAlias_NamespaceOnlyHasExports`
- `Vm_ImportAsAlias_BuiltInNamespacesStillCarvedOut`
- `Vm_ModuleCache_FilteredViewIsCached` (second importer sees the same set)
- `Vm_EmptyExportBlock_AllImportsFail`

**Static analysis / ImportResolver**

- `Analysis_ImportPrivateName_ReportsDoesNotExport`
- `Analysis_NamespaceImport_DotCompletionOnlyListsExports`
- `Analysis_LegacyModule_BehavesAsBefore`
- `Analysis_SXW001_HintsAtMissingExportBlock`
- `Analysis_DependencyTracking_RecomputesOnExportListChange`

**Formatter**

- `Format_ExportFn_PreservesAndAttachesDocComments`
- `Format_ExportBlock_SingleLineWhenItFits`
- `Format_ExportBlock_MultiLineWhenItOverflowsPrintWidth`

**LSP**

- `Lsp_AddMissingImport_OnlyProposesExportedNames`
- `Lsp_SemanticTokens_ExportKeywordIsHighlightedAsKeyword`
- `Lsp_HoverOnExportName_ShowsUnderlyingDecl`

**Docs / examples**

- `Docs_LanguageSpec_HasExportsSection`
- `Examples_NewExportExample_RunsAndShowsBehavior`

## 7. Open Questions

- **Q1**: Should we also ship `export { x } from "other.stash"` re-exports in this feature? (See D-5.) **Resolution:** No, defer. Out of scope.
- **Q2**: Should the analyzer emit SX-W001 (declared-but-not-exported) as a hint or a warning? **Working answer:** information-level hint; configurable via the existing diagnostic-suppression mechanism.
- **Q3**: Should `export interface I { fn m(); }` be parsed differently from non-exported interfaces? **Working answer:** No — the inner `InterfaceDeclStmt` is unchanged; only the wrapper differs.
- **Q4**: Should the formatter group `export {}` blocks at the top or bottom of the file by convention? **Working answer:** No — we leave authorial ordering alone in v1.

## 8. Phases

The plan below is the human-readable summary. Source of truth is `plan.yaml`.

| ID  | Title | Deps | Files (approx.) | Est. tokens |
| --- | --- | --- | --- | --- |
| 1A  | Lexer + Parser + AST nodes | — | `Stash.Core/Lexing/Keywords.cs`, `Stash.Core/Parsing/Parser.cs`, `Stash.Core/Parsing/AST/ExportDeclStmt.cs`, `Stash.Core/Parsing/AST/ExportBlockStmt.cs`, `Stash.Core/Parsing/AST/StmtType.cs`, `Stash.Core/Parsing/AST/IStmtVisitor.cs`, parser tests | ~45k |
| 1B  | Six-visitor stubs (resolver/validator/symbols/tokens/formatter/compiler) — pass-through semantics, no enforcement yet | 1A | All six visitor implementations | ~40k |
| 1C  | `ExportSetBuilder` semantics + SX001–SX005 in `SemanticValidator` | 1B | `Stash.Analysis/Visitors/SemanticValidator.cs`, new `Stash.Analysis/Models/ModuleExports.cs`, validator tests | ~45k |
| 1D  | Compiler: attach exports to `Chunk`; bytecode serializer round-trip | 1C | `Stash.Bytecode/Bytecode/Chunk.cs`, `Stash.Bytecode/Bytecode/Metadata.cs`, `Stash.Bytecode/Compilation/Compiler.cs`, `Stash.Bytecode/Serialization/BytecodeWriter.cs`, `BytecodeReader.cs`, compiler tests | ~50k |
| 1E  | VM enforcement at `LoadModule` boundary | 1D | `Stash.Bytecode/VM/VirtualMachine.Modules.cs`, VM tests | ~35k |
| 1F  | Analyzer: `ImportResolver` filtering + SX-W001 hint | 1C | `Stash.Analysis/Resolvers/ImportResolver.cs`, analyzer tests | ~45k |
| 1G  | LSP: code-action filter + semantic-token highlight + completion verification | 1F | `Stash.Lsp/Handlers/CodeActionHandler.cs`, LSP tests | ~35k |
| 1H  | Formatter pretty-printing | 1B | `Stash.Analysis/Visitors/StashFormatter.cs`, formatter tests | ~35k |
| 1I  | Playground tokenizer + VS Code tmLanguage | 1A | `Stash.Playground/wwwroot/js/stash-language.js`, `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` | ~20k |
| 1J  | Docs + example + language spec update | 1A | `docs/Stash — Language Specification.md`, `examples/module_exports.stash`, `CHANGELOG.md` | ~30k |
