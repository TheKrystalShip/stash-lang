# Unique Language Concepts — Volume 3

**Status:** Brainstorm / Design exploration
**Created:** 2026-05-05
**Context:** Follow-up to V1 and V2. Most items from V1 (duration/bytesize literals, semver, retry blocks, scheduler) and V2 (`secret`, `timeout`, `lock`, `log`, `defer`) shipped. The shelved items from V2 (`dry` mode, table parsing, health probes) are deliberately not revisited here — they remain as-is in the backlog.

This volume is the result of a survey of what other languages and shells are doing right, and — more importantly — what they're collectively **not** doing for sysadmins.

**Languages surveyed for inspiration:** Nushell (structured pipelines, `par-each`, scoped env), Deno (capability-based security, `--allow-net=host`, audit logs, permission broker), PowerShell (object pipelines, `Where-Object`), Elixir/F#/Gleam (`|>`, `use`/callback flattening), Python (pathlib, asyncio), Go (structured `defer`, goroutines), Ansible/Terraform/Chef (declarative resources, plan/apply), jq (query paths), Trio/Erlang (structured concurrency).

**The recurring observation:**

Every language that "solves" sysadmin scripting either (a) is a shell that can't write real programs (Bash, Fish), (b) is a programming language that ignores systems concerns (Python, Ruby), or (c) is a niche shell that asks you to throw away everything (Nushell, PowerShell). Stash sits in the rare gap of "real programming language with first-class sysadmin features." The V3 features below all extend that wedge.

**Design criteria — unchanged from V2:**

- Solves a problem sysadmins hit _every single week_
- Can't be cleanly solved with a library — needs language or runtime support
- Syntax is obvious and self-documenting
- Interaction with existing Stash features is clean, not bolted-on

---

## 1. Capability Sandbox — Permission Flags for Script Execution

**The problem:** Every general-purpose scripting language gives untrusted code _full system access by default_. You `curl https://example.com/install.sh | bash` and you've handed over your machine. You schedule a cron job written by someone else and it can read `~/.ssh/id_rsa`. You install a `pip` package and it can exfiltrate `~/.aws/credentials`. The only defense today is OS-level sandboxing (containers, seccomp, AppArmor), which is too coarse, too complex, and never used for one-off scripts.

Deno proved this can work for general-purpose code. No sysadmin language has it.

```bash
# Wide-open by default for personal use:
stash deploy.stash

# Locked down for cron / automation / untrusted snippets:
stash --allow-fs=/etc/nginx,/var/log \
      --allow-net=api.example.com,*.internal \
      --allow-env=DEPLOY_KEY,HOME \
      --allow-run=systemctl,nginx \
      deploy.stash

# Maximally paranoid — no I/O at all (pure computation):
stash --no-allow script.stash

# Inspect what permissions a script declares it needs:
stash --inspect-perms script.stash
# Required: fs.read(/etc/nginx), fs.write(/var/log), net(api.example.com:443), run(nginx)
```

**Per-namespace gating** (extends Stash's existing `StashCapabilities`):

| Capability        | Flag                          | Granularity                            |
| ----------------- | ----------------------------- | -------------------------------------- |
| File read         | `--allow-fs-read=PATH,...`    | Path prefix                            |
| File write        | `--allow-fs-write=PATH,...`   | Path prefix                            |
| Network           | `--allow-net=HOST[:PORT],...` | Hostname + optional port; `*` wildcard |
| Subprocess        | `--allow-run=BINARY,...`      | Binary name (resolved via PATH)        |
| Environment read  | `--allow-env=VAR,...`         | Variable name; `AWS_*` wildcard        |
| Environment write | `--allow-env-write=VAR,...`   | Variable name                          |
| FFI / native      | `--allow-ffi[=PATH]`          | All-or-specific-library                |
| Time/system info  | `--allow-sys`                 | All-or-nothing                         |

Plus `--deny-*` flags that take precedence (matching Deno's model).

**Inline declaration** — a script can declare its required permissions in a header, both for documentation and so the runtime can fail fast if invoked with insufficient privileges:

```stash
#!stash
@requires(
    fs.read: ["/etc/nginx"],
    fs.write: ["/var/log/myapp"],
    net: ["api.example.com:443"],
    run: ["nginx", "systemctl"]
)

// Now if invoked with `stash --allow-fs-read=/tmp script.stash`,
// the runtime aborts immediately with a clear error listing what's missing.
```

**Permission audit log** (Deno's `DENO_AUDIT_PERMISSIONS`):

```bash
STASH_AUDIT=audit.jsonl stash deploy.stash
# Every fs.read, fs.write, net call gets logged with timestamp + path/host
```

**Why this is a runtime feature, not a library:** A library can't intercept the `fs` namespace from inside the same VM. Permission checks must be enforced at the built-in dispatch layer, before the operation begins. Stash already has `StashCapabilities` as a coarse mechanism (Network, Shell, etc.) — this extends it with _value-level_ granularity (which path? which host?).

**Implementation sketch:**

- Extend `StashCapabilities` with per-resource ACL lists held on `VMContext`
- Each I/O-touching built-in checks the ACL before executing — `fs.readFile(path)` calls `ctx.CheckFsRead(path)` which throws `PermissionError` if denied
- CLI parses `--allow-*` flags into ACLs, populates `VMContext` before script start
- `@requires` directive parsed at lexer level (similar to existing `#!stash` shebang); matched against runtime ACLs at startup
- New error type `PermissionError` (extends `StashError`)
- New static analysis rule: SA08xx warns when a script accesses a resource not declared in `@requires`

**What other languages do:**

- **Deno**: Mature implementation. Per-path, per-host, per-binary. Audit log. Permission broker for centralized policy.
- **Node.js**: Experimental `--permission` model added in v20. Limited (no per-host network).
- **Python, Ruby, Bash, PowerShell, Nushell**: _Nothing._ Full system access by default.
- **Lua / WebAssembly**: Sandboxed by being embedded — not a CLI scripting model.

**Strategic value:** This is the single feature that makes Stash a serious choice for "running untrusted automation" — agentic AI code, CI scripts from PRs, cron jobs from less-trusted teammates. Combined with the `secret` type from V2, the pitch becomes: _"Stash is the only sysadmin scripting language where credentials don't leak and untrusted code can't escalate."_

**Risks:**

- Granularity tradeoffs: too fine and scripts become unusable; too coarse and the sandbox is meaningless. (Mitigation: copy Deno's proven granularity. They iterated to a good point.)
- Subprocess escape: `--allow-run=bash` essentially defeats the sandbox. (Mitigation: document loudly, same as Deno. Not a flaw — a tradeoff for usability.)
- Existing scripts break under `--allow-*` flags. (Mitigation: default mode remains wide-open. Sandbox is opt-in.)

---

## 2. Streaming Command Output — `$<(cmd)` Sigil

**The problem:** `$(command)` is great when commands finish quickly. But the moment you need to process output as it streams — `tail -f`, `kubectl logs -f`, `journalctl -f`, `inotifywait`, a long-running build — every language falls apart.

- **Bash**: `command | while read line` — works but no error handling, no exit detection, no timeout.
- **Python**: `subprocess.Popen` + `iter(p.stdout.readline, '')` + manual cleanup + thread for stderr + signal handling. Six lines minimum, easy to leak the process.
- **Ruby**: `IO.popen` with similar boilerplate.
- **Go**: `cmd.StdoutPipe()` + `bufio.Scanner` + goroutine + `cmd.Wait()`. Works but verbose.
- **Nushell**: Nice for finished output, but streaming `tail -f` is awkward.

Stash extends its existing 2D command-sigil grid with a third column: streaming.

|              | Capture (default) | Streaming (`<`) | Passthrough (`>`) |
| ------------ | ----------------- | --------------- | ----------------- |
| Lenient      | `$(cmd)`          | `$<(cmd)`       | `$>(cmd)`         |
| Strict (`!`) | `$!(cmd)`         | `$!<(cmd)`      | `$!>(cmd)`        |

**Modifier order rule:** `$` then optional `!` then optional direction marker (`<` or `>`) then `(`. One pattern, learn once. Strict streaming `$!<(cmd)` throws `CommandError` if the child exits with a non-zero code at natural completion (cleanup-induced kills don't trigger it).

### The handle: `StreamingProcess`

`$<(cmd)` evaluates to a `StreamingProcess` handle. The handle is iterable (yielding stdout lines by default) and exposes the process lifecycle.

```stash
struct StreamingProcess {
    pid: int,             // child PID, available immediately
    exitCode: int?,       // null while running; populated once the child exits
    signal: Signal?,      // built-in Signal enum if killed by signal; null otherwise
}

// Methods:
//   .kill(signal: Signal = Signal.Term)   - send a signal to the child
//   .wait()                                - block until exit (no iteration)
//   .lines()                               - explicit iterator over stdout lines
//   .json()                                - iterator over parsed JSON values (one per line)
//   .bytes(size: int)                      - iterator over binary chunks of `size` bytes
//   .framed(delim: string)                 - iterator over delimiter-separated records
```

### Iteration forms

```stash
// 1. Default — iterate stdout lines:
for (let line in $<(tail -f /var/log/nginx/access.log)) {
  if (str.contains(line, "ERROR")) {
    log.warn("nginx error: ${line}");
  }
}

// 2. Strict — throws CommandError on non-zero exit at natural completion:
for (let line in $!<(make build)) {
  io.println(line);
}

// 3. Dual iteration — mirrors `for (k, v in dict)`. Each tick yields one line in
//    arrival order; the variable for the other source is null.
for (let out, err in $<(kubectl logs -f my-pod)) {
  if (out != null) { handle(out); }
  if (err != null) { log.warn("kubectl: ${err}"); }
}

// 4. Framing methods on the handle produce different iterables:
for (let event in $<(kubectl get pods -w -o json).json())  { handle(event); }
for (let chunk in $<(cat big.bin).bytes(4096))             { upload(chunk); }
for (let record in $<(some-cmd).framed("\0"))              { process(record); }

// 5. Pipes work; only the last stage's exit code matters (matches existing $(a|b|c) semantics):
for (let line in $<(cat huge.log | grep ERROR)) { ... }

// 6. Inspect the handle after iteration completes:
let s = $<(make build);
for (let line in s) { io.println(line); }
io.println(s.exitCode);   // 0 on clean exit; signal-derived value if cleanup killed it
io.println(s.signal);     // null on clean exit; a Signal enum value otherwise

// 7. Composes with `timeout` from V2 — cleanup uses the early-exit path:
timeout 30s {
    for (let line in $<(kubectl logs -f my-pod)) { process(line); }
}
// On timeout: cancellation triggers the same SIGTERM/SIGKILL cleanup as a `break`,
// then TimeoutError propagates out of the `timeout` block.
```

### Cleanup contract

When iteration exits early — `break`, `return`, an unhandled exception, or `timeout` cancellation — the runtime sends `SIGTERM` to the child, waits a 5-second grace period, then sends `SIGKILL` if needed. FDs are closed and the child is reaped. **Every early-exit cause uses the same code path** — there are no special cases for timeout vs. break vs. throw.

### Consumption rules

- A `StreamingProcess` handle is **single-consumption**. Iterating it, calling `.json()` / `.bytes()` / `.framed()` / `.lines()`, or calling `.wait()` consumes it.
- A second consumption attempt throws `StateError`.
- `.exitCode`, `.pid`, `.signal`, and `.kill()` remain accessible after consumption.

### Default behaviors

| Concern                      | Default                                                                                                                                                                                      |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **stderr**                   | Real-time interleaving with stdout in arrival order (terminal-like). Only visible via the dual-iterator form `(out, err)`. The single-variable form `for (let line in ...)` discards stderr. |
| **`exitCode` while running** | Returns `null`. Non-blocking — never deadlocks. Becomes the integer exit code once the child exits.                                                                                          |
| **`.json()` malformed line** | Throws `ParseError`; iteration aborts and child cleanup runs. Robustness can be added later via opt-in (`.json({ skipMalformed: true })`) if real demand emerges.                            |
| **`.kill()` default signal** | `Signal.Term`.                                                                                                                                                                               |

### Static analysis rules

| Code   | Severity | Rule                                                                                                                                                            |
| ------ | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| SA07xx | Error    | `$<(cmd)` / `$!<(cmd)` cannot be combined with the passthrough modifier (`>`). The combinations `$<>(...)` and `$><(...)` and similar do not exist.             |
| SA07xx | Error    | `$<(cmd)` cannot appear as a stage in a pipe chain (it is a sink, not a stage). The pipe chain goes _inside_ the parens: `$<(a \| b \| c)`.                     |
| SA08xx | Error    | The dual-iterator form `for (let out, err in X)` requires `X` to be a streaming form (`$<(...)` or `$!<(...)`); using it on a captured `$(cmd)` is meaningless. |
| SA08xx | Warning  | A `$<(cmd)` whose handle is never iterated nor consumed (process leak detection).                                                                               |

### Why this is language/runtime, not a library

Cleanup-on-iteration-exit requires deep VM integration with the `for` loop and the process lifecycle. A library can return an iterator, but it cannot guarantee the child process gets killed and reaped when the loop exits early via `break`, `return`, an exception, or a `timeout` cancellation. That guarantee is the entire reason streaming is hard in every language today.

The dual-iterator form (`for (out, err in $<(cmd))`) reuses the exact same syntactic shape as `for (k, v in dict)`, which is only possible because the parser knows about the streaming form. A library function could not provide this without inventing parallel syntax.

### Implementation sketch

- New lexer tokens for `$<(` and `$!<(` parallel to existing `$(`, `$!(`, `$>(`, `$!>(`
- AST gains a `CommandStreamExpr` (or extends `CommandExecExpr` with a `Mode: Capture | Stream | Passthrough` enum) parallel to the existing strict flag
- New runtime type `StashStreamingProcess` implementing `IVMIterable` and the existing `IVMIterator` protocol, with a fresh "dispose on iterator exit" hook the VM's `for` loop calls on every exit path
- VM `for` loop wires SIGTERM + grace + SIGKILL cleanup as an implicit defer registered when iteration begins
- New error: `StateError` (extends `StashError`) for double-consume violations
- `Signal` enum already exists from the Process Namespace Decomposition work — reuse it for the `signal` field
- Stderr interleaving: a single bounded channel multiplexed by the runtime, preserving arrival order

### Composition with `secret`

Output lines are not tainted by default. If `cmd` itself was constructed from a `secret` (e.g. `$<(curl -H "Authorization: Bearer ${apiKey}")`), the _command string_ is redacted in error messages, but stdout lines from the command are not retroactively secret. Taint propagation across process boundaries is out of scope for v1.

### Composition with capability sandbox (#1 of this doc)

`$<(cmd)` requires the same `--allow-run=BINARY` capability as `$(cmd)`. Streaming does not bypass the sandbox.

### Risks

- **Backpressure:** if iteration is slower than the child's output rate, the OS pipe buffer fills and the child blocks. Acceptable — same as every other language. Document it.
- **PID reuse races on `.kill()`:** if the child has already been reaped and the OS has reused the PID, `.kill()` would target the wrong process. Mitigation: `.kill()` is a no-op once `exitCode` is non-null.
- **Stderr ordering on Windows:** the .NET pipe abstraction does not perfectly preserve cross-stream ordering. Document as best-effort, not strict.

---

## 3. `ensure` Resources — Idempotent Declarative State

**The problem:** Every config-management script repeats the same pattern: "if the file doesn't exist with the right contents, write it; if the package isn't installed, install it; if the service isn't running, start it." This is the core insight behind Ansible, Chef, Puppet, Salt, NixOS — but those are entire ecosystems requiring their own runtime, DSL, and inventory model. Plain scripts re-invent this badly every time.

```stash
// Ensure a file exists with specific contents (idempotent):
ensure file "/etc/nginx/sites-available/myapp" {
    content: tpl.render("nginx.conf.tpl", { port: 8080 }),
    owner: "root",
    group: "root",
    mode: 0o644
}
// Behavior: if the file already exists with matching content/owner/group/mode → no-op.
//           Otherwise → atomic write (temp + rename), set ownership/perms.
//           Returns: { changed: true|false, before: <old hash>, after: <new hash> }

// Ensure a directory tree exists:
ensure dir "/var/log/myapp" {
    owner: "myapp",
    mode: 0o750,
    recursive: true
}

// Ensure a symlink points where you want:
ensure symlink "/etc/nginx/sites-enabled/myapp" → "/etc/nginx/sites-available/myapp"

// Ensure a line is present in a file (idempotent line-in-file):
ensure line in "/etc/hosts" {
    match: "^127\\.0\\.0\\.1\\s+myapp",
    line: "127.0.0.1 myapp.local"
}

// Ensure a file is absent:
ensure absent "/tmp/old-marker"

// Aggregate results across many `ensure` blocks:
let report = ensure.report {
    ensure file "/etc/nginx/conf.d/site.conf" { content: nginxConf }
    ensure file "/etc/myapp/config.toml" { content: appConfig }
    ensure dir "/var/cache/myapp" { mode: 0o755 }
}
println("${report.changed} of ${report.total} resources changed")
if (report.changed > 0) {
    $(systemctl reload nginx)
}
```

**Composition with V2's shelved `dry` mode:** This is the killer use case for `dry`. Run the script with `--dry`, get a Terraform-like plan of what would change, run for real after review.

```bash
stash --dry deploy.stash
# DRY: would change /etc/nginx/conf.d/site.conf (content differs)
# DRY: no change to /etc/myapp/config.toml
# DRY: would create /var/cache/myapp (does not exist)
```

**Why this is a language construct, not a library:** A library function `ensureFile(path, opts)` is possible — and Ansible's modules basically are this. But the value of a language construct is:

1. **Uniform syntax** for many resource types (`file`, `dir`, `symlink`, `line`, `absent`) so users learn the pattern once.
2. **Aggregation primitive** (`ensure.report { }`) that collects change events across nested calls.
3. **Integration with `dry` mode** — the runtime knows these are declarative, can compute a plan automatically.
4. **Static analysis** can spot "you ensured this file twice with different content" as an error.

**Resource types in v1 (extensible later):**

| Resource                       | Asserts                                                |
| ------------------------------ | ------------------------------------------------------ |
| `ensure file PATH { ... }`     | Exists with content/owner/group/mode                   |
| `ensure dir PATH { ... }`      | Exists with owner/mode (optionally recursive)          |
| `ensure symlink PATH → TARGET` | Symlink exists pointing at target                      |
| `ensure line in FILE { ... }`  | Line matching pattern is present (replaced if differs) |
| `ensure absent PATH`           | File/dir does not exist                                |

User-extensible types are V2 of the feature — a `struct EnsureResource` interface that custom packages can implement, so `@stash/systemd` could add `ensure service "nginx" { state: "running", enabled: true }`.

**What other languages do:**

- **Ansible/Chef/Puppet/Salt**: Excellent, but they're entire ecosystems (DSL + inventory + runner). Overkill for a script.
- **Nix**: Declarative but completely different paradigm.
- **Python/Ruby**: Libraries exist (`python-ansible`, `chef-zero`), but no first-class syntax.
- **Bash**: `[[ -f file ]] || cat > file` patterns — error-prone, never quite right.
- **No general-purpose scripting language has this as a first-class construct.**

**Risks:**

- Creep into config-management-tool territory. (Mitigation: keep the scope tight — file/dir/symlink/line/absent only in v1. Don't add `ensure package` or `ensure service` — those belong in stdlib packages.)
- "Convergence" semantics imply many calls combine into a plan. (Decision: each `ensure` is independent and immediate by default. `ensure.report { }` block collects results but still executes immediately. True plan/apply with deferred execution is a separate, larger feature.)

---

## 4. Path Type with Literal Syntax

**The problem:** Every path in every Stash script is a string today. Strings don't know:

- What a separator is on the current OS
- Whether the path is absolute or relative
- Whether components have shell metacharacters that need escaping
- How to join safely without double-slashes
- That `/etc/foo/../bar` and `/etc/bar` are the same path

Python has `pathlib.Path` (library, no literal). Rust has `Path`/`PathBuf` (library). Nushell treats paths somewhat as data. **No language has path as a literal type.**

```stash
// Literal syntax — backtick-prefix or path"...":
let cfg = p"/etc/nginx/nginx.conf"
let log = p"~/logs/app.log"          // ~ expanded automatically
let rel = p"./scripts/deploy.sh"      // relative paths preserved as such

// Type-safe operations:
typeof(cfg)              // "path"
cfg.isAbsolute           // true
cfg.parent               // p"/etc/nginx"
cfg.name                 // "nginx.conf"
cfg.stem                 // "nginx"
cfg.ext                  // "conf"
cfg.parts                // ["/", "etc", "nginx", "nginx.conf"]

// Safe joining — never produces double-slashes, handles cross-platform:
let conf = p"/etc/nginx" / "sites-enabled" / "${siteName}.conf"
// On Windows: uses backslashes. On Unix: forward slashes.

// Existence and metadata are methods, not separate fs.* calls:
if (cfg.exists && cfg.isFile) { ... }
println(log.size)        // bytesize literal
println(log.mtime)       // duration since epoch

// Comparison is path-aware (normalized):
p"/etc/foo/../bar" == p"/etc/bar"     // true (after normalization)

// Reading/writing returns to fs.* but takes Path naturally:
let contents = fs.readFile(cfg)       // works
let contents = fs.readFile("/etc/nginx/nginx.conf")  // string still works (UFCS to path)

// Glob expansion produces paths:
let logs = p"/var/log/*.log".glob()   // returns array<path>

// Path arithmetic for sysadmin patterns:
let backup = cfg.with({ ext: "bak" })             // p"/etc/nginx/nginx.bak"
let archived = cfg.with({ stem: "${cfg.stem}.${time.now().format('YYYYMMDD')}" })

// Cross-platform path normalization:
p"C:\\Users\\admin\\config".toUnix()    // p"/c/Users/admin/config" (WSL-style)
```

**Why this is a language feature:** A library can give you `Path` methods, but only the language can give you the `p"..."` literal syntax (parsed as path, not string), the `/` operator overload for joining, and the implicit conversion when passing to `fs.*` functions. The literal syntax is what makes the type ergonomic — without it, you'd write `path("/etc/nginx/nginx.conf")` everywhere and people would just stick with strings.

**Implementation sketch:**

- New literal: `p"..."` produces a `StashPath` value. Existing `"..."` strings still work via implicit conversion at `fs.*` boundaries.
- New type: `StashPath` implementing `IVMComparable`, `IVMStringifiable`, `IVMFieldAccessible`
- Operator `/` overloaded when LHS is `StashPath`: produces a new `StashPath`
- Tilde and `${}` interpolation expanded at literal construction time
- `fs.*` built-ins accept `StashPath` or `string`

**Risks:**

- Yet another literal sigil (`p"..."`). Stash already has `@v1.2.3` and `@1.2.3.4`. (Mitigation: `p"..."` is consistent with Python's `f"..."`, Rust's `b"..."`. Familiar pattern.)
- Backward compatibility: existing scripts use strings. (Mitigation: strings continue to work everywhere paths are accepted.)

---

## 5. Structured Concurrency — `parallel` Block

**The problem:** Every script eventually needs to do N things at once — health-check 50 servers, hit 10 APIs, build 5 artifacts. Today this requires:

- **Bash**: `&` + `wait` — no error propagation, no early termination
- **Python**: `concurrent.futures.ThreadPoolExecutor` with manual exception aggregation
- **Go**: goroutines + sync.WaitGroup + channels for errors — verbose
- **Nushell**: `par-each` (good, but only over collections)

Trio (Python) and Erlang pioneered "structured concurrency": tasks are scoped to a block, the block doesn't return until all tasks finish, and errors propagate cleanly. Java added it in 21. **No scripting language has it as syntax.**

```stash
// Run independent tasks in parallel — block returns when all done:
parallel {
    branch { fetchUserData() }
    branch { fetchOrderData() }
    branch { fetchProductData() }
}
// All three complete (or all are cancelled if any throws)

// Capture results from each branch:
let (users, orders, products) = parallel {
    branch { fetchUserData() }
    branch { fetchOrderData() }
    branch { fetchProductData() }
}

// Parallel iteration — bounded concurrency:
parallel each (servers, max: 10) as server {
    healthCheck(server)
}
// Up to 10 concurrent health checks. Block returns when all 50 servers checked.

// Collect results from parallel iteration:
let healths = parallel.map(servers, max: 10, (server) => {
    return { name: server.name, ok: healthCheck(server) }
})

// Error semantics — fail-fast by default:
parallel {
    branch { riskyTask1() }
    branch { riskyTask2() }
    branch { riskyTask3() }
}
// If branch 2 throws: branches 1 and 3 are cancelled (cooperative — at I/O boundaries),
// the throw propagates from the parallel block.

// Error semantics — collect-all mode:
let results = parallel (mode: "collect") {
    branch { riskyTask1() }
    branch { riskyTask2() }
    branch { riskyTask3() }
}
// Returns array of { ok: T } | { error: StashError } — no auto-cancel.

// Composes with timeout (V2):
timeout 30s {
    parallel each (servers, max: 5) as s { deploy(s) }
}
// If 30s elapses: all in-flight branches receive cancel signal.
```

**Why this is a language construct:** Structured concurrency requires:

1. The block scope to act as a parent of all spawned tasks (no leaks possible)
2. Error aggregation — a branch throw must propagate _and_ cancel siblings
3. Cancellation tokens that thread through built-in I/O (already exists for `timeout` from V2)
4. Compile-time guarantee that `branch` only appears inside `parallel`

A library can provide `Promise.all()`-style behavior, but it can't give you the syntactic guarantee that "all spawned work is scoped to this block." That's the whole point.

**Implementation sketch:**

- New AST nodes: `ParallelBlock`, `BranchStmt`, `ParallelEachExpr`
- VM uses `Task.Run` with shared `CancellationTokenSource`
- Default: any branch exception → `Cancel()` on shared CTS → wait for siblings → rethrow
- Collect mode: catch all branch exceptions, return array of result-or-error
- `branch` outside `parallel` → SA08xx parse error
- Reuses cancellation infrastructure from V2's `timeout`

**What other languages do:**

- **Trio** (Python): `async with trio.open_nursery()` — pioneering work, but async-only
- **Java 21**: `StructuredTaskScope` — verbose, not a syntactic construct
- **Erlang/Elixir**: Supervision trees — different model, but same goal
- **Go**: `errgroup` package — library, not syntax, easy to misuse
- **Bash, Python (sync), Ruby, Nushell, PowerShell**: Nothing structured

**Risks:**

- Threading vs. fibers vs. async-await tradeoff. (Decision: use .NET tasks. Built-in I/O already supports cancellation tokens via timeout work.)
- Cooperative cancellation only — CPU-bound branches can't be killed. (Acceptable, document. Same as `timeout`.)

---

## 6. Safe Shell Interpolation — `safe$(...)` or Auto-Quote in `$(...)`

> **Status: Superseded.** This section's framing was investigated and partly debunked: Stash already does not route `$(...)` through `/bin/sh -c`, so command-execution injection is not the actual risk. The real (narrower) risks — argv-splitting, glob, and tilde injection on interpolated values — are addressed without a new sigil by [Safe Shell Interpolation — Sugar Over process.exec.md](Safe%20Shell%20Interpolation%20—%20Sugar%20Over%20process.exec.md). Read the section below for historical context only.

**The problem:** Every Stash script that does `$(ls ${userInput})` is a shell injection vulnerability. The user types `$(rm -rf ~)` and the script executes it. This is the #1 source of CVEs in shell scripts. Every language has it. Bash's only mitigation is `printf %q` plus discipline.

There is no scripting language where shell interpolation is _safe by default_.

```stash
// Today (and what stays — explicit "I know what I'm doing"):
$(ls ${userPath})
// If userPath = "; rm -rf ~", boom.

// New: safe variant — auto-quotes interpolated values as shell words:
safe$(ls ${userPath})
// Compiles to equivalent of: ls "${userPath@Q}" — userPath becomes a single shell-safe argument.

// Multiple args, all safely quoted:
safe$(grep ${pattern} ${file})
// pattern and file each become single shell-safe tokens.

// Lists expand to multiple safe arguments:
let files = ["a.txt", "b with spaces.txt", "c$(rm).txt"]
safe$(ls ${files})
// → ls "a.txt" "b with spaces.txt" "c\$(rm).txt"

// Mix safe and literal — literal command structure is allowed:
safe$(find ${dir} -name "*.log" -delete)
// "find", "-name", "*.log", "-delete" are literal. Only ${dir} is interpolated and quoted.

// Heredoc form for multi-line:
safe$"""
ssh ${user}@${host} "cat ${remotePath}"
"""
// All ${} interpolations are quoted. The literal ssh syntax is preserved.

// Unsafe is still available but requires explicit noEscape() or unsafe$():
$(rm -rf ${path})           // works as today (no quoting)
unsafe$(rm -rf ${path})     // explicit version — preferred for clarity
safe$(rm -rf ${noEscape(rawCmd)})   // safe block, explicit escape hatch for one value
```

**Static analysis:** A new SA08xx rule warns when `$(...)` is used with an interpolation that isn't a string literal. Suggested fix: switch to `safe$(...)` or wrap with `unsafe$(...)` for explicit acknowledgment.

**Why this is a language feature:** The escape logic must run at compile time on the AST of the interpolation, splitting the parts into "literal command structure" vs. "interpolated values that need quoting." A function `safeShell(template, args)` could exist (like SQL parameterized queries), but the syntax `safe$(...)` is far more ergonomic and lets the lexer / parser do the right thing.

**Implementation sketch:**

- New token: `safe$(` and `unsafe$(` lex like `$(` but with a flag
- Parser produces existing `ShellExpr` / `CommandExecExpr` AST node with `escapingMode: Safe | Unsafe | Default` field
- Compiler emits identical bytecode in `Default`/`Unsafe` modes
- In `Safe` mode: each interpolation segment goes through a runtime escape function (POSIX shell quoting on Unix, cmd.exe quoting on Windows)
- New built-in `noEscape(value)` returns a wrapper that bypasses escaping at one specific site

**What other languages do:**

- **Bash**: `printf %q` exists but is opt-in and rarely used
- **Python**: `subprocess.run([...], shell=False)` is safe but loses shell features
- **Ruby**: `Shellwords.escape` exists but rarely used
- **Nushell**: Avoids the problem by having structured pipelines (no string-shell layer)
- **PowerShell**: Similar — uses argument arrays
- **Go**: `exec.Cmd{Args: []string{...}}` — safe but ugly
- **No scripting language with bash-like `$(...)` syntax has a safe variant.**

**Risks:**

- Cross-platform shell-quoting semantics differ. (Decision: target POSIX `sh` quoting on Unix, `cmd.exe` on Windows. Power-shell is a third escape style — handle explicitly.)
- Users will reach for `safe$(...)` when they actually want shell features (globbing, pipes between commands inside the string). (Decision: `safe$(...)` permits literal `|`, `>`, `*`, etc. — only _interpolated_ values are quoted. The structure is yours, the data is sanitized.)

---

## 7. Filesystem Watch — `watch path { ... }`

**The problem:** Reactive sysadmin scripts — config reload on change, log tail with action, build-on-save — all require filesystem watching. Every language requires `inotify` (Linux), `FSEvents` (macOS), or `ReadDirectoryChangesW` (Windows) bindings. Bash needs `inotifywait`. Python needs `watchdog`. Each platform behaves differently.

```stash
// Watch a single file or directory:
watch p"/etc/nginx" {
    on change (event) {
        log.info("nginx config changed: ${event.path} (${event.kind})")
        $(systemctl reload nginx)
    }
}

// Watch with filters:
watch p"/var/log" (recursive: true, glob: "*.log") {
    on create (event) { tailNew(event.path) }
    on modify (event) { /* ignore */ }
    on delete (event) { archive(event.path) }
}

// Debounced — coalesce rapid events:
watch p"/src" (debounce: 200ms) {
    on change (event) { rebuild() }
}

// Composes with timeout — terminate after duration:
timeout 1h {
    watch p"/etc/nginx" {
        on change { reload() }
    }
}

// Or break out programmatically:
watch p"/etc/nginx" {
    on change (event) {
        if (deploymentDone()) {
            break  // exits the watch block, releases handles
        }
    }
}
```

**Event kinds:** `created`, `modified`, `deleted`, `renamed`, `metadata`. Generic `change` matches any.

**Why this is language/runtime:** Cross-platform FS watching is a notorious portability nightmare — every OS has different semantics around event coalescing, recursion, symlinks, and rename atomicity. A built-in abstraction can hide this and provide consistent behavior. The `watch ... { on event { ... } }` syntax also gives the runtime control over event-loop integration with the rest of Stash (composes with `timeout`, schedulers from V1, structured concurrency from #5 of this doc).

**Implementation sketch:**

- Use .NET `FileSystemWatcher` (cross-platform abstraction over OS APIs)
- New AST: `WatchStmt` with path, options dict, and event handler dictionary
- Compiler emits a closure per `on X { }` clause
- VM creates the watcher, registers cleanup defer, blocks on event queue with `await`-style semantics
- `break` from inside an `on` handler terminates the watch
- Debouncing: built-in timer that delays fire until events stop for `debounce` ms

**What other languages do:**

- **Python**: `watchdog` library — works but verbose, plus you write the event loop
- **Node.js**: `fs.watch()` — exists but notoriously buggy on Linux (delivers wrong events)
- **Bash**: `inotifywait` external tool, Linux-only
- **PowerShell**: `Register-ObjectEvent` on `FileSystemWatcher` — possible but obscure
- **No language has `watch X { on Y { ... } }` as syntax.**

---

## 8. Atomic File Operations — `atomic` Block + `fs.atomicWrite()`

**The problem:** Half-written config files brick services. Power loss during `fs.writeFile()` of `/etc/nginx/nginx.conf` and nginx won't start. Every config-management tool (Ansible, Chef) writes to a temp file, fsyncs, then renames — but no scripting language exposes this.

```stash
// Single-file atomic write — temp + fsync + rename:
fs.atomicWrite(p"/etc/nginx/nginx.conf", newConfig)
// Either the file ends up with newConfig in full, or unchanged. Never half-written.

// Multi-file atomic block — all-or-nothing across multiple files:
atomic {
    fs.writeFile(p"/etc/myapp/server.conf", serverConf)
    fs.writeFile(p"/etc/myapp/db.conf", dbConf)
    fs.writeFile(p"/etc/myapp/secrets.conf", secretsConf)
}
// All three writes succeed atomically (via temp files + barrier rename), or none do.
// If any throws: all temp files are deleted, no production file is touched.

// Composes with `ensure` from #3:
atomic {
    ensure file p"/etc/nginx/sites-enabled/a.conf" { content: confA }
    ensure file p"/etc/nginx/sites-enabled/b.conf" { content: confB }
}
// If b's render throws, a is also rolled back — neither config file is updated.
```

**Semantics:**

- Inside an `atomic` block, all `fs.writeFile`/`fs.delete`/`fs.move`/`ensure file` calls are buffered as pending operations to staged temp files.
- On block exit (no error): files are renamed into place in dependency order. On filesystems supporting `renameat2(... RENAME_EXCHANGE)` (Linux), use it; otherwise fall back to standard rename (which is atomic per-file but not across files).
- On exception: all staged temp files are deleted, no production file is touched.
- Reads inside the block see the staged values (transactional read-your-writes).

**Why this is language/runtime:** Multi-file atomicity requires intercepting writes in a scope. A library function `atomicMultiWrite(dict)` exists in some forms, but a block construct lets you mix writes with arbitrary logic and other operations.

**Risks:**

- True multi-file atomicity is impossible on most filesystems. (Mitigation: document as "best-effort." Single-file is truly atomic via rename. Multi-file is "all or none of the renames execute" — the brief window between renames is unavoidable.)
- Side effects other than file writes (network calls, command execution) are NOT rolled back. (Decision: this is _file_ atomicity, not transactional everything. Use `defer` for compensation patterns.)

---

## 9. Pipeline Operator `|>` — Function Chaining

**The problem:** Stash has UFCS, which handles most chaining cases (`arr.filter(...).map(...).reduce(...)`). But UFCS only works when the function is a method-like first-arg-is-self. Free functions and partial application don't compose nicely.

```stash
// Today, with UFCS:
let result = data.filter((x) => x.active).map((x) => x.name).reduce((a, b) => a + b, "")

// With pipeline operator — works for free functions too:
let result = data
    |> arr.filter((x) => x.active)
    |> arr.map((x) => x.name)
    |> str.join(", ")

// Mixing with regular calls:
let usage = "df -h"
    |> $(_)              // _ is the pipeline placeholder — runs as a shell command
    |> _.stdout
    |> str.parseTable()  // shelved from V2 — but a useful target
    |> arr.filter((row) => row.use_pct > 80)

// Composes with safe shell from #6:
let alerts = userInput
    |> str.trim()
    |> str.lines()
    |> arr.map((line) => safe$(grep ${line} access.log).stdout)
    |> arr.filter((output) => len(output) > 0)
```

**The placeholder `_`:** When the next stage isn't a single-arg call, use `_` to indicate where the piped value goes:

```stash
items |> arr.filter(_, predicate)              // _ becomes first arg
config |> http.post(url, body: _)              // _ becomes named arg
```

**Why language, not library:** The `|>` operator and `_` placeholder need parser support. UFCS is great for one direction; `|>` is better when the function isn't UFCS-friendly or when chaining across namespaces with different conventions.

**What other languages do:**

- **F#, Elixir, Hack, Gleam**: `|>` is foundational
- **OCaml, Haskell**: `|>` and `&` operators
- **JavaScript**: TC39 proposal stalled
- **Nushell, PowerShell**: `|` (their pipe is for objects, similar in spirit)
- **Bash**: `|` (text-only)

Stash with both UFCS _and_ `|>` is rare — very few languages have both. UFCS handles the "method-style" chaining; `|>` handles the "free function" chaining. Together they cover every case elegantly.

**Risks:**

- Two ways to do the same thing in many cases. (Decision: UFCS for method-style, `|>` for free functions. Document the convention. The `_` placeholder removes ambiguity.)
- Lower priority than features 1–8. (Yes — listed as Tier B for that reason.)

---

## 10. JSON-Path Query Expressions — `data[?...]` Syntax

**The problem:** Every script that processes JSON / dict data writes verbose `arr.filter(arr.map(...))` chains. `jq` exists as a CLI for a reason — its query syntax is dramatically more concise than equivalent code in any language.

```stash
let pods = json.parse($(kubectl get pods -A -o json).stdout)

// Today:
let failing = arr.filter(pods.items, (p) => {
    return p.status.phase != "Running" && p.metadata.namespace != "kube-system"
})
let names = arr.map(failing, (p) => p.metadata.name)

// With path queries:
let names = pods.items[? .status.phase != "Running" && .metadata.namespace != "kube-system"][.metadata.name]

// Wildcards descend into arrays:
let allContainerImages = pods.items[*].spec.containers[*].image

// Recursive descent (like jq's `..`):
let allEnvVars = pods..env[*].name

// Use as a method on any value:
data.query("users[? .active][.email]")          // string-form for dynamic queries
data.query(`users[? .active][.email]`)          // with backtick template
```

**Why language/runtime:** A library can offer a query function (`jmespath` packages exist for many languages), but inline syntax is dramatically more ergonomic. The `[? predicate]` and `[* ]` and `..` operators only work as parser-level constructs.

**Risks:**

- Adds parser complexity and a new mini-language. (Significant — this is the most expensive feature in V3 to implement.)
- Could be done as `data.query("...")` library function instead. (True — and that's the fallback if inline syntax is rejected.)

---

## 11. Plan / Apply — Two-Phase Execution

**The problem:** Terraform's killer feature is `terraform plan` showing what will change before `terraform apply` does it. Sysadmins universally love this pattern but no language supports it generically. The shelved V2 `dry` mode is a precursor.

Combine `dry` mode + `ensure` resources from #3 of this doc + an explicit `apply` step:

```bash
# Compute and show what would change, save plan to a file:
stash --plan=changes.plan deploy.stash
# Plan: 3 resource changes
#   ~ /etc/nginx/sites-enabled/site.conf  (content differs, 412 → 489 bytes)
#   + /var/cache/myapp/                    (does not exist)
#   - /tmp/old-marker                      (will be deleted)
#
# Plan saved to changes.plan. Apply with: stash --apply=changes.plan

# Review the plan, then apply:
stash --apply=changes.plan
# Applied 3 changes successfully.
```

**Why this is runtime:** Computing a plan requires running the script with all `ensure` operations switched to "compare and record, don't execute" mode. Then serializing the recorded operations. Then re-executing them on `apply`. This requires deep integration between `dry` mode, `ensure` resources, and a serialization format.

**Conviction:** Medium. Worth designing only if `ensure` (#3) and `dry` (V2 shelved) both ship.

---

## 12. Inline Progress UI — `progress "label" { ... }`

**The problem:** Every CLI script eventually wants progress bars / spinners / phased status. Every script reinvents this poorly. Libraries like `tqdm` (Python) work but lock you into one style.

```stash
// Spinner during a long operation:
progress "Deploying to production" {
    deploy()
}
// ⠋ Deploying to production...   (animated spinner until block exits)

// Progress bar over a known-length iteration:
progress.each "Migrating users" (users) as user {
    migrate(user)
}
// Migrating users  [████████████░░░░░░░░░] 47/100  (12s elapsed, ~14s remaining)

// Multi-phase progress:
progress.phases "Release ${version}" [
    "Build artifact",
    "Push to registry",
    "Update manifests",
    "Restart services"
] as phase {
    when phase == "Build artifact" { build() }
    when phase == "Push to registry" { push() }
    when phase == "Update manifests" { updateManifests() }
    when phase == "Restart services" { restart() }
}
// Auto-updates UI as each phase completes; shows ✓ for done, ⠋ for active, · for pending.
```

**Why language/runtime:** Terminal capability detection (TTY vs. not, color support, width) and graceful degradation (CI logs get "Phase 1/4: Build artifact" instead of an ANSI spinner) require runtime cooperation. Library implementations always get this wrong in some environment.

---

## Summary — Conviction Levels

| #   | Feature                     | Type               | Conviction      | Rationale                                                                                                              |
| --- | --------------------------- | ------------------ | --------------- | ---------------------------------------------------------------------------------------------------------------------- |
| 1   | Capability sandbox          | Runtime + CLI      | **Very High**   | Deno proved this for JS; nothing for sysadmin. Combined with `secret` from V2, becomes Stash's signature safety pitch. |
| 2   | Streaming command output    | Runtime            | **Very High**   | Universal need, universally painful. Auto-cleanup is the killer.                                                       |
| 3   | `ensure` resources          | Language construct | **Very High**   | Brings Ansible-style declarative state into a real scripting language. No competitor.                                  |
| 4   | Path type with literal      | Language + Runtime | **High**        | Strings as paths is a 50-year-old wart. Literal syntax makes the type genuinely usable.                                |
| 5   | Structured concurrency      | Language construct | **High**        | Trio/Java 21 proved the model. Composes with `timeout` from V2.                                                        |
| 6   | Safe shell interpolation    | Language construct | **High**        | Single most prevented class of bugs in shell scripting. Trivial syntax win.                                            |
| 7   | Filesystem watch            | Language + Runtime | **Medium-High** | Cross-platform pain hidden behind clean syntax. Composes with everything.                                              |
| 8   | Atomic file operations      | Language + Runtime | **Medium-High** | Single-file via `fs.atomicWrite()` is straightforward and high-value. Multi-file `atomic { }` is harder.               |
| 9   | Pipeline operator `\|>`     | Language construct | **Medium**      | Quality-of-life improvement. UFCS already covers most cases. Cheap to add.                                             |
| 10  | JSON-path query expressions | Language construct | **Medium**      | Huge ergonomic win, but most expensive to parse. Could ship as library first, then promote.                            |
| 11  | Plan / apply                | Runtime + CLI      | **Medium**      | Killer if `ensure` + `dry` ship. Standalone, less compelling.                                                          |
| 12  | Inline progress UI          | Stdlib + Runtime   | **Medium**      | Common need, but achievable with a well-designed package. Promote to language only if syntax wins.                     |

---

## The Strongest Narrative

If three features from V3 ship together, they create the next major Stash story:

> **Stash is the only sysadmin scripting language where:**
>
> 1. **Untrusted code can't escalate** — the capability sandbox (#1) is enforced at the runtime, not by hoping the OS catches it.
> 2. **Configuration changes are declarative and reviewable** — `ensure` resources (#3) plus plan/apply (#11) bring Terraform's safety to imperative scripts.
> 3. **Long-running orchestration is structured** — streaming command output (#2) plus structured concurrency (#5) make 50-server fleet operations a 5-line script with proper error semantics.

These four features (#1, #2, #3, #5) are the package. Everything else is incremental.

---

## Open Questions

1. **Capability inheritance under `parallel`** — does a `branch` inherit the parent's capabilities, or can it have a more restricted set? (Proposed: inherits by default; `branch (allow: { fs.read: ["/tmp"] })` to restrict.)

2. **`ensure` and `secret`** — when `ensure file` is given content from a `secret`, does the resulting file get permissions tightened automatically? (Proposed: yes — if content is tainted as secret, default mode becomes `0o600` and owner becomes the script's effective UID unless overridden.)

3. **Path literal sigil choice** — `p"..."` vs. `path"..."` vs. backtick variant. (Proposed: `p"..."`. Short, consistent with `f"..."` / `b"..."` from other languages.)

4. **Pipeline `|>` placeholder name** — `_` clashes with common variable conventions. Alternatives: `?`, `it`, `$$`. (Proposed: `_` is most familiar from Scala / Kotlin / OCaml.)

5. **Streaming back-pressure semantics** — if iteration is slow, the OS pipe buffer fills and the child blocks. Document, or add explicit buffering options? (Proposed: document. Match every other language.)

6. **Structured concurrency primitive name** — `parallel` vs. `concurrent` vs. `nursery` (Trio's term). (Proposed: `parallel`. Most intuitive for sysadmins who think in terms of "do these at the same time," not async theory.)

7. **`watch` vs. existing `every` from V1 scheduler** — both run repeatedly. Make sure docs differentiate clearly: `every 5m` is time-driven; `watch path` is event-driven.

8. **`safe$(...)` and existing `$(...)` migration path** — should we eventually deprecate unquoted interpolation in `$(...)`? Big breaking change. (Proposed: never deprecate. Add a static analysis warning that defaults to off, can be enabled with `// stash:warn-shell-injection` directive per-file.)
