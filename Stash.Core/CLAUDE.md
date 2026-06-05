# Stash.Core Guidelines

The foundation layer. Zero dependencies on any other Stash project. Every other project depends on it. Changes here ripple everywhere — proceed carefully.

## Project Layout

```
Stash.Core/
├── Lexing/
│   ├── Lexer.cs          → Two-pointer scanner: produces List<Token> from source text
│   ├── Token.cs          → Token record (Type, Lexeme, Literal, Line, Column)
│   └── TokenType.cs      → Token-type enum (keywords, operators, literals, delimiters)
│
├── Parsing/
│   ├── Parser.cs         → Recursive-descent parser: produces List<Stmt> from tokens
│   └── AST/              → One file per AST node type (see below)
│       ├── Expr.cs / IExprVisitor.cs    → Expression base + visitor interface
│       ├── Stmt.cs / IStmtVisitor.cs    → Statement base + visitor interface
│       └── [node-type files]           → One file per node type
│
├── Resolution/           → SemanticResolver: pre-pass that resolves variable bindings
│
├── Runtime/
│   ├── RuntimeValues.cs  → Shared value utilities (Stringify, IsTruthy, IsEqual, etc.)
│   ├── StashValue.cs     → Tagged union runtime value type
│   ├── StashValueTag.cs  → Value type discriminant enum
│   ├── StashCapabilities.cs → Feature gates (FileSystem, Network, Process, etc.)
│   ├── Types/            → Runtime types (StashDictionary, StashInstance, StashEnum, etc.)
│   ├── Protocols/        → IVM* interfaces (IVMArithmetic, IVMIndexable, etc.)
│   └── Stdlib/           → IStdlibProvider interface (injected, not imported)
│
├── Common/               → Shared utilities (SourceSpan, diagnostics, etc.)
└── Debugging/            → IDebugger interface contract
```

## The Visitor Pattern

**All visitors** must be updated when adding a new AST node:

| Visitor | Location | Purpose |
| ------- | -------- | ------- |
| `Compiler` | `Stash.Bytecode/Compilation/` | Compile AST → bytecode |
| `SemanticResolver` | `Stash.Core/Resolution/` | Resolve variable bindings |
| `SemanticValidator` | `Stash.Analysis/` | Produce diagnostics |
| `SymbolCollector` | `Stash.Analysis/` | Build scope tree for LSP |
| `SemanticTokenWalker` | `Stash.Lsp/Analysis/` | Semantic highlighting |
| `StashFormatter` | `Stash.Lsp/Analysis/` | Code formatting |

**Rule:** If you add a new AST node and don't update every visitor, you will get a compile error (the visitors implement the interfaces) — but you must also ensure the semantic behavior is correct in each, not just that it compiles.

## Adding a New AST Node

1. Create the node file in `Parsing/AST/` following existing patterns:
   ```csharp
   public record FooExpr(SourceSpan Span, Expr Left, Token Op) : Expr
   {
       public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitFooExpr(this);
   }
   ```
2. Add `VisitFooExpr(FooExpr expr)` to `IExprVisitor<T>` (or `IStmtVisitor<T>` for statements)
3. Implement `VisitFooExpr` in **every visitor** (see table above)
4. Add parser rule in `Parser.cs` at the appropriate precedence level
5. Update the SemanticResolver in `Resolution/` if the node introduces new scope or binding

## Lexer

`Lexer.cs` uses a two-pointer scanner (`_start`, `_current`). Key conventions:
- `Advance()` moves `_current` and returns the character
- `Match(char)` conditionally advances on match
- `Peek()` / `PeekNext()` for lookahead without advancing
- String interpolation is tokenized at the lexer level: `$"..."` produces `InterpolatedString*` tokens
- Shell commands `$(...)` are tokenized as `CommandStart` / `CommandEnd` / content tokens

## Parser

`Parser.cs` uses standard recursive-descent. Key conventions:
- `Consume(TokenType, message)` — advance and assert, throws `ParseError` on mismatch
- `Check(TokenType)` — peek without consuming
- `Match(params TokenType[])` — consume if any type matches
- Error recovery: `Synchronize()` skips to the next statement boundary on parse error
- `ParseError` is a sentinel exception caught by `Synchronize()` — never propagate it

## SemanticResolver

Runs before the Compiler. Resolves every identifier to its enclosing scope depth, turning runtime hash lookups into O(1) slot accesses. Key invariants:
- Every `IdentifierExpr` gets a resolved depth set via `Resolver.Resolve(expr, depth)`
- Closures capture variables by their scope depth
- Forward references within the same scope are allowed for function declarations

## Runtime Type System

Values use a tagged union (`StashValue` + `StashValueTag`). The `IVM*` protocols in `Runtime/Protocols/` define type behaviors:

| Protocol | Capability |
| -------- | ---------- |
| `IVMArithmetic` | `+`, `-`, `*`, `/`, `%` |
| `IVMComparable` | `<`, `<=`, `>`, `>=` |
| `IVMEquatable` | `==`, `!=` |
| `IVMTruthiness` | truthiness check |
| `IVMStringifiable` | `str()` conversion |
| `IVMFieldAccessible` | `.field` read |
| `IVMFieldMutable` | `.field` write |
| `IVMIndexable` | `[i]` read |
| `IVMIterable` | `for` loop source |
| `IVMIterator` | iterator state |
| `IVMSized` | `len()` |
| `IVMTyped` | `typeof()` |

**Never add hardcoded `if (value is X)` type switches** in the VM dispatch. Add the behavior to the protocol implementation on the type instead.

## StashCapabilities

Feature gate flags for the embedding API. Used by `Stash.Playground` to disable dangerous namespaces. When adding new capabilities:
1. Add the flag to `StashCapabilities.cs`
2. Gate the relevant `NamespaceBuilder` in `Stash.Stdlib` with `b.RequiresCapability()`
3. Update playground docs if it affects sandbox availability

## Key Invariants

- `SourceSpan` is on every AST node — never omit it. It's required for LSP range calculation and error reporting.
- `Stash.Core` must not reference `Stash.Bytecode`, `Stash.Stdlib`, `Stash.Analysis`, or any other Stash project.
- `RuntimeError` is a C# exception used during execution. `StashError` is a first-class Stash value created when `try/catch` catches a `RuntimeError`.
