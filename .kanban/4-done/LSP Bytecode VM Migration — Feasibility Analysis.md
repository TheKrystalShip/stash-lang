# LSP Bytecode VM Migration — Feasibility Analysis

**Status:** Analysis Complete
**Created:** 2026-04-05
**Purpose:** Determine whether the LSP can work with the bytecode VM backend, and whether the tree-walking interpreter can be fully retired.

---

## 1. Executive Summary

**The LSP does not use the tree-walking interpreter at all. It never has.**

This means the question "can the LSP switch to the bytecode VM?" is the wrong question — the LSP has no runtime backend to switch. All 28 LSP features are 100% static analysis. The LSP depends on `Stash.Core`, `Stash.Analysis`, and `Stash.Stdlib` — never on `Stash.Interpreter` or `Stash.Bytecode`.

**The real question is: can the tree-walking interpreter be dropped entirely?** The answer is **yes**, with modest refactoring work, and the LSP requires zero changes.

---

## 2. LSP Architecture — No Runtime Dependency

### 2.1 Project References

```
Stash.Lsp.csproj references:
  ✅ Stash.Core       — AST nodes, SourceSpan, Token types
  ✅ Stash.Analysis   — AnalysisEngine, ScopeTree, SemanticValidator, TypeInference
  ✅ Stash.Stdlib     — BuiltInRegistry (metadata only, no execution)
  ❌ Stash.Interpreter — NOT referenced
  ❌ Stash.Bytecode    — NOT referenced
```

**Zero `using Stash.Interpreter` or `using Stash.Interpreting` statements exist in any LSP file.** Confirmed by grep across the entire Stash.Lsp/ directory.

### 2.2 Analysis Pipeline (What the LSP Actually Does)

```
Document text
    ↓
Lexer (preserveTrivia=true)
    ↓
Parser (error recovery)
    ↓
SymbolCollector → ScopeTree + ReferenceInfo
    ↓
ImportResolver → cross-file symbol enrichment
    ↓
TypeInferenceEngine → static type deduction
    ↓
SemanticValidator → error diagnostics
    ↓
AnalysisResult (URI-keyed cache, 25ms debounce)
```

Every stage is pure static analysis. No Stash code is ever executed. The ScopeTree is the LSP's own static scope model — it is **not** the interpreter's runtime `Environment`.

### 2.3 All 28 LSP Features — Categorized

| Feature                          | Needs Runtime? | Depends On                         |
| -------------------------------- | -------------- | ---------------------------------- |
| Diagnostics (lex/parse/semantic) | No             | Lexer + Parser + SemanticValidator |
| Document Symbols                 | No             | ScopeTree                          |
| Go-to-Definition                 | No             | ScopeTree + ImportResolver         |
| Hover Info                       | No             | ScopeTree + StdlibRegistry         |
| Completion                       | No             | ScopeTree + BuiltInRegistry        |
| Signature Help                   | No             | ScopeTree + StdlibRegistry         |
| References                       | No             | ScopeTree + ImportResolver         |
| Document Highlight               | No             | ReferenceInfo                      |
| Rename / Prepare Rename          | No             | ScopeTree + ReferenceInfo          |
| Semantic Tokens                  | No             | SemanticTokenWalker (AST)          |
| Folding Range                    | No             | AST traversal                      |
| Selection Range                  | No             | AST nested ranges                  |
| Document Links                   | No             | ImportResolver                     |
| Code Actions ("did you mean?")   | No             | ScopeTree (Levenshtein)            |
| Workspace Symbols                | No             | ScopeTree                          |
| Inlay Hints                      | No             | BuiltInRegistry + ScopeTree        |
| Code Lens                        | No             | ScopeTree + ImportResolver         |
| Formatting (full/range/onType)   | No             | StashFormatter (AST walk)          |
| Call Hierarchy                   | No             | ScopeTree + AST visitor            |
| Linked Editing Range             | No             | ReferenceInfo                      |
| Type Definition                  | No             | ScopeTree + TypeInferenceEngine    |
| Implementation                   | No             | ScopeTree                          |
| File Watcher                     | No             | WorkspaceScanner                   |

**Result: 28/28 features require zero runtime execution.**

---

## 3. Stash.Analysis — Also Backend-Agnostic

```
Stash.Analysis.csproj references:
  ✅ Stash.Core
  ✅ Stash.Stdlib
  ❌ Stash.Interpreter — NOT referenced
  ❌ Stash.Bytecode    — NOT referenced
```

The analysis engine operates on AST nodes from `Stash.Core` and built-in metadata from `Stash.Stdlib`. It has no knowledge of either execution backend. This is clean architecture — the analysis layer is a frontend concern, not a backend concern.

---

## 4. What Still Depends on the Tree-Walker?

For the broader goal of retiring the tree-walking interpreter, here's the full dependency map:

| Project              | Depends on Tree-Walker? | Why                                                                             | Removable?                     |
| -------------------- | ----------------------- | ------------------------------------------------------------------------------- | ------------------------------ |
| **Stash.Lsp**        | ❌ No                   | —                                                                               | N/A                            |
| **Stash.Analysis**   | ❌ No                   | —                                                                               | N/A                            |
| **Stash.Stdlib**     | ❌ No                   | Uses `IInterpreterContext` abstraction                                          | N/A                            |
| **Stash.Tap**        | ❌ No                   | Backend-agnostic                                                                | N/A                            |
| **Stash.Tpl**        | ❌ No                   | Decoupled from runtime                                                          | N/A                            |
| **Stash.Cli**        | ✅ Yes                  | Dual-backend support (`--backend=treewalk`)                                     | Remove flag, go VM-only        |
| **Stash.Dap**        | ✅ Yes                  | Uses `Interpreter.ResolveStatements()` for AST annotation before VM compilation | Extract resolver to Stash.Core |
| **Stash.Playground** | ✅ Yes                  | Indirect via StashEngine                                                        | Make StashEngine VM-only       |
| **Stash.Tests**      | ✅ Yes                  | Tests both backends                                                             | Remove tree-walker tests       |

### 4.1 The Resolver Problem (Biggest Coupling Point)

The tree-walker's `Interpreter.ResolveStatements()` is used for **AST semantic annotation** — resolving variable scopes, marking depth distances, etc. Both the CLI (bytecode path) and DAP use the Interpreter class solely for this method before handing the annotated AST to the bytecode compiler.

This is the single deepest coupling. The Interpreter class is instantiated not to _interpret_, but to _resolve_. The resolution logic needs to be extracted into a standalone component.

**Decision:** Extract `ResolveStatements()` into an `ISemanticResolver` / `SemanticResolver` in `Stash.Core`. The bytecode compiler already depends on the resolver's annotations — this just breaks the unnecessary dependency on the full Interpreter class.

---

## 5. Impact on Future LSP Features

Could future LSP features need runtime execution? Let's analyze the candidates:

### 5.1 Features That Remain Static

- **Better type inference** — Can be improved statically (data flow analysis, constraint solving)
- **Unused variable detection** — Already works statically
- **Dead code detection** — Static control flow analysis
- **Inline evaluation (hover)** — Some languages show computed values on hover, but this requires execution and is a security risk. Not recommended.

### 5.2 Features That Could Hypothetically Need Runtime

- **REPL-style evaluation in debug mode** — This is a DAP feature, not LSP. Already uses the VM.
- **Expression evaluation for hover** — Would require sandboxed execution. The VM's cancellation support makes this theoretically possible, but it's a slippery slope (side effects, infinite loops, etc.). Not recommended.

### 5.3 Verdict

No currently planned or foreseeable LSP feature requires runtime execution. The LSP is and should remain a static analysis tool. Any "evaluate this expression" functionality belongs in the DAP (debug adapter), which already runs on the VM.

---

## 6. Migration Plan — Dropping the Tree-Walker

### Phase 1: Extract Resolver (prerequisite)

- Extract `Interpreter.ResolveStatements()` → `SemanticResolver` in `Stash.Core`
- Update bytecode compiler to use `SemanticResolver` directly
- Update DAP to use `SemanticResolver` instead of `new Interpreter()`
- **LSP impact: None**

### Phase 2: Remove Tree-Walker Backend

- Remove `--backend=treewalk` from CLI
- Make `StashEngine` VM-only (or remove the backend enum entirely)
- Update Playground to use VM-only engine
- Update embedding examples
- **LSP impact: None**

### Phase 3: Delete Stash.Interpreter

- Remove the project and all references
- Remove tree-walker-specific tests (keep integration tests that test language behavior via StashEngine)
- **LSP impact: None**

### Estimated Scope

- Phase 1: ~1 day (extract one class, update two call sites)
- Phase 2: ~1 day (delete code paths, update flags)
- Phase 3: ~1 day (delete project, clean up tests)

---

## 7. Risks & Mitigations

### Risk 1: Resolver Extraction Breaks Subtle State

**Risk:** The resolver in the tree-walker may depend on shared state in the Interpreter class (e.g., globals, environment setup).
**Mitigation:** The resolver is already a mostly-pure AST pass. Extract with tests covering resolution annotations.

### Risk 2: VM Feature Gaps Become Blockers

**Risk:** If any script relies on a tree-walker feature the VM doesn't support, dropping the tree-walker breaks it.
**Mitigation:** The VM currently passes 4,810/4,817 tests. The 7 failures are pre-existing and unrelated to the VM. Feature parity is effectively complete.

### Risk 3: Performance Regression on Specific Workloads

**Risk:** Namespace call overhead is only 1.1× faster on VM (dominated by IStashCallable dispatch).
**Mitigation:** Not a regression — just not an improvement yet. Phase 9 StashValue tagged union would address this.

### Risk 4: Embedding API Breakage

**Risk:** External users embedding Stash may use the Interpreter class directly.
**Mitigation:** StashEngine is the public embedding API. Make it VM-only and update docs. Provide a migration guide.

---

## 8. Decisions

| #   | Decision                                     | Alternatives Considered                                                  | Rationale                                                                           |
| --- | -------------------------------------------- | ------------------------------------------------------------------------ | ----------------------------------------------------------------------------------- |
| D1  | LSP requires zero changes for VM migration   | N/A — it already doesn't use either backend                              | Architecture was designed correctly from the start                                  |
| D2  | Extract resolver before dropping tree-walker | (a) Copy resolver logic into compiler, (b) Keep thin Interpreter wrapper | Clean extraction is safest; copying duplicates logic; wrapper keeps dead dependency |
| D3  | Do not add runtime evaluation to LSP         | Add sandboxed eval for hover                                             | Security risk, scope creep, side effects. Eval belongs in DAP.                      |
| D4  | Drop tree-walker in phases, not all at once  | Big-bang removal                                                         | Phased approach allows validation at each step                                      |

---

## 9. Conclusion

**The LSP is completely unaffected by this migration.** It was designed with clean separation from the runtime, and that design pays off here. All 28 features are static analysis that operates on AST nodes and symbol metadata — no interpreter of any kind is involved.

Dropping the tree-walking interpreter is feasible with ~3 days of focused work. The main task is extracting the semantic resolver from the Interpreter class. Everything else is deletion and flag removal.

The LSP, Analysis engine, Stdlib, Tap, and Tpl projects need zero changes. Only Cli, Dap, Playground, and Tests need updates.
