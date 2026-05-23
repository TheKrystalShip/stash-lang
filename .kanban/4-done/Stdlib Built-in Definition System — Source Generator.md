# Stdlib Built-in Definition System — Source Generator

> **Status:** Draft v0.1
> **Created:** May 6, 2026
> **Author:** Architect
> **Stage:** Backlog — design in progress

---

## 1. Motivation

The current way `Stash.Stdlib` defines built-in functions, constants, structs, and enums is verbose, repetitive, and increasingly hard to maintain. The user-cited example, `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs`, is **1,699 lines** for one namespace. Even a small namespace like `math` carries significant boilerplate per function:

```csharp
ns.Function("abs", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
{
    StashValue n = args[0];
    if (n.IsInt) return StashValue.FromInt(Math.Abs(n.AsInt));
    if (n.IsFloat) return StashValue.FromFloat(Math.Abs(n.AsFloat));
    throw new RuntimeError("First argument to 'math.abs' must be a number.", errorType: StashErrorTypes.TypeError);
},
    returnType: "number",
    documentation: "Returns the absolute value of a number.\n@param n The number\n@return The absolute value");
```

For one math function, the author writes:

- A name string (`"abs"`)
- A parameter array with name + stringly-typed Stash type (`Param("n", "number")`)
- A repetitive lambda signature (`static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>`)
- Manual argument extraction with the qualified function name embedded in error messages (`SvArgs.Long(args, 0, "math.abs")`)
- A `returnType` magic string
- An `isVariadic` flag plus manual arity guards
- A `documentation` blob with `@param`/`@return` syntax — entirely disconnected from the actual parameter list

Multiplied across 50+ namespaces and ~600 functions, this is **a significant maintenance and readability tax** with multiple silent failure modes (docs that drift from signatures, type strings that lie about runtime extraction, missing `errorType` tags, etc.).

### Pain points enumerated

| #   | Pain                                                          | Consequence                                                                           |
| --- | ------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| 1   | Lambda signature repeated for every function                  | Visual noise; "wall of code" in large files                                           |
| 2   | Stringly-typed Stash types disconnected from extraction       | Easy to declare `"int"` then call `SvArgs.String` and not catch the bug until runtime |
| 3   | Qualified name passed manually to every `SvArgs.*` call       | Refactoring a function name = updating N call sites silently                          |
| 4   | Documentation as a magic string with `@param` syntax          | No tooling enforcement; drifts from actual params                                     |
| 5   | `isVariadic` + manual arity guards                            | Every variadic body re-implements `if (args.Length < N)`                              |
| 6   | Optional params via `args.Length` checks                      | No declarative way to say "default = 0"                                               |
| 7   | Constants need value + type-string + display-string           | Three sources of truth that can disagree                                              |
| 8   | Auto-prefix of error messages opt-in                          | Easy to forget `errorType: StashErrorTypes.TypeError`                                 |
| 9   | `StdlibDefinitions._registry` hand-maintained                 | Adding a namespace requires editing two files                                         |
| 10  | Cross-cutting concerns (deprecation, capability) on each call | Repetition; easy to forget                                                            |

---

## 2. Goals

1. **Drastically reduce the line count and visual noise** per built-in declaration. Target: a typical `math.abs`-class function should fit on one declarative line plus a doc comment.
2. **Single source of truth.** The C# method signature drives the Stash signature. The C# symbol name drives the Stash name. The XML doc comment drives the documentation. No re-declaration.
3. **Compile-time validation.** Authoring mistakes (unsupported types, missing docs, name collisions) become C# build errors, not runtime surprises.
4. **AOT-safe.** The system must work with Native AOT (the existing hard constraint for `Stash.Cli`, `Stash.Check`, `Stash.Format`). Therefore: **source generator, not reflection.**
5. **Zero or negligible runtime cost.** The generated code must produce the same shape of `BuiltInFunction.DirectHandler` the runtime expects, with the same hot-path properties.
6. **Incremental migration.** New and old systems coexist for the entire migration window. No big-bang. The old `NamespaceBuilder` stays as a lower-level escape hatch indefinitely.
7. **Gradual cleanup.** Once a namespace migrates, its row in the hand-maintained `StdlibDefinitions._registry` table is removed — the generator-emitted registry takes over discovery for migrated namespaces.

### Non-goals

- **Changing language semantics or runtime behaviour.** This is purely a C#-side authoring change. Existing tests must keep passing without modification.
- **Auto-generating the Markdown reference doc** (`docs/Stash — Standard Library Reference.md`). Stays hand-maintained for now.
- **Cross-namespace re-export.** Functions exposed in multiple namespaces (e.g. `RegexMatch` in both `str.*` and `re.*`) keep using shared `*Impl` helpers, as today.
- **Per-function capability override.** Capability gating remains namespace-level via `[StashNamespace(Capability = ...)]`.
- **Replacing the runtime delegate (`BuiltInFunction.DirectHandler`).** The generator produces `DirectHandler` instances — the runtime contract is unchanged.

---

## 3. Current State

The current registration pipeline:

```
Stash.Stdlib/BuiltIns/*BuiltIns.cs         (one file per namespace)
   └─ static Define() returns NamespaceDefinition
       └─ var ns = new NamespaceBuilder("math");
       └─ ns.Function(name, params, lambda, returnType, ...);
       └─ ns.Constant(name, value, type, display, ...);
       └─ ns.Struct(name, fields);
       └─ ns.Enum(name, members);
       └─ return ns.Build();

Stash.Stdlib/StdlibDefinitions.cs
   └─ private static readonly (Func<NamespaceDefinition>, StashCapabilities)[] _registry
       = [ (() => MathBuiltIns.Define(), None), (() => ProcessBuiltIns.Define(), Process), ... ];
```

`NamespaceDefinition` carries:

- A `StashNamespace` (the runtime callable container)
- `IReadOnlyList<NamespaceFunction>` (LSP/analysis metadata)
- `IReadOnlyList<NamespaceConstant>`
- `IReadOnlyList<BuiltInStruct>`
- `IReadOnlyList<BuiltInEnum>`
- `StashCapabilities` (gating)

Both runtime and metadata are produced from the **same builder calls** — that pairing is a strength of the current system and must be preserved.

`SvArgs` (in `Stash.Stdlib/SvArgs.cs`) provides typed extraction helpers (`Long`, `Double`, `String`, `Bool`, `Byte`, `StashList`, `Dict`, etc.) — these become the generator's runtime helpers.

---

## 4. Design Overview

### 4.1 Authoring shape (the "after")

A migrated `MathBuiltIns` looks like this:

```csharp
namespace Stash.Stdlib.BuiltIns;

using Stash.Stdlib.Abstractions;

[StashNamespace]
public static partial class MathBuiltIns
{
    /// <summary>The ratio of a circle's circumference to its diameter (π ≈ 3.14159).</summary>
    [StashConst] public const double PI = Math.PI;

    /// <summary>Euler's number, the base of natural logarithms (e ≈ 2.71828).</summary>
    [StashConst] public const double E = Math.E;

    /// <summary>Returns the absolute value of a number.</summary>
    /// <param name="n">The number.</param>
    [StashFn]
    public static StashValue Abs(StashValue n)
    {
        if (n.IsInt) return StashValue.FromInt(Math.Abs(n.AsInt));
        if (n.IsFloat) return StashValue.FromFloat(Math.Abs(n.AsFloat));
        throw new RuntimeError($"First argument must be a number.", errorType: StashErrorTypes.TypeError);
    }

    /// <summary>Returns the smallest integer greater than or equal to a number.</summary>
    /// <param name="n">The number to round up.</param>
    [StashFn]
    public static double Ceil(double n) => Math.Ceiling(n);

    /// <summary>Rounds a number to the nearest integer, or to a specified number of decimal places.</summary>
    /// <param name="n">The number to round.</param>
    /// <param name="precision">Number of decimal places. Defaults to 0.</param>
    [StashFn]
    public static double Round(double n, long precision = 0)
        => precision >= 0
            ? Math.Round(n, (int)precision, MidpointRounding.AwayFromZero)
            : Math.Round(n / Math.Pow(10, -precision), MidpointRounding.AwayFromZero) * Math.Pow(10, -precision);

    /// <summary>Returns the smallest of two or more numbers.</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <param name="rest">Additional numbers to compare.</param>
    [StashFn]
    public static double Min(double a, double b, params StashValue[] rest)
    {
        double result = Math.Min(a, b);
        foreach (var v in rest) result = Math.Min(result, ToDouble(v));
        return result;
    }
}
```

**Compared to today**, the per-function noise is gone:

- No string for the function name → derived from `Abs` → `abs`
- No param array → derived from C# method parameters
- No lambda signature wrapper → method body is the body
- No qualified name in error messages → generator-injected at the marshal boundary
- No `returnType` string → derived from C# return type
- No `isVariadic` flag → derived from `params StashValue[]`
- No documentation magic string → derived from XML `<summary>` and `<param>` tags
- No `Define()` method to write → generated as `partial`

### 4.2 Naming rules (C# symbol → Stash name)

| Element                     | Rule                                           | Examples                                                                                              |
| --------------------------- | ---------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Namespace                   | Class name minus `BuiltIns` suffix, lowercased | `MathBuiltIns` → `math`, `EnvBuiltIns` → `env`                                                        |
| Function                    | C# method name with first character lowercased | `Abs` → `abs`, `IsEmpty` → `isEmpty`, `LastExitCode` → `lastExitCode`, `ToJson` → `toJson`            |
| Constant                    | C# symbol verbatim (preserve case)             | `PI` → `PI`, `SIGHUP` → `SIGHUP`, `MaxInt` → `MaxInt` (so don't write `MaxInt` if you mean `MAX_INT`) |
| Struct / Enum / Enum member | C# symbol verbatim PascalCase                  | `RegexMatch` → `RegexMatch`, `Signal.Hup` → `Signal.Hup`                                              |
| Parameter                   | C# parameter name verbatim                     | `n` → `n`, `precision` → `precision`                                                                  |

**Override escape hatches** for cases where the C# name doesn't fit:

- `[StashFn(Name = "new")]` — when the desired Stash name is a C# reserved word (rare; the few cases were mostly renamed away in the namespace audit)
- `[StashParam(Name = "base")]` — for parameters whose C# name uses `@base` to escape
- `[StashConst(Name = "MAX_VALUE")]` — for cases where the C# field can't have the desired Stash name
- `[StashNamespace(Name = "myns")]` — if class naming convention can't satisfy the Stash name (escape hatch only — convention should win)

> **Trade-off acknowledged:** This hard-links C# symbol names to Stash built-in names. Renaming a C# method renames the public Stash function. This is intentional — the user explicitly accepted that risk in exchange for eliminating the magic-string maintenance burden. The override hatch prevents the only painful case (reserved-word collisions).

### 4.3 Acronym convention for function names

Because the rule lowercases only the first character, **authors must follow .NET acronym guidance**: write multi-letter acronyms as PascalCase words, not all-caps.

- ✅ `UrlEncode` → `urlEncode`
- ❌ `URLEncode` → `uRLEncode` (broken)
- ✅ `JsonParse` → `jsonParse`
- ❌ `JSONParse` → `jSONParse` (broken)
- ✅ `XmlReader` → `xmlReader` (matches Microsoft's convention since .NET 1.x)

The generator emits a **build error** for any `[StashFn]` whose lowered name contains two consecutive uppercase letters, with a fix hint to use `XmlReader`-style PascalCase.

---

## 5. Type Marshalling

### 5.1 Parameter type table

The generator emits an extraction call per parameter based on its C# type. The table below is the full set supported in Phase A.

| C# type                                      | Stash type label     | Generator emits                         | Notes                                           |
| -------------------------------------------- | -------------------- | --------------------------------------- | ----------------------------------------------- |
| `long`                                       | `int`                | `SvArgs.Long(args, i, "math.abs")`      | Qualified name injected at codegen              |
| `double`                                     | `float`              | `SvArgs.Double(args, i, "math.abs")`    |                                                 |
| `double` + `[StashParam(Type = "number")]`   | `number`             | `SvArgs.Numeric(args, i, "math.abs")`   | Accepts int OR float, returns double            |
| `string`                                     | `string`             | `SvArgs.String(args, i, "math.abs")`    |                                                 |
| `bool`                                       | `bool`               | `SvArgs.Bool(args, i, "math.abs")`      |                                                 |
| `byte`                                       | `byte`               | `SvArgs.Byte(args, i, "math.abs")`      |                                                 |
| `byte[]`                                     | `buffer`             | `SvArgs.Buffer(args, i, "math.abs")`    | Phase A addition; helper to be added if missing |
| `List<StashValue>`                           | `array`              | `SvArgs.StashList(args, i, "math.abs")` |                                                 |
| `Dictionary<string, StashValue>`             | `dict`               | `SvArgs.Dict(args, i, "math.abs")`      |                                                 |
| `StashValue`                                 | `any`                | `args[i]` (passthrough)                 | Author handles type checking                    |
| `IStashCallable`                             | `function`           | `SvArgs.Callable(args, i, "math.abs")`  | Helper to be added if missing                   |
| `params StashValue[] rest`                   | `...any`             | All remaining args spread into array    | Must be the last parameter (enforced by C#)     |
| `IInterpreterContext ctx` (first param only) | not exposed to Stash | passes `ctx` through                    | Special-cased; not counted toward arity         |

**Future-extensible:** the generator's type→extractor mapping is a flat lookup the generator owns. Adding `StashError`, `StashStruct`-of-named-type, or other typed handles is a localized change in Phase B+.

### 5.2 Optional parameters

C# default values express Stash optional semantics:

```csharp
[StashFn]
public static double Round(double n, long precision = 0) { ... }
```

generates the equivalent of:

```csharp
double precision_marshal;
if (args.Length > 1) {
    if (args[1].IsNull) precision_marshal = 0;       // Stash null OR missing
    else precision_marshal = SvArgs.Long(args, 1, "math.round");
} else {
    precision_marshal = 0;
}
```

**Wait** — earlier the user picked **"missing-arg → default; explicit Stash null → null (distinguishable)"**. That answer applies to **nullable C# types**:

- **Non-nullable `long precision = 0`** — Stash `null` is rejected with a `TypeError` ("must be int"). Missing arg uses `0`. The two cases are NOT distinguishable from the body's perspective; both produce `0` only when missing.
- **Nullable `string? prefix = null`** — Missing arg → `null` (the C# default). Explicit Stash `null` → also `null`. The body sees a C# `null` in both cases; ambiguity is acceptable here because the body has no way to behave differently anyway.
- **Nullable wanted-distinguishable case:** for any built-in that needs to tell "user passed null" from "user omitted the arg", the author drops to a `StashValue` parameter and inspects `args.Length` / `IsNull` manually. This is rare enough to not justify additional attribute syntax.

> **Decision:** Stash `null` for an optional parameter is a **type error** for non-nullable C# parameters (matching today's `SvArgs.*` behaviour) and is allowed (collapses to C# default) for nullable C# parameters. Missing-arg uses C# default in both cases.

### 5.3 Variadic parameters

`params StashValue[] rest` is the only supported variadic form. `params double[]` is **not** supported — variadic args must be `StashValue` so the body can decide per-element type handling.

The generator computes `arity = -1` (matches today's `isVariadic: true` semantics) and passes `args[fixed_count..]` as the array.

### 5.4 Return value

The generator wraps the return value:

| C# return type                   | Generator emits                                   |
| -------------------------------- | ------------------------------------------------- |
| `void`                           | `... ; return StashValue.Null;`                   |
| `StashValue`                     | `return body;` (passthrough)                      |
| `long`                           | `return StashValue.FromInt(body);`                |
| `double`                         | `return StashValue.FromFloat(body);`              |
| `string`                         | `return StashValue.FromObj(body);`                |
| `bool`                           | `return StashValue.FromBool(body);`               |
| `byte`                           | `return StashValue.FromByte(body);`               |
| `List<StashValue>`               | `return StashValue.FromObj(body);`                |
| `Dictionary<string, StashValue>` | `return StashValue.FromObj(body);`                |
| Anything else                    | **build error** — author must return `StashValue` |

**Async (`Task<T>`) returns are out of scope for Phase A.** Authors continue using the synchronous `task.GetAwaiter().GetResult()` pattern inside the body. Adding `Task<T>` support is a small Phase F extension if it proves valuable.

### 5.5 `IInterpreterContext` injection

If the **first** C# parameter is typed `IInterpreterContext`, the generator injects `ctx` and does not count it toward Stash arity:

```csharp
[StashFn]
public static StashValue Exec(IInterpreterContext ctx, string command) { ... }
// Stash signature: process.exec(command: string)
// Stash arity: 1
```

If `IInterpreterContext` appears anywhere other than position 0, the generator emits a **build error**. The convention is rigid because (a) it matches today's `DirectHandler` signature and (b) it removes any guessing about "is this the ctx or a regular arg?"

---

## 6. Documentation

### 6.1 XML doc comments are the only doc source

```csharp
/// <summary>Returns the absolute value of a number.</summary>
/// <param name="n">The number.</param>
[StashFn]
public static double Abs(double n) => Math.Abs(n);
```

The generator parses XML doc comments and produces the same `documentation` string that today's `NamespaceFunction` uses for LSP hover and signature help.

**Exact mapping:**

- `<summary>...</summary>` → first paragraph of documentation
- `<param name="n">...</param>` → `\n@param n ...` line
- `<returns>...</returns>` → `\n@return ...` line
- `<remarks>...</remarks>` → appended after summary
- `<example>...</example>` → appended; rendered as fenced code in markdown contexts

> **Constraint:** the generated documentation string must be **byte-for-byte compatible** with what the current code produces, so that LSP hover output is unchanged after migration. The generator includes a small reference test that reproduces existing docstrings exactly.

### 6.2 Missing-doc diagnostic

A `[StashFn]` method without a `<summary>` produces a Roslyn warning (`STASH_DOC001`). Configurable to error in `Stash.Stdlib.csproj` once migration is far enough along. Initially: warning, so migration of unfinished docs doesn't block builds.

`<param>` tags are recommended but not required (warning only) — many trivial functions don't need per-param docs.

---

## 7. Constants, Structs, Enums

### 7.1 Constants

```csharp
[StashNamespace]
public static partial class MathBuiltIns
{
    /// <summary>The ratio of a circle's circumference to its diameter.</summary>
    [StashConst] public const double PI = Math.PI;

    /// <summary>SIGHUP signal number.</summary>
    [StashConst, StashDeprecated("Signal.Hup")]
    public const long SIGHUP = 1;
}
```

Generator infers:

- **Stash name** = C# field name verbatim (`PI`, `SIGHUP`)
- **Stash type** = inferred from C# type (`double` → `float`, `long` → `int`, `string` → `string`, `bool` → `bool`)
- **Display value** = `value.ToString(CultureInfo.InvariantCulture)`. Override via `[StashConst(Display = "3.141592653589793")]` — needed for cases where the runtime literal is more precise than the desired hover label.

Field must be `const` or `static readonly` (build error otherwise — diagnostic `STASH_CONST001`).

### 7.2 Structs

```csharp
[StashStruct]
public sealed record RegexMatch(string Match, long Index, List<RegexGroup> Groups);
```

Generator produces the equivalent of today's `b.Struct("RegexMatch", new[] { Field("match", "string"), Field("index", "int"), Field("groups", "array") })`.

Field names are lowered (PascalCase → camelCase, same as function names): `Match` → `match`. Override per field via `[StashField(Name = "x")]`.

The C# record is **for declaration only** — it does not need to be the same type the runtime uses. The struct is registered as a `StashStruct` in the runtime as today. (Future extension: bidirectional marshalling, but not in this spec.)

### 7.3 Enums

```csharp
[StashEnum]
public enum Signal
{
    Hup = 1, Int = 2, Quit = 3, Kill = 9, Usr1 = 10, Usr2 = 12, Term = 15
}
```

- Enum name verbatim (`Signal` → `Signal`)
- Member names verbatim (`Hup` → `Hup`)
- Backing integer values are preserved as the runtime ordinal mapping (matches the current `SignalNumbers` dictionary)

For `[StashStruct]` and `[StashEnum]` types declared inside a `[StashNamespace]` class, the type registers in that namespace. Types declared **outside** a namespace class (top-level globals like `Signal`) register on the global namespace.

### 7.4 Deprecation

Continues the established pattern (declare alias in the **old** location, forward to a shared helper):

```csharp
[StashNamespace]
public static partial class ProcessBuiltIns
{
    /// <summary>Changes the current working directory. Deprecated; use env.chdir.</summary>
    [StashFn, StashDeprecated("env.chdir")]
    public static void Chdir(IInterpreterContext ctx, string dir)
        => CurrentProcessImpl.Chdir(ctx, dir, "process.chdir");
}
```

`[StashDeprecated("newName")]` produces the same `DeprecationInfo` the analyzer's `SA0830` rule already consumes — no analyzer change required. Applies to `[StashFn]`, `[StashConst]`. (Struct and enum deprecation remains unsupported — same as today.)

---

## 8. Capability Gating

```csharp
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class HttpBuiltIns { ... }
```

The generator wires this into the registry exactly the same way today's `(() => HttpBuiltIns.Define(), StashCapabilities.Network)` row does. Capability remains a namespace-level decision.

For namespaces with no capability requirement (the common case), the attribute parameter is omitted: `[StashNamespace] public static partial class MathBuiltIns { ... }` defaults to `StashCapabilities.None`.

---

## 9. Generator Architecture

### 9.1 Project layout

```
Stash.Stdlib.Generators/        (NEW — netstandard2.0)
  └─ StashNamespaceGenerator.cs       (IIncrementalGenerator entry point)
  └─ NamespaceModel.cs                (parsed model — class + members)
  └─ TypeMarshaller.cs                (C# → Stash type + extractor mapping)
  └─ DocCommentParser.cs              (XML → documentation string)
  └─ NamingRules.cs                   (PascalCase → camelCase, diagnostics)
  └─ CodeEmitter.cs                   (emits the Define() partial method + registry)
  └─ Diagnostics.cs                   (DiagnosticDescriptor catalogue STASH_GEN0xx)

Stash.Stdlib/Abstractions/      (NEW folder inside existing Stash.Stdlib)
  └─ StashNamespaceAttribute.cs
  └─ StashFnAttribute.cs
  └─ StashParamAttribute.cs
  └─ StashConstAttribute.cs
  └─ StashStructAttribute.cs
  └─ StashEnumAttribute.cs
  └─ StashFieldAttribute.cs
  └─ StashDeprecatedAttribute.cs

Stash.Stdlib/Stash.Stdlib.csproj
  └─ <ProjectReference Include="..\Stash.Stdlib.Generators\..." OutputItemType="Analyzer" />

Stash.Stdlib/obj/Generated/     (auto, not committed; visible in IDE)
  └─ Stash.Stdlib.Generators/.../MathBuiltIns.g.cs
  └─ Stash.Stdlib.Generators/.../StdlibRegistry.g.cs
```

### 9.2 Why `IIncrementalGenerator`

- Roslyn's incremental generator pipeline caches per-file work, so a single-method change reruns only the affected namespace
- Required for tolerable IDE responsiveness on a 50+ namespace codebase
- Pure source generators (no analyzer state) keep the build deterministic

### 9.3 What the generator emits per namespace

Given:

```csharp
[StashNamespace]
public static partial class MathBuiltIns
{
    [StashConst] public const double PI = Math.PI;

    /// <summary>Returns the absolute value of a number.</summary>
    [StashFn]
    public static double Abs(double n) => Math.Abs(n);
}
```

Generated `MathBuiltIns.g.cs` (roughly):

```csharp
namespace Stash.Stdlib.BuiltIns;

partial class MathBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("math");

        ns.Constant("PI", PI, "float", "3.141592653589793",
            "The ratio of a circle's circumference to its diameter.");

        ns.Function("abs", new[] { new BuiltInParam("n", "float") },
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                double n = SvArgs.Double(args, 0, "math.abs");
                return StashValue.FromFloat(Abs(n));
            },
            returnType: "float",
            documentation: "Returns the absolute value of a number.\n@param n The number.");

        return ns.Build();
    }
}
```

**Critical:** the generator emits **calls to the existing `NamespaceBuilder` API**. This guarantees:

1. The runtime contract is unchanged — same `BuiltInFunction.DirectHandler` shape, same metadata records
2. Old hand-written and new generated namespaces coexist transparently
3. Bugs in the generator surface as build errors or familiar runtime errors, not novel runtime semantics
4. The migration is genuinely incremental — switch a namespace and nothing else changes

### 9.4 Generated registry

The generator also emits one `StdlibRegistry.g.cs` per assembly:

```csharp
namespace Stash.Stdlib;

internal static class GeneratedStdlibRegistry
{
    public static IEnumerable<(Func<NamespaceDefinition> Factory, StashCapabilities Required)> All()
    {
        yield return (() => Stash.Stdlib.BuiltIns.MathBuiltIns.Define(), StashCapabilities.None);
        yield return (() => Stash.Stdlib.BuiltIns.PathBuiltIns.Define(), StashCapabilities.None);
        // ... one row per [StashNamespace] in this assembly
    }
}
```

The hand-maintained `StdlibDefinitions._registry` array is replaced by:

```csharp
private static readonly (Func<NamespaceDefinition>, StashCapabilities)[] _registry =
    GeneratedStdlibRegistry.All()
        .Concat(LegacyRegistry)  // hand-maintained list of NOT-YET-MIGRATED namespaces
        .ToArray();
```

`LegacyRegistry` shrinks as namespaces migrate; once empty, it is deleted entirely.

### 9.5 Per-assembly generator activation

Each assembly that wants to host attributed built-in declarations references the generator:

- `Stash.Stdlib` — primary
- `Stash.Tap` — has its own built-ins (`test`, `describe`, `assert.*`)
- `Stash.Tpl` — has `tpl.*` built-ins (currently registered in `Stash.Stdlib/BuiltIns/TplBuiltIns.cs`; may move)

Each gets its own `GeneratedStdlibRegistry.g.cs`. They register additively.

---

## 10. Diagnostics (Compile-Time Validation)

The generator emits Roslyn diagnostics. Codes follow the `STASH_GEN0xx` convention to avoid clashing with the analyzer's `SA0xxx`.

| Code           | Severity | Message                                                                                                                                                                                                                 |
| -------------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `STASH_GEN001` | Error    | Unsupported parameter type `T` on `[StashFn]` method `M`. Supported types: long, double, string, bool, byte, byte[], List<StashValue>, Dictionary<string, StashValue>, StashValue, IStashCallable, params StashValue[]. |
| `STASH_GEN002` | Error    | Unsupported return type `T` on `[StashFn]` method `M`. Use `StashValue` to return arbitrary values.                                                                                                                     |
| `STASH_GEN003` | Error    | `IInterpreterContext` parameter must be the first parameter of `[StashFn]` method `M`.                                                                                                                                  |
| `STASH_GEN004` | Error    | Reserved C# parameter name `@base` in method `M` requires `[StashParam(Name = "base")]`.                                                                                                                                |
| `STASH_GEN005` | Error    | Two `[StashFn]` methods in namespace `ns` produce the same Stash name `name`.                                                                                                                                           |
| `STASH_GEN006` | Error    | `[StashConst]` may only be applied to `const` or `static readonly` fields.                                                                                                                                              |
| `STASH_GEN007` | Error    | `[StashFn]` method `M` produces Stash name `xYZ` containing two consecutive uppercase letters. Use PascalCase acronyms (e.g. `UrlEncode` not `URLEncode`).                                                              |
| `STASH_GEN008` | Error    | `[StashNamespace]` class must be `partial` and `static`.                                                                                                                                                                |
| `STASH_GEN009` | Error    | `[StashStruct]` / `[StashEnum]` declared on a type that is not accessible from the generated `Define()` method.                                                                                                         |
| `STASH_DOC001` | Warning  | `[StashFn]` method `M` is missing an XML `<summary>` doc comment.                                                                                                                                                       |
| `STASH_DOC002` | Warning  | `[StashFn]` parameter `p` of method `M` is missing a `<param>` doc comment.                                                                                                                                             |

`STASH_DOC001` is a warning initially so migration of partially-documented namespaces doesn't block builds. Promote to error after Phase E.

---

## 11. Migration Strategy

Per-namespace, opt-in, side-by-side. The phases:

### Phase A — Foundation

- Create `Stash.Stdlib.Generators` netstandard2.0 project
- Implement attribute types in `Stash.Stdlib/Abstractions/`
- Implement `StashNamespaceGenerator` (incremental generator)
- Implement type marshalling, naming rules, doc parsing, code emission
- Wire generator into `Stash.Stdlib.csproj` as analyzer
- Add `GeneratedStdlibRegistry.g.cs` emission and merge into `StdlibDefinitions._registry`
- Implement diagnostics `STASH_GEN001`–`STASH_GEN009`, `STASH_DOC001`/`002`
- Write **marshal-error golden tests** in `Stash.Tests` proving that generated extraction reproduces the existing `SvArgs.*` error messages exactly
- **Acceptance:** generator produces no namespaces yet; existing tests all pass; hand-maintained `_registry` unchanged.

### Phase B — Pilot: `math`

- Convert `Stash.Stdlib/BuiltIns/MathBuiltIns.cs` to attributed form
- Remove its row from `StdlibDefinitions._registry`
- Verify all existing `MathBuiltInsTests` pass with **no test changes**
- Confirm LSP hover output is byte-identical for `math.abs`, `math.PI`, etc. via a generated-doc snapshot test
- **Acceptance:** all existing tests pass; LSP/analysis metadata unchanged; generated `MathBuiltIns.g.cs` is reviewable.

### Phase C — Medium pilot: `path` and `conv`

- Cover more parameter shapes (defaults, variadic, multiple types)
- Surface generator gaps (likely some C# type the table didn't anticipate)
- Refine type marshalling table and diagnostics

### Phase D — The big one: `process`

- The originally cited 1699-line file
- Exercises every advanced feature: `IInterpreterContext` injection, deprecation aliases, options-as-struct-or-dict (handled by author dropping to `StashValue`), structs, enums, embedded-mode checks, capability gating
- Validates the system at scale
- **Critical milestone:** if `process` migrates cleanly, the rest is mechanical

### Phase E — Mass migration

- Convert the remaining ~40 namespaces in batches grouped by shape:
  - Simple stateless: `str`, `re`, `conv`, `path`, `crypto`, `encoding`, `buf`, `json`, `csv`, `ini`, `xml`, `yaml`, `toml`, `time`
  - Capability-gated networking: `http`, `tcp`, `udp`, `ws`, `dns`, `ssh`, `sftp`, `net`
  - System-touching: `fs`, `archive`, `sys`, `signal`, `term`, `args`, `env`, `shell`, `prompt`
  - Higher-level: `pkg`, `task`, `scheduler`, `log`, `complete`, `alias`, `assert`, `test`, `tpl`
- Each batch: convert, run tests, verify LSP parity. No batch should require generator changes; if it does, the generator gets a Phase A-style fix first.

### Phase F — Cleanup (optional)

- Once `_registry`'s `LegacyRegistry` list is empty, delete it
- Promote `STASH_DOC001` from warning to error
- Decide fate of `NamespaceBuilder`:
  - **User's choice:** keep as the lower-level escape hatch indefinitely. Used by anything the generator can't model (e.g. dynamically-defined runtime built-ins, host-injected callbacks like the `AliasBuiltIns` static handlers).

---

## 12. Behaviour Parity & Testing

### 12.1 Existing tests are the contract

The Stash test suite (5,800+ tests) is the **regression line**. **No test changes during migration.** If a migrated namespace fails a test, the generator or the migrated declaration is wrong — the test stays.

### 12.2 New marshal-error golden tests

In `Stash.Tests/Stdlib/SourceGenerator/`:

- For each supported parameter type, a tiny `[StashFn]` test fixture is declared (in test-only attributed code)
- Tests assert: passing the wrong type produces an error message **identical** to what `SvArgs.*` produces today
- Tests assert: optional params with defaults work correctly with missing args, explicit Stash null, and explicit values
- Tests assert: variadic spreading works with 0, 1, and many extra args
- Tests assert: `IInterpreterContext` injection wires correctly

This guarantees that any future change to extraction semantics fails fast in a focused test rather than as a diffuse failure across the real test suite.

### 12.3 Doc-output snapshot tests

For each migrated namespace, a snapshot test asserts that the generated documentation strings match the previous hand-written ones byte-for-byte (until both forms exist; once a namespace migrates, the snapshot becomes the new baseline). Catches XML doc parsing regressions.

### 12.4 LSP/analysis no-change verification

LSP hover and analysis metadata are derived from `NamespaceFunction`, `NamespaceConstant`, etc. Since the generator emits calls to the same builder API that produces these records, no LSP/analysis change is required. Existing LSP tests (`Stash.Tests/Lsp/*`) act as a wide regression net for this.

---

## 13. Performance Considerations

- **Build time:** Roslyn incremental generators are designed for this scale. The `Stash.Stdlib` build adds maybe 1–3 seconds of generator work on a clean build, well-cached on incremental builds. Worth measuring on the pilot to confirm.
- **Runtime:** The generated `Define()` method is structurally identical to a hand-written one. The marshalling code paths use the same `SvArgs.*` helpers as today. **Zero runtime cost change** — verify via existing benchmarks (`benchmarks/run_all_benchmarks.sh`).
- **Startup:** Same as today — `Define()` is invoked once per namespace at VM/registry construction, lazily. No reflection.
- **Memory:** Same as today — same record types stored in the same lists.

If the auto-marshal path ever shows a measurable cost in profiling, the `[StashFn(Raw = true)]` escape hatch lets the author drop to a hand-written `static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) => StashValue` body. **Phase A includes this escape hatch in the design but does not need to use it on any pilot namespace** — only added to a namespace if profiling demands it.

---

## 14. Open Questions / Risks

1. **XML doc comment parsing variance.** Roslyn exposes the parsed `<summary>` etc. via `ISymbol.GetDocumentationCommentXml()`. Need to confirm whitespace/escape handling matches the current `documentation` strings. Mitigation: doc-output snapshot tests in Phase B catch any drift on day one.
2. **Generator interaction with Native AOT.** Source generators run at build time, not at runtime, so they're inherently AOT-safe. But the **emitted code** must avoid reflection, dynamic types, and unconstrained generics that the AOT trimmer can't see. The emitted code uses only existing `Stash.Stdlib` APIs that already pass AOT — low risk.
3. **Symbol collision between generated and hand-written namespaces.** Phase A diagnostic `STASH_GEN005` only catches collisions **within** the generator's view. If a hand-written `MathBuiltIns.Define()` and an attributed `MathBuiltIns` partial both exist, both `Define()` methods would be `partial` candidates and C# would error — fine. If two attributed classes claim the same namespace name, the generator errors. Cross-mode collisions (hand-written ns "math" + attributed ns "math" via `[StashNamespace(Name = "math")]` on a different class) are caught at registry construction with a thrown exception. Acceptable.
4. **Cross-platform types in attributed code.** Today, `SshBuiltIns.cs` references `Renci.SshNet` types lazily so WASM builds don't break. The new system must preserve this — the generator only inspects type names via the symbol table; it doesn't load runtime types. The `Define()` method bodies still execute lazily at registry construction, gated by capability. Same pattern works.
5. **Refactoring cost of name-from-symbol.** Renaming a C# method now silently renames the public Stash function. **Mitigation:** the analyzer can grow a rule that flags `[StashFn]` methods missing an explicit `Name` only when the C# name has changed (via Git history). **Out of scope for this spec** — flagged as a follow-up if it bites in practice.

---

## 15. Decision Log

| Date       | Decision                                                                                                     | Alternatives                                     | Rationale                                                            |
| ---------- | ------------------------------------------------------------------------------------------------------------ | ------------------------------------------------ | -------------------------------------------------------------------- |
| 2026-05-06 | Source generator + attributes (Option A)                                                                     | Strongly-typed fluent API, reflection attributes | AOT-safe, biggest verbosity win, single source of truth              |
| 2026-05-06 | Auto-marshal by default; `[StashFn(Raw=true)]` escape hatch                                                  | Always auto-marshal / always raw                 | Best of both worlds; raw path preserved for hot paths                |
| 2026-05-06 | XML doc comments only (no doc attributes)                                                                    | Attributes only / both                           | Native C# tooling, IDE support, no new syntax                        |
| 2026-05-06 | C# symbol name drives Stash name (verbatim for constants/structs/enums; first-char-lowercased for functions) | Name in attribute                                | Eliminates magic-string maintenance burden — explicit user request   |
| 2026-05-06 | Acronyms must be PascalCase (`UrlEncode`); generator errors on `URLEncode`                                   | Allow either                                     | Consistent .NET convention; clean Stash names                        |
| 2026-05-06 | Optional rule: missing-arg → C# default; explicit null → null (for nullable C# types only)                   | Collapse missing/null                            | Aligns with Stash's distinction between absent and null              |
| 2026-05-06 | Auto-prefix marshal errors with qualified name; author-thrown errors unchanged                               | Inject helper / wrap-and-rewrite                 | Least magical option that still fixes the boilerplate                |
| 2026-05-06 | `IInterpreterContext` injected via convention (first param of that type)                                     | Attribute opt-in / always inject                 | Matches ASP.NET minimal API ergonomics                               |
| 2026-05-06 | Capability gating stays namespace-level                                                                      | Per-function override                            | Out of scope; today's pattern works                                  |
| 2026-05-06 | No cross-namespace re-export feature                                                                         | Add re-export attribute                          | Existing shared `*Impl` pattern is fine                              |
| 2026-05-06 | Markdown reference doc stays hand-maintained                                                                 | Auto-generate                                    | Out of scope; can be added later                                     |
| 2026-05-06 | Pilot is `math` (small + simple); second pilot `path`/`conv` (more shapes); then `process`                   | Pilot `process` directly                         | De-risk generator basics before tackling the 1699-line file          |
| 2026-05-06 | `NamespaceBuilder` kept as lower-level escape hatch indefinitely                                             | Delete after migration                           | Useful for dynamic registrations and host-injected callbacks         |
| 2026-05-06 | Generator emits calls to the existing `NamespaceBuilder` API                                                 | Generate raw VM types                            | Guarantees runtime contract is unchanged; preserves migration safety |
| 2026-05-06 | Apply the new system to `Stash.Tap` and `Stash.Tpl` too                                                      | Stash.Stdlib only                                | Consistent authoring experience across all built-in hosts            |
| 2026-05-06 | Generator emits per-assembly registry replacing hand-maintained `_registry`                                  | Keep registry hand-maintained                    | Removes a recurring "forgot to add to two places" footgun            |

---

## 16. Acceptance Criteria

The spec is implementation-complete when:

- [ ] `Stash.Stdlib.Generators` project exists, references `Microsoft.CodeAnalysis.CSharp` (latest LTS), targets `netstandard2.0`
- [ ] All eight attribute types in `Stash.Stdlib/Abstractions/` exist with XML docs
- [ ] Generator emits valid `Define()` methods, recognised at C# build time
- [ ] All eleven diagnostics (STASH_GEN001–009, STASH_DOC001–002) emit at the right call sites with correct severity
- [ ] Marshal-error golden tests in `Stash.Tests/Stdlib/SourceGenerator/` cover every supported parameter type
- [ ] `MathBuiltIns` migrated; all `MathBuiltInsTests` pass with no changes
- [ ] LSP hover snapshot for `math.abs`, `math.PI`, `math.round` is byte-identical to pre-migration
- [ ] No regression in `benchmarks/run_all_benchmarks.sh` for `bench_namespace_calls.stash`
- [ ] `path` and `conv` migrated successfully without generator changes
- [ ] `process` migrated successfully; line count drops from 1699 to a target under 800 (target, not hard requirement — depends on body complexity)
- [ ] CHANGELOG entry under "Internal" section noting the new built-in declaration system
- [ ] CONTRIBUTING.md updated with a "How to add a built-in function" section showing the attributed form
