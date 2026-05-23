# Interfaces — Design & Implementation Analysis

> **Status:** Proposal
> **Created:** March 2026
> **Purpose:** Analyze what it takes to add interfaces to Stash, keeping its scripting-first identity intact.

---

## Table of Contents

1. [Design Philosophy](#1-design-philosophy)
2. [Proposed Syntax](#2-proposed-syntax)
3. [Semantic Rules](#3-semantic-rules)
4. [Runtime Behavior](#4-runtime-behavior)
5. [Type System Integration](#5-type-system-integration)
6. [Implementation Roadmap](#6-implementation-roadmap)
7. [File-by-File Change Map](#7-file-by-file-change-map)
8. [Design Decisions & Alternatives](#8-design-decisions--alternatives)
9. [Edge Cases & Open Questions](#9-edge-cases--open-questions)
10. [Type Hint Enforcement in Interface Signatures](#10-type-hint-enforcement-in-interface-signatures)
11. [Best Practices & Future-Proofing](#11-best-practices--future-proofing)

---

## 1. Design Philosophy

Stash is a **scripting language first**. Interfaces must serve pragmatic scripting needs — not mimic Java/C#. The goals:

1. **Lightweight contracts** — Assert that a struct has certain methods/fields without inheritance hierarchies.
2. **Duck-typing bridge** — Stash is dynamic; interfaces should formalize what already works informally but not add ceremonial overhead.
3. **`is` operator integration** — `value is Printable` should answer "does this struct fulfill the Printable contract?"
4. **Zero runtime overhead for non-users** — If you don't use interfaces, nothing changes. No vtables, no hidden fields.
5. **Import-friendly** — Interfaces export/import like structs and enums via the existing module system.

### What interfaces are NOT in Stash

- **Not inheritance.** No `extends`, no method resolution order, no diamond problem.
- **Not abstract classes.** No default implementations (keep it simple for v1).
- **Not generics.** No `interface Collection<T>` — Stash is dynamically typed.
- **Not mandatory.** A struct works perfectly fine without ever mentioning an interface.

### Core model: Structural conformance with explicit declaration

A struct **implements** an interface by declaring it does. The runtime verifies that all required methods and fields exist. This is a hybrid model — explicit declaration (like Go's implicit satisfaction is too magical for error messages) but structural checking (the struct doesn't inherit anything).

---

## 2. Proposed Syntax

### Interface Declaration

```stash
interface Printable {
    toString(),
    toJson()
}
```

Methods list their name and parameter count (excluding `self`). No bodies — interfaces are pure contracts.

With parameter names for documentation:

```stash
interface Serializable {
    serialize(format),
    deserialize(data)
}
```

Fields can also be required:

```stash
interface Identifiable {
    id,
    name,
    getDisplayName()
}
```

### Struct Implementation

```stash
struct User : Printable, Identifiable {
    id,
    name,
    email

    fn toString() {
        return $"{self.name} <{self.email}>";
    }

    fn toJson() {
        return json.stringify({
            id: self.id,
            name: self.name,
            email: self.email
        });
    }

    fn getDisplayName() {
        return self.name;
    }
}
```

The `: InterfaceName, ...` syntax after the struct name declares which interfaces the struct intends to satisfy. This is checked at struct definition time — if `User` is missing `toString()`, the developer finds out immediately, not when the struct is used.

### Type Checking

```stash
let user = User { id: 1, name: "Alice", email: "alice@example.com" };

io.println(user is Printable);      // true
io.println(user is Identifiable);   // true
io.println(user is Serializable);   // false — User doesn't implement Serializable
```

### Interfaces as Function Parameter Contracts (documentation-only for v1)

```stash
/// @param item Must implement Printable
fn printItem(item) {
    io.println(item.toString());
}
```

Runtime enforcement of parameter types is a future concern (type annotations exist but are not enforced). Interfaces integrate with the existing pattern — they're checkable via `is` but not automatically enforced on calls.

### Multi-interface

```stash
interface Readable {
    read()
}

interface Writable {
    write(data)
}

struct File : Readable, Writable {
    path

    fn read() {
        return fs.readFile(self.path);
    }

    fn write(data) {
        fs.writeFile(self.path, data);
    }
}
```

---

## 3. Semantic Rules

### 3.1 Declaration Rules

1. An interface **must** have at least one member (field or method). Empty interfaces are a parse error.
2. Interface members are either **fields** (bare identifiers) or **method signatures** (identifier + parenthesized parameter list).
3. Method signatures specify parameter **count**, not types (Stash is dynamically typed).
4. Interfaces **cannot** contain method bodies. A body is a parse error.
5. Interface names follow the same rules as struct/enum names — identifiers, PascalCase by convention.
6. An interface is declared at module scope or function scope (same as struct/enum).
7. Duplicate member names within an interface are a parse error.

### 3.2 Implementation Rules

1. A struct declares interface conformance with `: Interface1, Interface2` after its name.
2. **All** fields and methods required by each interface **must** exist on the struct.
3. Method parameter count must match (method with 2 params in interface → method with 2 params in struct).
4. Missing members produce a **runtime error at struct definition time** — fail fast, not at usage.
5. A struct can implement zero or more interfaces.
6. Implementing the same interface twice is a warning (idempotent, not an error).

### 3.3 `is` Operator Rules

1. `value is InterfaceName` returns `true` if the value is a `StashInstance` whose struct implements `InterfaceName`.
2. `value is InterfaceName` returns `false` for non-struct values (null, int, string, dict, etc.).
3. The `is` check uses the **interface name** as a string match against the struct's interface list — nominal, not structural at runtime (structural check already happened at definition time).

### 3.4 typeof() Behavior

- `typeof(someInterface)` → `"interface"` (the definition object itself)
- `typeof(instanceOfStructThatImplementsInterface)` → `"struct"` (instances are still structs)
- Interfaces don't change the type of instances. They're metadata on the struct template.

---

## 4. Runtime Behavior

### 4.1 New Runtime Type: `StashInterface`

```
StashInterface
├── Name: string
├── RequiredFields: List<string>
├── RequiredMethods: Dictionary<string, int>   // method name → parameter count
└── ToString() → "<interface Printable>"
```

Lightweight. No method dispatch, no vtable. Just a contract descriptor.

### 4.2 StashStruct Changes

`StashStruct` gains one new field:

```
StashStruct
├── Name: string
├── Fields: List<string>
├── Methods: Dictionary<string, StashFunction>
├── Interfaces: List<StashInterface>              // ← NEW
```

At struct definition time (interpreter `VisitStructDeclStmt`), the interpreter:

1. Resolves each interface name from the environment.
2. Validates that all required fields exist in the struct's field list.
3. Validates that all required methods exist in the struct's method dictionary with matching parameter counts.
4. Stores the resolved interface references on the struct template.

### 4.3 StashInstance Changes

`StashInstance` needs **no changes**. It already carries a `Struct` reference. The `is` operator can check `instance.Struct.Interfaces`.

### 4.4 Method Dispatch

**No changes.** Interface methods are regular struct methods dispatched through the existing `GetField → BoundMethod → CallWithSelf` chain. Interfaces don't add indirection — they just validate that the methods exist.

### 4.5 Import/Export

**No changes needed.** `StashInterface` is stored in the environment like `StashStruct` and `StashEnum`. The existing `VisitImportStmt` retrieves any object by name from module environments — interfaces will import transparently:

```stash
// printable.stash
interface Printable {
    toString()
}

// main.stash
import { Printable } from "printable.stash";

struct Item : Printable {
    name
    fn toString() { return self.name; }
}
```

---

## 5. Type System Integration

### 5.1 `is` Operator Extension

Current `VisitIsExpr` handles custom types via a fallback:

```csharp
_ => (value is StashInstance instance && instance.TypeName == typeName) ||
     (value is StashEnumValue enumVal && enumVal.TypeName == typeName)
```

The new logic adds interface checking to this fallback:

```csharp
_ => (value is StashInstance instance && (
         instance.TypeName == typeName ||
         (instance.Struct?.Interfaces.Any(i => i.Name == typeName) ?? false)
     )) ||
     (value is StashEnumValue enumVal && enumVal.TypeName == typeName)
```

This means `user is Printable` checks:

1. Is the struct name "Printable"? (existing behavior, handles direct struct type check)
2. Does the struct implement an interface named "Printable"? (new behavior)

### 5.2 typeof() Extension

In `GlobalBuiltIns.cs`, add to the switch:

```csharp
StashInterface => "interface",
```

### 5.3 Stringify Extension

In `RuntimeValues.Stringify`, add:

```csharp
if (value is StashInterface iface)
{
    return iface.ToString();
}
```

---

## 6. Implementation Roadmap

### Phase 1: Core Language (Lexer → Parser → AST → Interpreter)

| Step | Component                    | Change                                                             | Effort  |
| ---- | ---------------------------- | ------------------------------------------------------------------ | ------- |
| 1.1  | `TokenType.cs`               | Add `Interface` token type                                         | Trivial |
| 1.2  | `Lexer.cs`                   | Add `"interface"` → `TokenType.Interface` to keyword dict          | Trivial |
| 1.3  | `InterfaceDeclStmt.cs`       | New AST node (`Name`, `Fields`, `MethodSignatures`, `Span`)        | Small   |
| 1.4  | `IStmtVisitor.cs`            | Add `VisitInterfaceDeclStmt(InterfaceDeclStmt)`                    | Trivial |
| 1.5  | `Parser.cs`                  | Add `InterfaceDeclaration()` method; dispatch from `Declaration()` | Medium  |
| 1.6  | `Parser.cs`                  | Extend `StructDeclaration()` to parse `: Interface1, Interface2`   | Small   |
| 1.7  | `StructDeclStmt.cs`          | Add `List<Token> Interfaces` field                                 | Trivial |
| 1.8  | `StashInterface.cs`          | New runtime type (name, required fields, required methods)         | Small   |
| 1.9  | `StashStruct.cs`             | Add `List<StashInterface> Interfaces` field                        | Trivial |
| 1.10 | `Interpreter.Statements.cs`  | Implement `VisitInterfaceDeclStmt` (create + define)               | Small   |
| 1.11 | `Interpreter.Statements.cs`  | Extend `VisitStructDeclStmt` to resolve + validate interfaces      | Medium  |
| 1.12 | `Interpreter.Expressions.cs` | Extend `VisitIsExpr` for interface checking                        | Small   |
| 1.13 | `GlobalBuiltIns.cs`          | Add `StashInterface` cases to `typeof` and `Stringify`             | Trivial |
| 1.14 | `RuntimeValues.cs`           | Add `StashInterface` to `Stringify`                                | Trivial |
| 1.15 | `Resolver.cs`                | Implement `VisitInterfaceDeclStmt` (Declare + Define)              | Trivial |

### Phase 2: Analysis & LSP Support

| Step | Component                | Change                                                                     | Effort |
| ---- | ------------------------ | -------------------------------------------------------------------------- | ------ |
| 2.1  | `SymbolCollector.cs`     | Implement `VisitInterfaceDeclStmt` (add symbols for interface + members)   | Small  |
| 2.2  | `SemanticValidator.cs`   | Implement `VisitInterfaceDeclStmt` (validate no duplicate members)         | Small  |
| 2.3  | `SemanticTokenWalker.cs` | Implement `VisitInterfaceDeclStmt` (emit semantic tokens for highlighting) | Small  |
| 2.4  | `StashFormatter.cs`      | Implement `VisitInterfaceDeclStmt` (format interface declarations)         | Small  |
| 2.5  | `BuiltInRegistry.cs`     | Add `BuiltInInterface` record type (for LSP completions)                   | Small  |

### Phase 3: Extension & Syntax Highlighting

| Step | Component               | Change                                    | Effort  |
| ---- | ----------------------- | ----------------------------------------- | ------- |
| 3.1  | `stash.tmLanguage.json` | Add `interface` keyword highlighting rule | Trivial |
| 3.2  | `stash.json` (snippets) | Add interface snippet                     | Trivial |

### Phase 4: Testing

| Step | Component             | What to test                                                                                             |
| ---- | --------------------- | -------------------------------------------------------------------------------------------------------- |
| 4.1  | `LexerTests.cs`       | `interface` keyword tokenized as `TokenType.Interface`                                                   |
| 4.2  | `ParserTests.cs`      | Interface declaration parsing, struct-with-interfaces parsing                                            |
| 4.3  | `InterpreterTests.cs` | Interface definition, struct implementation, validation errors, `is` operator, `typeof()`, import/export |

### Estimated Total: ~25-30 discrete code changes across 15-20 files.

---

## 7. File-by-File Change Map

### Stash.Core (Lexer + Parser + AST)

| File                                          | Change                                                                                                                                                        |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Stash.Core/Lexing/TokenType.cs`              | Add `Interface` enum member                                                                                                                                   |
| `Stash.Core/Lexing/Lexer.cs`                  | Add `["interface"] = TokenType.Interface` to `_keywords` dict                                                                                                 |
| `Stash.Core/Parsing/AST/InterfaceDeclStmt.cs` | **NEW FILE.** AST node: `Name` (Token), `Fields` (List\<Token\>), `MethodSignatures` (List\<InterfaceMethod\> with name + param count), `Span`                |
| `Stash.Core/Parsing/AST/IStmtVisitor.cs`      | Add `T VisitInterfaceDeclStmt(InterfaceDeclStmt stmt);`                                                                                                       |
| `Stash.Core/Parsing/AST/StructDeclStmt.cs`    | Add `List<Token> Interfaces` property, update constructor                                                                                                     |
| `Stash.Core/Parsing/Parser.cs`                | Add `InterfaceDeclaration()` method; add `Match(TokenType.Interface)` in `Declaration()`; extend `StructDeclaration()` to parse `: Iface1, Iface2` after name |

### Stash.Interpreter (Runtime)

| File                                                        | Change                                                                                  |
| ----------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `Stash.Interpreter/Interpreting/Types/StashInterface.cs`    | **NEW FILE.** Runtime interface descriptor: `Name`, `RequiredFields`, `RequiredMethods` |
| `Stash.Interpreter/Interpreting/Types/StashStruct.cs`       | Add `List<StashInterface> Interfaces` property                                          |
| `Stash.Interpreter/Interpreting/Interpreter.Statements.cs`  | Add `VisitInterfaceDeclStmt`; extend `VisitStructDeclStmt` to validate interfaces       |
| `Stash.Interpreter/Interpreting/Interpreter.Expressions.cs` | Extend `VisitIsExpr` fallback to check interface conformance                            |
| `Stash.Interpreter/Interpreting/Resolver.cs`                | Add `VisitInterfaceDeclStmt` (Declare + Define)                                         |
| `Stash.Interpreter/Interpreting/RuntimeValues.cs`           | Add `StashInterface` case to `Stringify`                                                |
| `Stash.Interpreter/Interpreting/BuiltIns/GlobalBuiltIns.cs` | Add `StashInterface => "interface"` to `typeof` switch                                  |

### Stash.Analysis (LSP Visitors)

| File                                             | Change                                                                       |
| ------------------------------------------------ | ---------------------------------------------------------------------------- |
| `Stash.Analysis/Visitors/SymbolCollector.cs`     | Add `VisitInterfaceDeclStmt` — register interface + member symbols           |
| `Stash.Analysis/Visitors/SemanticValidator.cs`   | Add `VisitInterfaceDeclStmt` — validate no duplicate members                 |
| `Stash.Analysis/Visitors/SemanticTokenWalker.cs` | Add `VisitInterfaceDeclStmt` — emit semantic tokens                          |
| `Stash.Analysis/Visitors/StashFormatter.cs`      | Add `VisitInterfaceDeclStmt` — format interface declarations                 |
| `Stash.Analysis/Builtins/BuiltInRegistry.cs`     | Add `BuiltInInterface` record (optional, for built-in interface completions) |

### VS Code Extension

| File                                                           | Change                          |
| -------------------------------------------------------------- | ------------------------------- |
| `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` | Add `interface` to keyword list |
| `.vscode/extensions/stash-lang/snippets/stash.json`            | Add interface snippet           |

### Tests

| File                                           | Change                                                                             |
| ---------------------------------------------- | ---------------------------------------------------------------------------------- |
| `Stash.Tests/Lexing/LexerTests.cs`             | Token scanning for `interface` keyword                                             |
| `Stash.Tests/Parsing/ParserTests.cs`           | AST construction for interface declarations, struct-with-interfaces                |
| `Stash.Tests/Interpreting/InterpreterTests.cs` | Interface definition, conformance validation, `is` checks, `typeof()`, error cases |

### Documentation

| File                                         | Change                                      |
| -------------------------------------------- | ------------------------------------------- |
| `docs/Stash — Language Specification.md`     | Add Section 5f: Interfaces                  |
| `docs/Stash — Standard Library Reference.md` | Document `typeof()` returning `"interface"` |

---

## 8. Design Decisions & Alternatives

### Decision 1: Explicit `implements` syntax via `:` vs. Pure structural typing

**Chosen: Explicit with `:`.**

Alternatives considered:

| Approach                          | Pros                                                                                                                   | Cons                                                                                                                                          |
| --------------------------------- | ---------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| **`:` syntax** (Go-ish, explicit) | Clear error messages ("User missing method toString required by Printable"), familiar from TypeScript/C#, discoverable | Requires struct author to know about interfaces                                                                                               |
| **Pure structural** (Go-style)    | Zero boilerplate, any struct with the right methods "just works"                                                       | Terrible error messages at use-site, hard to discover what interface a struct satisfies, requires runtime structural check on every `is` call |
| **`implements` keyword**          | Very explicit, Java-familiar                                                                                           | Verbose for a scripting language, adds another keyword                                                                                        |

The `:` syntax is the best balance — one character of boilerplate, but clear contract declaration and fail-fast validation.

### Decision 2: Validation at definition time vs. at usage time

**Chosen: Definition time.**

When `struct User : Printable { ... }` is evaluated, the interpreter **immediately** checks that all `Printable` members exist on `User`. If they don't, a runtime error is thrown right there — not when someone later calls `user.toString()` or checks `user is Printable`.

This follows Stash's general philosophy of fail-fast. It means the error points to the struct definition, not some distant call site.

### Decision 3: Fields in interfaces vs. methods-only

**Chosen: Both fields and methods.**

For a scripting language, requiring specific data fields is useful:

```stash
interface Identifiable {
    id,           // must have an 'id' field
    getId()       // must have a getId() method
}
```

This is more practical than methods-only — a sysadmin defining a `Server` interface can say "every server struct needs a `host` field" without forcing a getter pattern.

### Decision 4: No default implementations (v1)

**Chosen: No defaults.**

Default method implementations (like Java 8 interfaces or Rust traits with defaults) add significant complexity:

- Where does the default method's closure bind?
- What if two interfaces provide conflicting defaults?
- How does `self` resolution work for default implementations?

For a scripting language, pure contracts are sufficient for v1. Default implementations can be added later if there's demand.

### Decision 5: No interface composition/extension (v1)

Considered but deferred:

```stash
// NOT in v1:
interface ReadWrite : Readable, Writable {
    // combines both
}
```

Interface extension adds complexity (checking transitivity, handling diamond patterns in validation). For v1, a struct simply lists all interfaces it implements explicitly. Interface composition can be added later.

---

## 9. Edge Cases & Open Questions

### 9.1 Interface name collisions with structs/enums

```stash
struct Printable { ... }
interface Printable { ... }  // Error? Or shadowing?
```

**Recommendation:** Runtime error on redefinition in the same scope, just like redeclaring any other name. The resolver already handles this — `Declare()` can detect duplicates.

### 9.2 `is` ambiguity: struct name vs. interface name

```stash
struct Foo : Bar { ... }
interface Bar { ... }

let f = Foo {};
f is Bar;    // true — but is it because Foo implements Bar, or because "Bar" is a struct name?
```

**Resolution:** The `is` operator first checks struct type name match, then checks interface membership. Since `Bar` is an interface (not a struct), the struct name check fails, and the interface check succeeds. No ambiguity.

### 9.3 Dynamic field addition after struct definition

Stash structs currently don't allow adding new fields at runtime (`SetField` throws if the field doesn't exist in `_fields`). So interface field requirements are stable after validation.

### 9.4 Interface checking on non-user-defined structs

Built-in types like `CommandResult` (created by `$(...)`) are `StashInstance` values with a `StashStruct` template. They could theoretically implement interfaces too, but that would require extending built-in struct registration. **Defer for v1** — only user-defined structs declared with `:` can implement interfaces.

### 9.5 Parameter count vs. parameter names in interface methods

Interface method signatures only check parameter **count**, not names. This is consistent with Stash's dynamic typing — parameter names are implementation details:

```stash
interface Serializable {
    serialize(format)       // requires 1 parameter
}

struct Data : Serializable {
    value
    fn serialize(fmt) {     // "fmt" ≠ "format" — that's fine, count matches
        return self.value;
    }
}
```

### 9.6 Method default parameters

If a struct method has default parameters, what parameter count does the interface see?

```stash
interface Greetable {
    greet(name)   // requires 1 parameter
}

struct Bot : Greetable {
    fn greet(name = "world") {  // MinArity=0, Arity=1
        return "Hello, " + name;
    }
}
```

**Recommendation:** The interface checks against `Arity` (max parameter count), not `MinArity`. So `greet(name = "world")` has Arity 1 and satisfies `greet(name)`.

### 9.7 Interfaces in the Playground

The Stash Playground (Blazor WASM) runs the same interpreter. No special handling needed — interfaces work everywhere the interpreter runs.

---

## 10. Type Hint Enforcement in Interface Signatures

### 10.1 Syntax

Interface method signatures can optionally include type annotations for parameters and return types, using the same syntax as regular function declarations:

```stash
interface IPrintable {
    toString() -> string
}

interface ISerializer {
    serialize(data: string, format: string) -> string,
    deserialize(input: string) -> dict
}

interface IIdentifiable {
    id: int,
    name: string,
    getDisplayName() -> string
}
```

Type annotations in interfaces are **optional** — omitting them means "any type is acceptable" (consistent with Stash's dynamic typing). An interface can mix annotated and unannotated members:

```stash
interface IConfigurable {
    config,                    // field, any type
    reload() -> bool,          // method, must return bool
    validate(schema)           // method, parameter type unspecified
}
```

### 10.2 Enforcement Model: Two-Tier Checking

Type hint enforcement in interfaces follows Stash's existing two-tier model for type annotations:

**Tier 1 — Runtime (Interpreter):** Checks method existence, field existence, and parameter count. Type annotations are **stored but not enforced**. This is consistent with how Stash handles type hints everywhere — they are cosmetic at runtime.

**Tier 2 — Static Analysis (LSP/SemanticValidator):** Checks that type annotations on the implementing struct's methods/fields **match** the interface's type annotations. Mismatches produce **warnings**, not errors — again, consistent with Stash's existing type hint behavior.

### 10.3 Matching Rules

When an interface specifies a type annotation, the implementing struct's corresponding member **should** have a compatible annotation:

| Interface Signature       | Struct Implementation                | LSP Result                                                                                                        |
| ------------------------- | ------------------------------------ | ----------------------------------------------------------------------------------------------------------------- |
| `toString() -> string`    | `fn toString() -> string { ... }`    | ✅ Match                                                                                                          |
| `toString() -> string`    | `fn toString() { ... }`              | ⚠️ Warning: "Method 'toString' in 'User' should return 'string' as required by interface 'IPrintable'"            |
| `toString() -> string`    | `fn toString() -> int { ... }`       | ⚠️ Warning: "Method 'toString' in 'User' returns 'int' but interface 'IPrintable' requires 'string'"              |
| `serialize(data: string)` | `fn serialize(data: string) { ... }` | ✅ Match                                                                                                          |
| `serialize(data: string)` | `fn serialize(data) { ... }`         | ⚠️ Warning: "Parameter 'data' of 'serialize' in 'User' should be 'string' as required by interface 'ISerializer'" |
| `serialize(data: string)` | `fn serialize(data: int) { ... }`    | ⚠️ Warning: "Parameter 'data' of 'serialize' in 'User' is 'int' but interface 'ISerializer' requires 'string'"    |
| `name: string`            | `name` (field, no type hint)         | ⚠️ Warning: "Field 'name' in 'User' should be 'string' as required by interface 'IIdentifiable'"                  |
| `config` (no type hint)   | `config`                             | ✅ Match (no type constraint)                                                                                     |
| `config` (no type hint)   | `config: dict`                       | ✅ Match (struct is more specific than interface requires)                                                        |

**Key principle:** If the interface doesn't specify a type, any type on the struct is valid. If the interface specifies a type but the struct omits it, that's a warning (the struct should be explicit). If both specify types and they differ, that's a warning.

### 10.4 Runtime Representation Changes

The `StashInterface` runtime type needs to store type annotation metadata:

```
StashInterface
├── Name: string
├── RequiredFields: List<InterfaceField>          // name + optional type hint
│   └── InterfaceField { Name: string, TypeHint: string? }
├── RequiredMethods: List<InterfaceMethod>         // name, param count, param types, return type
│   └── InterfaceMethod { Name: string, Arity: int, ParameterTypes: List<string?>, ReturnType: string? }
└── ToString() → "<interface IPrintable>"
```

This is slightly richer than the minimal representation in Section 4.1. The type metadata is stored at definition time but only consumed by the LSP analysis layer — the runtime interpreter ignores it during conformance checking.

### 10.5 AST Changes

The `InterfaceDeclStmt` AST node stores type annotations parsed from the interface body:

- **Method signatures:** Parameter types (`List<Token?>`) and return type (`Token?`), reusing the same representation as `FnDeclStmt`.
- **Field requirements:** Field name (`Token`) and optional type hint (`Token?`), reusing the same representation as `StructDeclStmt` field types.

### 10.6 Implementation Impact

| Component               | Additional Work                                                                                                                                                                             |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Parser**              | Parse type annotations in interface method signatures (`: Type` on params, `-> Type` on return) and field type hints (`: Type`). Reuse existing `FnDeclaration()` logic for method parsing. |
| **Interpreter**         | Store type metadata on `StashInterface` but skip type checking during `VisitStructDeclStmt` validation. No additional runtime cost.                                                         |
| **SemanticValidator**   | New validation pass in `VisitStructDeclStmt`: for each interface the struct implements, compare type annotations and emit warnings on mismatches.                                           |
| **SymbolCollector**     | Include interface type annotations in symbol info for hover tooltips (e.g., hovering over an interface method shows its expected types).                                                    |
| **TypeInferenceEngine** | When inferring the type of a struct method call, can use the interface's declared return type as a hint if the method itself lacks a type annotation.                                       |

### 10.7 Example

```stash
interface IPrintable {
    toString() -> string
}

interface ISerializer {
    serialize(data: string) -> string,
    deserialize(raw: string) -> dict
}

// ✅ Correct — all type hints match
struct Document : IPrintable, ISerializer {
    title: string,
    content: string

    fn toString() -> string {
        return self.title;
    }

    fn serialize(data: string) -> string {
        return json.stringify({ title: self.title, content: self.content });
    }

    fn deserialize(raw: string) -> dict {
        return json.parse(raw);
    }
}

// ⚠️ LSP warnings — missing return type annotations
struct LazyDocument : IPrintable {
    title

    fn toString() {    // Warning: should return 'string' per IPrintable
        return self.title;
    }
}
// Note: LazyDocument still WORKS at runtime — warnings are advisory only.
```

---

## 11. Best Practices & Future-Proofing

This section surveys interface/protocol/trait implementations across major languages and distills architectural recommendations to ensure Stash's interface design can grow without breaking changes.

### 11.1 Industry Survey

| Language       | Model                 | Conformance                                            | Key Features                                                                                                                                                     | Lesson for Stash                                                                                                              |
| -------------- | --------------------- | ------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| **Go**         | Implicit structural   | Automatic if methods match                             | Interface embedding (composition), empty `interface{}` as `any`, no generics originally (added in 1.18)                                                          | Simplicity wins adoption. Go started minimal and added generics later. Don't over-specify v1.                                 |
| **Rust**       | Traits                | Explicit `impl Trait for Type`                         | Default implementations, associated types, trait bounds (generic constraints), trait objects (`dyn Trait`), blanket implementations                              | Separation of declaration (`trait`) from implementation (`impl`) keeps the door open for retroactive conformance.             |
| **Swift**      | Protocols             | Explicit `: Protocol` on type                          | Protocol extensions (default impls), protocol inheritance, protocol composition (`Named & Aged`), conditional conformance, associated types, `is`/`as?` checking | Protocol extensions are the gold standard for adding defaults without polluting the protocol definition itself.               |
| **TypeScript** | Structural interfaces | Automatic (shape-based)                                | Interface extension (`extends`), intersection types (`&`), optional members (`?`), readonly, generics, declaration merging                                       | Structural typing pairs well with gradual typing. TS interfaces double as documentation and IDE tooling contracts.            |
| **Java/C#**    | Nominal interfaces    | Explicit `implements`/`:`                              | Default methods (Java 8+), static methods, multiple interface inheritance, generic interfaces                                                                    | Default methods solved the "interface evolution" problem — adding new methods without breaking existing implementations.      |
| **Python**     | ABCs + duck typing    | Explicit (register) or structural (`__subclasshook__`) | Abstract methods, virtual subclasses, `isinstance()` checking                                                                                                    | Hybrid model (explicit + structural) gives maximum flexibility. Python's ABCs are optional — duck typing remains the default. |

### 11.2 Architectural Recommendations

The following recommendations ensure Stash's v1 interface implementation can evolve without requiring breaking changes or major refactors:

#### R1: Store Full Signature Metadata (Even If Unused Now)

Store the complete method signature — parameter names, parameter types, return type, and async flag — in the `StashInterface` runtime type. Even though v1 only checks parameter count, having the full metadata available means:

- Future type checking can be added without changing the interface definition syntax
- The LSP can provide richer completions and hover information immediately
- Interface serialization (for package registry/metadata) captures the full contract

**Already addressed:** Section 10.4 specifies the richer `InterfaceField` and `InterfaceMethod` structures.

#### R2: Separate Conformance Checking from Struct Evaluation

Structure the conformance check as an independent, extensible step rather than inlining it in `VisitStructDeclStmt`:

```csharp
// Good: separate, extensible
private void ValidateInterfaceConformance(StashStruct @struct, StashInterface iface, SourceSpan span)
{
    ValidateRequiredFields(@struct, iface, span);
    ValidateRequiredMethods(@struct, iface, span);
    // Future: ValidateTypeAnnotations(@struct, iface, span);
    // Future: ValidateAssociatedTypes(@struct, iface, span);
}
```

This makes it trivial to add new conformance checks (type annotations, generic constraints, associated types) without touching the struct evaluation logic.

#### R3: Interface Composition Is the Highest-Priority Future Feature

Across every surveyed language, interface/protocol/trait **composition** (combining multiple contracts into one) was added early and became essential:

- Go: interface embedding (`type ReadWriter interface { Reader; Writer }`)
- Swift: protocol inheritance (`protocol PrettyPrintable: Printable`)
- TypeScript: interface extension (`interface Shape extends Drawable, Serializable`)
- Rust: trait bounds (`T: Read + Write`)

**Recommendation:** The v1 internal representation should use a structure that trivially supports composition. Specifically, `StashInterface` should have an `Extends` field (initially empty) that references parent interfaces:

```
StashInterface
├── Name: string
├── Extends: List<StashInterface>              // empty in v1, enables composition later
├── RequiredFields: List<InterfaceField>
├── RequiredMethods: List<InterfaceMethod>
```

When composition is added, the conformance check simply walks the `Extends` chain and collects all transitive requirements. No changes to the checking logic — just more requirements to check.

#### R4: Default Implementations Should Use a Separate Mechanism

Swift's protocol extensions are the best model for default implementations:

```swift
// Swift: defaults are separate from the protocol definition
protocol TextRepresentable {
    var textualDescription: String { get }
}
extension TextRepresentable {
    var prettyDescription: String { return textualDescription }
}
```

**Recommendation for Stash:** If/when default implementations are added, use an `extend interface` syntax rather than putting bodies in the original `interface` block:

```stash
// Hypothetical future syntax
interface Printable {
    toString() -> string         // required — no body
}

extend Printable {
    fn toDebugString() -> string {   // default implementation
        return "[Debug] " + self.toString();
    }
}
```

This keeps the original `interface` block as a pure contract and avoids the diamond problem — if two extended interfaces provide conflicting defaults, the struct must explicitly implement the conflicting method.

**Architectural implication for v1:** Don't design `StashInterface` with a `DefaultMethods` field. If defaults are needed later, they'll live in a separate `InterfaceExtension` mechanism.

#### R5: Don't Rely Solely on String Name Matching

The v1 `is` operator uses string matching (`interface.Name == typeName`). This works when all interfaces are in the same environment, but breaks with:

- Interfaces with the same name from different modules
- Future namespaced interfaces

**Recommendation:** Use object identity (reference equality) for the internal conformance check, and string matching only as the user-facing `is` operator's resolution step. Specifically:

```csharp
// In VisitStructDeclStmt: store resolved StashInterface references (not names)
struct.Interfaces.Add(resolvedInterface);  // reference, not string

// In VisitIsExpr: resolve the name to an interface, then check reference
var resolved = LookupType(typeName);
if (resolved is StashInterface iface)
    return instance.Struct.Interfaces.Contains(iface);
```

This is already implied by Section 4.2 (store "resolved interface references"), but worth making explicit as a principle.

#### R6: Reserve Room for Interface-as-Type-Constraint (Future)

Several languages use interfaces as parameter type constraints:

```typescript
// TypeScript
function print(item: Printable) { ... }

// Rust
fn print(item: &dyn Printable) { ... }
fn print<T: Printable>(item: &T) { ... }
```

Stash already has type hints on function parameters. A future version could enforce interface constraints:

```stash
// Future syntax (no changes needed now)
fn printItem(item: Printable) {
    io.println(item.toString());
}
```

**Recommendation for v1:** No code changes needed, but the `SemanticValidator` should already recognize interface names as valid type hints (currently it only recognizes built-in types, struct names, and enum names). Add interface names to the valid type hint list. This gives immediate LSP support (no "unknown type" warning for `item: Printable`) and prepares for future enforcement.

### 11.3 What to Avoid

Based on lessons from languages that struggled with interface evolution:

1. **Don't add generic type parameters to interfaces.** Stash is dynamically typed. Generic interfaces (`interface Collection<T>`) require a type system that doesn't exist. If generics are ever needed, they should be added to the language holistically, not interface-specific.

2. **Don't add covariance/contravariance.** This is only meaningful in statically typed languages with subtype relationships. Stash's dynamic typing makes this irrelevant.

3. **Don't allow runtime interface modification.** Interfaces should be immutable after definition. Allowing dynamic member addition to interfaces would make conformance checking unreliable.

4. **Don't make interfaces affect method dispatch.** Interfaces are metadata, not dispatch mechanisms. Method calls should always go through the struct's method dictionary directly, never through an interface vtable. This keeps the performance model simple and predictable.

5. **Don't require interfaces for the `is` operator to work on structs.** `value is StructName` must continue to work without interfaces. Interfaces augment `is`, they don't replace it.

### 11.4 Evolution Path

A realistic roadmap for interface feature evolution, ordered by value and implementation complexity:

| Phase              | Feature                                                              | Complexity | Dependencies                                |
| ------------------ | -------------------------------------------------------------------- | ---------- | ------------------------------------------- |
| **v1** (this spec) | Core interfaces: declaration, conformance, `is` checking, type hints | Medium     | None                                        |
| **v1.1**           | Interface composition: `interface RW : Readable, Writable { }`       | Low        | v1 (add `Extends` field)                    |
| **v1.2**           | Interface as valid type hint: `fn print(item: Printable)`            | Low        | v1 (add to SemanticValidator's valid types) |
| **v2**             | Default implementations via `extend interface`                       | Medium     | v1.1 (needs composition for full utility)   |
| **v2.1**           | Conditional conformance hints (LSP only)                             | Medium     | v2                                          |
| **v3**             | Runtime type constraint enforcement on function parameters           | High       | Language-wide type enforcement decision     |

---

## Appendix: Grammar Extension

```
// New production rule
interface_decl  → "interface" IDENT "{" interface_member ("," interface_member)* "}"
interface_member → IDENT "(" params? ")"    // method signature
                 | IDENT                     // required field

// Modified struct declaration
struct_decl     → "struct" IDENT (":" IDENT ("," IDENT)*)? "{"
                  (IDENT ",")*              // fields
                  (fn_decl)*                // methods
                  "}"
```

## Appendix: Example Test Cases

```stash
// Basic interface definition and implementation
interface Printable {
    toString()
}

struct Item : Printable {
    name
    fn toString() {
        return self.name;
    }
}

let item = Item { name: "Widget" };
assert(item is Printable);                 // true
assert(typeof(Printable) == "interface");  // true
assert(item.toString() == "Widget");       // true

// Missing method → error at struct definition
interface Saveable {
    save(),
    load(path)
}

// This should fail: "Struct 'Broken' does not implement method 'load' (2 params) required by interface 'Saveable'"
// struct Broken : Saveable {
//     fn save() { }
// }

// Multiple interfaces
interface Named { name }
interface Aged { age }

struct Person : Named, Aged {
    name,
    age
}

let p = Person { name: "Alice", age: 30 };
assert(p is Named);    // true
assert(p is Aged);     // true

// Interface with field + method requirements
interface Configurable {
    config,
    reload()
}

struct Service : Configurable {
    config,
    host

    fn reload() {
        self.config = fs.readFile("config.json");
    }
}
```
