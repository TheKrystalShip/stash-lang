# Elevate — Scoped Privilege Elevation

> **Status:** Proposal
> **Created:** March 2026
> **Purpose:** Add scoped privilege elevation to Stash so that libraries and scripts can execute commands with elevated permissions without handling sudo/runas directly, keeping credentials entirely in the OS domain.

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Proposed Syntax](#2-proposed-syntax)
3. [Semantic Rules](#3-semantic-rules)
4. [Runtime Behavior](#4-runtime-behavior)
5. [Cross-Platform Strategy](#5-cross-platform-strategy)
6. [Impact on @stash/ufw and Other Packages](#6-impact-on-stashufw-and-other-packages)
7. [Implementation Roadmap](#7-implementation-roadmap)
8. [File-by-File Change Map](#8-file-by-file-change-map)
9. [Design Decisions & Alternatives](#9-design-decisions--alternatives)
10. [Edge Cases & Open Questions](#10-edge-cases--open-questions)
11. [Security Considerations](#11-security-considerations)
12. [Future Enhancements](#12-future-enhancements)
13. [Prerequisite Improvements](#13-prerequisite-improvements)

---

## 1. Problem Statement

The `$()` command expression redirects stdout/stderr and is **non-interactive**: the child process cannot prompt the user for credentials. When a library like `@stash/ufw` wraps commands that require root (`sudo ufw enable`), there is no clean mechanism for the consumer (the script author) to provide credentials interactively. The problem compounds when libraries abstract over commands — the consumer may not even know that root is required.

### Current workarounds and their flaws

| Workaround                            | Mechanism                                                | Why it fails                                                                                                                 |
| ------------------------------------- | -------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `$>(sudo -v)` before library calls    | Passthrough command pre-authenticates the sudo timestamp | Fragile: credentials expire (15-min default), non-discoverable, the consumer must know the library's internal implementation |
| `sudo -S` with piped password         | Password piped via stdin                                 | Password transits Stash memory, appears in logs and debugger state; every call becomes `echo "$pass" \| sudo -S ufw ...`     |
| `process.spawn()` + `process.write()` | Low-level spawning + writing to stdin                    | Complex polling loop, password lives in a Stash `string` variable, every library reimplements the same wheel                 |
| Pre-escalate the whole script         | Run the entire script under `sudo stash script.stash`    | Principle of least privilege violated — the entire interpreter (including file writes, network calls) runs as root           |

None of these approaches are satisfactory. They either push credential management onto the consumer, expose secrets in memory, or require every library author to solve the same problem.

### What elevate solves

The `elevate` block solves this **at the language level**:

- Credential acquisition is handled once, interactively, by the OS's native tools (`sudo`, `gsudo`)
- No password ever enters Stash data structures
- Libraries can call `$(ufw enable)` without any sudo wiring — the elevation context is inherited dynamically
- The privilege window is explicitly bounded by the block — outside it, commands run unprivileged
- Consumer code is clean: `elevate { ufw.enable(); }`

---

## 2. Proposed Syntax

### Basic form — platform default elevator

```stash
elevate {
    $(ufw enable);
    $(ufw allow 22/tcp);
    $(ufw allow 443/tcp);
}
```

### Named elevator — BSD/doas users, custom tools

```stash
elevate("doas") {
    $(pkg install nginx);
    $(pkg install certbot);
}
```

### Checking results inside the block

Commands inside the `elevate` block return `CommandResult` normally. Assign results to variables within the block:

```stash
elevate {
    let update = $(apt update);
    let upgrade = $(apt upgrade -y);
    if (upgrade.exitCode != 0) {
        io.println("Upgrade failed: " + upgrade.stderr);
    }
}
```

`elevate` is a statement (like `while` or `if`), not an expression — it cannot appear on the right side of an assignment.

### Library-level usage (the primary motivation)

```stash
import "@stash/ufw" as ufw;
import "@stash/systemd" as systemd;

elevate {
    ufw.config.enable();
    ufw.rules.allow(UfwRule { port: 22, proto: "tcp" });
    systemd.service.restart("nginx");
}
```

The library functions call `$(ufw enable)`, `$(systemctl restart nginx)` etc. internally — they don't need to know about or handle elevation. The `elevate` block's dynamic scope propagates into all called functions.

### Key syntactic properties

- `elevate` is a **statement** node, like `while` or `if` — not a special expression
- The block body is a standard `BlockStmt`
- The optional string argument specifies the elevation program; omitting it uses the platform default
- Commands inside the block that do not already begin with the elevator program are auto-prefixed
- If a command already starts with `sudo`, `doas`, `gsudo`, or the configured elevator, no double-prefixing occurs

---

## 3. Semantic Rules

### 3.1 Nesting

`elevate` inside `elevate` is a **no-op**. The inner block executes normally under the outer elevation context. The credential acquisition step (which is interactive) only runs once — when the outermost `elevate` block is entered. The `SemanticValidator` emits a **warning** (not error) for nested `elevate` to inform the author.

```stash
elevate {
    elevate {           // Warning: nested elevate has no effect
        $(ufw enable);  // elevated — outer context applies
    }
}
```

### 3.2 Already-elevated process

If the interpreter detects it is already running as a privileged user, it uses `Environment.IsPrivilegedProcess` (.NET 8+, AOT-safe):

- **Unix:** Checks `geteuid() == 0` internally
- **Windows:** Checks `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)` internally

This single API handles both platforms without conditional compilation or P/Invoke. It is the recommended detection mechanism for .NET 8+ applications.

The block executes **without any prefixing and without credential acquisition** — completely transparent. No `sudo -v` is run, no prompts appear. The block behaves exactly as if `elevate { }` were not there.

### 3.3 Dynamic scope (not lexical)

The elevation flag lives in `ExecutionContext`. Any function called during block execution — including functions from imported modules — inherits the elevation context:

```stash
fn restartService(name) {
    $(systemctl restart ${name});   // elevated if called inside elevate { }
}

elevate {
    restartService("nginx");       // $(systemctl ...) inside is elevated
}

restartService("nginx");           // NOT elevated — outside the block
```

This is the critical semantic property. Lexical elevation (only syntactically-interior commands) would break library calls entirely.

### 3.4 What gets elevated

Only `$()` and `$>()` command expressions are affected by the elevation context.

| Expression                    | Elevated inside block?                                                            |
| ----------------------------- | --------------------------------------------------------------------------------- |
| `$(ufw enable)`               | ✅ Yes — auto-prefixed                                                            |
| `$>(sudo passwd)`             | ✅ Already prefixed — runs as `$>(sudo passwd)` unchanged per §3.6 no-prefix rule |
| `process.spawn("ufw", [...])` | ❌ No — low-level, user has full control                                          |
| `process.exec("ufw enable")`  | ❌ No — low-level, user has full control                                          |
| `process.daemonize(...)`      | ❌ No                                                                             |

The rationale: `$()` and `$>()` are the high-level command-execution primitives that Stash abstracts. `process.*` functions are explicitly low-level escape hatches — users invoking them have opted out of language-level abstractions.

### 3.5 Elevation propagates into imports

The elevation context propagates into functions from imported modules when they are called from within the block. This is automatic — it is a consequence of the dynamic scope on `ExecutionContext`, not a special import behavior.

### 3.6 Commands that are already prefixed

A command that already begins with the elevation program (or any common elevation program) is **not double-prefixed**. The check compares the resolved program name against a static list:

```
["sudo", "doas", "gsudo", "runas"]
```

Additionally, if a command starts with the currently configured `_elevationCommand`, it is also skipped. This prevents both `sudo sudo ufw enable` and handles the case where the user explicitly includes `sudo` in a script that runs inside `elevate`.

### 3.7 EmbeddedMode (Playground / WASM)

The `elevate` keyword throws a `RuntimeError` in embedded mode, matching the existing `$>()` guard pattern:

```
RuntimeError: Privilege elevation is not available in embedded mode.
```

---

## 4. Runtime Behavior

The interpreter's handling of `VisitElevateStmt` follows three distinct phases.

### Phase 1: Credential Acquisition

Executed when the interpreter enters an `elevate` block for the first time (elevation not already active):

1. **Already-root check** — detect privileged execution (see §3.2). If true: set `_elevationActive = true`, skip all remaining steps, proceed to Phase 2.
2. **Already-active check** — if `_ctx.ElevationActive` is already `true` (nested elevate), proceed directly to Phase 2 with no changes.
3. **EmbeddedMode check** — if `_ctx.IsEmbedded`, throw `RuntimeError` immediately.
4. **Elevation program resolution** — resolve the elevator string (from the optional argument node, or platform default). Check for the elevator binary via raw `Process.Start("which", ["<elevator>"])` (Unix) or `Process.Start("where", ["<elevator>"])` (Windows) — internal process launch, not a `$()` call, to avoid pipe-context and elevation-flag interactions. If the program is not found, throw:
   ```
   RuntimeError: Cannot find elevation program 'sudo'. Install it or specify an alternative: elevate("doas") { ... }
   ```
5. **Interactive credential validation:**
   - Unix: execute `$>(sudo -v)` — passthrough to the terminal. The user sees the `[sudo] password for user:` prompt and authenticates. If the command exits non-zero (user cancelled or wrong password), throw:
     ```
     RuntimeError: Credential acquisition failed. User cancelled or authentication error.
     ```
   - Windows: `$>(gsudo cache on -d -1)` — passthrough to the terminal. Triggers the UAC consent dialog. The `-d -1` flag enables caching for the session so subsequent commands don't re-prompt. If the user denies UAC or the dialog fails, the command exits non-zero → throw `RuntimeError: Credential acquisition failed. User cancelled or authentication error.`
6. Set `_ctx.ElevationActive = true` and `_ctx.ElevationCommand = <resolved elevator>`.

### Phase 2: Block Execution

Execute the block body statements normally. In `VisitCommandExpr`, after resolving the program name and arguments:

1. Check `_ctx.ElevationActive`. If false, proceed as normal.
2. If true, compare the resolved program name against the no-prefix list (`["sudo", "doas", "gsudo", "runas", _ctx.ElevationCommand]`). If it matches any, proceed without modification.
3. If the program is not in the no-prefix list:
   - New `program` = `_ctx.ElevationCommand` (e.g., `"sudo"`)
   - New `ArgumentList` = `[originalProgram, ...originalArgs]`
   - `ProcessStartInfo` is built with `FileName = "sudo"`, `Arguments = "ufw enable"` (for example)

This modification happens at the ProcessStartInfo level — the AST node is not mutated.

### Phase 3: Cleanup

Executed in a `finally` block wrapping the Phase 2 body execution:

1. Set `_ctx.ElevationActive = false`
2. Set `_ctx.ElevationCommand = null`
3. If an exception occurred during Phase 2, re-throw after cleanup

No credential invalidation (`sudo -k`) is performed by default — see §9 Decision 5 for rationale. The OS's normal credential timeout (typically 5–15 minutes for sudo) governs expiry.

### Pseudocode

```csharp
public object? VisitElevateStmt(ElevateStmt stmt)
{
    // Phase 1: Acquisition
    if (EmbeddedMode)
        throw new RuntimeError(stmt.Span, "Privilege elevation is not available in embedded mode.");

    bool wasAlreadyActive = _ctx.ElevationActive;

    bool isPrivileged = IsAlreadyPrivileged();

    if (!wasAlreadyActive && !isPrivileged)
    {
        string elevator = stmt.Elevator != null
            ? (string)Evaluate(stmt.Elevator)
            : GetPlatformDefaultElevator();

        EnsureElevatorExists(elevator, stmt.Span);
        AcquireCredentials(elevator, stmt.Span);
        _ctx.ElevationCommand = elevator;
        _ctx.ElevationActive = true;
    }
    // If already privileged or already inside an elevate block, skip acquisition.
    // Commands run without prefixing when already root — no ElevationActive flag needed.

    // Phase 2: Execution
    object? result = null;
    try
    {
        // Execute visits the BlockStmt, which creates a child scope automatically.
        // Do NOT use ExecuteBlock() — its signature requires an explicit Environment.
        result = Execute(stmt.Body);
    }
    finally
    {
        // Phase 3: Cleanup — only if this was the outermost elevate
        if (!wasAlreadyActive)
        {
            _ctx.ElevationActive = false;
            _ctx.ElevationCommand = null;
        }
    }

    return result;
}
```

---

## 5. Cross-Platform Strategy

### Unix (Linux + macOS)

| Step                     | Implementation                                                                                |
| ------------------------ | --------------------------------------------------------------------------------------------- |
| Already elevated         | `Environment.IsPrivilegedProcess` (checks `geteuid() == 0` internally on Unix)                 |
| Default elevator         | `"sudo"`                                                                                      |
| Elevator discovery       | Raw `Process.Start("which", ["sudo"])` — internal check, not a `$()` call                     |
| Fallback elevator        | If `sudo` not found, try `"doas"` (common on OpenBSD, Alpine); if neither found, RuntimeError |
| Credential acquisition   | `$>(sudo -v)` — passthrough to terminal; user sees and interacts with the prompt              |
| Credential cleanup       | Not performed by default (OS timeout governs); future `cleanup: true` option                  |
| Double-prefix prevention | Program names `["sudo", "doas"]` are skipped                                                  |

macOS note: macOS ships with `sudo` but not `doas`. The fallback order on macOS is `sudo` only — no doas fallback unless the user explicitly specifies `elevate("doas")`.

### Windows

| Step                     | Implementation                                                                                                                                                               |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Already elevated         | `Environment.IsPrivilegedProcess` (checks `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)` internally)                                                          |
| Default elevator         | `"gsudo"` (open-source, supports UAC, available via `winget install gerardog.gsudo` or `scoop install gsudo`)                                                                |
| Elevator discovery       | Raw `Process.Start("where", ["gsudo"])` — internal check, not a `$()` call                                                                                                  |
| Credential acquisition   | `$>(gsudo cache on -d -1)` — enables credential caching for the current session and triggers the UAC consent dialog; subsequent `gsudo` calls skip UAC                       |
| Credential cleanup       | Not performed by default (session cache remains); future `cleanup: true` option would run `gsudo cache off`                                                                  |
| Fallback                 | If gsudo not found, RuntimeError with install instructions: `"Install gsudo: winget install gerardog.gsudo"`                                                                 |
| Double-prefix prevention | Program names `["gsudo", "runas"]` are skipped                                                                                                                               |

#### Windows: Detailed Credential Flow

1. **Discovery:** Run `where gsudo` via raw `Process.Start`. On failure, throw `RuntimeError` with install instructions.
2. **Cache activation:** Run `$>(gsudo cache on -d -1)` in passthrough mode. This triggers the UAC consent dialog (the standard Windows elevation prompt). The `-d -1` flag sets an unlimited duration so the cache remains active for the entire block. If the UAC consent is denied or the dialog is dismissed, `gsudo cache on` exits with a non-zero code → throw `RuntimeError: Credential acquisition failed.`
3. **Block execution:** Each `$()` and `$>()` command in the block is prefixed with `gsudo`. Because the cache is active, no further UAC prompts appear.
4. **Cleanup:** On block exit (in the `finally` block), cleanup is not performed by default. The gsudo cache expires when the console session ends. A future `cleanup: true` option would invoke `gsudo cache off`.

#### Windows: CacheMode Considerations

gsudo supports three cache modes configured via `gsudo config CacheMode`:
- **`Auto`** — Caches elevation per-console window. Most user-friendly.
- **`Explicit`** — Only caches when `gsudo cache on` is explicitly called. This is the default in recent gsudo versions.
- **`Disabled`** — No caching; every `gsudo` invocation triggers UAC.

The `elevate` block always runs `gsudo cache on` explicitly, which works regardless of the configured CacheMode:
- In `Auto` or `Explicit` mode, caching is enabled → subsequent commands are elevated without UAC prompts.  
- In `Disabled` mode, `gsudo cache on` fails silently (returns 0 but doesn't actually cache). Each command in the block triggers a separate UAC prompt. This is functional but annoying — the user should switch to `Auto` or `Explicit` mode. A future enhancement could detect this and emit a warning.

#### Windows: Headless and Non-Interactive Environments

UAC prompts require a desktop session. In environments without a desktop (SSH sessions, CI/CD runners, Windows services, headless containers), the UAC dialog cannot be displayed and `gsudo cache on` will fail.

**Behavior:** `gsudo cache on` exits with non-zero → `RuntimeError: Credential acquisition failed. UAC consent is required but no desktop session is available.`

**Workarounds:**
- Run the Stash interpreter "As Administrator" from the start (the already-elevated check handles this transparently)
- In CI/CD: configure the build agent to run with administrator privileges
- Via SSH: connect as an administrator user, or run `gsudo config CacheMode Disabled` and use `gsudo --copyns` (if available)

#### Windows: `runas` is NOT a Viable Elevator

The built-in `runas` command is intentionally excluded as a default elevator because:
- It opens a **new interactive window** rather than sharing the current console
- It does **not** forward stdin/stderr back to the caller
- It requires specifying a user account (`runas /user:Administrator cmd`)
- There is no credential caching mechanism

If a user explicitly specifies `elevate("runas")`, the commands will be prefixed with `runas`, but the behavior will be broken for captured commands `$()`. Only passthrough `$>()` has any chance of working, and even then the output goes to a separate window. The spec does not prevent this usage but does not recommend it.

### Playground / WASM (EmbeddedMode)

`elevate` throws `RuntimeError` immediately upon entry, before any block execution. The same guard pattern is used by `$>()`. No platform detection is performed.

### Known limitation: PolicyKit (`pkexec`)

On modern Linux distributions, some system operations are governed by PolicyKit rather than sudoers. A user may have polkit rules granting `systemctl start nginx` without needing `sudo`. In that scenario, `elevate { $(systemctl start nginx); }` runs `sudo systemctl start nginx`, which may be refused by sudoers even though `pkexec systemctl start nginx` would succeed.

`elevate` does not detect or integrate with PolicyKit. Users on polkit-governed systems can use the named elevator form as a workaround: `elevate("pkexec") { ... }`. However, `pkexec`'s credential caching behavior differs from `sudo -v` — it prompts per-command via a polkit agent dialog, not once per session. This makes it a less smooth experience.

### NOPASSWD compatibility

On systems where the user's sudoers entry includes `NOPASSWD`, `sudo -v` exits immediately with code 0 without prompting. Phase 1 credential acquisition succeeds transparently — no prompt appears, the block executes normally. This is the correct and expected behavior for password-less sudo configurations.

---

## 6. Impact on @stash/ufw and Other Packages

The `elevate` block is particularly motivated by the pattern in system utility packages like `@stash/ufw`, `@stash/systemd`, and `@stash/docker`, all of which currently carry their own sudo-wiring boilerplate.

### Before: ufw with sudo boilerplate

```stash
// lib/common.stash — before elevate
let _sudo = true;

fn set_sudo(enabled) {
    _sudo = enabled;
}

fn get_sudo() {
    return _sudo;
}

fn _cmd_prefix() {
    if (_sudo) {
        return "sudo ufw";
    }
    return "ufw";
}

fn exec(args) {
    return cli_exec.exec($"{_cmd_prefix()} {args}");
}
```

The consumer must know to configure `ufw.set_sudo(false)` if already running as root, or accept that sudo is always used. The library exposes implementation details (`set_sudo`, `get_sudo`) that are not part of its logical API.

### After: ufw without sudo boilerplate

```stash
// lib/common.stash — with elevate
fn exec(args) {
    return cli_exec.exec($"ufw {args}");
}
```

The `_sudo`, `set_sudo()`, `get_sudo()`, and `_cmd_prefix()` infrastructure is entirely eliminated. The library is elevation-agnostic.

### Consumer code comparison

**Before:**

```stash
import "@stash/ufw" as ufw;

$>(sudo -v);   // consumer must pre-authenticate

ufw.config.enable();
ufw.rules.allow_port(22, "tcp");
ufw.rules.allow_port(443, "tcp");
```

**After:**

```stash
import "@stash/ufw" as ufw;

elevate {
    ufw.config.enable();
    ufw.rules.allow("22/tcp");
    ufw.rules.allow("443/tcp");
}
```

A single credential prompt occurs at block entry. All library calls inside are automatically elevated. The library code contains no sudo logic — `elevate` handles it transparently.

### Same pattern for other packages

`@stash/systemd`:

```stash
elevate {
    systemd.service.enable("nginx");
    systemd.service.start("nginx");
    systemd.service.restart("postgresql");
}
```

`@stash/docker` (for privileged operations):

```stash
elevate {
    docker.network.create("internal", "bridge");
    docker.volume.create("data");
}
```

`@stash/apt`:

```stash
elevate {
    apt.update();
    apt.install(["nginx", "certbot", "postgresql-15"]);
}
```

### Windows package examples

`@stash/choco` (Chocolatey package manager):

```stash
elevate {
    choco.install(["nodejs", "python3", "git"]);
    choco.upgrade("all");
}
```

`@stash/winservice` (Windows service management):

```stash
elevate {
    winservice.restart("w3svc");     // IIS
    winservice.start("postgresql");
}
```

`@stash/winfw` (Windows Firewall):

```stash
elevate {
    winfw.allow_port(443, "tcp");
    winfw.allow_program("C:\\nginx\\nginx.exe");
}
```

The same `elevate` block pattern works identically on Windows — the only difference is the underlying elevator (`gsudo` instead of `sudo`). Package authors write platform-agnostic code; the interpreter handles elevator selection.

Every system-administration package benefits from the same simplification. The pattern is consistent and composable — multiple library calls from different packages share one credential prompt.

---

## 7. Implementation Roadmap

### Phase 1 — Core Language (~10 files)

| Step | Component                   | Change                                                                                                          | Effort  |
| ---- | --------------------------- | --------------------------------------------------------------------------------------------------------------- | ------- |
| 1.1  | `TokenType.cs`              | Add `Elevate` variant to the Keywords region                                                                    | Trivial |
| 1.2  | `Lexer.cs`                  | Add `["elevate"] = TokenType.Elevate` to `_keywords` dictionary                                                 | Trivial |
| 1.3  | `ElevateStmt.cs`            | **NEW FILE** — `ElevateStmt : Stmt` with `Expr? Elevator`, `BlockStmt Body`, `SourceSpan Span`                  | Small   |
| 1.4  | `IStmtVisitor.cs`           | Add `T VisitElevateStmt(ElevateStmt stmt)` method                                                               | Trivial |
| 1.5  | `Parser.cs`                 | Add `if (Match(TokenType.Elevate)) return ElevateStatement();` in `Statement()`; implement `ElevateStatement()` | Medium  |
| 1.6  | `ExecutionContext.cs`       | Add `bool ElevationActive { get; set; }` and `string? ElevationCommand { get; set; }` properties                | Trivial |
| 1.7  | `Interpreter.Statements.cs` | Implement `VisitElevateStmt` — Phase 1/2/3 from §4                                                              | Medium  |
| 1.8  | `Interpreter.Commands.cs`   | Check `_ctx.ElevationActive` in `VisitCommandExpr` and prepend elevator if needed                               | Small   |

### Phase 2 — Analysis Visitors (~5 files)

| Step | Component                | Change                                                                                | Effort  |
| ---- | ------------------------ | ------------------------------------------------------------------------------------- | ------- |
| 2.1  | `Resolver.cs`            | Add `VisitElevateStmt` — resolve optional Elevator expression, resolve body block     | Small   |
| 2.2  | `SemanticValidator.cs`   | Add `VisitElevateStmt` — warn on nesting, error on EmbeddedMode context if detectable | Small   |
| 2.3  | `SemanticTokenWalker.cs` | Add `VisitElevateStmt` — emit keyword semantic token for `elevate`, walk body         | Trivial |
| 2.4  | `SymbolCollector.cs`     | Add `VisitElevateStmt` — walk body to collect symbols declared inside the block       | Trivial |
| 2.5  | `StashFormatter.cs`      | Add `VisitElevateStmt` — format `elevate { ... }` and `elevate("doas") { ... }`       | Small   |

### Phase 3 — VS Code Extension (~2 files)

| Step | Component               | Change                           | Effort  |
| ---- | ----------------------- | -------------------------------- | ------- |
| 3.1  | `stash.tmLanguage.json` | Add `elevate` to keyword pattern | Trivial |
| 3.2  | `stash.json` (snippets) | Add `elevate` block snippet      | Trivial |

### Phase 4 — Tests + Documentation (~4 files)

| Step | Component              | What to cover                                                                                                        |
| ---- | ---------------------- | -------------------------------------------------------------------------------------------------------------------- |
| 4.1  | `LexerTests.cs`        | `elevate` tokenizes as `TokenType.Elevate`                                                                           |
| 4.2  | `ParserTests.cs`       | Basic elevate; elevate with elevator string; source span precision                                                   |
| 4.3  | `InterpreterTests.cs`  | Elevation flag propagation into function calls; nesting is a no-op; EmbeddedMode rejection; already-root passthrough |
| 4.4  | Language specification | Add `elevate` block section                                                                                          |

### Estimated total: ~18–22 discrete changes across 12–15 files.

---

## 8. File-by-File Change Map

### Stash.Core (Lexer, Parser, AST)

| File                                     | Change                                                                                                                                                                                    |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Stash.Core/Lexing/TokenType.cs`         | Add `Elevate` variant to the Keywords region                                                                                                                                              |
| `Stash.Core/Lexing/Lexer.cs`             | Add `["elevate"] = TokenType.Elevate` to `_keywords` dictionary (near existing keyword entries around line 106)                                                                           |
| `Stash.Core/Parsing/AST/ElevateStmt.cs`  | **NEW FILE** — `ElevateStmt : Stmt` with `Expr? Elevator`, `BlockStmt Body`, `SourceSpan Span`; `Accept<T>(IStmtVisitor<T> v) => v.VisitElevateStmt(this)`                                |
| `Stash.Core/Parsing/AST/IStmtVisitor.cs` | Add `T VisitElevateStmt(ElevateStmt stmt);`                                                                                                                                               |
| `Stash.Core/Parsing/Parser.cs`           | Add `if (Match(TokenType.Elevate)) return ElevateStatement();` in `Statement()`; implement `private Stmt ElevateStatement()` that consumes optional `( expr )`, then calls `ParseBlock()` |

### Stash.Interpreter

| File                                                       | Change                                                                                                                                                                                                         |
| ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Stash.Interpreter/Interpreting/ExecutionContext.cs`       | Add `bool ElevationActive { get; set; }` and `string? ElevationCommand { get; set; }` properties                                                                                                               |
| `Stash.Interpreter/Interpreting/Interpreter.Statements.cs` | Add `VisitElevateStmt` — Phase 1 acquisition, Phase 2 block execution, Phase 3 `finally` cleanup                                                                                                               |
| `Stash.Interpreter/Interpreting/Interpreter.Commands.cs`   | In `VisitCommandExpr`, after resolving program and arguments: if `_ctx.ElevationActive` and program not in no-prefix list, prepend `_ctx.ElevationCommand` to the argument list and set `FileName` accordingly |
| `Stash.Interpreter/Interpreting/Resolver.cs`               | Add `VisitElevateStmt` — resolve `stmt.Elevator` if non-null, then `Resolve(stmt.Body)` (scope is handled by `VisitBlockStmt` — do NOT add `BeginScope()`/`EndScope()` here)                                   |

### Stash.Analysis

| File                                             | Change                                                                                                                                                               |
| ------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Stash.Analysis/Visitors/SemanticTokenWalker.cs` | Add `VisitElevateStmt` — emit `keyword` semantic token at the `elevate` keyword span; recurse into body                                                              |
| `Stash.Analysis/Visitors/SemanticValidator.cs`   | Add `VisitElevateStmt` — track nesting depth; warn `"Nested elevate block has no effect"` if depth > 0; validate elevator expression is a string literal if provided |
| `Stash.Analysis/Visitors/SymbolCollector.cs`     | Add `VisitElevateStmt` — recurse into body; no new symbols at the elevate level itself                                                                               |
| `Stash.Analysis/Visitors/StashFormatter.cs`      | Add `VisitElevateStmt` — emit `elevate` keyword; emit `("...") ` if elevator present; format body block with standard indentation                                    |

### VS Code Extension

| File                                                           | Change                                                                                                |
| -------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` | Add `"elevate"` to the keyword control-flow pattern                                                   |
| `.vscode/extensions/stash-lang/snippets/stash.json`            | Add snippet: `elevate` → `elevate {\n\t$0\n}` and `elevate-named` → `elevate("${1:sudo}") {\n\t$0\n}` |

### Tests

| File                                           | Change                                                                                                                                                                   |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Stash.Tests/Lexing/LexerTests.cs`             | Add test: `elevate` scans as `TokenType.Elevate`                                                                                                                         |
| `Stash.Tests/Parsing/ParserTests.cs`           | Add tests: basic `elevate { }` produces `ElevateStmt` with null `Elevator`; `elevate("doas") { }` sets `Elevator` to string literal; source span covers the entire block |
| `Stash.Tests/Interpreting/InterpreterTests.cs` | Add tests: elevation flag set/cleared; nesting is no-op (flag stays true, no re-auth); EmbeddedMode throws `RuntimeError`; already-root proceeds without `sudo -v`       |

### Documentation

| File                                     | Change                                                                                                                   |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `docs/Stash — Language Specification.md` | Add `elevate` block section — syntax, semantics, examples; likely a new subsection after the command expression sections |

---

## 9. Design Decisions & Alternatives

### Decision 1: Statement vs. Expression

**Chosen: Statement.**

| Approach               | Pros                                                                                                                          | Cons                                                                                                                                                       |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Statement** (chosen) | Consistent with control-flow constructs (`while`, `if`, `for`); semantics match — elevation is about side effects, not values | Cannot be used inline in an expression position                                                                                                            |
| **Expression**         | Can assign result: `let r = elevate { ... }`                                                                                  | What is the return type if credential acquisition fails? Adds complexity to failure semantics; misleads authors into treating elevation as value-producing |

As a statement, `elevate` cannot appear in expression position (`let result = elevate { ... }` is a parse error). Commands inside the block return values normally, so results should be captured within the block via `let` declarations. If expression semantics are needed in the future, `elevate` can be promoted to `ElevateExpr : Expr` — but the added complexity (failure return types, interaction with `??` and `try`) is not justified for v1.

### Decision 2: Dynamic scope vs. Lexical scope

**Chosen: Dynamic scope.**

| Approach             | Pros                                                                                                                                      | Cons                                                                                                                                      |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| **Dynamic** (chosen) | Library functions called from within the block are automatically elevated; no changes needed to library code; correct behavior by default | Elevation extends to any function call, which might be surprising if misunderstood                                                        |
| **Lexical**          | Only syntactically-interior commands are elevated; easier to reason about statically                                                      | Library calls (e.g., `ufw.config.enable()` → `$(ufw enable)` inside a module) would NOT be elevated; breaks the primary use case entirely |

The dynamic scope is the only approach that makes library calls work without modification. It is clearly documented (§3.3) so authors understand the semantics.

### Decision 3: Auto-prefix ProcessStartInfo vs. Environment variable

**Chosen: Auto-prefix (modify ProcessStartInfo).**

| Approach                                        | Pros                                                                                                                                 | Cons                                                                                                                                                                       |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Auto-prefix** (chosen)                        | Works for both `$()` (captured) and `$>()` (passthrough); deterministic; platform-abstractable; no child process coordination needed | ProcessStartInfo is modified at the interpreter level (not visible at source level, can be surprising to debug)                                                            |
| **Environment variable** (e.g., `SUDO_ASKPASS`) | Doesn't require modifying process launch                                                                                             | Platform-specific; `sudo` still needs `-A` flag for ASKPASS to work in capture mode; env var persists into child processes unintentionally; doesn't work for Windows gsudo |

Auto-prefixing is the clean solution. The modification is transparent — the source `$(ufw enable)` just works, and the elevation context explains the behavior.

### Decision 4: Credential acquisition at block entry vs. lazy

**Chosen: At block entry (eager).**

| Approach                             | Pros                                                                                                               | Cons                                                                                                                                              |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Block entry** (chosen)             | User knows exactly when they'll be prompted (at the `elevate {` line); one prompt for the whole block; predictable | Prompts even if the first commands don't need elevation (rare in practice)                                                                        |
| **Lazy** (first elevated command)    | No prompt if somehow no commands need elevation                                                                    | User gets prompted mid-execution, potentially after significant work has already happened; much harder to implement cleanly in `VisitCommandExpr` |
| **Pre-check + interactive fallback** | `$(sudo -n true)` first; interactive only if no cached credentials                                                 | Extra subprocess; still needs interactive fallback; more moving parts                                                                             |

Prompt once at block start is the most predictable and user-friendly behavior. It mirrors how `sudo` is typically used interactively: `sudo <action>` prompts once for a session.

### Decision 5: Credential cleanup on block exit

**Chosen: No cleanup by default.**

Running `$(sudo -k)` at block exit would invalidate the sudo timestamp, limiting the credential window. However:

- It is annoying in development (re-prompting on every `elevate` block)
- The OS's standard sudo timeout (5–15 min) is already a reasonable security control
- `sudo -k` also affects other processes in the same session that may legitimately hold cached credentials (e.g., another terminal)

**Decision:** Default is no cleanup. A future `elevate(cleanup: true) { ... }` option can be added if there is demand. This defers the decision without foreclosing it.

### Decision 6: Elevator not found

**Chosen: RuntimeError at block entry.**

If `which sudo` (Unix) or `where gsudo` (Windows) fails, the error is thrown immediately with a helpful, actionable message:

```
RuntimeError: Cannot find elevation program 'sudo'. Install it or specify an alternative: elevate("doas") { ... }
```

For Windows gsudo not found:

```
RuntimeError: Cannot find elevation program 'gsudo'. Install it with: winget install gerardog.gsudo
```

Throwing at entry (before executing any block statements) is correct — it prevents partial execution under non-elevated conditions.

### Decision 7: Already-prefixed commands

**Chosen: Skip prefixing based on program name comparison.**

The no-prefix list `["sudo", "doas", "gsudo", "runas"]` plus the currently configured `_elevationCommand` is checked before prefixing. This prevents `sudo sudo ufw enable` from occurring when a script explicitly includes `sudo` in a command inside an `elevate` block.

The check is a simple string equality comparison on the resolved program name — cheap, deterministic, and clear.

---

## 10. Edge Cases & Open Questions

### 10.1 Nested elevate with different elevators

```stash
elevate("sudo") {
    elevate("doas") {     // Warning: nested elevate has no effect
        $(pkg install x); // elevated with "sudo", not "doas"
    }
}
```

**Decision:** The inner `elevate("doas")` is completely ignored. The outer elevator (`sudo`) remains active. The `SemanticValidator` emits a warning. If the author intended to switch elevators, they should not nest — they should use separate sequential `elevate` blocks.

### 10.2 elevate in async/parallel contexts

If Stash adds async tasks or parallel blocks, the elevation flag is on `ExecutionContext`. If child tasks receive a **copy** of the parent's `ExecutionContext`, the copy will include `ElevationActive = true` and `ElevationCommand = "sudo"`. This means parallel commands launched inside `elevate { }` will also be elevated — which is probably the correct behavior. This requires no special handling beyond ensuring `ExecutionContext` is properly copied for child execution contexts.

### 10.3 sudo credential timeout mid-block

The sudo credential cache (default 5–15 minutes) could expire during a long-running `elevate` block. In that case, a subsequent `$(command)` will receive a non-zero exit code from `sudo` (typically `1` with "sudo: a password is required" on stderr).

**Current behavior:** The command fails and Stash propagates the error normally. Recovery requires the user to exit and re-enter the `elevate` block.

**Open question:** Should the interpreter detect the sudo auth-expired pattern (specific exit code + stderr pattern) and automatically re-invoke `$>(sudo -v)` to re-authenticate? This adds significant complexity (polling stderr, pattern matching) and is deferred for a future enhancement.

### 10.4 Passthrough commands `$>()` inside elevate

`$>(command)` passes stdin/stdout/stderr through to the terminal. Inside `elevate`, `$>(command)` is also prefixed:

```stash
elevate {
    $>(visudo);   // becomes $>(sudo visudo)
}
```

This is correct behavior — `visudo` requires root. The passthrough mode means the user can interact with the program normally, and sudo prefixing still routes it through the elevated credential.

### 10.5 `process.spawn` / `process.exec` inside elevate

These are explicitly **not** affected by elevation (see §3.4). If a library uses `process.spawn("ufw", [...])` internally, the library must handle elevation itself. This is a deliberate design boundary — `process.*` is the low-level escape hatch. Authors using it have explicitly opted out of the abstraction layer.

This difference should be clearly documented in the language specification and the `@stash/ufw` / `@stash/systemd` package READMEs.

### 10.6 Pipe chains inside elevate

In a pipe expression, each command is a separate `CommandExpr` node:

```stash
elevate {
    $(cat /etc/passwd) | $(grep root) | $(wc -l);
}
```

Each command node independently goes through `VisitCommandExpr`. Each is prefixed:

```
sudo cat /etc/passwd | sudo grep root | sudo wc -l
```

This is correct and expected. Each step in the pipeline runs as root. If the author only wants the first command elevated (to read the file), they should not use the pipe inside `elevate` — they should capture the output outside the block, for example.

### 10.7 Redirected commands — known limitation

```stash
elevate {
    $(echo "new entry" >> /etc/hosts);    // ❌ FAILS — redirect is handled by Stash, not the elevated command
}
```

**This is a known limitation.** Stash's `VisitRedirectExpr` captures the inner command's stdout and then writes to the target file using `File.AppendAllText()` / `File.WriteAllText()` in C# — the Stash interpreter process itself, which is NOT elevated. The file write fails with an `IOException` (permission denied) even though the inner command runs elevated.

**Workaround:** Use an explicit shell invocation to keep the redirect inside the elevated process:

```stash
elevate {
    $(sh -c "echo 'new entry' >> /etc/hosts");    // ✅ works — sh handles the redirect as root
    $(tee -a /etc/hosts) | "new entry";           // ✅ alternative — tee writes as root
}
```

This limitation exists because Stash's redirect operators (`>`, `>>`, `2>`, etc.) are implemented in the interpreter, not delegated to a shell. The interpreter process is never elevated — only the child processes it spawns are.

### 10.8 Running as root already — complete no-op

When `geteuid() == 0` (Unix) or `IsInRole(Administrator)` (Windows), the `elevate` block is **completely transparent**:

- No `sudo -v` / `gsudo --check` is run
- No command prefixing occurs
- The block executes identically to a bare `{ }` block

This makes scripts that are sometimes run as root and sometimes not work correctly in both situations without modification.

### 10.9 What if the elevator program is found but non-functional?

`which sudo` may succeed on systems where `sudo` exists but has no user entries in `/etc/sudoers`. In that case, `sudo -v` (the credential acquisition step) will fail with a non-zero exit, which triggers the appropriate RuntimeError. The "elevator not found" check and the "credential acquisition failed" check are separate guard levels — the first is a quick fast-fail, the second catches misconfigured elevators.

---

## 11. Security Considerations

### No credentials in Stash memory

The design specifically avoids ever storing passwords in Stash variables, interpreter state, or logs. All credential handling is OS-native:

- **Unix:** `sudo` stores a timestamp in `/var/db/sudo/` (or `/run/sudo/`). The actual password is never passed through Stash.
- **Windows:** `gsudo` uses Windows Credential Manager / UAC consent flow. Stash passes no credentials.

This is a hard invariant — there is no code path in the `elevate` implementation where a password string exists in C# or Stash memory.

### Scoped minimum privilege

Elevation is explicitly bounded by the block. Outside the block, commands run with the normal user's permissions. This is the principle of least privilege applied at the language level — authors are nudged toward the smallest possible elevated scope.

### No credential forwarding over network

`elevate` is a local-only construct. It works with local credential managers (sudo timestamp, UAC). It has no concept of remote sudo, SSH forwarding, or credential delegation. If a script needs to run elevated commands on a remote host, `$>(ssh user@host sudo command)` (or similar) is outside the scope of `elevate` — use `$>()` directly.

### Audit trail preserved

Since `elevate` uses the real `sudo` / `gsudo` binaries, all elevated commands are logged to the standard system audit infrastructure:

- **Linux:** `/var/log/auth.log` or `journalctl -u sudo` via PAM
- **macOS:** ASL (Apple System Log) / Unified Logging via `log show --predicate 'process == "sudo"'`
- **Windows:** Windows Security Event Log via gsudo's audit integration

No Stash-specific audit infrastructure is needed — the OS already handles it.

### EmbeddedMode guard

The Stash Playground (Blazor WASM) and any embedded interpreter use cannot call `elevate`. The guard at Phase 1 entry (before any block execution occurs) prevents even partial elevation in those contexts.

### Command injection in auto-prefixed commands

The auto-prefixing in `VisitCommandExpr` modifies `ProcessStartInfo.FileName` and the argument array. It does **not** construct a shell command string — it uses the `ProcessStartInfo` structured API. There is no shell injection surface: the program name and each argument remain separate process arguments, not concatenated into a shell string. This is the same safe practice used throughout `Interpreter.Commands.cs`.

---

## 12. Future Enhancements

| Enhancement                   | Description                                                                                                                                                 |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `elevate(cleanup: true)`      | Invoke `sudo -k` / `gsudo --reset` on block exit to immediately invalidate credentials                                                                      |
| `elevate.isActive()` built-in | Query from inside a function whether an elevation context is currently active; useful for libraries that want to emit a warning if called without elevation |
| `sys.isElevated()` built-in   | Check whether the current process is already running as root/Administrator; complements `elevate`                                                           |
| Credential expiry detection   | Detect `sudo: a password is required` on stderr and auto-retry with `$>(sudo -v)` for long-running blocks                                                   |
| `elevate.withEnv(vars) { }`   | Pass specific environment variables into the elevated subprocesses; useful for configs that sudo strips via `env_reset`                                     |
| Per-platform elevator config  | Allow `stash.json` or `~/.stashrc` to specify the default elevator per platform, avoiding `elevate("doas")` boilerplate for doas users                      |
| `elevate` + timeout           | `elevate(timeout: 30m) { }` — hint that credentials should be considered valid for at most N minutes within the block                                       |
| LSP hover documentation       | The LSP hover on `elevate` shows the resolved elevator and whether credentials are currently cached (requires DAP-level state exposure)                     |

---

## 13. Prerequisite Improvements

Before implementing the `elevate` block, three interpreter infrastructure improvements are required. These are independent, non-breaking refactors that make the interpreter's process-spawning capabilities reusable — a hard requirement for `VisitElevateStmt` (which must spawn `$>(sudo -v)` for credential acquisition) and for the `VisitCommandExpr` elevation prefix logic.

### 13.1 Extract Process-Spawning Helpers

**Problem:** The process-spawning logic in `Interpreter.Commands.cs` is inline within `VisitCommandExpr`. Both the passthrough branch (terminal-inheriting) and the capture branch (stdout/stderr redirecting) build `ProcessStartInfo`, start the process, and read output — but this logic is not callable from any other visitor method.

`VisitElevateStmt` needs to:

- Run `$>(sudo -v)` in passthrough mode for credential acquisition (Phase 1)
- The existing passthrough branch in `VisitCommandExpr` is the exact logic needed, but it's embedded in a method that also handles command string building, tilde expansion, and result wrapping

**Solution:** Extract two `internal` helper methods:

```csharp
/// Runs a process in passthrough mode — inherits the terminal's stdin/stdout/stderr.
internal (string Stdout, string Stderr, int ExitCode) RunPassthrough(
    string program, List<string> arguments, SourceSpan span)

/// Runs a process in captured mode — redirects and reads stdout/stderr.
internal (string Stdout, string Stderr, int ExitCode) RunCaptured(
    string program, List<string> arguments, string? stdin, SourceSpan span)
```

`VisitCommandExpr` is then refactored to delegate to these helpers. The behavior is identical — only the factoring changes. Both methods throw `RuntimeError` on process start failure.

**Files changed:** `Stash.Interpreter/Interpreting/Interpreter.Commands.cs`

### 13.2 Add Elevation State to ExecutionContext

**Problem:** `ExecutionContext` has no fields for tracking elevation state. `VisitElevateStmt` needs to set a flag that `VisitCommandExpr` reads, and this flag must propagate through function calls (which share the same `ExecutionContext`) and into forked child interpreters (async/parallel tasks).

**Solution:** Add two properties to `ExecutionContext`:

```csharp
/// <summary>Whether commands should be auto-prefixed with the elevation program.</summary>
public bool ElevationActive { get; set; }

/// <summary>The elevation program to prefix (e.g., "sudo", "doas", "gsudo"). Null when not elevated.</summary>
public string? ElevationCommand { get; set; }
```

These default to `false` / `null` and require no constructor changes. The `Fork()` method must be updated to copy both fields into the child context:

```csharp
var forkedCtx = new ExecutionContext(taskScope)
{
    Output = _ctx.Output,
    ErrorOutput = _ctx.ErrorOutput,
    Input = _ctx.Input,
    CurrentFile = _ctx.CurrentFile,
    CancellationToken = cancellationToken,
    ElevationActive = _ctx.ElevationActive,        // NEW
    ElevationCommand = _ctx.ElevationCommand,       // NEW
};
```

**Files changed:** `Stash.Interpreter/Interpreting/ExecutionContext.cs`, `Stash.Interpreter/Interpreting/Interpreter.cs`

### 13.3 Fix Pseudocode in §4

**Problem:** The pseudocode in §4 uses `ExecuteBlock(stmt.Body)` but the actual `ExecuteBlock` method signature is `ExecuteBlock(List<Stmt> statements, Environment environment)` — it takes a statement list and an explicit environment, not a `BlockStmt`. Use `Execute(stmt.Body)` instead, which visits the `BlockStmt` node. `VisitBlockStmt` will create a child scope naturally from the current enclosing scope, which is the correct behavior — the point being avoided is manually constructing an `Environment` argument, not suppressing scope creation.

**Solution:** Replace `result = ExecuteBlock(stmt.Body);` with `result = Execute(stmt.Body);` in the §4 pseudocode. Add a comment explaining the scoping semantic.

> This fix has already been applied to the §4 pseudocode above.
