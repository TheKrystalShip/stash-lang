# Formatter — Rule-Based Architecture Redesign

**Status:** Backlog — Design
**Created:** 2025-04-15
**Purpose:** Replace the current imperative formatting approach (hardcoded `ctx.Space()`/`ctx.NewLine()` calls scattered across 40+ static methods) with a rule-based architecture where formatting decisions are declarative, context-aware, centrally registered, and easy to find, modify, and extend.

---

## 1. Problem Statement

The current formatter has a solid foundation (Wadler-Lindig Doc IR, comment preservation, DocPrinter) but the AST→Doc translation layer is a maintenance nightmare:

1. **Rules are invisible.** There is no way to answer "what are all the newline rules?" or "where is the rule for spacing around `=`?" without reading ~1,200 lines across `StatementFormatter.cs`, `ExpressionFormatter.cs`, and `StashFormatter.cs`.

2. **Rules are context-blind.** The same statement type (e.g., `VarDeclStmt`) is formatted identically whether it appears at the top level, inside a function body, inside a struct definition, or inside a `for` init clause. There is no mechanism for context-dependent behavior.

3. **Common patterns are duplicated.** Array, dict, and struct-init formatting use nearly identical Mark/WrapFrom/SoftLine patterns (50-70 lines each). Trailing comma logic is repeated in three places. Blank-line-between-statements logic exists in `StashFormatter` but cannot be reused for blank-lines-between-struct-members.

4. **Configuration is disconnected.** Adding a new formatting option requires edits in `FormatConfig.cs` (property + parsing), `FormatterContext.cs` (field), the specific formatter method (usage), and `Program.cs` (help text). There is no single source of truth linking "this config option controls this formatting behavior."

**User pain:** Every time a rule misbehaves, it takes significant effort to locate the responsible code, understand its implicit context assumptions, and change it without breaking other formatting.

---

## 2. Design Goals

1. **Findability** — Given a formatting behavior (e.g., "blank lines between functions at top level"), you can locate the rule in < 30 seconds.
2. **Context-awareness** — Rules can vary based on scope: top-level script, function body, struct body, enum body, control flow body, lambda body, etc.
3. **Composability** — Common patterns (collection formatting, parameter lists, comma-separated items) are shared, not duplicated.
4. **Configurability** — Rules that users should control are wired to `FormatConfig` properties in exactly one place.
5. **Testability** — Individual rules can be tested in isolation with a minimal AST + context, not just via full-file formatting.
6. **Simplicity** — This is a ~2,500-line formatter for a language with 46 AST nodes. The architecture must be proportionate. No plugin registries, no dynamic rule loading, no visitor-pattern-on-top-of-visitor-pattern.

---

## 3. Architecture Overview

### 3.1 What We Keep (Unchanged)

| Component         | File               | Rationale                                                                                              |
| ----------------- | ------------------ | ------------------------------------------------------------------------------------------------------ |
| **Doc IR**        | `Doc.cs`           | Solid Wadler-Lindig implementation. No changes needed.                                                 |
| **DocPrinter**    | `DocPrinter.cs`    | Renders Doc→string correctly. No changes needed.                                                       |
| **TriviaHandler** | `TriviaHandler.cs` | Comment preservation works. May need minor interface changes but logic is sound.                       |
| **ImportSorter**  | `ImportSorter.cs`  | Post-processing step, orthogonal to rule system. Keep as-is.                                           |
| **FormatConfig**  | `FormatConfig.cs`  | Configuration loading (`.stashformat`, `.editorconfig`, CLI). Keep structure, extend with new options. |

### 3.2 What We Replace

| Current                                      | Replacement                                                | Why                                                    |
| -------------------------------------------- | ---------------------------------------------------------- | ------------------------------------------------------ |
| `StatementFormatter.cs` (19 static methods)  | `StmtPrinters/` — one lean printer per statement category  | Printers consult rules instead of hardcoding decisions |
| `ExpressionFormatter.cs` (22 static methods) | `ExprPrinters/` — one lean printer per expression category | Same                                                   |
| `FormatterContext.cs` (flat state bag)       | `FormatContext` — enriched with scope stack + rule lookup  | Context-awareness                                      |
| Newline logic in `StashFormatter.cs` loop    | `SpacingRules` — declarative inter-statement spacing       | Centralized, context-aware                             |

### 3.3 New Components

```
Stash.Analysis/Formatting/
├── Doc.cs                          # UNCHANGED
├── DocPrinter.cs                   # UNCHANGED
├── TriviaHandler.cs                # UNCHANGED (minor interface tweaks)
├── ImportSorter.cs                 # UNCHANGED
├── FormatConfig.cs                 # Extended with new options
│
├── FormatContext.cs                # NEW — replaces FormatterContext
│   ├── Scope stack (TopLevel, FunctionBody, StructBody, etc.)
│   ├── Token cursor (moved from old FormatterContext)
│   ├── Doc emission (moved from old FormatterContext)
│   └── Rule lookup (resolves config → behavior)
│
├── Rules/                          # NEW — all formatting rules
│   ├── SpacingRules.cs             # Inter-statement blank lines / newlines
│   ├── BraceRules.cs              # Brace placement and single-line blocks
│   ├── OperatorRules.cs           # Spacing around operators
│   ├── CollectionRules.cs         # Array/dict/struct-init grouping + trailing commas
│   ├── ParameterRules.cs          # Function parameter list formatting
│   └── PunctuationRules.cs        # Semicolons, colons, commas, brackets
│
├── Printers/                       # NEW — lean AST→Doc translators
│   ├── DeclarationPrinter.cs       # var, const, fn, struct, enum, interface, extend, import
│   ├── ControlFlowPrinter.cs       # if, while, do-while, for, for-in, switch, try-catch
│   ├── ExpressionPrinter.cs        # All expression types
│   ├── BlockPrinter.cs             # Block statement (shared by all block-containing nodes)
│   └── CollectionPrinter.cs        # Array, dict, struct-init (shared pattern)
│
└── StashFormatter.cs               # SIMPLIFIED — just walks AST, delegates to printers
```

---

## 4. Core Abstractions

### 4.1 FormattingScope — Context Awareness

The key missing piece in the current formatter. A scope stack that tracks where we are in the AST:

```csharp
/// <summary>
/// Describes the formatting context at the current AST position.
/// Rules consult this to vary behavior by location.
/// </summary>
internal enum ScopeKind
{
    TopLevel,           // Script-level statements
    FunctionBody,       // Inside fn { ... }
    StructBody,         // Inside struct { ... } (member declarations)
    EnumBody,           // Inside enum { ... }
    InterfaceBody,      // Inside interface { ... }
    ExtendBody,         // Inside extend { ... }
    ControlFlowBody,    // Inside if/while/for/switch { ... }
    LambdaBody,         // Inside => { ... } or => expr
    TryCatchBody,       // Inside try/catch/finally { ... }
    ElevateBody,        // Inside elevate { ... }
    SwitchCase,         // Inside case: or default: block
}
```

The `FormatContext` maintains a `Stack<ScopeKind>` that printers push/pop as they enter/exit scopes:

```csharp
ctx.PushScope(ScopeKind.FunctionBody);
// ... format function body contents ...
ctx.PopScope();
```

Rules query the current scope:

```csharp
// "How many blank lines between members in this scope?"
int blanks = ctx.BlankLinesBetween(currentStmt, previousStmt);
```

### 4.2 Spacing Rules — Declarative Inter-Statement Spacing

This is the #1 pain point. Currently, the newline-vs-blank-line decision is hardcoded in `StashFormatter.cs` with a single `IsDeclaration()` check. The new system makes this a table:

```csharp
/// <summary>
/// Determines spacing (newlines / blank lines) between consecutive statements
/// based on their types and the enclosing scope.
/// </summary>
internal static class SpacingRules
{
    /// <summary>
    /// Returns the number of blank lines to insert between two consecutive statements
    /// in the given scope. Returns 0 for a single newline (no blank line).
    /// </summary>
    internal static int BlankLinesBetween(Stmt prev, Stmt current, ScopeKind scope, FormatConfig config)
    {
        // Example rule table (illustrative — actual rules TBD during implementation):
        //
        // TopLevel:
        //   fn  → fn:    config.BlankLinesBetweenBlocks (default 1)
        //   fn  → var:   config.BlankLinesBetweenBlocks
        //   var → var:   0  (consecutive vars stay tight)
        //   import → import: 0
        //   import → anything: config.BlankLinesBetweenBlocks
        //   any declaration → any: config.BlankLinesBetweenBlocks
        //
        // FunctionBody:
        //   any → any:   0  (single newline between statements)
        //   local fn → any: 1  (blank line after nested function)
        //
        // StructBody:
        //   field → field: 0
        //   method → method: config.BlankLinesBetweenBlocks
        //   field → method: config.BlankLinesBetweenBlocks
        //
        // ... etc.
    }
}
```

**Key design decision:** These rules are a static method with a clear match-on-(prev, current, scope) structure, not an abstract class hierarchy. This keeps it simple and greppable. Every spacing decision is in one file.

### 4.3 Brace Rules — Placement and Single-Line Blocks

```csharp
/// <summary>
/// Controls brace placement style and single-line block eligibility.
/// </summary>
internal static class BraceRules
{
    /// <summary>
    /// Whether a block with the given statements can be rendered on a single line.
    /// </summary>
    internal static bool AllowSingleLine(BlockStmt block, ScopeKind parentScope, FormatConfig config)
    {
        if (!config.SingleLineBlocks) return false;
        if (block.Statements.Count != 1) return false;
        // Could extend: allow for if-bodies but not function bodies, etc.
        return true;
    }

    /// <summary>
    /// Whitespace before opening brace. Currently always a space,
    /// but centralized here for future configurability.
    /// </summary>
    internal static void BeforeOpenBrace(FormatContext ctx)
    {
        ctx.Space();
    }
}
```

### 4.4 Collection Rules — Shared Grouping Pattern

The current array/dict/struct-init formatting is 50-70 lines each with nearly identical logic. Extract the common pattern:

```csharp
/// <summary>
/// Shared formatting for comma-separated collections that can be
/// single-line or multi-line (arrays, dicts, struct inits, parameter lists).
/// </summary>
internal static class CollectionRules
{
    /// <summary>
    /// Formats a bracketed, comma-separated collection with smart line breaking.
    /// Handles: opening bracket, elements with separators, trailing comma, closing bracket.
    /// </summary>
    internal static void FormatCollection(
        FormatContext ctx,
        int elementCount,
        Action<int> formatElement,     // callback to format element at index i
        CollectionStyle style)         // config: brackets, trailing comma, bracket spacing
    {
        // Encapsulates the Mark/WrapFrom/SoftLine/TrailingComma pattern
        // currently duplicated in FormatArray, FormatDictLiteral, FormatStructInit
    }
}

internal record CollectionStyle(
    bool BracketSpacing,               // spaces inside { }
    TrailingCommaStyle TrailingComma,   // none | all
    bool AllowSingleLine               // try flat mode?
);
```

### 4.5 Operator Rules — Spacing Around Operators

```csharp
/// <summary>
/// Controls spacing around operators (binary, assignment, arrow, etc.).
/// </summary>
internal static class OperatorRules
{
    /// <summary>Whether to insert spaces around a binary operator.</summary>
    internal static bool SpaceAroundBinaryOp(TokenType op) => true; // all binary ops get spaces

    /// <summary>Whether to insert spaces around assignment operators.</summary>
    internal static bool SpaceAroundAssignment() => true;

    /// <summary>Whether to insert a space after unary operators (!, -).</summary>
    internal static bool SpaceAfterUnaryOp() => false;

    /// <summary>Whether to insert spaces around the arrow in return type annotations.</summary>
    internal static bool SpaceAroundArrow() => true;
}
```

### 4.6 Printers — Lean AST→Doc Translators

Printers replace the current `StatementFormatter` and `ExpressionFormatter`. They are **thin** — their job is to emit tokens in the right order and consult rules for spacing/grouping decisions. They do NOT make policy decisions themselves.

Example — the current `FormatFnDecl` hardcodes `ctx.Space()` before the body brace. The new version consults `BraceRules`:

```csharp
// In DeclarationPrinter.cs
internal static void PrintFnDecl(FnDeclStmt stmt, FormatContext ctx)
{
    if (stmt.IsAsync)
    {
        ctx.EmitToken(); // async
        ctx.Space();
    }
    ctx.EmitToken(); // fn
    ctx.Space();
    ctx.EmitToken(); // name

    // Parameter list — delegate to shared collection pattern
    ParameterRules.FormatParameterList(stmt, ctx);

    if (stmt.ReturnType != null)
    {
        ctx.Space();
        ctx.EmitToken(); // ->
        ctx.Space();
        ctx.EmitToken(); // type
    }

    BraceRules.BeforeOpenBrace(ctx);
    ctx.PushScope(ScopeKind.FunctionBody);
    BlockPrinter.Print(stmt.Body, ctx);
    ctx.PopScope();
}
```

---

## 5. FormatContext — The Enriched Context

Replaces `FormatterContext`. Same token cursor and Doc emission, plus scope awareness:

```csharp
public sealed class FormatContext
{
    // ── Configuration ─────────────────────────────────────────
    internal readonly FormatConfig Config;

    // ── Scope tracking ────────────────────────────────────────
    private readonly Stack<ScopeKind> _scopes = new();
    internal ScopeKind CurrentScope => _scopes.Count > 0 ? _scopes.Peek() : ScopeKind.TopLevel;
    internal void PushScope(ScopeKind scope) => _scopes.Push(scope);
    internal void PopScope() => _scopes.Pop();

    // ── Token cursor + Doc emission (moved from FormatterContext) ──
    internal List<Doc> Docs;
    internal int Indent;
    internal Token[] CodeTokens;
    internal int Cursor;
    internal Token? LastCodeToken;
    internal PendingWs Pending;

    // ── Trivia ────────────────────────────────────────────────
    internal readonly TriviaHandler Trivia;

    // ── Rule consultation shortcuts ───────────────────────────

    /// <summary>
    /// Returns the number of blank lines to insert between two statements
    /// in the current scope.
    /// </summary>
    internal int BlankLinesBetween(Stmt prev, Stmt current)
        => SpacingRules.BlankLinesBetween(prev, current, CurrentScope, Config);
}
```

### 5.1 Scope Stack Usage

Every printer that introduces a new scope pushes/pops:

```
TopLevel (default)
  └─ FunctionBody  (pushed by DeclarationPrinter.PrintFnDecl)
       └─ ControlFlowBody  (pushed by ControlFlowPrinter.PrintIf)
  └─ StructBody  (pushed by DeclarationPrinter.PrintStructDecl)
       └─ FunctionBody  (method inside struct)
```

This means `SpacingRules` can answer "1 blank line between methods in a struct, but 0 blank lines between statements in a function body" without any special-casing in the printers themselves.

---

## 6. Rule File Organization

All rules live in `Stash.Analysis/Formatting/Rules/`. The directory is the answer to "where are the formatting rules?":

| File                  | Controls                                  | Example Decisions                                                      |
| --------------------- | ----------------------------------------- | ---------------------------------------------------------------------- |
| `SpacingRules.cs`     | Newlines / blank lines between statements | "1 blank line between top-level functions, 0 between consecutive vars" |
| `BraceRules.cs`       | Brace placement, single-line blocks       | "Allow single-line `if` bodies, space before `{`"                      |
| `OperatorRules.cs`    | Spaces around operators                   | "Space around `=`, no space after `!`"                                 |
| `CollectionRules.cs`  | Array/dict/struct-init grouping           | "Trailing comma in multi-line arrays, bracket spacing"                 |
| `ParameterRules.cs`   | Function parameter formatting             | "Break params to multi-line if > 3 or exceeds printWidth"              |
| `PunctuationRules.cs` | Semicolons, colons, commas                | "Space after `:` in type hints, no space before `;`"                   |

**Finding a rule:** Ask "what kind of formatting decision is this?" → go to that file. No hunting through 1,200 lines of imperative code.

---

## 7. Detailed Spacing Rules Design

This section deserves deep treatment since it's the primary motivation for the redesign.

### 7.1 The Rule Table

`SpacingRules.BlankLinesBetween()` is structured as a match on `(prevKind, currentKind, scope)`:

```csharp
internal static int BlankLinesBetween(Stmt prev, Stmt current, ScopeKind scope, FormatConfig config)
{
    var prevKind = Classify(prev);
    var currentKind = Classify(current);

    return scope switch
    {
        ScopeKind.TopLevel => TopLevelSpacing(prevKind, currentKind, config),
        ScopeKind.FunctionBody => FunctionBodySpacing(prevKind, currentKind, config),
        ScopeKind.StructBody => StructBodySpacing(prevKind, currentKind, config),
        ScopeKind.EnumBody => 0,  // enum variants are always single-newline separated
        ScopeKind.InterfaceBody => InterfaceBodySpacing(prevKind, currentKind, config),
        _ => DefaultSpacing(prevKind, currentKind, config),
    };
}
```

### 7.2 Statement Classification

Instead of matching on 19 concrete `Stmt` types everywhere, classify into formatting-relevant categories:

```csharp
internal enum StmtCategory
{
    Import,             // import, import-as
    VarDecl,            // let, const
    FnDecl,             // fn (top-level or nested)
    TypeDecl,           // struct, enum, interface
    ExtendDecl,         // extend blocks
    ControlFlow,        // if, while, for, switch, try-catch
    SimpleStatement,    // expression-stmt, return, throw, break, continue
    Block,              // bare { ... } block
}
```

### 7.3 Example: Top-Level Spacing

```csharp
private static int TopLevelSpacing(StmtCategory prev, StmtCategory current, FormatConfig config)
{
    // Imports cluster together
    if (prev == StmtCategory.Import && current == StmtCategory.Import)
        return 0;

    // Blank line after import block
    if (prev == StmtCategory.Import)
        return config.BlankLinesBetweenBlocks;

    // Consecutive var/const declarations cluster
    if (prev == StmtCategory.VarDecl && current == StmtCategory.VarDecl)
        return 0;

    // Any declaration boundary gets blank lines
    if (IsDecl(prev) || IsDecl(current))
        return config.BlankLinesBetweenBlocks;

    // Default: single newline
    return 0;
}
```

### 7.4 Example: Struct Body Spacing

```csharp
private static int StructBodySpacing(StmtCategory prev, StmtCategory current, FormatConfig config)
{
    // Fields cluster together
    if (prev == StmtCategory.VarDecl && current == StmtCategory.VarDecl)
        return 0;

    // Blank line between methods
    if (prev == StmtCategory.FnDecl && current == StmtCategory.FnDecl)
        return config.BlankLinesBetweenBlocks;

    // Blank line between field block and method block
    if (prev == StmtCategory.VarDecl && current == StmtCategory.FnDecl)
        return config.BlankLinesBetweenBlocks;

    return 0;
}
```

**This is the payoff.** Today, you cannot distinguish "blank line between top-level functions" from "blank line between struct methods" — they're both the same `IsDeclaration()` check. With the scope-aware rule table, they're separate entries that can have different values.

---

## 8. Migration Strategy

### 8.1 Phased Approach (Recommended)

The redesign touches the same output, so we can validate incrementally:

**Phase 1: Introduce FormatContext + Scope Stack**

- Create `FormatContext` alongside existing `FormatterContext`
- Add scope push/pop calls in `StashFormatter` visitor methods
- No behavioral change — just infrastructure

**Phase 2: Extract Rules**

- Create `Rules/` directory
- Move spacing logic from `StashFormatter` loop → `SpacingRules`
- Move single-line block logic → `BraceRules`
- Move trailing comma logic → `CollectionRules`
- Each extraction is validated by running the full test suite — output must not change

**Phase 3: Create Printers, Replace Formatters**

- Create `Printers/` directory
- Port `StatementFormatter` methods → printer methods that consult rules
- Port `ExpressionFormatter` methods → printer methods that consult rules
- Extract `CollectionPrinter` from the three duplicated collection patterns
- Delete `StatementFormatter.cs` and `ExpressionFormatter.cs`

**Phase 4: Delete Old Code**

- Remove `FormatterContext.cs` (replaced by `FormatContext`)
- Remove any remaining dead code
- Final validation: all formatter tests pass with identical output

### 8.2 Test Strategy

**Critical invariant:** Phases 1-3 must produce **byte-identical output** to the current formatter on all existing test cases. The goal is architectural improvement, not behavioral change.

**After Phase 4:** New tests can be added for context-dependent behaviors that were previously impossible (e.g., different spacing in struct bodies vs. function bodies).

Suggested test additions:

- Unit tests per rule file (e.g., `SpacingRulesTests.cs` that tests `BlankLinesBetween()` with various stmt/scope combinations)
- Integration tests for new context-aware behaviors
- Regression tests: snapshot of all `examples/*.stash` files formatted before and after

---

## 9. What This Does NOT Change

- **Configuration file format** (`.stashformat`) — same keys, same loading
- **CLI interface** (`stash-format` commands and flags) — identical
- **LSP integration** — `StashFormatter.Format()` and `FormatRange()` keep same signatures
- **Doc IR** — no changes to `Doc.cs` or `DocPrinter.cs`
- **Comment handling** — `TriviaHandler` stays, may get minor interface changes

---

## 10. Tradeoffs and Risks

### 10.1 Tradeoff: Indirection vs. Discoverability

**Adding indirection** (rules consulted by printers) makes it harder to read a single printer and understand everything it does — you have to follow the rule call. But it makes it **much easier** to find and change a specific behavior, which is the stated goal.

**Mitigation:** Rules are static methods in well-named files, not abstract class hierarchies. You can Ctrl+Click into any rule call and immediately see the logic. No interfaces, no DI, no dynamic dispatch.

### 10.2 Tradeoff: Scope Stack Overhead

Pushing/popping a `Stack<ScopeKind>` adds a small cost per block entry/exit. This is negligible — formatting is already I/O-bound (reading files, writing output), and the stack operations are O(1).

### 10.3 Risk: Behavioral Drift During Migration

If migration phases introduce subtle output differences, tests might not catch edge cases.

**Mitigation:** Run the formatter on all `examples/*.stash` files and diff output before/after each phase. Also run on any available real-world `.stash` codebases.

### 10.4 Risk: Over-Engineering the Rule System

It's tempting to make rules fully data-driven (JSON rule definitions, rule priority ordering, etc.). This would be overkill for 46 AST nodes.

**Mitigation:** Rules are plain C# methods with `switch` expressions. No rule engine, no priority system, no dynamic loading. If the `switch` gets too big, split it into private methods. That's it.

---

## 11. Alternatives Considered

### Alternative A: Full Prettier-Style Plugin System

Each AST node type has a registered "printer plugin" implementing an interface. A plugin registry resolves the right printer at runtime.

**Rejected because:** Overkill for 46 nodes. Adds abstraction layers (interfaces, registry, resolution) that provide no benefit when there's exactly one printer per node type and no external plugins.

### Alternative B: Data-Driven Rule Tables (JSON/TOML)

Define all formatting rules in a configuration file, not in code. A generic formatter reads the config and applies it.

**Rejected because:** Most formatting rules involve complex conditional logic (e.g., "trailing comma only in multi-line mode when the collection has > 1 element"). Expressing this in data requires inventing a rule DSL, which is strictly more complex than writing the same logic in C#.

### Alternative C: Keep Current Architecture, Just Refactor

Extract common patterns into helpers but keep the same `StatementFormatter`/`ExpressionFormatter` structure.

**Rejected because:** This doesn't solve the core problem (context-blindness). Without a scope stack, you cannot have different blank-line rules for struct bodies vs. function bodies. And without a `Rules/` directory, you cannot answer "where are all the spacing rules?" without reading every formatter method.

### Alternative D: Clean Rewrite with No Phased Migration

Throw away the current formatter code and write from scratch.

**Rejected because:** The current formatter handles 46 node types correctly. A from-scratch rewrite would require re-implementing all of them, including edge cases that were discovered and fixed over time. Phased migration preserves those battle-tested behaviors.

---

## 12. Open Questions

1. **Should scope be more granular?** E.g., should `ControlFlowBody` distinguish between `if`-body, `while`-body, and `for`-body? Currently I don't see a use case, but this is easy to add later by splitting the enum variant.
Answer: Not right now, we start with a general `ControlFlowBody` and if in the future more granularity control is needed, it will be added.

2. **Should rules be instance methods on a `RuleSet` object rather than static methods?** Instance methods would allow injecting different rule sets (e.g., a "compact" vs. "spacious" style). Currently seems unnecessary — config knobs in `FormatConfig` handle this. But worth revisiting if multiple "style presets" become a goal.
Answer: Seems unnecessary, not for this spec.

3. **Should `ParameterRules` handle UFCS call formatting too?** The uniform function call syntax `x.fn(y)` has parameter-list-like formatting needs. Need to check if the current formatter treats these identically to regular calls.
Answer: Yes, ensure they are also accounted for.

4. **How should `FormatRange` interact with scope?** When formatting a range, we may enter mid-scope (e.g., formatting lines 10-20 inside a function). The current approach formats the whole file and splices the range. If we keep that approach, scope tracking works naturally. If we ever move to true range-formatting, we'd need to reconstruct the scope stack from the AST path — doable but more complex.
Answer: Keep it the way it is for now, stash files should never be too big so the formatter can handle it.

---

## 13. Implementation Checklist (for Orchestrator)

When this spec moves to `1-todo/`, the implementing agent should:

- [ ] Create `FormatContext.cs` with scope stack, migrating fields from `FormatterContext`
- [ ] Create `Rules/SpacingRules.cs` — extract from `StashFormatter` main loop
- [ ] Create `Rules/BraceRules.cs` — extract from `StatementFormatter.FormatBlock`
- [ ] Create `Rules/CollectionRules.cs` — extract shared pattern from array/dict/struct-init
- [ ] Create `Rules/OperatorRules.cs` — extract from `ExpressionFormatter.FormatBinary` etc.
- [ ] Create `Rules/ParameterRules.cs` — extract from `StatementFormatter.FormatFnDecl`
- [ ] Create `Rules/PunctuationRules.cs` — extract spacing around `:`, `;`, `,`
- [ ] Create `Printers/DeclarationPrinter.cs` — port from `StatementFormatter`
- [ ] Create `Printers/ControlFlowPrinter.cs` — port from `StatementFormatter`
- [ ] Create `Printers/ExpressionPrinter.cs` — port from `ExpressionFormatter`
- [ ] Create `Printers/BlockPrinter.cs` — extract from `StatementFormatter.FormatBlock`
- [ ] Create `Printers/CollectionPrinter.cs` — shared collection formatting
- [ ] Update `StashFormatter.cs` — delegate to new printers, add scope push/pop
- [ ] Add scope push/pop at all scope-introducing nodes
- [ ] Delete `StatementFormatter.cs` and `ExpressionFormatter.cs`
- [ ] Delete `FormatterContext.cs` (replaced by `FormatContext`)
- [ ] Verify byte-identical output on all existing tests
- [ ] Add `SpacingRulesTests.cs` — unit tests for spacing rule table
- [ ] Add `BraceRulesTests.cs` — unit tests for single-line block eligibility
- [ ] Add context-aware formatting integration tests (struct body vs function body spacing)
- [ ] Update `Stash.Format/README.md` if new config options are added
