# RFC: Module Exports — Re-export Forms (`export ... from`)

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-18
> **Slug:** export-from-import

## Summary

Add two new statement forms that combine an `import` with an `export` in a single declaration, making barrel/index files (`index.stash`) ergonomic.

1. **Namespace re-export:** `export "lib/data.stash" as data;` — mirrors `import "lib/data.stash" as data;` but additionally surfaces `data` in the current module's export set.
2. **Selective named re-export:** `export { foo, bar } from "lib/types.stash";` — mirrors `import { foo, bar } from "lib/types.stash";` but additionally adds `foo` and `bar` to the current module's export set under their original names.

`export * from "path";` is **rejected on principle** (matches Stash's existing rejection of wildcard imports).

Both forms compile by **desugaring to a hidden `import` + an export-set contribution**: no new bytecode, no new opcodes, no `.stashc` format bump. Runtime semantics ride on the existing `import` machinery and the existing `BuildExportedEnvironment` filter shipped by the parent `module-exports` feature.

## Motivation

Today, a package `index.stash` that wants to re-expose a sub-module's namespace must hand-roll the pattern:

```stash
import "lib/data.stash" as data;
export { data };
```

For a `lib/types.stash` that defines `Color`, `Size`, and `Direction`, the barrel pattern is:

```stash
import { Color, Size, Direction } from "lib/types.stash";
export { Color, Size, Direction };
```

The names are written three times: once at the source declaration, once on the import line, once on the export line. Authors forget one of them. The intent ("re-export") is buried in two cooperating statements.

The proposed forms collapse this into one line:

```stash
export "lib/data.stash" as data;
export { Color, Size, Direction } from "lib/types.stash";
```

This is purely an ergonomics + barrel-file readability win. The runtime model is unchanged.

## Goals

- Add the two new statement forms at the surface (parser + AST).
- Compile both forms by desugaring to an internal import operation that populates the importer's globals exactly as a hand-written `import` would, plus contributing names to the chunk's `ModuleExports.Names`.
- Detect and diagnose re-export of names not in the source module's exported set (`SA0809`).
- Detect re-export cycles in the analyzer (`SA0810`) — independently of ordinary import-cycle handling, because re-export cycles are unambiguously bugs.
- Surface the original declaration through LSP hover / go-to-def for re-exported names.
- Preserve `.stashc` format compatibility: no version bump. `ModuleExports.Names` stays `IReadOnlySet<string>`.
- Preserve the dispatch-loop size constraint: no new opcodes.

## Non-Goals

- **No wildcard re-export** (`export * from "path";`). Symmetric with the existing rejection of wildcard imports.
- **No alias form** (`export { foo as bar } from "path";`). Symmetric with D-6 of the parent feature, which deferred local aliasing. May be added later in a single PR that adds both local and re-export aliasing together.
- **No "re-export a single member of a namespaced import"** (`import "p" as p; export { p.foo };`). Out of scope; the two new forms already cover the index-file use case.
- **No migration of existing `examples/packages/`** to the new syntax. Example/migration work is not feature scope.
- **No change to the private-by-default rule.** That is the sibling `exports-private-default` spec. This feature is designed to work whether or not private-by-default is in place — see `context.md` (created at implementation time if needed; design is captured here).
- **No `export ... as ...` for a sub-path of an import** (`export "lib/data.stash" as { d };`). Not a recognised form.
- **No new opcodes, no bytecode-format bump, no runtime cache changes.** The desugaring strategy makes these unnecessary.

## Design

### Surface

Grammar additions (BNF fragment, extending the parent `module-exports` grammar):

```
statement              = ... existing forms ...
                       | exportDecl
                       | exportBlock
                       | exportModuleAs                  // NEW
                       | exportFrom ;                    // NEW

exportModuleAs         = "export" expression "as" identifier ";" ;
exportFrom             = "export" "{" exportName ("," exportName)* ","? "}" "from" expression ";" ;
exportName             = identifier ;
```

Notes:

- The path expression for **both** forms is the same `Expression()` parse used by `import` (`Parser.cs:726` and `Parser.cs:733`). Any expression that evaluates to a string at runtime is accepted, exactly matching `import`'s grammar. This delivers feature parity between `import` and `export` for dynamic module paths.
- `from` is consumed via the existing contextual-identifier path (`ConsumeIdentifier("from", …)` at `Parser.cs:725`). No new soft-keyword machinery is required for `from`.
- The disambiguation between `export { … };` (block, parent feature) and `export { … } from <expr>;` (re-export, this feature) happens **after** the closing `}` — peek for the contextual `from` identifier vs `;`.
- The disambiguation between `export <expr> as <id>;` (re-export path form) and `export` used as an ordinary identifier in an expression statement (e.g., `export(args);`, `export.foo = 1;`) lives in `IsExportKeyword()`. See "Soft-keyword disambiguation" below.

#### Soft-keyword disambiguation (path form)

`export` is a soft keyword: it must continue to parse as a plain identifier when used as one (e.g., `let export = fn(x) => x;` followed by `export(42);`). Today the follow-set in `IsExportKeyword` is a finite set of tokens (`fn`, `const`, `struct`, `enum`, `interface`, `let`, `extend`, `import`, `{`, plus `async fn`). The new path form `export <expression> as <id>;` cannot be recognised by a constant-bounded lookahead because the path is an arbitrary expression.

The disambiguation rule for the path form is:

> `export` activates as a keyword (path branch) iff, starting at the token immediately after `export`, there exists a token sequence ending in `As Identifier Semicolon` at brace/paren/bracket depth zero before the next statement-terminator semicolon at depth zero, and the first token after `export` is an **expression starter** (per `IsExpressionStarter`).

In practice this is implemented as a one-pass scan from `_current + 1`:

1. Require `IsExpressionStarter(_tokens[_current + 1].Type)`. If not, fall back to identifier semantics (i.e., `IsExportKeyword` returns `false`).
2. Walk forward tracking `()`, `[]`, `{}` depth. Stop at the first `Semicolon` token at depth 0, or at EOF.
3. Activate the keyword iff the three tokens immediately before that stopping semicolon are `As Identifier Semicolon`.

This is a bounded-effort scan (statement-local; never crosses a depth-0 `;`). It is unambiguous because:

- An ordinary expression statement starting with `export` cannot end in `as <Identifier> ;` — `as` is a reserved keyword used only for casts (`expr as Type`) and import aliases. A cast's right-hand side is a type, not an identifier followed by `;`. (Verify against current `Expression()` precedence chain in Phase 2A — see Open Question Q4.)
- The brace-depth tracking ensures a nested `{ a as b; }` inside an object literal or block expression does not falsely trigger.

The block form (`export { … } from …`) keeps its existing disambiguation: after the closing `}`, peek for the contextual `from` identifier vs `;`. The path form requires no `from`; the lookahead key is `as <Identifier> ;` at the trailing edge.

The path-form lookahead replaces (does not extend) the old "next token is `TokenType.String`" gate proposed in the earlier draft. Conservative string-only restriction is removed; expressions are first-class for module paths, matching `import`.

Examples:

```stash
// 1) Namespace re-export
export "lib/data.stash" as data;

// 2) Selective named re-export
export { Color, Size, Direction } from "lib/types.stash";

// 3) An index.stash barrel file (the motivating pattern)
export "lib/data.stash" as data;
export "lib/io.stash"   as io_helpers;
export { Color, Size, Direction } from "lib/types.stash";
export { encode, decode }          from "lib/codec.stash";
```

Disallowed (compile-time error):

```stash
export * from "lib/x.stash";                 // SA0822 wildcard re-export
export { foo as f } from "lib/x.stash";      // parser error — alias deferred
export {} from "lib/x.stash";                // SA0823 empty re-export list
export "lib/x.stash";                        // parser error — missing 'as <alias>'
export "lib/x.stash" as { d };               // parser error — unrecognised form
export "lib/x.stash" as data;                // OK — but if `data` collides with an
                                             //   existing top-level binding, SA0824 duplicate-binding
```

### Semantics

#### Desugaring (the load-bearing decision)

Both new forms desugar to existing import semantics plus an export-set contribution. The desugaring is **conceptual** (described in the spec; the compiler does the work directly) — there is no AST rewrite step.

| Surface form | Equivalent existing code |
| --- | --- |
| `export <path-expr> as data;` | `import <path-expr> as data;` plus `data` added to the chunk's `ModuleExports.Names`. The alias `data` is **also** bound as a local namespace in the current module — usable inside the same file as `data.foo`, etc. |
| `export { foo, bar } from <path-expr>;` | `import { foo, bar } from <path-expr>;` plus `foo` and `bar` added to the chunk's `ModuleExports.Names`. The names `foo` and `bar` are **also** bound as locals in the current module — usable directly inside the same file. |

#### Local binding semantics (same-module use)

A re-export statement is **both** a local binding and an export-set contribution. This follows directly from the desugaring above: because the compiler emits the same `Import`/`ImportAs` instructions a hand-written form would emit, the alias / selected names land in the current module's globals dictionary exactly as they would after a plain `import`. The new statements therefore:

1. **Bind the alias (or selected names) as locals.** `export "lib/x.stash" as x;` lets the same file write `x.foo()` immediately after. `export { Color } from "lib/types.stash";` lets the same file write `let c = Color.Red;`.
2. **Add the same names to `Chunk.Exports.Names`.** Downstream importers see them on the re-exporting module.
3. **Participate in normal scope-lookup rules.** Resolution is identical to a hand-written `import ... as name;` or `import { ... } from "..."` — same scope, same depth, same SemanticResolver behavior.

Rationale: users expect feature parity with `import`. An author migrating from the two-line pattern `import "lib/x.stash" as x; export { x };` to the one-line `export "lib/x.stash" as x;` would be surprised if the latter lost the ability to use `x` in the same file. The desugaring already provides this naturally; we make it an explicit guarantee.

Diagnostic implications:

- **Unused-symbol diagnostics:** a re-exported binding counts as "used" because its name is in the module's export set. A name that is both re-exported and referenced inside the module is also used; a name that is only re-exported is still used. A name that is *neither* in the export set nor referenced cannot occur via this feature — every re-exported name is in the export set by definition.
- **SA0814 (redundant pair):** the desugaring equivalence makes this hint precise. `import { x } from "p"; export { x };` is functionally identical to `export { x } from "p";` in both observable runtime behavior (the export set and the local binding `x`) and source-side IDE behavior (hover, go-to-def). Suggesting the rewrite is therefore lossless.
- **Duplicate binding (SA0824):** unchanged. If the alias of `ExportModuleAsStmt` collides with another top-level binding, SA0824 fires — same rule that would have fired for an equivalent `import ... as alias;`.

Consequences of this strategy (each verified against the existing code):

1. **No new opcodes.** The compiler emits the same `Import`/`ImportAs` instructions it would emit for the equivalent hand-written form. The VM dispatch loop is untouched. This preserves the size constraint in `Stash.Bytecode/CLAUDE.md`.
2. **No bytecode-format bump.** `Chunk.Exports` already carries a `ModuleExports` record whose `Names` is an `IReadOnlySet<string>`. The re-exported names are added to that set the same way locally-defined exports are. The serializer (`BytecodeWriter`/`BytecodeReader`) needs **zero** changes.
3. **Runtime filter is already correct.** `BuildExportedEnvironment` (`VirtualMachine.Modules.cs:140`) copies every entry in `globals` whose key is in `exports.Names`. The desugared `import` puts `data` / `foo` / `bar` in the re-exporting module's globals; the existing filter passes them through to importers.
4. **Transitive re-export chains work for free.** When A re-exports B which re-exports C: A's `LoadModule` runs B's chunk → B's `LoadModule` runs C's chunk → both filters chain naturally. The module cache keys on resolved path, so a shared transitive target is loaded once.
5. **Symbol-kind preservation is automatic at runtime.** The runtime value of a re-exported function is the same `StashValue` (a function); a re-exported namespace is the same `StashNamespace`. LSP/analyzer get richer treatment — see "Static analysis" below.

#### Parser

Extend `ExportDeclaration()` (`Parser.cs:750`) with two new branches:

- **Block branch (already exists)**: after parsing `export { name, name }`, peek the token after `}`. If it is the contextual identifier `from`, consume it, parse the path expression, consume `;`, and produce an **`ExportFromStmt`**. Otherwise, the existing behavior produces `ExportBlockStmt` as today.
- **Path branch (new)**: at `ExportDeclaration` entry, after the existing reserved-form checks (`Let`, `Extend`, `Import`, `LeftBrace`, declaration-site `Fn`/`Const`/`Struct`/`Enum`/`Interface`, `async fn`), parse `expression "as" identifier ";"` and produce **`ExportModuleAsStmt`**. This path is reached only when `IsExportKeyword` already returned `true` via the new path-form rule (see "Soft-keyword disambiguation" in §Surface), so the trailing `as <id> ;` shape is guaranteed.

The follow-set in `IsExportKeyword()` (`Parser.cs:2875`) is **extended** with a path-form rule: when the next token is an expression starter, scan forward at depth 0 to the next `;` and activate the keyword iff the final three tokens are `As Identifier Semicolon`. This mirrors the parity requirement: any expression `import` accepts for a module path, `export ... as name;` also accepts.

Wildcard form `export * from "p";` is rejected with a clear parser-side error that names `SA0822`.

Two new AST node types in `Stash.Core/Parsing/AST/`:

```csharp
public sealed class ExportModuleAsStmt : Stmt
{
    public Token ExportKeyword { get; }    // soft-keyword token
    public Expr Path { get; }              // module path expression
    public Token AsKeyword { get; }
    public Token Alias { get; }            // local + exported name
    public ExportModuleAsStmt(Token export, Expr path, Token asKw, Token alias, SourceSpan span);
    public override T Accept<T>(IStmtVisitor<T> v) => v.VisitExportModuleAsStmt(this);
}

public sealed class ExportFromStmt : Stmt
{
    public Token ExportKeyword { get; }
    public List<Token> Names { get; }       // must be non-empty (SA0823 if empty)
    public Token FromKeyword { get; }       // the contextual `from` token
    public Expr Path { get; }
    public ExportFromStmt(Token export, List<Token> names, Token from, Expr path, SourceSpan span);
    public override T Accept<T>(IStmtVisitor<T> v) => v.VisitExportFromStmt(this);
}
```

Two nodes (not one) because validation rules and visitor handling are different — and it mirrors the existing `ImportStmt` / `ImportAsStmt` split.

#### Export set construction

`ModuleExportsBuilder.Build` (`Stash.Analysis/Models/ModuleExportsBuilder.cs`) is extended to recognize the two new statement kinds in the top-level walk:

| Statement | Contribution to `ModuleExports.Names` |
| --- | --- |
| `ExportModuleAsStmt` | The alias identifier. Also adds to the **top-level index** under `SymbolKind.Namespace` with `isImport: true` (mirrors `ImportAsStmt`). |
| `ExportFromStmt` | Every name in `Names`. Adds to the **top-level index** under `SymbolKind.Namespace` (no kind info available locally) with `isImport: true`. |

Duplicate detection (`SA0808`) is unchanged: a name re-exported twice, or re-exported and locally exported, is a duplicate.

#### Diagnostics

New `DiagnosticDescriptors` entries (all in the SA08xx imports range, continuing from the parent feature's SA0805–SA0808):

| Code | Level | When | Message |
| --- | --- | --- | --- |
| `SA0809` | Error | Re-export of a name that is not in the source module's exported set | `Module '{0}' does not export '{1}'.` |
| `SA0810` | Error | Re-export cycle detected in the analyzer's module graph | `Re-export cycle detected: {0}.` |
| `SA0822` | Error | `export * from "path";` | `Wildcard re-export is not supported. Use 'export { name, ... } from "..."' to list names explicitly.` |
| `SA0823` | Error | `export {} from "path";` | `Empty re-export list. Use a regular import for side effects.` |
| `SA0824` | Error | Alias of `ExportModuleAsStmt` collides with another top-level binding | `Cannot re-export module as '{0}': a binding with that name already exists.` |
| `SA0814` | Information | `import { x } from "p"; export { x };` — redundant pair | `This import-export pair can be written as 'export { {0} } from "{1}"'.` |

`SA0814` is the only information-level addition; it is a hint, not a warning.

#### Compiler

In `Stash.Bytecode/Compilation/Compiler.Declarations.cs` (or a sibling file dedicated to import/module ops):

- `VisitExportModuleAsStmt(stmt)`: emit the **same** instruction sequence the compiler emits for `ImportAsStmt` with `path = stmt.Path`, `alias = stmt.Alias`. The export-set contribution is already in `Chunk.Exports` via the `ModuleExportsBuilder` run by the analysis pipeline.
- `VisitExportFromStmt(stmt)`: emit the **same** instruction sequence as `ImportStmt` with `names = stmt.Names`, `path = stmt.Path`. Export-set contribution likewise via the builder.

No changes to `Compiler.cs` infrastructure, register allocation, or the global slot allocator.

#### Runtime / VM

**Zero source-code changes.** The desugared instructions already do the right thing:

- `ExecuteImport` puts each selectively-imported name into the importer's globals dict.
- `ExecuteImportAs` puts the namespace alias into the importer's globals dict.
- `BuildExportedEnvironment` filters by `Chunk.Exports.Names` at module-load time, and those names now include the re-exported ones.

The VM phase exists purely to **prove** via end-to-end tests that the desugaring is correct in practice, including transitive chains, namespace re-export through three levels, and module cache sharing.

#### Static analysis

`ImportResolver` is extended to:

1. **Walk the new statement kinds**, computing module resolution for each `ExportFromStmt.Path` and `ExportModuleAsStmt.Path` the same way it walks `ImportStmt` and `ImportAsStmt`. This populates the analyzer's import graph.
2. **Validate selective re-export source filtering** (`SA0809`): for each name in `ExportFromStmt.Names`, if the source module's `Exports` is non-null and `HasExplicitExports`, the name must be in `Exports.Names`. If the source has `HasExplicitExports == false` (legacy), every top-level name is implicitly exported and the check passes.
3. **Detect re-export cycles** (`SA0810`): when the import-graph walk discovers a re-export edge that closes a cycle of re-export edges, emit `SA0810`. Ordinary import edges in the cycle don't count (they're allowed). The cycle detection runs on the subgraph of re-export edges only.
4. **Enrich `ExportEntry`** with an optional source-module path: re-exported names get `OriginPath != null`, locally-exported names get `OriginPath == null`. This lives in `Stash.Analysis/Models/ModuleExportsBuilder.cs` (the rich entry), not in the bytecode-layer `ModuleExports`. LSP uses this to drive hover and go-to-def.

#### LSP

Three small additions:

- **Hover** on a re-exported name in the importer shows the original declaration (via `ExportEntry.OriginPath`).
- **Go-to-definition** on a re-exported name jumps to the source module's declaration, not the `export { … } from` line.
- **Semantic tokens**: emit `keyword` for both the `export` token (already done by parent feature) and the contextual `from` identifier inside `ExportFromStmt`.

The "add missing import" code action (`CodeActionHandler.cs`) is unaffected — it lists exported names from a module, and re-exported names are members of the source module's export set just like locally-defined ones.

#### Formatter

`StashFormatter` learns two new `Visit` methods:

- `VisitExportModuleAsStmt`: `export <path> as <alias>;`
- `VisitExportFromStmt`: same Doc-combinator layout as the destructured `import { … } from <path>` printer — single-line if it fits, otherwise broken across lines with a trailing comma.

#### Playground / VS Code grammar

`from` is already highlighted in the import context. No additional tokenizer work is required for the new forms — they reuse the existing keyword set. We add a regression test that the new shapes lex/tokenise without error in both the Monarch (playground) and tmLanguage (VS Code) grammars.

### Implementation Path

```
Parser recognises new forms (ExportModuleAsStmt, ExportFromStmt)
    -> ModuleExportsBuilder collects re-exported names into Chunk.Exports
    -> ImportResolver validates source-side export membership (SA0809) and re-export cycles (SA0810)
    -> Compiler emits ordinary Import/ImportAs sequences (no new opcodes)
    -> VM runs unchanged; BuildExportedEnvironment filter passes re-exported names through
    -> LSP surfaces origin-module info for hover/go-to-def
    -> Formatter, tokenisers, docs.
```

The key invariant the path preserves: **the runtime view of a re-exported name is byte-for-byte identical to the equivalent hand-written `import + export {}` pair**.

## Acceptance Criteria

**End-to-end behaviour:**

1. A `lib/` index file using `export "lib/x.stash" as x;` and `export { a, b } from "lib/y.stash";` is loaded, and an importer of the index file sees `x`, `a`, and `b` with their correct runtime values.
2. Three-level transitive re-export chain (A re-exports B's `foo`; B re-exports C's `foo`) works at runtime: importing A's `foo` returns C's `foo`'s value.
3. Namespace re-export caches correctly: importing the same re-exporting module twice does not re-execute the target module.

**Failure path behaviour:**

4. `export { private_thing } from "lib/x.stash"` where `private_thing` is module-private in `lib/x.stash` produces `SA0809` and does not compile.
5. `export * from "lib/x.stash"` produces `SA0822`.
6. `export {} from "lib/x.stash"` produces `SA0823`.
7. Re-export cycle `A -> B -> A` produces `SA0810` from the analyzer.

**Cross-entrypoint behaviour:**

8. Hover on `x` after `import { x } from "index.stash";` (where index re-exports `x` from `lib/x.stash`) shows the original declaration in `lib/x.stash`.
9. `.stashc` written by a compiler version with this feature and read by the same version round-trips a re-exporting module identically (no format bump means version compatibility is identical to the parent feature's contract).
10. The bytecode dispatch loop is unchanged (no new opcodes; verified by inspection of `OpCode.cs` and by `Disassembler` tests).

**Same-module local binding (D-12):**

11. After `export "lib/x.stash" as x;` in module M, code in the same module M can call `x.foo()` and see the same runtime value an `import "lib/x.stash" as x;` would have produced.
12. After `export { foo, bar } from "lib/types.stash";` in module M, code in the same module M can reference `foo` and `bar` directly (no namespace prefix), with the same runtime values an equivalent `import { foo, bar } from "lib/types.stash";` would have produced.

**Dynamic path expressions (D-9):**

13. `export some_const_path as x;` where `some_const_path` is a top-level `const` with a string value parses, compiles, and at module-load time loads the module pointed to by that string. Mirrors `import some_const_path as x;`.
14. `export { foo } from path_helper();` where `path_helper` is a function that returns a string parses and compiles. Runtime errors propagate identically to `import { foo } from path_helper();`.

**Soft-keyword disambiguation (D-9 corollary):**

15. The expression statement `export(42);` (where `export` is used as an ordinary identifier referencing a local variable) parses unchanged. So does `let cast = x as Foo;` and any other existing `as`-cast usage. Regression-test list lives in Phase 2A.

**Unused-import diagnostic (D-11):**

16. A module that only imports `lib/x.stash` and only re-exports symbols from it (no other references) does NOT produce an unused-import diagnostic. Same for the parent feature's `import { x } from "p"; export { x };` two-line pattern.

## Phases

The phase list lives in `plan.yaml`. There are **10 phases**, mirroring the parent `module-exports` plan's sizing:

| ID | Title |
| --- | --- |
| 2A | Parser + AST nodes for re-export forms |
| 2B | Six-visitor pass-through stubs |
| 2C | ModuleExportsBuilder extension + SA0822/SA0823/SA0824 in validator |
| 2D | Compiler: emit ordinary Import/ImportAs for new nodes |
| 2E | VM verification (tests only; zero source changes expected) |
| 2F | ImportResolver: SA0809 (source-export check), SA0810 (cycles), SA0814 (hint), ExportEntry origin path |
| 2G | LSP: hover/go-to-def via origin path + `from` semantic token |
| 2H | Formatter pretty-printing |
| 2I | Playground tokenizer + tmLanguage regression coverage |
| 2J | Docs + spec update + example |

## Open Questions

- **Q1 — RESOLVED 2026-05-18.** Path expression form. **Decision:** allow any expression `import` accepts. Feature parity with `import` is the design constraint. Implementation: replace the "next token is `TokenType.String`" gate with the lookahead rule documented in "Soft-keyword disambiguation" (scan to next depth-0 `;`; activate iff trailing tokens are `As Identifier Semicolon`). See decision **D-9** below.
- **Q2 — RESOLVED 2026-05-18.** SA0814 ships as an information-level hint only in this feature. A code action that auto-rewrites the redundant `import { x } from "p"; export { x };` pair into `export { x } from "p";` is a follow-up, tracked separately. See decision **D-10**.
- **Q3 — RESOLVED 2026-05-18.** Any `export` statement counts as a use of the imported name/module for unused-import diagnostics. This applies to the parent feature's `export { x };` block form (when `x` was imported) as well as to both new forms in this feature. A module that only imports and only re-exports a name MUST NOT trigger unused-import. See decision **D-11**.
- **Q4 — NEW.** Can the `Expression()` parser produce a sequence whose final three tokens at depth 0 are `As Identifier Semicolon` *without* being a valid re-export path expression? `as` is currently used as a cast operator (`expr as Type`); a cast's right-hand side is a type reference, not a bare `Identifier` followed by `;` at the same statement position. We believe this is safe — the disambiguation lookahead is unambiguous in current grammar — but Phase 2A must verify by inspection of `Expression()` and `as`-cast precedence and add a regression test exercising `let x = y as Foo;` parsing unchanged. If a corner case is found, the fallback is to require the path expression to be one of: a string literal, a parenthesised expression, an identifier reference, a function call, a member access — i.e., the empirical subset that `import` paths actually take in the wild. Track the verification result in this Open Question and either close or amend.
- **Q5 — NEW.** Should `examples/packages/` (the existing barrel example used by the parent feature) be migrated to the new syntax in this feature, to provide a real-world smoke test? The "Non-Goals" section explicitly defers migration. **Working answer:** keep deferred; ship a fresh `examples/reexport_barrel.stash` in Phase 2J and migrate `examples/packages/` in a follow-up.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-18 | **D-1**: Desugar both new forms to ordinary Import/ImportAs at compile time, with an export-set contribution. No new opcodes, no `.stashc` format bump. | Preserves the dispatch-loop size constraint (`Stash.Bytecode/CLAUDE.md`). Preserves the parent feature's runtime model untouched. Transitive chains and module caching ride on existing machinery. |
| 2026-05-18 | **D-2**: Two AST nodes (`ExportModuleAsStmt`, `ExportFromStmt`) instead of one. | Validation rules differ; mirrors the existing `ImportStmt`/`ImportAsStmt` split; every visitor cleanly dispatches without an inner-discriminator switch. |
| 2026-05-18 | **D-3**: No `export { x as y } from "p";` alias form. | Symmetric with parent feature's D-6 deferral of local-export aliasing. Will add both forms in a future single PR. |
| 2026-05-18 | **D-4**: No `export * from "p";`. | Matches Stash's principled rejection of wildcard imports — name-collision opacity is the same hazard. |
| 2026-05-18 | **D-5**: Re-export cycle detection lives in the analyzer (`SA0810`), not the runtime. | Cycles in the re-export subgraph are unambiguous bugs (unlike general import cycles, which sometimes work). The analyzer has the full graph; the runtime has only the per-load view. |
| 2026-05-18 | **D-6**: Selective re-export source check (`SA0809`) treats a source module with `HasExplicitExports == false` as exporting every top-level name. | Matches the parent feature's runtime behavior: legacy modules expose everything. |
| 2026-05-18 | **D-7**: `ExportEntry` (analysis layer) gains an optional `OriginPath`. Bytecode-layer `ModuleExports.Names` remains `IReadOnlySet<string>`. | LSP needs origin information; the runtime doesn't. Keeping origin info out of bytecode preserves format compatibility. |
| 2026-05-18 | **D-8**: This feature is orthogonal to `exports-private-default`. | The desugar strategy means re-exports work identically whether the default is "public" or "private." |
| 2026-05-18 | **D-9**: The path expression in both new forms accepts any expression `import` accepts (not just string literals). | Feature parity with `import` dynamic-path support. Authors moving between `import "p" as x;` and `export "p" as x;` should not lose grammar capability. Soft-keyword disambiguation uses a depth-tracked scan for trailing `as Identifier ;` — see brief §Surface. |
| 2026-05-18 | **D-10**: SA0814 ships as info-level hint only; auto-rewrite code action is deferred. | Hint provides discoverability without intrusion. Code-action work is independently scoped and can land in a separate small PR once the feature is stable. |
| 2026-05-18 | **D-11**: Re-export statements count as uses for the unused-import diagnostic. | Consistent with the desugaring equivalence (`export { x } from "p"` ≡ `import { x } from "p"; export { x };`). A barrel-file module that re-exports symbols without referencing them locally would otherwise be flooded with false positives. |
| 2026-05-18 | **D-12**: A re-export statement binds its alias / selected names as locals in the same module, in addition to contributing them to the export set. | This falls out of the desugaring (`import` already binds locals). Making it an explicit guarantee preserves feature parity with `import` and removes a footgun where users would be surprised that `export "lib/x.stash" as x;` doesn't let them write `x.foo()` in the same file. |
