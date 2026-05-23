# Formatter Modularization — Architecture Analysis

**Status:** Backlog — Analysis
**Created:** 2026-04-11
**Purpose:** Analyze industry patterns for formatter architecture and recommend a modularization strategy for StashFormatter.

---

## 1. Current State

`StashFormatter.cs` is a **~1,476-line monolithic class** that implements both `IStmtVisitor<int>` and `IExprVisitor<int>`. It handles every concern in a single file:

| Concern                          | Lines | Methods                                                                                  |
| -------------------------------- | ----- | ---------------------------------------------------------------------------------------- |
| Configuration & init             | ~40   | 2 constructors                                                                           |
| Whitespace control (max-upgrade) | ~30   | `Space`, `SoftNewLine`, `NewLine`, `BlankLine`                                           |
| Indentation & pending flush      | ~50   | `IndentString`, `WritePending`                                                           |
| Token cursor management          | ~30   | `NextIs`, `EmitToken`                                                                    |
| Doc IR mark/wrap helpers         | ~20   | `Mark`, `WrapFrom`                                                                       |
| Trivia interleaving              | ~100  | `FlushTriviaBefore`, `ProcessTrivia`, `FlushRemainingTrivia`, `FormatBlockComment`       |
| Operator/declaration detection   | ~15   | `IsCompoundOperator`, `NextIsCompoundOperator`, `IsDeclaration`                          |
| Public API                       | ~190  | `Format`, `FormatRange`, `NormalizeEol`, `DetectSourceEol`, `SortFormattedImports`, etc. |
| Statement visitors               | ~420  | 22 `VisitXxxStmt` methods                                                                |
| Expression visitors              | ~490  | 26 `VisitXxxExpr` methods                                                                |

**Existing modularity:**

- The Doc IR (`Doc.cs`, `DocPrinter.cs`, `DocDebugPrinter.cs`) is already cleanly separated in `Formatting/`.
- `FormatConfig` is a separate model.
- `Stash.Format/` (CLI tool) is cleanly separated from the core formatter.

**What's monolithic:** The formatting logic for every AST node type lives in a single class with shared mutable state (`_docs`, `_indent`, `_cursor`, `_triviaCursor`, `_pending`, `_lastCodeToken`, `_ignoreLines`).

---

## 2. Industry Patterns — How Real Formatters Are Organized

### 2.1 Biome (Rust) — "One File Per AST Node"

Biome (formerly Rome) uses the most granular modular pattern in the industry:

```
biome_js_formatter/src/
├── js/
│   ├── statements/
│   │   ├── block_statement.rs
│   │   ├── if_statement.rs
│   │   ├── for_statement.rs
│   │   ├── return_statement.rs
│   │   └── ... (one file per statement type)
│   ├── expressions/
│   │   ├── array_expression.rs
│   │   ├── call_expression.rs
│   │   ├── binary_expression.rs
│   │   └── ... (one file per expression type)
│   ├── declarations/
│   └── bindings/
├── utils/          (shared formatting helpers)
├── comments.rs     (comment handling)
├── context.rs      (formatting context/state)
├── trivia.rs       (trivia handling)
└── separated.rs    (list separator logic)
```

**Key design:**

- Each AST node implements a `FormatNode` trait in its own file (~30–150 lines each).
- A `FormatContext` carries shared state (indentation, options).
- Comment handling is a dedicated module.
- Trivia management is a dedicated module.
- Shared utilities (`utils/`) for common patterns like formatting argument lists, binary chains, assignment right-hand sides.

**Strengths:** Maximum single-responsibility. Adding a new AST node = adding one file. Easy to review, blame, and test in isolation.
**Weaknesses:** Many small files. Shared logic between similar nodes (e.g., all binary-like expressions) must be extracted to utils or risk duplication.

### 2.2 Prettier (JavaScript) — "Big Print Function + Doc IR"

Prettier's architecture splits the problem into well-defined phases:

```
Source → Parser → AST → Printer (print function) → Doc IR → DocPrinter → String
```

The printer is a single large `print(path, options, print)` function that switches on `node.type`. The key modularity comes from:

1. **Doc IR as the modularity boundary** — The printer's only job is to produce Docs. The Wadler-Lindig algorithm (DocPrinter) is entirely separate.
2. **Comment handling is a separate subsystem** — `handleComments` has three sub-handlers (`ownLine`, `endOfLine`, `remaining`).
3. **Embedded language support** via the `embed` function — separate from the main print path.
4. **Utility functions** are extracted to `src/utils/` for common patterns.

**Strengths:** Simple mental model (one function, one switch). Doc IR provides a clean boundary. Proven at massive scale.
**Weaknesses:** The print function itself is still monolithic (thousands of lines). The switch statement can become a coordination nightmare.

### 2.3 dartfmt (Dart) — "Visitor + ChunkBuilder + LineSplitter Pipeline"

dartfmt (by Bob Nystrom) uses a three-stage pipeline:

```
AST → SourceVisitor → ChunkBuilder → (Chunks, Rules, Spans) → LineSplitter → String
```

1. **SourceVisitor** — AST traversal that emits chunks, rules, and spans. This is the "style definition" layer (equivalent to Stash's visitor methods). Still monolithic by design — "there's no rocket science here, but there are a lot of hairy corner cases."
2. **ChunkBuilder** — The formatting state machine that manages chunk construction, indentation, and nesting. Analogous to Stash's `WritePending`/`EmitToken`/`Mark`/`WrapFrom` infrastructure.
3. **LineSplitter** — The optimization backend that finds the best line-breaking configuration. Analogous to Stash's `DocPrinter`.

**Key insight from Nystrom:** "The IR is structured to be the right data structure for the algorithm the back end wants to use." The modularity comes from separating _what_ to format (visitor) from _how_ to emit (builder) from _where_ to break (splitter).

### 2.4 rustfmt — "Visitor + Rewrite Trait + Shape"

rustfmt uses a pattern where each syntactic construct implements a `Rewrite` trait:

```rust
trait Rewrite {
    fn rewrite(&self, context: &RewriteContext, shape: Shape) -> Option<String>;
}
```

- `Shape` carries the available width and indentation at a point.
- `RewriteContext` carries formatting options and source map info.
- Each construct's `rewrite` returns `None` if it can't fit, letting the caller try a different layout.
- Organized into files by construct category: `expr.rs`, `items.rs`, `types.rs`, `patterns.rs`, etc.

### 2.5 Black (Python) — "Line + Visitor + Transformers Pipeline"

Black uses a multi-pass pipeline:

```
Source → AST → Line objects → Transform passes → Merge lines → String
```

1. **LineGenerator** turns the AST into logical `Line` objects (one per statement).
2. **Transform passes** operate on Line objects independently: normalize strings, add/remove trailing commas, handle magic trailing comma, etc.
3. **Line merging** attempts to combine lines that fit within the column limit.

**Key modularity:** Each transform is an independent function that takes lines and returns lines. New formatting rules = new transform functions. The pipeline is explicit and ordered.

---

## 3. Pattern Taxonomy

From the survey, formatter modularization falls into **three architectural families**:

| Family                        | Representatives | Core Idea                                                                         | Modularity Unit                  |
| ----------------------------- | --------------- | --------------------------------------------------------------------------------- | -------------------------------- |
| **A. Per-Node Dispatch**      | Biome, rustfmt  | Each AST node type has its own formatting implementation in a separate file/class | One file per node type           |
| **B. Phased Pipeline**        | Black, dartfmt  | Formatting is split into sequential phases that transform representations         | One module per phase             |
| **C. Monolith + IR Boundary** | Prettier        | Single print function, but clean separation between style emission and rendering  | Functions/utils extracted ad-hoc |

These aren't mutually exclusive. Biome uses per-node dispatch _and_ a Doc IR backend. dartfmt uses a pipeline where the first phase is still a monolithic visitor.

---

## 4. Analysis: What Applies to StashFormatter?

### 4.1 What Stash Already Has (and Should Keep)

Stash's formatter already uses several proven patterns:

1. **Wadler-Lindig Doc IR** — This is the same core algorithm as Prettier and Biome. `Doc.cs` + `DocPrinter.cs` are already cleanly separated. **Keep as-is.**
2. **Token cursor with trivia interleaving** — This is a sound approach (similar to dartfmt's SourceVisitor). The token cursor ensures tokens are emitted in source order.
3. **Max-upgrade whitespace semantics** — A clean abstraction for managing whitespace precedence. Well-isolated.
4. **Mark/Wrap pattern** — Elegant retroactive indentation. Not common in other formatters but it works well with the Doc IR.

### 4.2 What Should Be Modularized

The 1,476-line monolith has **five clearly separable concerns**:

#### Concern 1: Formatting Engine Infrastructure

The token cursor, trivia handling, whitespace semantics, mark/wrap helpers, and pending flush. These are the _mechanisms_ that AST node formatters use to emit Docs.

Currently: ~250 lines of private methods interleaved with everything else.
Should be: A `FormatterContext` or `FormatEmitter` class that encapsulates all emission machinery.

#### Concern 2: Statement Formatting

22 `VisitXxxStmt` methods (~420 lines) defining how each statement type is formatted.

Currently: 22 methods directly in `StashFormatter`.
Should be: Either per-node classes (Biome style) or a dedicated `StatementFormatter` module.

#### Concern 3: Expression Formatting

26 `VisitXxxExpr` methods (~490 lines) defining how each expression type is formatted.

Currently: 26 methods directly in `StashFormatter`.
Should be: Either per-node classes (Biome style) or a dedicated `ExpressionFormatter` module.

#### Concern 4: Public API / Orchestration

`Format`, `FormatRange`, `NormalizeEol`, `SortFormattedImports`, `EmitIgnoredStatement` — the entry points and post-processing.

Currently: ~190 lines in `StashFormatter`.
Should be: A thin `StashFormatter` facade that orchestrates the pipeline.

#### Concern 5: Trivia & Comment Handling

`FlushTriviaBefore`, `ProcessTrivia`, `FlushRemainingTrivia`, `FormatBlockComment` — comment preservation logic.

Currently: ~100 lines in `StashFormatter`.
Should be: A dedicated `TriviaHandler` or integrated into the emission infrastructure.

---

## 5. Recommended Architecture

### Option A: Partial Classes (Minimal Disruption)

Split `StashFormatter.cs` into partial classes by concern:

```
Visitors/
├── StashFormatter.cs                  (core: fields, constructors, Format(), FormatRange(), orchestration)
├── StashFormatter.Emission.cs         (EmitToken, WritePending, Mark, WrapFrom, Space/NewLine/BlankLine)
├── StashFormatter.Trivia.cs           (FlushTriviaBefore, ProcessTrivia, FlushRemainingTrivia, FormatBlockComment)
├── StashFormatter.Statements.cs       (all VisitXxxStmt methods)
├── StashFormatter.Expressions.cs      (all VisitXxxExpr methods)
```

**Pros:** Zero API changes. No new types. Same runtime behavior. Git blame preserved per-line. Lowest risk.
**Cons:** Doesn't actually decouple anything — all partial classes share the same mutable state. Doesn't enable independent testing. Purely a file organization improvement.

**Verdict:** Good first step for immediate readability, but insufficient for true modularity.

### Option B: Extracted Modules with Shared Context (Recommended)

Introduce a `FormatterContext` that encapsulates all emission machinery, then split node formatting into focused classes:

```
Formatting/
├── Doc.cs                             (existing — Doc IR nodes)
├── DocPrinter.cs                      (existing — Wadler-Lindig renderer)
├── DocDebugPrinter.cs                 (existing — debug output)
├── FormatterContext.cs                 (NEW — token cursor, trivia, whitespace, mark/wrap, indent)
├── TriviaHandler.cs                   (NEW — comment interleaving logic)
├── StatementFormatter.cs              (NEW — all VisitXxxStmt implementations)
├── ExpressionFormatter.cs             (NEW — all VisitXxxExpr implementations)
└── ImportSorter.cs                    (NEW — import sorting post-processor)

Visitors/
└── StashFormatter.cs                  (SLIM — public API facade, pipeline orchestration, delegates to modules)
```

**How it works:**

1. `StashFormatter.Format()` creates a `FormatterContext` with the token/trivia arrays, config, and ignore lines.
2. `StashFormatter` still implements `IStmtVisitor<int>` and `IExprVisitor<int>` (for AST dispatch), but each visitor method is a one-liner that delegates to `StatementFormatter` or `ExpressionFormatter`.
3. `StatementFormatter` and `ExpressionFormatter` receive `FormatterContext` and use its emission methods (`ctx.EmitToken()`, `ctx.Space()`, `ctx.NewLine()`, etc.).
4. `TriviaHandler` is owned by `FormatterContext` and handles all comment interleaving.
5. `ImportSorter` is a pure post-processing function.

**Sketch:**

```csharp
// FormatterContext.cs — encapsulates all emission state and operations
public sealed class FormatterContext
{
    private readonly List<Doc> _docs = new();
    private readonly Token[] _codeTokens;
    private readonly Token[] _triviaTokens;
    private readonly TriviaHandler _triviaHandler;
    private readonly FormatConfig _config;

    private int _cursor;
    private int _indent;
    private Pending _pending;

    // Emission primitives — used by statement/expression formatters
    public void Space() { ... }
    public void SoftNewLine() { ... }
    public void NewLine() { ... }
    public void BlankLine() { ... }
    public void EmitToken() { ... }
    public bool NextIs(TokenType type) { ... }
    public int Mark() { ... }
    public void WrapFrom(int mark, Func<Doc, Doc> wrapper) { ... }

    // Config access
    public bool TrailingComma => _config.TrailingComma;
    public bool BracketSpacing => _config.BracketSpacing;
    public bool SingleLineBlocks => _config.SingleLineBlocks;
    // ...

    // Finalize — flush remaining trivia and return Doc list
    public List<Doc> Finalize() { ... }
}

// StatementFormatter.cs — all statement formatting logic
public static class StatementFormatter
{
    public static void FormatVarDecl(VarDeclStmt stmt, FormatterContext ctx) { ... }
    public static void FormatFnDecl(FnDeclStmt stmt, FormatterContext ctx) { ... }
    public static void FormatIf(IfStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt) { ... }
    // ... one method per statement type
}

// ExpressionFormatter.cs — all expression formatting logic
public static class ExpressionFormatter
{
    public static void FormatLiteral(LiteralExpr expr, FormatterContext ctx) { ... }
    public static void FormatBinary(BinaryExpr expr, FormatterContext ctx, Action<Expr> formatExpr) { ... }
    public static void FormatCall(CallExpr expr, FormatterContext ctx, Action<Expr> formatExpr) { ... }
    // ... one method per expression type
}

// StashFormatter.cs — thin facade
public sealed class StashFormatter : IStmtVisitor<int>, IExprVisitor<int>
{
    private FormatterContext? _ctx;

    public string Format(string source) { /* orchestration pipeline */ }

    public int VisitVarDeclStmt(VarDeclStmt stmt)
    {
        StatementFormatter.FormatVarDecl(stmt, _ctx!);
        return 0;
    }

    public int VisitBinaryExpr(BinaryExpr expr)
    {
        ExpressionFormatter.FormatBinary(expr, _ctx!, e => e.Accept(this));
        return 0;
    }
    // ... same delegation pattern for all visitors
}
```

**Pros:**

- True separation of concerns — emission machinery, statement formatting, and expression formatting are independently testable.
- `FormatterContext` creates a single, well-defined interface between the infrastructure and the formatting logic.
- Adding a new AST node = add one method in `StatementFormatter` or `ExpressionFormatter` + one delegation line in `StashFormatter`.
- The `StashFormatter` facade stays small and focused on orchestration.
- `TriviaHandler` encapsulates the trickiest and most bug-prone code (comment positioning).
- Follows the same principle as the static analyzer refactor: extract concerns into dedicated classes with explicit contracts.

**Cons:**

- More types to create. Requires passing `FormatterContext` and callbacks everywhere.
- Some visitor methods need the ability to recurse into sub-nodes — this requires passing an `Action<Stmt>` / `Action<Expr>` callback (the `formatStmt` / `formatExpr` delegates above), which is slightly less elegant than direct `stmt.Accept(this)` calls.
- Migration effort: need to move ~900 lines of visitor bodies into new classes.

### Option C: Per-Node Classes (Biome Style)

One class per AST node, each implementing a `IFormatNode<T>` interface:

```
Formatting/
├── Nodes/
│   ├── Statements/
│   │   ├── FormatVarDeclStmt.cs
│   │   ├── FormatFnDeclStmt.cs
│   │   ├── FormatIfStmt.cs
│   │   └── ... (22 files)
│   ├── Expressions/
│   │   ├── FormatLiteralExpr.cs
│   │   ├── FormatBinaryExpr.cs
│   │   ├── FormatCallExpr.cs
│   │   └── ... (26 files)
```

**Pros:** Maximum granularity — perfect git blame, easy to find "how is X formatted?", trivial to add new nodes.
**Cons:** 48+ new files for single-method classes. Stash doesn't have the volume of node types that Biome does (Biome has hundreds). Overhead of coordinating 48 files outweighs the benefit at Stash's scale. The analysis-rule refactor works at 51 rules because rules are _independent_ — formatter nodes are _not_ (they share emission state and recursion patterns).

**Verdict:** Over-engineered for Stash's ~48 node types. The Biome pattern makes sense for Rust (where traits + separate files are idiomatic) and for Biome's ~200+ node types. At Stash's scale, grouping by category (Option B) is more appropriate.

---

## 6. Recommendation

**Option B (Extracted Modules with Shared Context)** is the right choice. It:

- Mirrors the successful Stash.Analysis refactor pattern (extract concerns, inject context).
- Matches the Prettier/dartfmt principle of "monolithic style definitions but modular infrastructure."
- Stays proportionate to Stash's complexity (~48 node types, not 200+).
- Creates testable units (you can test `StatementFormatter.FormatIf` by giving it a mock `FormatterContext`).
- Leaves the door open for further granularity later (Option C) if the language grows significantly.

### Suggested Implementation Order

1. **Phase 1: Extract `FormatterContext`** — Move token cursor, trivia handling, whitespace primitives, mark/wrap into a dedicated class. `StashFormatter` methods call through `_ctx`. No behavioral change.
2. **Phase 2: Extract `TriviaHandler`** — Move `FlushTriviaBefore`, `ProcessTrivia`, `FlushRemainingTrivia`, `FormatBlockComment` into a class owned by `FormatterContext`.
3. **Phase 3: Extract `StatementFormatter`** — Move all `VisitXxxStmt` bodies into static methods. `StashFormatter` becomes a thin delegation layer for statements.
4. **Phase 4: Extract `ExpressionFormatter`** — Same for expressions.
5. **Phase 5: Extract `ImportSorter`** — Move `SortFormattedImports`, `ExtractImportPath`, `SortImportNames` into a pure utility class.

Each phase can be a separate PR, each with its own test validation pass. At no point does the public API or formatted output change.

---

## 7. Comparison with Stash.Analysis Refactor

| Dimension          | Analysis Refactor                                | Proposed Formatter Refactor                                                              |
| ------------------ | ------------------------------------------------ | ---------------------------------------------------------------------------------------- |
| Monolith broken    | `SemanticValidator` (~2000+ lines?)              | `StashFormatter` (~1476 lines)                                                           |
| Module unit        | `IAnalysisRule` (one class per rule)             | Category-level classes (`StatementFormatter`, `ExpressionFormatter`)                     |
| ~Module count      | 51 rules                                         | 5–6 modules                                                                              |
| Context object     | `RuleContext` (immutable, passed per-invocation) | `FormatterContext` (mutable, shared per-format-call)                                     |
| Dispatch mechanism | `RuleRegistry` + type-indexed lookup             | `IStmtVisitor`/`IExprVisitor` + delegation                                               |
| Independence       | Rules are fully independent                      | Node formatters share emission state (intentional — formatting is inherently sequential) |

The key difference: analysis rules are embarrassingly parallel (each rule can run in isolation), while formatting is inherently sequential (each token must be emitted in order, with shared indentation/whitespace state). This is why per-node isolation (Option C) doesn't yield as much benefit for formatters as per-rule isolation does for linters.

---

## 8. Risks

| Risk                                    | Mitigation                                                                                                           |
| --------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Regression in formatted output          | Run full formatter test suite after each phase. Output must be byte-identical.                                       |
| Callback overhead for recursion         | Modern .NET inlines delegates well. Benchmark before/after to confirm no perf regression.                            |
| `FormatterContext` becomes a god object | Keep it focused on _emission primitives_. Formatting _decisions_ stay in `StatementFormatter`/`ExpressionFormatter`. |
| Lost IntelliSense locality              | Statement/expression formatters are in the same project; cross-references work fine.                                 |

---

## 9. Non-Goals

- **Not** rearchitecting the Doc IR or DocPrinter — these are already well-separated.
- **Not** switching to a completely different formatting algorithm (e.g., dartfmt's best-first search).
- **Not** adding new formatting capabilities. This is a pure structural refactor.
- **Not** changing the public API of `StashFormatter`.
