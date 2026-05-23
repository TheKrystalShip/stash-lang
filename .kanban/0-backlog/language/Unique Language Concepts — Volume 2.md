# Unique Language Concepts — Volume 2

**Status:** Brainstorm / Design exploration
**Created:** 2025-04-12
**Context:** Follow-up to "Unique language concepts.md" — features 1–4 shipped (duration/bytesize literals, semver, retry blocks, scheduler). Features 5–7 (guard expressions, structured diff, ensure blocks) were shelved as unconvincing. This document proposes a new batch of candidates.

**Design criteria — what makes the cut:**

- Solves a problem sysadmins hit _every single week_
- Can't be cleanly solved with a library — needs language or runtime support
- The syntax is obvious and self-documenting
- Interaction with existing Stash features is clean, not bolted-on

---

## 1. ~~`secret` Type — Auto-Redacting Sensitive Values~~ IMPLEMENTED

**The problem:** Every sysadmin script handles credentials — API keys, tokens, passwords, connection strings. Every language lets you accidentally leak them via `println()`, string interpolation, error messages, log output, or stack traces. This is the #1 cause of credential exposure in scripts. No language prevents it.

```stash
// Mark a value as secret:
let apiKey = secret(env.get("API_KEY"))
let dbPassword = secret(fs.readFile("/run/secrets/db_pass").trim())

// Secrets work normally in authorized contexts:
http.get("https://api.example.com", {
    headers: { "Authorization": "Bearer ${apiKey}" }
})
// ↑ This works — http namespace is secret-aware, sends the real value

// But secrets auto-redact everywhere else:
println(apiKey)                    // prints: ******
println("Key is: ${apiKey}")       // prints: Key is: ******
let msg = "token=" + apiKey        // msg = "token=******"

// Error messages are safe:
// "Connection failed: password=******" — never leaks the real value

// Secrets propagate through operations:
let connStr = secret("postgres://user:${dbPassword}@db:5432/app")
// ↑ connStr is also a secret because it was constructed from one

// Explicit unwrap when you genuinely need the raw value:
let raw = apiKey.reveal()          // returns the actual string
// This is auditable — grep for .reveal() in code review

// Type checking:
typeof(apiKey)                     // "secret"
apiKey is secret                   // true
len(apiKey)                        // works — returns length of underlying value
```

**Why this is a language feature, not a library:** Redaction must happen at the runtime level — in `Stringify()`, in string interpolation (`RuntimeOps.Interpolate`), in error message construction, in the REPL's output formatter. A library can't intercept `println()` or `"${value}"`. The runtime can.

**Implementation sketch:**

- New `StashValue` tag: `Secret` wrapping another `StashValue`
- `RuntimeValues.Stringify()` returns `"******"` for Secret-tagged values
- `RuntimeOps.Interpolate()` calls `Stringify()` → auto-redacted
- `secret()` global function wraps any value
- `.reveal()` unwraps — returns the inner value
- `http`, `process` (for command args), and `fs.writeFile` receive the real value internally
- Taint propagation: any string concatenation involving a secret produces a secret

**What other languages do:** Nothing. Python has no concept. Go has no concept. Even Vault client libraries can't prevent `fmt.Println(password)`. HashiCorp Sentinel has "sensitive" markers but it's not a general-purpose language. Pulumi has `Output<T>.secret()` but only for IaC state files, not runtime behavior.

**Risks:**

- Taint propagation complexity — how far does it spread? (Decision: string operations that include a secret produce a secret. Comparison, length, etc. return normal values.)
- Performance — extra tag check on every stringify. (Mitigation: Secret is rare; the check is a single tag comparison.)
- False sense of security — memory dumps can still leak. (Mitigation: document this. The goal is preventing _accidental_ leakage, not adversarial extraction.)

---

## 2. ~~`timeout` Block — Time-Bounded Execution~~ IMPLEMENTED

**The problem:** Every sysadmin script wraps operations in timeout logic. HTTP calls hang. SSH connections stall. Database queries run forever. Every language makes this painful — Python needs `signal.alarm` or `threading.Timer`, Go needs `context.WithTimeout`, Bash needs the `timeout` command or background process tricks. No language makes it a clean block construct.

```stash
// Simple timeout:
let result = timeout 30s {
    http.get("https://slow-api.example.com/data")
}

// Timeout composes with try:
let data = try timeout 10s {
    json.parse(http.get(url).body)
}
if (data is Error) {
    println("Request timed out or failed: ${data.message}")
}

// Timeout composes with retry:
retry (3, delay: 2s) {
    timeout 5s {
        $(ssh deploy@prod "systemctl status nginx")
    }
}

// Nested timeouts — inner wins if smaller:
timeout 60s {
    for (let server in servers) {
        timeout 10s {
            healthCheck(server)  // each server gets 10s max
        }
    }
    // entire loop gets 60s max
}

// Timeout on any operation:
timeout 5m {
    $(apt update && apt upgrade -y)
}
```

**Why this is a language construct:** A function can't transparently cancel an arbitrary block of code. The runtime needs to set up a cancellation mechanism (CancellationToken in .NET) that threads through built-in operations (`http.get`, `$()`, `process.spawn`, `time.sleep`). This is exactly what Go's `context.WithTimeout` does — but requiring manual plumbing. As a keyword, the runtime handles all the plumbing.

**Implementation sketch:**

- New AST node: `TimeoutExpr` (expression — returns the block's value or throws TimeoutError)
- Compiler emits: push timeout duration → `OpCode.TimeoutStart` → block bytecode → `OpCode.TimeoutEnd`
- VM creates a `CancellationTokenSource` with the specified timeout
- All built-in I/O operations (`http.*`, `$()`, `process.*`, `time.sleep`, `fs.*`) check the ambient cancellation token
- On timeout: throws a `TimeoutError` (catchable with `try`)
- Nesting: inner timeout takes precedence if shorter; outer timeout still counts down

**What other languages do:**

- Go: `context.WithTimeout()` + manual plumbing through every function
- Python: `signal.alarm()` (Unix only), `asyncio.wait_for()` (async only), or threading hacks
- Bash: `timeout` command (applies to a single command, not a block of logic)
- Ruby: `Timeout.timeout(secs) { }` — exists but has notorious thread-safety issues
- No language gives you `timeout 30s { arbitrary_code }` that just works across all I/O

**Risks:**

- CPU-bound code can't be cancelled cooperatively. (Decision: timeout only guarantees interruption of I/O operations. CPU-bound loops check for cancellation at loop boundaries — same as Go's approach.)
- What if the block has side effects that need cleanup? (Decision: compose with `try/finally` — `timeout 30s { try { ... } finally { cleanup() } }`)

---

## 3. ~~`lock` Block — File-Based Mutual Exclusion~~ IMPLEMENTED

**The problem:** Deployment scripts, cron jobs, and maintenance tasks must not run concurrently. Every sysadmin writes file locking wrong — race conditions between check-and-create, stale locks from crashed processes, no cleanup on signals. This is a solved problem in theory but nobody gets it right in practice.

```stash
// Ensure only one instance runs:
lock "/var/run/deploy.lock" {
    println("Starting deployment...")
    deploy(version)
    println("Done.")
}
// Lock file is atomically created on entry, removed on exit (even on error/signal)

// With options:
lock "/var/run/backup.lock" (wait: 30s) {
    // Wait up to 30s for the lock — throw if still locked
    backup()
}

lock "/var/run/rotate.lock" (wait: 0s) {
    // Fail immediately if locked (non-blocking)
    rotateLogs()
}

// Lock with stale detection:
lock "/var/run/job.lock" (stale: 1h) {
    // If lock file is older than 1h, assume stale and steal it
    longRunningJob()
}

// Composes with retry and timeout:
retry (3, delay: 10s) {
    lock "/var/run/deploy.lock" (wait: 0s) {
        timeout 5m {
            deploy()
        }
    }
}
```

**Why this is a language construct:** File locking requires signal handling (SIGINT/SIGTERM cleanup), atomic file operations, PID tracking for stale detection, and guaranteed cleanup even on unhandled errors. A library function can handle some of this, but guaranteed cleanup on process termination requires runtime integration. Also, the `lock path { }` syntax is far cleaner than `let l = lockFile(path); try { ... } finally { l.release() }`.

**Implementation sketch:**

- New AST node: `LockStmt` with path expression, optional options dict, and body block
- Creates lock file with PID content (for stale detection)
- Uses OS-level advisory locks (`flock` on Linux/macOS, `LockFileEx` on Windows)
- Registers cleanup on `AppDomain.ProcessExit` and `Console.CancelKeyPress`
- `wait` option: poll interval with total timeout
- `stale` option: check lock file mtime + PID liveness

**What other languages do:** Every language requires a library or manual implementation. Python: `fcntl.flock()` + manual cleanup. Bash: `flock` command (Linux only). Ruby: `File.flock`. None provide a clean block syntax with automatic cleanup, stale detection, and cross-platform support.

---

## 4. `dry` Mode — Side-Effect Suppression

**The problem:** Every deployment script needs a dry-run mode. Ansible has `--check`. Terraform has `plan`. `make` has `-n`. But no programming language has dry-run as a runtime concept. You always end up wrapping every side-effecting call in `if (!dryRun) { ... } else { println("Would do X") }` — which is tedious, error-prone, and doubles the code.

```stash
// Run a script in dry mode from the CLI:
// $ stash deploy.stash --dry

// Inside the script, side effects are automatically suppressed:
fn deploy(version, env) {
  let config = config.read("deploy.toml");  // ✓ reads still work
  let servers = arr.filter(config.servers, (s) => s.env == env);

  for (let server in servers) {
    // In dry mode: prints "DRY: Would execute: ssh deploy@web-1 ..."
    // In normal mode: actually executes
    $(ssh deploy@${server.host} "systemctl restart app");
    // In dry mode: prints "DRY: Would write 234 bytes to /etc/app/config.toml"
    // In normal mode: actually writes
    fs.writeFile("/etc/app/config.toml", config.render(version));
  }
}

// Check mode programmatically:
if (sys.isDry()) {
  io.println("Running in dry-run mode — no changes will be made");
}

// Force a real operation even in dry mode (escape hatch):
dry.allow {
  io.println("Deployment started");  // logging should always happen
}

// Activate dry mode programmatically for a scope:
dry {
  dangerousOperation();  // suppressed
}
```

**What gets suppressed in dry mode:**
| Operation | Dry behavior |
|-----------|-------------|
| `$(command)` | Prints the command, returns synthetic `{ stdout: "", exitCode: 0 }` |
| `$>(command)` | Prints the command, no execution |
| `fs.writeFile()` | Prints what would be written (path + size) |
| `fs.delete()` | Prints what would be deleted |
| `fs.copy()`, `fs.move()` | Prints source → destination |
| `http.post/put/patch/delete()` | Prints method + URL + body size |
| `process.spawn()` | Prints command, returns mock handle |
| Reads (`fs.readFile`, `http.get`, `env.get`) | Execute normally — reads are safe |

**Why this is a runtime feature, not a convention:** Convention-based dry-run (checking a flag before every operation) has 100% defect rate in real codebases — someone always forgets to check. Making it a runtime mode that the interpreter enforces means you get dry-run for free in any script without any code changes. The script author writes the real logic once; the runtime handles suppression.

**Implementation sketch:**

- CLI flag: `--dry` sets a runtime flag on the VM
- Every built-in function that performs I/O checks the flag
- Intercepted operations call `io.eprintln()` with a `DRY:` prefix and operation description
- `sys.isDry()` exposes the flag to scripts
- `dry { }` block enables dry mode for a scope (pushes flag, pops on exit)
- `dry.allow { }` exempts a scope from dry mode (for logging, auditing)
- Read operations are never suppressed

**What other languages do:** Nothing at the language level. Every framework reinvents this: Ansible `--check`, Chef `why-run`, Puppet `--noop`, Terraform `plan`. But these are tool-specific. No general-purpose language lets you write a script and get dry-run for free.

**Risks:**

- Scripts that depend on command output for control flow will break in dry mode (suppressed commands return empty stdout). (Mitigation: document this. Provide `dry.allow { }` escape hatch for commands that are read-like but use `$()` syntax.)
- What about database operations? (Not built-in, so out of scope. Package authors can check `sys.isDry()` to implement dry support.)

---

## 5. ~~`log` Namespace — Structured Leveled Logging~~ IMPLEMENTED

**The problem:** Every script starts with `println()` and eventually needs proper logging — levels, timestamps, structured fields, output format control, file rotation. Every language requires importing and configuring a logging library. For a sysadmin scripting language where scripts become long-lived services (via `every`/`schedule`), logging should be zero-config and built-in.

```stash
// Zero-config — just works:
log.info("Server started on port ${port}")
// 2025-04-12T10:30:00Z INFO  Server started on port 8080

log.warn("Disk usage at ${usage}%")
log.error("Connection failed: ${err.message}")
log.debug("Request payload: ${json.pretty(body)}")

// Structured fields (key-value context):
log.info("Deployment complete", {
    version: @v2.4.1,
    env: "production",
    duration: elapsed,
    servers: len(servers)
})
// 2025-04-12T10:30:00Z INFO  Deployment complete  version=2.4.1 env=production duration=12.4s servers=3

// Configure output format:
log.format("json")
log.info("Request handled", { method: "GET", path: "/api/users", status: 200 })
// {"time":"2025-04-12T10:30:00Z","level":"info","msg":"Request handled","method":"GET","path":"/api/users","status":200}

// Log level filtering:
log.level("warn")           // only warn and above
log.debug("This is hidden") // suppressed

// Log to file:
log.output("app.log")

// Scoped context (adds fields to all log calls in scope):
log.with({ requestId: reqId, user: username }) {
    log.info("Processing request")         // includes requestId + user
    log.info("Query completed", { rows: n }) // includes requestId + user + rows
}

// Log level from CLI:
// $ stash server.stash --log-level=debug
// $ stash server.stash --log-format=json
```

**Supported levels:** `trace`, `debug`, `info`, `warn`, `error`, `fatal`

**Output formats:** `text` (human-readable, default), `json` (structured), `logfmt` (key=value)

**Why this is stdlib, not a language construct:** Logging doesn't need new syntax — it needs a well-designed namespace with sensible defaults. The key insight is that every sysadmin script needs logging, and the path from `println()` to proper logging should be zero friction. No library install, no configuration boilerplate, no import.

**Integration with `secret` type:** If proposal #1 ships, `log.info("Connecting with ${password}")` auto-redacts secrets. The logging layer inherits redaction from the runtime.

**Integration with `dry` mode:** If proposal #4 ships, log calls are never suppressed in dry mode (they're reads, not writes). And suppressed operations get logged at debug level: `DRY: Would execute: ssh deploy@prod ...`

---

## 6. ~~`defer` Statement — LIFO Cleanup~~ IMPLEMENTED

**The problem:** Stash has `try/finally`, which works for single-resource cleanup. But sysadmin scripts often acquire multiple resources that each need independent cleanup — temp files, network connections, lock files, process handles. With `try/finally`, this becomes deeply nested or requires a single finally block that tracks what was acquired.

```stash
fn deployToCluster(version) {
    let tmpDir = fs.tempDir()
    defer fs.delete(tmpDir, { recursive: true })

    let artifact = download(version, tmpDir)

    let conn = ssh.connect("deploy@prod")
    defer conn.close()

    let backup = backupCurrent(conn)
    defer {
        if (deployFailed) {
            restore(conn, backup)
        }
    }

    upload(conn, artifact)
    activate(conn, version)
}
// On function exit (normal or error):
//   1. Restore backup if failed (last defer = first to run)
//   2. Close SSH connection
//   3. Delete temp directory
```

**Compare with try/finally:**

```stash
// The same logic without defer — deeply nested:
fn deployToCluster(version) {
    let tmpDir = fs.tempDir()
    try {
        let artifact = download(version, tmpDir)
        let conn = ssh.connect("deploy@prod")
        try {
            let backup = backupCurrent(conn)
            try {
                upload(conn, artifact)
                activate(conn, version)
            } catch (e) {
                restore(conn, backup)
                throw e
            }
        } finally {
            conn.close()
        }
    } finally {
        fs.delete(tmpDir, { recursive: true })
    }
}
```

**Why `defer` over `try/finally`:** Defer keeps cleanup next to acquisition — the intent is clearer and the code is linear instead of nested. Go proved this pattern scales well. The LIFO (last-in-first-out) execution order matches natural resource dependency — you release resources in reverse order of acquisition.

**Implementation sketch:**

- New AST node: `DeferStmt` with a statement or block body
- Compiler emits the deferred body as a closure, pushes to a per-function defer stack
- At function exit (return, throw, or fall-through), VM pops and executes deferred closures in LIFO order
- Errors in deferred blocks are collected, not swallowed — the first error propagates, others are attached

**Go's `defer` vs. Stash's:** Go's `defer` evaluates arguments eagerly and defers the call. Stash's `defer` defers the entire block/statement, which is more intuitive for scripting. The block captures variables by reference (closures), so `defer { cleanup(conn) }` uses the value of `conn` at execution time, not declaration time.

---

## 7. Command Output Table Parsing — `str.parseTable()` / UFCS `.parseTable()`

**The problem:** The #1 pattern in sysadmin scripts is running a command and extracting structured data from its columnar output. Think `ps aux`, `df -h`, `docker ps`, `kubectl get pods`, `netstat -tlnp`, `lsblk`, `mount`. Every single one produces a text table, and every script uses fragile `awk`/`cut`/`grep` pipelines to parse it. This is the single biggest source of fragile shell scripts.

```stash
// Instead of:
let raw = $(ps aux).stdout
let lines = str.lines(raw)
let headers = str.split(str.trim(lines[0]))
// ... 20 lines of parsing ...

// Just:
let procs = $(ps aux).stdout.parseTable()
// Returns: [
//   { USER: "root", PID: "1", CPU: "0.0", MEM: "0.1", COMMAND: "/sbin/init" },
//   { USER: "www", PID: "1234", CPU: "2.1", MEM: "1.4", COMMAND: "nginx: worker" },
//   ...
// ]

// Now use normal Stash operations:
let heavy = procs.filter((p) => toFloat(p.CPU) > 50.0)
let myProcs = procs.filter((p) => p.USER == env.user())

// Works with any columnar command:
let disks = $(df -h).stdout.parseTable()
let fullDisks = disks.filter((d) => {
    let pct = toInt(str.replace(d["Use%"], "%", ""))
    return pct > 90
})

// Docker:
let containers = $(docker ps).stdout.parseTable()
let running = containers.filter((c) => c.STATUS.startsWith("Up"))

// Kubernetes:
let pods = $(kubectl get pods -A).stdout.parseTable()
let failing = pods.filter((p) => p.STATUS != "Running")

// Custom delimiter (for things like /etc/passwd):
let users = fs.readFile("/etc/passwd").parseTable({ delimiter: ":", headers: ["user","pass","uid","gid","info","home","shell"] })
```

**Why this is stdlib, not a language construct:** It's a parsing function, not new syntax. But it belongs in the standard library (not an external package) because parsing command output is a _fundamental operation_ for a shell scripting language — as fundamental as string splitting or regex matching.

**Implementation sketch:**

- `str.parseTable(text)` — auto-detect whitespace-aligned columns using header positions
- `str.parseTable(text, options)` — override delimiter, provide custom headers, skip lines
- Column detection algorithm: find header row, determine column boundaries by whitespace gaps, split data rows at those boundaries
- Returns `array` of `dict` (one dict per row, headers as keys)
- Via UFCS: `$(cmd).stdout.parseTable()` works naturally
- Edge cases: columns with spaces (e.g., `COMMAND` in `ps`), missing values, variable-width columns — use the standard "fixed-width column detection" algorithm that `column -t` uses

**What other languages do:** Python has no built-in. You use `subprocess` + manual parsing or `pandas.read_fwf()` (a data science library). Ruby has no built-in. Bash has `awk`. PowerShell is the closest — `Get-Process` returns objects, not text — but that's because PowerShell's commands return objects. Stash wraps external commands that return text, so parsing is essential.

---

## 8. Health Probes — `net.probe()` / `http.probe()`

**The problem:** Monitoring, deployment verification, and service readiness checks all need to probe whether something is reachable. Is this port open? Is this HTTP endpoint returning 200? Is this process alive? Every script cobbles this together from `http.get` + `try` + timeout logic. A dedicated probe function with sensible defaults eliminates boilerplate.

```stash
// TCP port probe:
if (net.probe("db-server", 5432)) {
    println("Database is reachable")
}

// HTTP health check:
if (http.probe("https://api.example.com/health")) {
    println("API is healthy")
}

// With options:
let ready = http.probe("https://api.example.com/health", {
    timeout: 5s,
    expect: 200,           // expected status code (default: any 2xx)
    interval: 1s,          // retry interval
    retries: 5             // number of retries before declaring failure
})

// Wait for a service to be ready (blocking probe):
net.waitFor("localhost", 5432, { timeout: 30s })
// ↑ Blocks until port is reachable or timeout expires

// In deployment scripts:
$(docker compose up -d)
net.waitFor("localhost", 8080, { timeout: 60s })
println("Service is ready")

// Probe multiple services:
let services = [
    { name: "api", host: "localhost", port: 8080 },
    { name: "db", host: "localhost", port: 5432 },
    { name: "redis", host: "localhost", port: 6379 }
]

for (let svc in services) {
    let ok = net.probe(svc.host, svc.port, { timeout: 5s })
    println("${svc.name}: ${ok ? 'UP' : 'DOWN'}")
}
```

**Why this is stdlib:** No new syntax needed — just well-designed functions in the `net` and `http` namespaces. But the value is immense: every deployment script, every monitoring script, every docker-compose orchestration needs "wait for this to be ready." Currently that requires `retry { try timeout ... { http.get(...) } }` — three nested constructs for what should be one function call.

**Integration:** Composes naturally with `timeout` (proposal #2) and `retry` (already shipped). But `probe` encapsulates the common case so you don't need to compose them manually.

---

## Summary — Conviction Levels

| #   | Feature           | Type               | Conviction      | Rationale                                                                                                          |
| --- | ----------------- | ------------------ | --------------- | ------------------------------------------------------------------------------------------------------------------ |
| 1   | `secret` type     | Language + Runtime | **Very High**   | No language has this. Credential leakage is the #1 security issue in scripts. Elegant solution.                    |
| 2   | `timeout` block   | Language construct | **Very High**   | Universal need, universally painful. Clean syntax, deep runtime integration.                                       |
| 3   | `lock` block      | Language construct | **High**        | File locking is always implemented wrong. Language-level guarantees solve this properly.                           |
| 4   | `dry` mode        | Runtime mode       | **High**        | No language has this. Every deployment tool reinvents it. Transformative for script safety.                        |
| 5   | `log` namespace   | Stdlib             | **High**        | Every script needs logging. Zero-config + structured fields + level filtering. Synergizes with `secret` and `dry`. |
| 6   | `defer` statement | Language construct | **Medium-High** | Proven pattern from Go. Strictly better ergonomics than try/finally for multi-resource cleanup. Less novel.        |
| 7   | Table parsing     | Stdlib             | **Medium-High** | Solves the #1 command output parsing pain. Pure stdlib function, no language changes needed.                       |
| 8   | Health probes     | Stdlib             | **Medium**      | Very useful but achievable with existing constructs. Convenience over novelty.                                     |

**Strongest package:** Features 1 + 2 + 4 together create a narrative — "Stash is the language where deployment scripts are safe by default." Secrets don't leak, operations don't hang, and you can dry-run anything. No other language offers this combination.

---

## Open Questions

1. **`secret` taint propagation** — How aggressive? Only string concatenation, or also array inclusion (`[apiKey, other]`)? Proposed: string operations only. Arrays/dicts containing secrets are not themselves secret (but printing them would still redact the secret element via `Stringify()`).

2. **`timeout` and CPU-bound code** — Cooperative cancellation at loop boundaries? Or just I/O? Proposed: I/O only (matching Go's approach). Document that `timeout` is for I/O-bound blocks.

3. **`dry` mode granularity** — Per-function? Per-block? Global only? Proposed: global (`--dry` flag) + scoped (`dry { }` block for testing). Not per-function.

4. **`lock` cross-platform behavior** — `flock` on Unix, `LockFileEx` on Windows. Are semantics identical? (Advisory vs. mandatory locking differences need investigation.)

5. **`defer` in non-function contexts** — Does `defer` work in top-level scripts (deferred until script exit)? In blocks? Proposed: function-scoped only, matching Go. Top-level defers execute on script exit.

6. **`log` namespace name** — Is `log` available? Check it doesn't conflict with `math.log()`. (No conflict — `log` would be a namespace, `math.log()` is a namespaced function. Different symbols.)
