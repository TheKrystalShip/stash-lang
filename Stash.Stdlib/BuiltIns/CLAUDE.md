# Standard Library Guidelines

The Stash standard library is partitioned into namespace files under `Stash.Stdlib/BuiltIns/`. Every namespace — including the global namespace (Stash names visible without a prefix, like `typeof`, `len`, `range`, plus globally-visible structs and enums) — is authored using the **source-generator authoring shape**: a `[StashNamespace]`-attributed `static partial class` whose member methods, fields, and nested types declare the public Stash surface. The generator (`Stash.Stdlib.Generators`) emits the corresponding `Define()` partial and a `GeneratedStdlibRegistry.g.cs` that wires every namespace into `StdlibDefinitions._registry`. There is **no hand-maintained registry list** — adding a new attributed class is the only step required to register a namespace.

The global namespace is just `[StashNamespace(Name = "")]`. It is otherwise authored exactly the same way as every other namespace. Per-function capability gating (e.g. `exit` only present when `Environment` is granted) is expressed with `[StashFn(Capability = …)]`.

See `docs/Stash — Standard Library Reference.md` for the user-facing API and `.kanban/**/Stdlib Built-in Definition System — Source Generator.md` for the design that drives the authoring shape.

## File Layout

| File pattern          | Role                                                                                                                              |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `*BuiltIns.cs`        | One per Stash namespace. Holds the `[StashNamespace]` attributed partial class. `GlobalBuiltIns.cs` uses `Name = ""`.             |
| `*Impl.cs`            | Shared implementation helpers (e.g. `RegexImpl`, `SignalImpl`, `CurrentProcessImpl`, `NetSocketImpl`). Not Stash-visible.         |
| `StashJsonContext.cs` | Source-generated System.Text.Json type metadata used by `JsonBuiltIns`.                                                           |

## Authoring Shape

```csharp
namespace Stash.Stdlib.BuiltIns;

using Stash.Stdlib.Abstractions;

/// <summary>Sample namespace.</summary>
[StashNamespace]
public static partial class FooBuiltIns
{
    /// <summary>The answer.</summary>
    [StashConst] public const long ANSWER = 42;

    /// <summary>Doubles a number.</summary>
    /// <param name="n">The number.</param>
    [StashFn]
    public static long Double(long n) => n * 2;

    /// <summary>Repeats a string.</summary>
    /// <param name="s">The string.</param>
    /// <param name="count">Times to repeat. Defaults to 1.</param>
    [StashFn]
    public static string Repeat(string s, long count = 1)
        => string.Concat(System.Linq.Enumerable.Repeat(s, (int)count));
}
```

The C# method body **is** the function body — there is no lambda wrapper, no manual `SvArgs.*` extraction, and no qualified-name string. The generator emits the marshal boundary, hooks `<summary>`/`<param>` doc comments into the LSP/analysis metadata, and registers the namespace.

### Naming convention (C# symbol → Stash name)

| Element       | Rule                                           | Example                              |
| ------------- | ---------------------------------------------- | ------------------------------------ |
| Namespace     | Class name minus `BuiltIns`, lowercased        | `MathBuiltIns` → `math`              |
| Function      | C# method name with first character lowercased | `Abs` → `abs`, `IsEmpty` → `isEmpty` |
| Constant      | C# field name verbatim                         | `PI` → `PI`, `SIGHUP` → `SIGHUP`     |
| Struct / Enum | C# type name verbatim                          | `RegexMatch` → `RegexMatch`          |
| Field / Param | C# name verbatim, first char lowercased        | `Match` → `match`                    |

Acronyms must follow `UrlEncode` style — `URLEncode` is rejected (`STASH_GEN007`). Use `[StashFn(Name = "...")]`, `[StashParam(Name = "...")]`, etc. only as escape hatches when the C# convention can't satisfy the desired Stash name.

### Capability gating

Usually set on the namespace, so every function in it shares the gate:

```csharp
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class HttpBuiltIns { ... }
```

For the rare case where a single function in an otherwise-ungated namespace must be gated (e.g. the global `exit` requires `Environment`), use `[StashFn(Capability = …)]`:

```csharp
[StashFn(Capability = StashCapabilities.Environment, ReturnType = "never")]
public static void Exit(IInterpreterContext ctx, long code = 0L) { ... }
```

| Gate        | Namespaces                                                               |
| ----------- | ------------------------------------------------------------------------ |
| None        | most non-side-effecting namespaces (math, str, arr, dict, json, conv, …) |
| FileSystem  | `fs`, `archive`, `pkg`                                                   |
| Network     | `http`, `tcp`, `udp`, `ws`, `dns`, `ssh`, `sftp`, `net`                  |
| Process     | `process`, `args`                                                        |
| Environment | `env`                                                                    |
| Shell       | `shell`                                                                  |
| Scheduler   | `scheduler`                                                              |

## Parameter Marshalling

The generator maps C# parameter types to extraction calls. Supported types and their Stash labels:

| C# type                                  | Stash label | Extractor                               |
| ---------------------------------------- | ----------- | --------------------------------------- |
| `long`                                   | `int`       | `SvArgs.Long`                           |
| `double`                                 | `float`     | `SvArgs.Double`                         |
| `double` + `[StashParam(Type="number")]` | `number`    | `SvArgs.Numeric` (accepts int or float) |
| `string`                                 | `string`    | `SvArgs.String`                         |
| `bool`                                   | `bool`      | `SvArgs.Bool`                           |
| `byte`                                   | `byte`      | `SvArgs.Byte`                           |
| `byte[]`                                 | `buffer`    | `SvArgs.Buffer`                         |
| `List<StashValue>`                       | `array`     | `SvArgs.StashList`                      |
| `Dictionary<string, StashValue>`         | `dict`      | `SvArgs.Dict`                           |
| `IStashCallable`                         | `function`  | `SvArgs.Callable`                       |
| `StashValue`                             | `any`       | passthrough                             |
| `params StashValue[] rest`               | `...any`    | spread                                  |
| `IInterpreterContext` (first only)       | n/a         | injected, not arity-counted             |

Optional parameters use C# defaults:

```csharp
[StashFn]
public static double Round(double n, long precision = 0) { ... }
```

Stash `null` is a `TypeError` for non-nullable parameters and collapses to the C# default for nullable parameters (`string? prefix = null`).

`SvArgs.*` is the only extraction helper class — it is shared by both generator-emitted bodies and hand-rolled raw built-ins.

## Return Values

| C# return                           | Generator emits                                   |
| ----------------------------------- | ------------------------------------------------- |
| `void`                              | returns `StashValue.Null`                         |
| `StashValue`                        | passthrough                                       |
| `long` / `double` / `bool` / `byte` | wraps with `StashValue.From{Int,Float,Bool,Byte}` |
| `string`                            | wraps with `StashValue.FromObj`                   |
| `List<StashValue>`                  | wraps with `StashValue.FromObj`                   |
| `Dictionary<string, StashValue>`    | wraps with `StashValue.FromObj`                   |
| anything else                       | build error — return `StashValue` instead         |

Use `[StashFn(ReturnType = "...")]` when the Stash-visible label needs to be more specific than the C# return type can express (`number`, `secret`, `null`, `any`, named struct/enum).

## Escape Hatches

- `[StashFn(Raw = true)]` — drop to a hand-rolled handler with the canonical
  `(IInterpreterContext, ReadOnlySpan<StashValue>) -> StashValue` signature.
  Use only when auto-marshalling cannot express the body's needs (e.g. inspecting
  `StashEnumValue` or branching on union-typed args). XML `<param>` tags still
  drive the LSP signature.
- `NamespaceBuilder` (`Stash.Stdlib/Registration/`) — kept indefinitely as the
  underlying API. Used by `GlobalBuiltIns` and emitted from the generator. Direct
  use is reserved for cases the generator doesn't model (capability-aware
  globals, host-injected callbacks).

## Constants, Structs, Enums

```csharp
[StashConst] public const double PI = Math.PI;

[StashStruct]
public sealed record RegexMatch
{
    public string Value { get; init; } = "";
    public long Index { get; init; }
}

[StashEnum]
public enum Signal { Hup = 1, Int = 2, Term = 15 }
```

Field/member names follow the same lowercase-first-char rule. `[StashField]` overrides per-property names.

## Deprecation

```csharp
[StashFn, StashDeprecated("env.chdir")]
public static void Chdir(IInterpreterContext ctx, string dir)
    => CurrentProcessImpl.Chdir(ctx, dir, "process.chdir");
```

Produces a `DeprecationInfo` consumed by the analyzer's `SA0830` rule.

## Cross-Platform Requirement

Every namespace must work on Linux, macOS, and Windows. Use `RuntimeInformation` or `env.os` for branching. Avoid hard-coded Unix paths or shell commands. Capability-gated namespaces with platform-specific dependencies (e.g. `Renci.SshNet`) are loaded lazily — the namespace's factory is only invoked when the capability is enabled, so referenced types are never JITted in environments that don't support them.

## Adding a New Built-In

1. Implementation — drop a new method (or fields/types) into the appropriate `*BuiltIns.cs` partial. Add `[StashFn]` (or `[StashConst]`/`[StashStruct]`/`[StashEnum]`) and write XML `<summary>` + `<param>` docs.
2. Tests — add coverage in `Stash.Tests/` using the per-namespace test file convention.
3. Documentation — update `docs/Stash — Standard Library Reference.md`.

The generator picks the new symbol up automatically on the next build — there is no registration table to edit.
