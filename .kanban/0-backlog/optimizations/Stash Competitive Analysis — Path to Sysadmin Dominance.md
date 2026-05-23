# Stash Competitive Analysis — Path to Sysadmin Dominance

**Status:** Strategic Analysis
**Created:** 2026-04-26
**Purpose:** Honest audit of Stash's current position relative to Bash, Python, PowerShell, Nushell, and Elvish. Identifies what must change for Stash to become the unambiguous choice for system administration scripting. This document is the strategic layer above individual feature specs — it sets priorities and explains the "why" behind each.

---

## Executive Summary

Stash is in a stronger position than most of its creators probably realize. The language core is genuinely excellent — the `$()` execution syntax, structs, enums, duration/IP/semver literals, `retry`/`timeout`/`secret`/`defer` blocks, signal handling, the scheduler, and a 33-namespace stdlib are features that no other sysadmin language has simultaneously. The tooling (LSP, DAP, formatter, 68 static analysis rules, VS Code extension) is production-quality and would embarrass most languages twice Stash's age.

But there are real gaps, and some of them are severe enough to be dealbreakers for the target use case. This document names them, ranks them, and proposes a path forward. No sugarcoating: Stash can become the dominant sysadmin scripting language, but only if it closes the gaps identified in **Tier A** and most of **Tier B**.

**The strategic positioning:** Stash's sweet spot is _scripting_ — automation, deployment pipelines, monitoring, infrastructure management — not interactive shell replacement (yet). The unique features (retry blocks, timeout blocks, secret type, scheduler, duration/bytesize/IP literals) are script-focused. Lean into this strength. The interactive shell gaps are real but secondary to winning the scripting segment first.

---

## 1. What Stash Already Gets Right

Before the criticism, a frank accounting of what's genuinely good — because this is the foundation you're building on.

### 1.1 Syntax and Developer Experience

**The `$()` syntax is one of Stash's best decisions.** It's explicit, unambiguous, and integrates naturally with expressions. In Python you write `subprocess.run(["ls", "-la"], capture_output=True).stdout.decode()`; in bash you write `ls -la` but immediately lose the ability to compose it with real expressions. In Stash:

```stash
let result = $(ls -la /opt);
if (result.exitCode != 0) { throw "ls failed: ${result.stderr}"; }
let entries = str.split(result.stdout, "\n");
```

This is better than both. The explicit `$()` boundary means the parser is never confused, and the return value is a structured object with `stdout`, `stderr`, and `exitCode` — not a raw string.

**The pipe and redirection syntax is first-class:**

```stash
$(cat /var/log/syslog) | $(grep "ERROR") >> "/tmp/errors.log";
$(make build) 2> "/tmp/build.log";
```

This is cleaner than Python's subprocess and as readable as bash.

**C-style braces and semicolons** are the right choice for a sysadmin language. The target audience knows C, Go, JavaScript, or C#. Not Haskell, not Ruby. Familiarity lowers adoption friction.

### 1.2 The Unique Type System

These features exist in no other sysadmin scripting language:

| Feature                                        | What it solves                                   | Why nobody else has it                 |
| ---------------------------------------------- | ------------------------------------------------ | -------------------------------------- |
| `duration` literals (`5s`, `2h30m`)            | Magic numbers in timeouts/intervals              | Go has `time.Duration` but no literals |
| `bytes` literals (`500MB`, `2GB`)              | Magic numbers in size comparisons                | No language has this                   |
| `ip` literals (`@192.168.1.1`, `@10.0.0.0/24`) | IP address math, CIDR containment                | No language has this                   |
| `semver` literals (`@v2.4.1`)                  | Correct version comparison                       | No language has this built-in          |
| `secret` type with auto-redaction              | Credential leak prevention                       | No general-purpose language has this   |
| `retry` blocks                                 | Network retry without boilerplate                | Most languages need libraries          |
| `timeout` blocks                               | Time-bounded execution without context threading | Go needs manual context plumbing       |
| `elevate` blocks                               | Privilege escalation with scope                  | No language has this                   |
| `scheduler` / `every`/`schedule` blocks        | Built-in cron-like scheduling                    | No scripting language has this         |
| `defer` statement                              | LIFO resource cleanup                            | Go has it; Python/Bash don't           |

This is a genuinely impressive suite. Any one of these would be a blog-worthy feature in another language. Stash has all of them simultaneously.

### 1.3 The Stdlib

33 namespaces, 492+ functions. The important ones for sysadmin:

- `ssh`, `sftp` — remote execution and file transfer built-in
- `http` — full HTTP client (GET/POST/PUT/DELETE/PATCH/HEAD, async, streaming headers)
- `net` — TCP, UDP, WebSockets, DNS resolution
- `process` — process management, spawn, signal sending
- `crypto` — hashing, HMAC, UUID, random bytes, AES-256-GCM encryption
- `json`, `yaml`, `toml`, `ini`, `xml`, `csv` — every config format covered
- `archive` — zip, tar, gzip built-in
- `log` — structured logging with levels, formats, and scoped context
- `scheduler` — OS service management (systemd, launchd, Task Scheduler)
- `sys` — signal handling, hostname, which, OS detection
- `term` — ANSI colors, terminal width, bold/italic/underline formatting
- `task` — parallel task execution (`task.all`, `task.race`, `task.limit`)
- `fs` — comprehensive file system including `fs.watch`, permissions, `fs.glob`

Python requires pip for many of these. Bash requires external tools for all of them. Stash has them out of the box.

### 1.4 The Tooling Story

LSP with 27 handlers (completion, hover, diagnostics, semantic tokens, formatting, rename, go-to-definition, call hierarchy, inlay hints). DAP with 18 handlers. A code formatter. 68 static analysis rules. A VS Code extension. A REPL with history. Native AOT compilation for fast startup. This is not the tooling of a toy language — it's production-grade.

---

## 2. Competitive Position Analysis

### 2.1 vs. Bash

**Where Stash already wins (for scripting):**

- Structured data (structs, enums, typed arrays) vs. bash's string-everything philosophy
- Error handling (`try/catch`, `try` expression, `retry` blocks) vs. bash's `$?` and `set -e` fragility
- Duration/bytesize/IP/semver types vs. bash magic numbers
- `secret` type vs. accidentally `echo $PASSWORD` in bash
- First-class functions, closures, lambdas vs. bash's limitation of function-as-string-returning-subprocess
- LSP/DAP/formatter/linter vs. no tooling for bash
- `timeout` blocks vs. bash's `timeout` command-prefix (applies to commands, not code blocks)
- `scheduler` vs. external cron
- Cross-platform (Linux, macOS, Windows) vs. bash's "technically on Windows via WSL/Git Bash"

**Where bash still wins:**

- **Interactive use** — `git status` just works. In Stash: `$(git status)`. The `$()` wrapper is the right design for scripts but creates friction for interactive use.
- **Streaming pipes** — `tail -f | grep | head` streams data between processes in real-time. Stash's pipe implementation is currently broken — the left side's stdout is never connected to the right side's stdin at all (all pipe tests are skipped due to deadlocks). This is a fundamental limitation.
- **`cd` and working directory** — No `cd` built-in. A dealbreaker for interactive use.
- **Glob expansion** — `rm *.log` just works. In Stash, globs don't work in `$()`.
- **Heredocs** — `cat << EOF ... EOF` patterns aren't possible.
- **Ubiquity** — Bash is on every Unix system by default. Stash requires installation.

**Verdict:** Stash is already a better scripting language than bash for any script over ~30 lines. The interactive shell gap is real but solvable. The ubiquity problem is a distribution/adoption challenge, not a language problem.

### 2.2 vs. Python

**Where Stash already wins:**

- No `import subprocess; result = subprocess.run(["cmd"], capture_output=True, text=True, check=True)` — `$()` is objectively superior
- No virtual environments, no `requirements.txt`, no package manager friction for scripts
- Duration/bytesize/IP/semver literals are genuinely unique
- `secret` type is genuinely unique
- `retry`/`timeout` blocks are cleaner than `tenacity`/`asyncio.wait_for`
- `scheduler` is cleaner than `APScheduler` or `celery`
- Cross-platform behavior is more consistent (Python's `os.path` vs `pathlib` split, subprocess quirks on Windows)

**Where Python still wins:**

- **Ecosystem** — PyPI has ~500,000 packages. Stash has 3. This is the single biggest competitive disadvantage. Every data-processing, cloud-provider, database, ML task has a Python package. Stash has `$()` wrappers.
- **Pattern matching** — Python 3.10+ structural pattern matching is excellent. Stash's `switch` is value-based only.
- **Generators / lazy sequences** — Python's generators are powerful for large-data processing. Stash has no equivalent.
- **List/dict comprehensions** — Python's `[x*2 for x in items if x > 0]` is very ergonomic. Stash's `arr.map`/`arr.filter` work but are more verbose.
- **f-string expressions** — Python's `f"{len(items):,} items"` with format specs. Stash interpolation is simpler.
- **Exception hierarchy** — Python's rich exception type system. Stash errors are flat.

**Verdict:** Python wins on ecosystem. Stash wins on shell integration and domain-specific types. The path to beating Python is: (a) grow the package ecosystem quickly, (b) structural pattern matching, (c) ensure every common sysadmin workflow is covered by first-party packages (`@stash/aws`, `@stash/kubectl`, `@stash/git`, etc.).

### 2.3 vs. PowerShell

**Where Stash already wins:**

- Cross-platform by design (PowerShell is better on Windows, clunky on Linux)
- Cleaner syntax for most programmers (C-style vs. `$variable` cmdlet syntax)
- `secret` type is genuinely unique
- Duration/IP/semver literals don't exist in PowerShell
- `retry`/`timeout`/`defer` blocks are cleaner than PowerShell equivalents

**Where PowerShell still wins:**

- **Object pipeline** — `Get-Process | Where-Object CPU -gt 100 | Sort-Object CPU | Select-Object -First 5`. PowerShell's pipeline passes .NET objects, not strings. This is genuinely powerful for Windows administration.
- **Windows integration** — AD, Exchange, Azure AD, Office 365, WMI, COM objects. PowerShell owns Windows administration.
- **cmdlet ecosystem** — Thousands of cmdlets from Microsoft and vendors.
- **Verbose/debug/warning streams** — PowerShell's separate output streams (1-6) are excellent for structured script output.

**Verdict:** Don't fight PowerShell on Windows enterprise. Own Linux/macOS administration, CI/CD, and cloud-native workflows. These are larger and faster-growing markets.

### 2.4 vs. Nushell

**Where Stash already wins:**

- C-style syntax is more familiar to most developers
- Better programming language features (closures, interfaces, generics in the type system sense, struct methods)
- `secret` type, `retry`/`timeout` blocks, duration/semver/IP literals — uniquely Stash
- LSP/DAP quality is higher
- `scheduler` built-in
- Richer stdlib in areas like crypto, archive, ssh

**Where Nushell wins:**

- Structured pipelines — Nushell pipelines carry structured data (tables/records), not strings
- Bare command execution — `git status` just works
- Better interactive shell experience
- Shell-native features (completions, aliases, config)
- `where`, `select`, `sort-by`, `group-by` as pipeline verbs — very ergonomic for tabular data

**Verdict:** Nushell is Stash's closest direct competitor for the "modern shell" segment. The key differentiators Stash needs are: (a) the unique safety features (secret type), (b) the scheduling/retry/timeout language primitives, and (c) the better stdlib breadth. The interactive shell gap must close.

### 2.5 vs. Elvish

Elvish is a thoughtful, well-designed shell but has limited adoption. Stash's LSP/DAP/formatter/stdlib breadth already exceeds Elvish's. The interactive shell gap applies here too.

---

## 3. Gap Analysis — Ranked by Impact

### Tier A — Existential Gaps (No Excuses)

These prevent Stash from competing in the target use case. They aren't nice-to-haves.

#### A1. Streaming Pipes

**Current state:** Pipes are **broken, not just buffered.** The investigation reveals:

- `OpCode.Pipe` exists and is dispatched correctly.
- `ExecutePipe` in `VirtualMachine.Strings.cs` validates that both sides are command results, then **just returns the right side's result** — the left side's stdout is never extracted, never passed as stdin to the right side's process.
- `ExecCaptured` in `VirtualMachine.Process.cs` does have a `stdin` string parameter and can write it to the child process, but `ExecuteCommand` always calls it with `null` for stdin. The plumbing is there but the wiring is missing.
- **All pipe tests are skipped** in `InterpreterTests.cs` with the message: *"Pipes are blocking the test suite due to a deadlock issue. Need to investigate and fix before re-enabling."*
- The language spec itself documents the intended design as **streaming concurrent execution** using OS-level pipe file descriptors — not buffered. This was the design intent from day one.

**Impact:** No pipe currently does anything useful. `$(echo hello) | $(cat)` doesn't pass "hello" to cat. `tail -f /var/log/nginx/access.log | $(grep "ERROR")` deadlocks. Every pipeline in the examples directory is silently incorrect.

**What it takes:** The language spec already specifies the correct design: all stages in a pipe chain launch simultaneously as OS-level processes connected by kernel-level pipe file descriptors — stdout of each stage flows directly into stdin of the next. The `ExecCaptured` string-buffering approach must be replaced with a streaming execution path for pipe chains. `ProcessStartInfo` already supports connecting stdout/stdin via `Process.StandardOutput` → `Process.StandardInput` stream connections, or via OS handle inheritance.

**Trade-off:** Streaming pipes mean the terminal nodes in a chain can't use `.stdout` for capture in the same way. The design needs to distinguish: single `$()` commands (buffered, `.stdout` available) from pipeline chains (streaming, `.exitCode` only, or captured into a file/variable at the terminating end). The spec's existing `$>()` passthrough syntax may be the right place to anchor this distinction.

**Priority:** Must-fix. This isn't a missing feature — it's a broken one. Every example involving pipes in the docs is wrong.

---

#### A2. Interactive Shell Essentials

**Current state:** No `cd`, no `~/.stashrc`, no tab completion, hardcoded `stash> ` prompt.

**Impact:** You cannot use Stash as your daily shell. You drop back to bash for interactive work, which means you never fully commit to Stash, which means you never develop the muscle memory, which means adoption stalls.

**What it takes (in priority order):**

1. `cd`, `pwd`, `pushd`, `popd` as built-in functions that change `Environment.CurrentDirectory`
2. `~/.stashrc` (or `~/.config/stash/init.stash`) loaded on REPL startup
3. Prompt customization — a `PROMPT` variable or function evaluated before each line
4. Tab completion — at minimum: file paths and command names from PATH; ideally: Stash variable and function names, namespace members
5. Glob expansion in `$()` — `$(rm *.log)` should expand `*.log` before execution

The "bare command execution" question (typing `git status` without `$()`) is a harder problem. Approach B from the "Can Stash Become a Shell?" analysis (REPL-only fallback when parse fails) is the right direction but should not be blocking items 1-5 above.

**Priority:** Items 1-3 are achievable in days. Items 4-5 in weeks. Do them.

---

#### A3. The CLI Boilerplate Structural Problem

**Current state:** 396 `dict.has` calls, 540 lines of option-checking boilerplate across 3 packages. Growing to 33+ packages multiplies this to ~6,000 lines of identical patterns.

**Impact:** The package ecosystem cannot scale. Packages are hard to maintain, hard to contribute to, and systematically use dict-based option bags instead of structs (violating the language's own design philosophy). This is a project sustainability issue, not a feature gap.

**What it takes:**

1. `@stash/cli` shared toolkit package — shared `exec`, `exec_json`, `parse_table`, `parse_properties`, `check_tool`
2. Struct-based option types instead of dicts for all new packages (per the user's coding preferences — **never use anonymous dictionary definitions where structs exist**)
3. `build_args(base, opts, spec)` spec-driven flag builder to eliminate per-option `dict.has` blocks

**Priority:** Before the package roadmap can scale, this infrastructure must exist. Fix the foundation.

---

### Tier B — Major Real-World Pain Points

These are the features that early adopters will complain about loudest. Not existential, but high-friction.

#### B1. `str.parseTable()` — CLI Output Parsing

**Current state:** Parsing `ps aux`, `df -h`, `kubectl get pods`, `docker ps`, `netstat -tlnp` requires manual string splitting. This is fragile and tedious.

**What it solves:** The single most common sysadmin pattern is "run a command, parse its columnar output, work with structured data." Making this a built-in function transforms `awk`-style scripting into clean structured code:

```stash
let procs = $(ps aux).stdout.parseTable();
// → [{ USER: "root", PID: "1", %CPU: "0.0", ... }, ...]

let highCPU = arr.filter(procs, (p) => conv.toFloat(p["%CPU"]) > 50.0);
```

**Design considerations:**

- Must handle variable whitespace column separators
- Must handle headers with spaces in names (e.g., `%CPU`, `START TIME`)
- Must handle right-aligned vs left-aligned columns
- Optional: hint-based parsing (`columns: ["USER", "PID", ...]`) for formats with no header

**Priority:** High. This single function eliminates more scripting boilerplate than any other stdlib addition.

---

#### B2. `lock` Block — File-Based Mutual Exclusion

**Current state:** Not implemented. Already designed in "Unique Language Concepts — Volume 2.md."

**What it solves:** Deployment scripts, cron jobs, and maintenance tasks must not run concurrently. File locking in bash requires `flock`, race-condition-prone check-and-create patterns, and manual cleanup. In Stash:

```stash
lock "/var/run/deploy.lock" {
    deploy(version);
}
// Lock atomically created, auto-released on exit, stale lock detection included
```

**Key design requirements:**

- POSIX advisory locking (`flock` on Linux/macOS, `LockFileEx` on Windows)
- PID-based stale lock detection
- Configurable wait timeout (`wait: 30s`)
- Guaranteed cleanup on SIGTERM/SIGINT/unhandled errors
- Works inside `defer` and `retry` blocks correctly

**Priority:** High. Every non-trivial deployment script needs this. The design is already done in Volume 2.

---

#### B3. `dry` Mode — Side-Effect Suppression

**Current state:** Not implemented. Already designed in "Unique Language Concepts — Volume 2.md."

**What it solves:** Infrastructure scripts need a "what would this do?" mode. Ansible has `--check`, Terraform has `plan`, Chef has `why-run`. No general-purpose language provides this. In Stash:

```stash
// $ stash deploy.stash --dry
// All $(cmd), fs.writeFile(), http.post(), process.spawn() print what they'd do
// fs.readFile(), http.get(), env.get() still execute (reads are safe)
```

**Key design requirements:**

- CLI flag `--dry` sets runtime mode on the VM
- Every write-effect built-in checks the flag and prints instead
- `sys.isDry()` for conditional logic
- `dry { }` block for scoped dry mode
- `dry.allow { }` to exempt logging/read operations from suppression
- Synthetic return values for suppressed operations (empty stdout, exitCode 0) to prevent null-dereference crashes

**Priority:** High. Terraform's `plan` command is a major reason people trust it for infrastructure changes. Stash needs this for the same reason.

---

#### B4. Structural Pattern Matching

**Current state:** `switch` is value-based (works on scalars). You cannot match on struct field combinations, array shapes, or type+value patterns.

**What it solves:**

```stash
// Process API responses without manual field checking:
let msg = response switch {
    { status: 200, body } => json.parse(body),
    { status: 404 } => null,
    { status: s } where s >= 500 => throw "Server error: ${s}",
    _ => throw "Unexpected response: ${response}"
}

// Process command results:
let result = $(git status) switch {
    { exitCode: 0, stdout: s } where str.contains(s, "nothing to commit") => "clean",
    { exitCode: 0 } => "dirty",
    { exitCode: _, stderr: e } => throw "git failed: ${e}"
}
```

**Design considerations:**

- Match on dict/struct field presence and values
- `where` guard clauses in patterns
- Binding matched values to names
- Must not conflict with existing `switch` expression syntax
- This is a significant parser and compiler addition

**Priority:** Medium-high. Python 3.10 structural match is a major reason Python is ergonomic for API-response processing. Stash's API-heavy stdlib calls deserve the same.

---

#### B5. `regex` Namespace (or enhanced `str`)

**Current state:** `str.match(text, pattern)` exists, but named captures, lookahead, replace-with-callback, and regex-based splits are not available. Text processing is a massive sysadmin use case.

**What it solves:**

```stash
// Named captures (currently only positional):
let m = regex.match("/var/log/nginx/access.log", "(?P<date>\d{4}-\d{2}-\d{2}) (?P<ip>[\d.]+)");
println(m.date);   // "2024-01-15"
println(m.ip);     // "192.168.1.100"

// Replace with callback:
let result = regex.replace(text, "(\d+)ms", (m) => "${conv.toInt(m[1]) * 1000}μs");

// Global find-all:
let ips = regex.findAll(logLine, "(\d{1,3}\.){3}\d{1,3}");
```

**Priority:** Medium-high. Log parsing, config file processing, output scraping — all need regex. What's there now is basic.

---

#### B6. Package Ecosystem — Tier 1 Packages

**Current state:** `@stash/docker`, `@stash/podman`, `@stash/systemd`. Everything else is raw `$()`.

**What's needed (by user frequency):**

| Package            | Why Critical                             |
| ------------------ | ---------------------------------------- |
| `@stash/git`       | Every developer, every CI script         |
| `@stash/kubectl`   | Every Kubernetes user                    |
| `@stash/aws`       | SigV4 signing, S3, SQS, Lambda, ECS      |
| `@stash/terraform` | Infrastructure automation                |
| `@stash/helm`      | Kubernetes packaging                     |
| `@stash/ansible`   | Configuration management integration     |
| `@stash/nginx`     | Web server config management             |
| `@stash/postgres`  | Database operations via `psql` wrapper   |
| `@stash/redis`     | Cache operations via `redis-cli` wrapper |

**Priority:** Must grow the ecosystem systematically. Each missing package is a "no, I can't replace my Python script with Stash" moment. But fix the CLI boilerplate problem (Tier A3) first — otherwise these packages will be built on the same broken foundation.

---

#### B7. HTTP Server / Simple Request Handler

**Current state:** Stash is an excellent HTTP client. There is no HTTP server capability.

**What it solves:** Sysadmin scripts increasingly need to expose endpoints — health checks, metrics, webhook receivers, simple control APIs. Without a server:

```stash
// What should be possible but isn't:
http.serve(8080, (req) => {
    return req.path switch {
        "/health" => { status: 200, body: "ok" },
        "/metrics" => { status: 200, body: prometheus.format(collectMetrics()) },
        _ => { status: 404, body: "not found" }
    };
});
```

This is different from building a web application — it's for scripts that happen to need to respond to HTTP. The bar is intentionally low: single-threaded, synchronous, no routing framework.

**Priority:** Medium. Not universal, but increasingly common in cloud-native environments.

---

#### B8. Heredocs

**Current state:** Triple-quoted strings (`"""..."""`) exist for multi-line strings. Heredocs are different — they feed multi-line content directly as stdin to a command.

**What it solves:**

```stash
// Feed SQL to psql without temp files:
$(psql -U admin mydb) << SQL
    SELECT count(*) FROM users WHERE created_at > NOW() - INTERVAL '1 day';
    SELECT pg_size_pretty(pg_database_size('mydb'));
SQL

// Feed config to a command:
$(ssh root@server "tee /etc/nginx/sites-enabled/app") << CONF
server {
    listen 80;
    server_name ${domain};
    root ${webroot};
}
CONF
```

**Design:** `$(cmd) << MARKER\n...\nMARKER` or `$(cmd) <<< "string"` (here-string). The content between markers is fed as stdin to the command.

**Priority:** Medium. Common pattern in bash that's currently impossible in Stash.

---

### Tier C — Differentiation and Polish

These make Stash better but don't block adoption.

#### C1. Interactive Terminal Helpers

`term.spinner()`, `term.progress()`, `term.select()`, `term.table()`. Listed as Tier 2 in v1.0 analysis but still not done. Professional scripts for interactive use need progress feedback.

```stash
let spinner = term.spinner("Deploying...");
try {
    deploy();
    spinner.succeed("Deployed in ${elapsed}");
} catch (e) {
    spinner.fail("Deploy failed: ${e.message}");
}

let env = term.select("Deploy to:", ["staging", "production"]);

// Tabular output:
term.table(servers, {
    columns: ["host", "role", "status"],
    widths: [20, 10, 10]
});
```

#### C2. `json.query()` — JSONPath

Processing complex JSON from Kubernetes, cloud APIs, and GitHub requires navigating nested structures. Manual navigation is verbose.

```stash
let pods = json.parse($(kubectl get pods -o json).stdout);
let running = json.query(pods, "$.items[?(@.status.phase=='Running')].metadata.name");
```

#### C3. `diff` — Structural Comparison

Config drift detection: compare two dicts/files and get a structured description of changes.

```stash
let old = toml.parseFile("config.toml");
let new = toml.parseFile("config.toml.new");
let changes = diff(old, new);
for (let c in changes) {
    log.warn("Config changed: ${c.path}: ${c.old} → ${c.new}");
}
```

Already designed in "Unique language concepts.md" (feature 6). Not implemented.

#### C4. Guard Expressions (`where` Clauses)

Already designed in "Unique language concepts.md" (feature 5). Reduce parameter validation boilerplate:

```stash
fn deploy(version, env, replicas)
    where version is semver,
          env in ["staging", "production"],
          replicas in 1..100
{
    // Guards auto-generate: "Guard failed: 'env' must be in [staging, production], got 'development'"
}
```

#### C5. Scoped Environment Changes

```stash
// Change env vars for a block, then restore:
env.with({ "NODE_ENV": "production", "PORT": "8080" }) {
    $(node server.js);
}
// Original env restored after block
```

This pattern is common (Python's `mock.patch.dict`, Go's `os.Setenv` + defer `os.Unsetenv`). Stash's `defer` can approximate it but the ergonomics are worse.

#### C6. Process Substitution

```stash
// diff two command outputs without temp files:
$(diff <($(kubectl get pods -o yaml)) <(cat expected-pods.yaml));
```

This is a bash feature used by advanced scripts. Lower priority than other items.

#### C7. `@stash/sqlite` Package

Log analysis, inventory management, metrics storage. SQLite is the right embedded database for scripting. Better as a package than a built-in (keeps the core lean). Should use struct-based APIs.

#### C8. `env.change` / Working Directory Scoping

```stash
// Execute commands in a different directory without permanent cd:
fs.inDir("/opt/app") {
    $(make build);
    $(make test);
}
// Original directory restored
```

---

## 4. The Strategic Roadmap

Three phases, in order. Do not skip phases.

### Phase 1 — Win the Scripting Segment (3-6 months)

**Goal:** Any Python or Bash script for sysadmin purposes should be rewritable in Stash with less code and more safety. Grow the package ecosystem to cover the top 15 tools. Fix the CLI boilerplate problem.

1. `@stash/cli` — shared CLI toolkit (fixes the boilerplate crisis)
2. Migrate existing packages to struct-based option types
3. `str.parseTable()` — columnar CLI output parsing
4. `lock` block implementation
5. `dry` mode implementation
6. `@stash/git` package
7. `@stash/kubectl` package
8. `@stash/aws` package (SigV4 + S3/SQS/Lambda)
9. `json.query()` (JSONPath)
10. Enhanced `regex` namespace
11. Heredoc support
12. Interactive terminal helpers (spinner, progress, select, table)

### Phase 2 — Win the Interactive Shell Segment (6-12 months)

**Goal:** A developer can set Stash as their login shell and be productive within a week.

1. `cd`, `pwd`, `pushd`, `popd` built-ins
2. `~/.stashrc` loading on REPL startup
3. Prompt customization (`PROMPT` function)
4. Tab completion (file paths + PATH commands + Stash identifiers)
5. Glob expansion in `$()` commands
6. Streaming pipes (the big architectural change)
7. REPL bare-command fallback (Approach B: try-parse-then-shell)
8. `fs.inDir()` scoped working directory
9. `env.with()` scoped environment changes

### Phase 3 — Differentiate and Dominate (12-24 months)

**Goal:** Features that make Stash the obvious choice — not just competitive, but genuinely better in ways competitors cannot easily replicate.

1. Structural pattern matching (guards + field matching in `switch`)
2. Guard expressions (`where` clauses on functions and struct fields)
3. `diff` built-in for structured comparison
4. HTTP server (`http.serve`)
5. Streaming I/O / generators (for large file processing, SSE, chunked transfer)
6. `@stash/sqlite`, `@stash/postgres`, `@stash/redis` packages
7. Binary protocol support (if the philosophical decision is made to speak protocols natively)
8. Process substitution (`<(cmd)`)

---

## 5. The Philosophical Question You Must Answer

The Systems Integration Gap Analysis raises a critical question that this document cannot answer for you:

> **Is Stash a language that speaks binary protocols, or a language that orchestrates tools that do?**

For Redis: native RESP protocol client, or `redis-cli` wrapper? For PostgreSQL: wire protocol client, or `psql` wrapper?

My strong recommendation: **orchestrates tools**. Here's why:

1. The sysadmin use case is almost always about orchestrating existing infrastructure, not building infrastructure. You call `redis-cli`, you call `psql` — you don't implement the protocol.
2. Binary protocol clients are a massive maintenance burden. They require parsing complex binary formats, handling edge cases in the protocol, and tracking changes across versions.
3. The time invested in binary protocol clients is time not invested in the orchestration features that actually differentiate Stash from Python.
4. The `$()` syntax plus good CLI wrappers is genuinely competitive with Python's `redis` or `psycopg2` packages for sysadmin use cases.

Exception: if a protocol is commonly accessed without a CLI tool available, or if the CLI tool is painful, a native client makes sense. `@stash/sqlite` is a good example — no CLI tool is typically available in production environments.

---

## 6. What Stash Gets Wrong That Must Stop

### 6.1 Dict-Based Option Bags in Package Code

The existing `@stash/docker`, `@stash/podman`, `@stash/systemd` packages use anonymous dicts as option bags for every function. This violates Stash's own design philosophy. The language has structs and interfaces precisely to avoid this pattern.

Before: `docker.containers.run(image, { name: "web", detach: true, rm: false })`

After: `docker.containers.run(image, docker.RunOptions { name: "web", detach: true })`

Structs with named fields: enforce required fields at construction time, are self-documenting, get IDE autocompletion, and avoid the `dict.has` checking hell. **All new packages must use struct-based option types. Existing packages should be migrated.**

### 6.2 The 396 `dict.has` Calls Must Go

Not a design debate — this is pure technical debt that must be removed as part of the `@stash/cli` toolkit introduction. This is documented in "CLI Wrapper Sustainability" and should be treated as a blocker on the package ecosystem roadmap.

### 6.3 The Namespace Count in Documentation

Multiple docs say "24 namespaces" when the actual count is 33+. This creates a credibility problem. A project that can't accurately describe itself in its own documentation does not inspire confidence. Fix it.

---

## 7. The Single Sentence That Defines the Strategy

Stash must become the language where **the dangerous stuff is safe by default** (secrets auto-redact, retries auto-backoff, timeouts auto-cancel, locks auto-release, dry-run auto-suppresses side effects) and **the common stuff is first-class** (IP addresses are values, durations are values, versions are values, structured command output is structured data, cloud tools have typed APIs).

No other language offers this combination. That's the pitch.

---

## Decision Log

| Date       | Decision                                                 | Rationale                                                                                                                              |
| ---------- | -------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-26 | Positioned Stash as scripting-first, shell-second        | The unique features (retry/timeout/secret/scheduler) are script-focused. Win scripting before claiming the interactive shell segment.  |
| 2026-04-26 | Recommended "orchestrates tools" over "speaks protocols" | Binary protocol clients are high-cost, low-differentiation. CLI wrapper ecosystem + `$()` is sufficient for sysadmin use cases.        |
| 2026-04-26 | Tier A includes streaming pipes as existential gap       | Investigation confirmed pipes are broken, not just buffered — `ExecutePipe` doesn't connect left stdout to right stdin at all. All pipe tests skipped due to deadlock. Language spec already describes the correct streaming design.  |
| 2026-04-26 | CLI boilerplate crisis is Tier A, not Tier B             | The ecosystem cannot scale on its current foundation. 33 packages × 540 lines of boilerplate = unsustainable. Must fix before growing. |
| 2026-04-26 | `lock` and `dry` mode prioritized in Phase 1             | These are the "safe by default" differentiators that define Stash's brand. Deploy scripts without them are incomplete.                 |
| 2026-04-26 | Struct-based option types mandated for all packages      | Aligns with language philosophy. Dict-based options are the anti-pattern Stash was designed to eliminate.                              |
