# Strict Command Syntax — Language Feature Specification

## Status: Draft

## Summary

Stash's shell command syntax `$(...)` follows a foundational principle: **commands never throw**. They always return a `CommandResult { stdout, stderr, exitCode }` value, leaving error handling to the caller. This is safe and explicit, but creates verbosity when the caller always wants to treat non-zero exit codes as errors.

The strict command syntax `$!(...)` is an opt-in alternative that **throws a `CommandError` on non-zero exit codes**. It is syntactic sugar for the common pattern of executing a command, checking the exit code, and throwing on failure.

---

## 1. Motivation

### 1.1 The Verbose Pattern

Today, treating a command failure as an error requires manual checking:

```stash
let result = $(deploy.sh --env production)
if (result.exitCode != 0) {
    throw { type: "CommandError", message: result.stderr }
}
```

Or with a helper function:

```stash
fn runOrThrow(cmd) {
    if (cmd.exitCode != 0) {
        throw { type: "CommandError", message: cmd.stderr }
    }
    cmd
}

runOrThrow($(deploy.sh --env production))
```

This is repetitive in scripts where most commands are expected to succeed, and failure should halt execution.

### 1.2 The Strict Alternative

```stash
$!(deploy.sh --env production)
```

One character of difference (`!`) communicates explicit intent: "I expect this command to succeed; throw if it doesn't."

### 1.3 Composition with Retry Blocks

Strict commands compose naturally with `retry` blocks (see `docs/specs/Retry Blocks.md`):

```stash
// Without strict commands — requires manual throw or until clause:
retry (3) {
    let r = $(deploy.sh)
    if (r.exitCode != 0) { throw { type: "CommandError", message: r.stderr } }
    r
}

// With strict commands — concise and intentional:
retry (3) {
    $!(deploy.sh)
}
```

---

## 2. Syntax

### 2.1 Basic Form

```stash
$!(command args...)
```

Identical to `$(...)` except for the `!` after `$`. All existing command features work: string interpolation, pipes, redirects, environment variables.

### 2.2 Examples

```stash
// Simple command:
$!(mkdir -p /opt/myapp)

// With interpolation:
$!(docker push ${registry}/${image}:${tag})

// With pipes:
$!(cat /etc/hosts | grep ${hostname})

// Chained strict commands:
$!(git add .)
$!(git commit -m "deploy: ${version}")
$!(git push origin main)
```

---

## 3. Semantics

### 3.1 Success (Exit Code 0)

When the command exits with code 0, `$!(...)` returns a `CommandResult` — identical to `$(...)`:

```stash
let result = $!(echo "hello")
println(result.stdout)     // "hello"
println(result.exitCode)   // 0
```

### 3.2 Failure (Non-Zero Exit Code)

When the command exits with a non-zero code, `$!(...)` throws a `CommandError`:

```stash
$!(false)  // exit code 1 → throws CommandError
```

The `CommandError` carries the full `CommandResult` for inspection:

```stash
try {
    $!(curl -f https://unreachable.example.com)
} catch (e) {
    println(e.type)        // "CommandError"
    println(e.message)     // "Command failed with exit code 7: curl -f https://unreachable.example.com"
    println(e.exitCode)    // 7
    println(e.stderr)      // "curl: (7) Failed to connect..."
    println(e.command)     // "curl -f https://unreachable.example.com"
}
```

### 3.3 Error Properties

The `CommandError` is a `RuntimeError` subtype with these properties:

| Property   | Type     | Description                              |
|------------|----------|------------------------------------------|
| `.type`    | `string` | Always `"CommandError"`                  |
| `.message` | `string` | Human-readable: includes exit code and command |
| `.exitCode`| `int`    | The non-zero exit code                   |
| `.stderr`  | `string` | The command's stderr output              |
| `.stdout`  | `string` | The command's stdout output (partial)    |
| `.command` | `string` | The command string that was executed      |

### 3.4 Equivalence

`$!(cmd)` is semantically equivalent to:

```stash
fn __strict_command(cmd) {
    let r = $(cmd)
    if (r.exitCode != 0) {
        throw {
            type: "CommandError",
            message: "Command failed with exit code ${r.exitCode}: ${cmd}",
            exitCode: r.exitCode,
            stderr: r.stderr,
            stdout: r.stdout,
            command: cmd
        }
    }
    r
}
```

Note: The `throw { type: "...", message: "..." }` syntax uses Stash's dict-based custom error construction (see Language Specification §Error Handling). This is the standard mechanism for throwing typed errors in Stash — the dict fields are mapped to error properties by the runtime.

---

## 4. Interaction with Other Features

### 4.1 With `try` Expression

```stash
let result = try $!(risky-command)
if (result is Error) {
    log.warn("Command failed: ${result.message}")
}
```

### 4.2 With `try/catch`

```stash
try {
    $!(systemctl restart nginx)
} catch (e) {
    if (e.exitCode == 3) {
        log.warn("Service not found, installing...")
        $!(apt install -y nginx)
    } else {
        throw e  // Re-throw unexpected failures
    }
}
```

### 4.3 With `retry` Blocks

```stash
retry (3, delay: 2s) {
    $!(deploy.sh --env production)
}
```

Since `$!(...)` throws on failure, the retry block's default exception-based mechanism catches and retries it. No `until` clause needed.

### 4.4 With `retry` and Error Filtering

```stash
retry (3, delay: 2s, on: [CommandError]) {
    $!(systemctl restart myservice)
    $!(curl -f https://localhost:8080/health)
}
```

### 4.5 With Null-Coalescing

```stash
let version = try $!(node --version) ?? "unknown"
```

---

## 5. The `$(...)` Contract Is Unchanged

`$!(...)` is a **separate syntax** that does not affect `$(...)` in any way:

- `$(...)` continues to never throw. Its behavior is unchanged.
- Code using `$(...)` does not need to be updated.
- `$!(...)` is opt-in — developers choose which commands get strict checking.
- Both syntaxes can coexist in the same script:

```stash
// Strict: deployment must succeed
$!(deploy.sh --env production)

// Non-strict: health check is informational
let health = $(curl -s https://localhost:8080/health)
if (health.exitCode != 0) {
    log.warn("Health check failed, but deployment succeeded")
}
```

---

## 6. Design Decisions

### 6.1 Why `$!` Instead of a Flag or Function

| Alternative | Example | Drawback |
|------------|---------|----------|
| `$(cmd, strict: true)` | Verbose, changes command syntax | |
| `strict $(cmd)` | New keyword before `$` | Parsing complexity |
| `runStrict($(cmd))` | Function wrapper | Can't integrate at lexer level, double execution |
| `$!(cmd)` | Single character addition | None — concise, visible, unambiguous |

The `!` sigil is a common convention for "assertive" or "force" operations (Ruby's `!` methods, Swift's `!` unwrap). It signals "I'm asserting this will succeed."

### 6.2 Why Not Make `$(...)` Throw by Default

Changing `$(...)` to throw would be backward-incompatible and would violate the principle that commands are safe operations. Many scripts check exit codes conditionally — making all commands throw would force `try` wrapping everywhere. The opt-in `$!(...)` preserves the default safety while offering explicit intent for the strict path.

### 6.3 Why Not a Script-Level Setting

A script-level `use strict_commands` directive was considered but rejected:

- It changes the meaning of all `$(...)` calls in the file, which is surprising when reading code.
- Code copied between files would behave differently depending on the directive.
- Per-command opt-in with `$!(...)` makes intent visible at the call site.

---

## 7. Lexer and Parser Considerations

### 7.1 Token Type

A new `StrictCommandStart` token is introduced, triggered by the `$!` sequence followed by `(`. The lexer already handles `$` for command syntax and string interpolation; the addition of `$!(` as a distinct token start is unambiguous.

### 7.2 AST Node

The existing `CommandExpr` AST node gains a `Strict: bool` property, or a new `StrictCommandExpr` subclass is introduced. Both approaches are valid; the choice depends on how much shared logic exists.

### 7.3 Interpreter

The interpreter evaluates the command identically to `$(...)`, then checks the exit code. On non-zero, it constructs and throws a `CommandError`.

---

## 8. Static Analysis

- Warn when `$!(...)` is wrapped in a redundant exit code check:
  ```stash
  let r = $!(cmd)
  if (r.exitCode != 0) { ... }  // Warning: $! already throws on non-zero exit
  ```
- Suggest `$!(...)` when `$(...)` is always followed by an exit code check and throw.

---

## 9. Edge Cases

### 9.1 Interactive Commands

For commands with TTY passthrough (interactive mode), `$!(...)` still checks the exit code after the command completes. The TTY interaction is not affected.

### 9.2 Signal-Terminated Commands

When a command is killed by a signal (e.g., SIGKILL), the exit code is typically 128 + signal number (e.g., 137 for SIGKILL). `$!(...)` treats this as non-zero and throws.

### 9.3 Piped Commands

For piped commands (`$!(cmd1 | cmd2)`), the exit code is that of the **last command** in the pipeline, consistent with shell conventions. If any earlier command fails but the final command succeeds, no error is thrown.

This is the same behavior as `$(...)` for piped commands. The two syntaxes are identical in how they evaluate pipelines; `$!(...)` only differs in what happens after the final exit code is determined.
