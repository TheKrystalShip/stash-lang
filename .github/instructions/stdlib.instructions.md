---
description: "Use when: adding, modifying, or debugging built-in namespace functions (arr, dict, str, math, time, json, fs, path, env, sys, http, crypto, io, conv, process, log, term, store, encoding, ini, config, args, tpl, test/assert), working in BuiltIns/ files, or updating the Standard Library Reference docs."
applyTo: "Stash.Interpreter/Interpreting/BuiltIns/**"
---

# Standard Library Guidelines

The Stash standard library comprises 26 namespaces with ~280+ functions, each in its own file under `Stash.Interpreter/Interpreting/BuiltIns/`. See `docs/Stash — Standard Library Reference.md` for the complete API reference.

## All 26 Namespaces

| Namespace       | File                  | Scope                                                                                                                    |
| --------------- | --------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| **Global**      | `GlobalBuiltIns.cs`   | `typeof`, `len`, `range`, `exit`, `hash`, `sleep`                                                                        |
| **io**          | `IoBuiltIns.cs`       | `println`, `print`, `readLine`, `confirm`                                                                                |
| **conv**        | `ConvBuiltIns.cs`     | `toStr`, `toInt`, `toFloat`, `toHex`, `fromHex`, `toBool`                                                                |
| **arr**         | `ArrBuiltIns.cs`      | 37 functions — `push`, `pop`, `map`, `filter`, `sort`, `sortBy`, `reduce`, `groupBy`, `chunk`, `flatten`, `unique`, etc. |
| **dict**        | `DictBuiltIns.cs`     | 21 functions — `new`, `get`, `set`, `merge`, `map`, `filter`, `pick`, `omit`, `defaults`, etc.                           |
| **str**         | `StrBuiltIns.cs`      | 38 functions — `upper`, `split`, `replace`, `match`, `format`, `slug`, `wrap`, `title`, `padStart`, etc.                 |
| **math**        | `MathBuiltIns.cs`     | `abs`, `ceil`, `round`, `sin`, `cos`, `sqrt`, `pow`, `random`, `clamp`, `lerp`, etc.                                     |
| **time**        | `TimeBuiltIns.cs`     | `now`, `millis`, `format`, `parse`, `year`, `month`, `day`, `add`, `diff`, etc.                                          |
| **json**        | `JsonBuiltIns.cs`     | `parse`, `stringify`, `pretty`, `valid`                                                                                  |
| **ini**         | `IniBuiltIns.cs`      | `parse`, `stringify`                                                                                                     |
| **config**      | `ConfigBuiltIns.cs`   | `read`, `write`, `parse`, `stringify`                                                                                    |
| **http**        | `HttpBuiltIns.cs`     | `get`, `post`, `put`, `patch`, `delete`, `download`                                                                      |
| **process**     | `ProcessBuiltIns.cs`  | `exec`, `spawn`, `wait`, `kill`, `pid`, `signal`, `onExit`, `daemonize`, etc.                                            |
| **fs**          | `FsBuiltIns.cs`       | 27 functions — `readFile`, `writeFile`, `glob`, `walk`, `stat`, `readable`, `writable`, etc.                             |
| **path**        | `PathBuiltIns.cs`     | `abs`, `dir`, `base`, `ext`, `join`, `normalize`, `isAbsolute`, etc.                                                     |
| **env**         | `EnvBuiltIns.cs`      | `get`, `set`, `all`, `cwd`, `home`, `hostname`, `loadFile`, `saveFile`, etc.                                             |
| **args**        | `ArgsBuiltIns.cs`     | `list`, `count`, `parse`                                                                                                 |
| **crypto**      | `CryptoBuiltIns.cs`   | `md5`, `sha256`, `hmac`, `uuid`, `randomBytes`, etc.                                                                     |
| **encoding**    | `EncodingBuiltIns.cs` | `base64Encode`, `base64Decode`, `urlEncode`, `urlDecode`, `hexEncode`, `hexDecode`                                       |
| **term**        | `TermBuiltIns.cs`     | `color`, `bold`, `style`, `strip`, `table`, `clear`, etc. + color constants                                              |
| **sys**         | `SysBuiltIns.cs`      | `cpuCount`, `diskUsage`, `uptime`, `loadAvg`, `networkInterfaces`, etc.                                                  |
| **log**         | `LogBuiltIns.cs`      | `debug`, `info`, `warn`, `error`, `setLevel`, `setFormat`, etc.                                                          |
| **store**       | `StoreBuiltIns.cs`    | `set`, `get`, `has`, `keys`, `values`, `size`, `scope`, `all`, etc.                                                      |
| **tpl**         | `TplBuiltIns.cs`      | `render`, `renderFile`, `compile` (see TPL instructions)                                                                 |
| **test/assert** | `TestBuiltIns.cs`     | `test`, `describe`, `skip`, `assert.*` (see TAP instructions)                                                            |

## Registration Pattern

Every namespace follows the same static `Register` method pattern:

```csharp
public static class FooBuiltIns
{
    public static void Register(Environment globals)
    {
        var foo = new StashNamespace("foo");

        foo.Define("bar", new BuiltInFunction("foo.bar", 2, (interp, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'foo.bar' must be a string.");
            // implementation
            return result;
        }));

        globals.Define("foo", foo);
    }
}
```

**Key rules:**

- `BuiltInFunction(qualifiedName, paramCount, handler)` — `paramCount` is exact count, or `-1` for variadic
- Error messages use format: `"First argument to 'namespace.function' must be a {type}."`
- All namespaces are registered in `Interpreter.DefineBuiltIns()` and frozen post-registration

### Capability-Gated Namespaces

Some namespaces are only registered when their capability is enabled:

| Gate        | Namespaces                                                                                                        |
| ----------- | ----------------------------------------------------------------------------------------------------------------- |
| Always      | io, conv, arr, dict, str, math, time, json, ini, config, path, tpl, store, crypto, encoding, term, sys, log, test |
| Environment | env                                                                                                               |
| Process     | process, args                                                                                                     |
| FileSystem  | fs                                                                                                                |
| Network     | http                                                                                                              |

## Cross-Platform Requirement

Stash runs on Linux, macOS, and Windows. All built-in functions must work across all three platforms. Use `RuntimeInformation` or `env.os` for platform-specific behavior. Avoid hardcoding Unix paths or shell commands.

## Shared Helpers (RuntimeValues.cs)

Reuse these utilities across built-in implementations:

| Helper                                   | Purpose                                                                       |
| ---------------------------------------- | ----------------------------------------------------------------------------- |
| `RuntimeValues.Stringify(value)`         | Convert any value to display string (handles lists, dicts, ranges, instances) |
| `RuntimeValues.IsTruthy(value)`          | Falsy: `null`, `false`, `0`, `0.0`, `""`                                      |
| `RuntimeValues.IsEqual(a, b)`            | Strict equality, **no type coercion** (5 ≠ "5")                               |
| `RuntimeValues.ToDouble(value)`          | Promote long to double for arithmetic                                         |
| `RuntimeValues.PadString(...)`           | Shared logic for `str.padStart`/`str.padEnd`                                  |
| `RuntimeValues.CreateCommandResult(...)` | Factory for `CommandResult` instances (stdout, stderr, exitCode)              |

## Runtime Types (Types/)

Built-ins interact with these runtime types:

| Type                           | Usage                                                                                                                                  |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| `StashDictionary`              | Mutable key-value store. Methods: `Get`, `Set`, `Has`, `Remove`, `Keys`, `Values`, `Freeze`. Keys must be string, int, float, or bool. |
| `StashNamespace`               | Container for built-in functions. Methods: `Define`, `Get`, `Freeze`.                                                                  |
| `StashInstance`                | Struct instances with named fields. Methods: `GetField`, `SetField`, `GetFields`. Reference equality.                                  |
| `StashStruct`                  | Struct type definition with field names.                                                                                               |
| `StashEnum` / `StashEnumValue` | Enum type and member values. Identity comparison.                                                                                      |
| `StashRange`                   | Range object (e.g., `1..10`). Properties: `Start`, `End`, `Step`.                                                                      |
| `IStashCallable`               | Interface for callable values (functions, lambdas, bound methods).                                                                     |

## LSP Integration (BuiltInRegistry.cs)

When adding a new function, also register its metadata in `Stash.Lsp/Analysis/BuiltInRegistry.cs` for IDE support:

```csharp
new NamespaceFunction("foo", "bar",
    new[] { new BuiltInParam("input", "string"), new BuiltInParam("count", "int") },
    ReturnType: "string",
    IsVariadic: false,
    Documentation: "Returns input repeated count times.\n@param input The string to repeat\n@param count Number of repetitions\n@return The repeated string")
```

Documentation uses `@param name description` and `@return description` tags for structured hover display.

## Adding a New Built-In Function

Checklist:

1. **Implementation** — Add function in the appropriate `*BuiltIns.cs` file using the registration pattern above
2. **LSP metadata** — Register in `Stash.Lsp/Analysis/BuiltInRegistry.cs` with params, return type, and documentation
3. **Tests** — Add tests in the matching `Stash.Tests/Interpreting/*BuiltInsTests.cs` file using `{Function}_{Scenario}_{Expected}()` naming
4. **Documentation** — Update `docs/Stash — Standard Library Reference.md` with function table entry and usage example

## Test Pattern

Each namespace has a matching test file (`ArrBuiltInsTests.cs`, `StrBuiltInsTests.cs`, etc.). Tests use a shared helper:

```csharp
private static object? Run(string source)
{
    // Lex → Parse → Interpret source, then evaluate and return "result" variable
}

private static void RunExpectingError(string source)
{
    // Same pipeline, but Assert.Throws<RuntimeError>(...)
}
```

Test naming: `{Function}_{Scenario}_{Expected}()` — e.g., `SortBy_NumericKey_SortsCorrectly()`
