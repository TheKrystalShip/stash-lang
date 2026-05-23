### 1. ~~First-class duration & byte-size literals~~ --- Implemented

The single most common source of magic numbers in sysadmin scripts. Every timeout, interval, rotation policy, and disk threshold is a raw integer that means nothing without context.

```stash
// Instead of:
let timeout = 5000              // milliseconds? seconds? who knows
let maxSize = 1073741824        // is this 1GB? Quick, do the math
let interval = 3600             // seconds? Must be an hour?

// Language-level literals:
let timeout = 5s
let maxSize = 1GB
let interval = 2h30m
let rotateAt = 500MB
let ttl = 7d

// Arithmetic just works:
let elapsed = time.since(startTime)
if (elapsed > 30s) { log.warn("Slow operation") }

// Comparison is type-safe — can't compare duration to bytes:
if (fs.size("log.txt") > 100MB) { rotate() }    // ✓ bytes vs bytes
// if (elapsed > 500MB) { ... }                  // ✗ type error: duration vs bytes

// Decomposition:
let d = 90s
println(d.minutes)    // 1
println(d.seconds)    // 30
println(d.totalMs)    // 90000
```

**Why it's novel:** Go has `time.Duration` but no literals. Rust has no built-in duration type. Python uses `timedelta` objects that require imports. No language gives you `5s` or `2GB` as literal syntax with type-safe arithmetic. This directly solves the magic-number problem that plagues every script.

**Literal grammar:** `<number><unit>` where units are `ms`, `s`, `m`, `h`, `d` for duration; `B`, `KB`, `MB`, `GB`, `TB` for bytes. Compound duration: `2h30m15s`. The lexer already handles `@` for IP — this is the same pattern with suffix detection.

---

### 2. ~~First-class semver type~~ --- Implemented

Version comparison is everywhere in deployment, CI/CD, and package management, and everyone does it wrong with string comparison.

```stash
// Literal syntax — reuse @ sigil with v prefix:
let current = @v2.4.1
let minimum = @v2.0.0
let prerelease = @v3.0.0-beta.2

// Comparison follows semver spec (not lexicographic!):
@v1.10.0 > @v1.9.0           // true (numeric, not string)
@v2.0.0-alpha < @v2.0.0      // true (pre-release < release)
@v1.0.0-alpha.1 < @v1.0.0-alpha.beta  // true (semver ordering)

// Range containment with `in`:
@v2.4.1 in @v2.x             // true — major version match
@v2.4.1 in @v2.4.x           // true — minor version match
@v3.0.0 in @v2.x             // false

// Practical use:
let nodeVersion = semver($(node --version).stdout)
if (nodeVersion < @v18.0.0) {
    throw "Node 18+ required, found ${nodeVersion}"
}

// Access components:
println(current.major)    // 2
println(current.minor)    // 4
println(current.patch)    // 1
```

**Why it's novel:** No language has semver as a first-class type. Everyone parses version strings with regex or external libraries. Stash could make `@v1.2.3 > @v1.0.0` just work — with correct pre-release ordering, range matching, and wildcard support. This is uniquely valuable for a language targeting deployment and automation scripts.

---

### 3. ~~Retry blocks as a language construct~~ --- Implemented

Every network-touching script needs retry logic. Today, everyone writes ad-hoc retry loops or pulls in a library. Making it a language construct is a perfect fit for a scripting language.

```stash
// Language-level retry with configurable backoff:
let response = retry (3, backoff: "exponential") {
    http.get("https://api.example.com/health")
}

// With full options:
retry (5, delay: 1s, backoff: "exponential", maxDelay: 30s, on: [Error]) {
    let conn = ssh.connect({ host: "prod-server", user: "deploy" })
    deploy(conn)
}

// Retry returns the successful result, or throws after exhaustion:
let data = try retry (3) {
    json.parse(http.get(url).body)
}
if (data is Error) {
    log.error("All retries failed: ${data.message}")
}
```

**Why it's a language construct, not a function:** A function-based retry can't access the block's scope naturally, can't integrate with the error system (`try retry`), and can't provide clear stack traces showing which attempt failed. As a keyword, it composes with `try`, has access to block scope, and the interpreter can provide retry-specific diagnostics (which attempt, what error, total elapsed time).

---

### ~~4. Built-in cron/scheduler expressions~~ --- Implemented

Stash scripts often become long-running services. Instead of requiring external cron or systemd timers, make scheduling a language primitive.

```stash
// Simple interval:
every 5m {
    let health = http.get("https://api.example.com/health")
    if (health.status != 200) {
        alert("API unhealthy: ${health.status}")
    }
}

// Cron expression:
schedule "0 2 * * *" {
    log.info("Starting nightly backup")
    backup()
}

// Multiple schedules in one script:
every 30s { collectMetrics() }
every 5m  { checkDiskSpace() }
schedule "0 3 * * 0" { weeklyReport() }

// With named schedules for control:
let job = every 1m { ping(server) }
job.pause()
job.resume()
job.cancel()
```

**Why it's novel:** No scripting language has scheduling built in. You always need cron, systemd timers, Task Scheduler, or an external library. For a sysadmin language, it makes natural sense — monitoring scripts, health checks, and rotation policies are all time-driven. The interpreter can manage the event loop internally, handle signal-based shutdown, and provide diagnostics on schedule execution.

---

### 5. Guard expressions / runtime contracts

Function parameter validation is repetitive boilerplate in every script. Language-level guards make validation declarative and self-documenting.

```stash
// Guard clauses on function parameters:
fn deploy(version, env, replicas)
    where version is semver,
          env in ["staging", "production"],
          replicas in 1..100
{
    // Body only executes if all guards pass
    // Guards auto-generate descriptive error messages:
    // "Guard failed: 'env' must be in [staging, production], got 'development'"
}

// On struct fields:
struct ServerConfig {
    host: string where len(host) > 0,
    port: int where port in 1..65535,
    workers: int where workers > 0,
    timeout: duration where timeout >= 1s
}

// On let bindings:
let port = toInt(env.get("PORT")) where port in 1..65535
```

**Why it's novel:** Python has type hints but no runtime enforcement. TypeScript checks at compile time but not runtime. Eiffel pioneered design-by-contract but it's verbose. Stash could make contracts concise and practical — `where` clauses that auto-generate clear error messages. For scripting, where you're constantly validating config values, user input, and API responses, this eliminates the most tedious boilerplate.

---

### 6. Structured diff as a language primitive

Config management, deployment verification, and audit scripts all need to compare states. No scripting language makes this easy.

```stash
// Diff two dicts (config comparison):
let oldConfig = config.read("app.toml")
let newConfig = config.read("app.toml.new")
let changes = diff(oldConfig, newConfig)

for (let change in changes) {
    println("${change.path}: ${change.old} → ${change.new}")
}
// Output:
// server.port: 8080 → 9090
// server.workers: 4 → 8
// database.pool_size: 10 → 20

// Diff two files:
let d = diff(fs.readFile("before.conf"), fs.readFile("after.conf"))
println(d.additions)    // 3 lines added
println(d.deletions)    // 1 line removed
println(d.hunks)        // structured diff hunks

// Diff two arrays (server inventory):
let expected = ["web-1", "web-2", "web-3"]
let actual = getRunningServers()
let d = diff(expected, actual)
println(d.missing)      // servers that should be running but aren't
println(d.extra)        // unexpected servers
```

**Why it's novel:** Every language requires a diff library. Python has `difflib`, Ruby has gems, but none make diffing a core language operation. For a sysadmin language, comparing the current state against the desired state is a fundamental operation — config drift detection, deployment verification, inventory auditing. Making `diff()` a global function that works on strings, arrays, and dicts with structured output would be distinctly useful.

---

### 7. Inline assertions with `ensure` blocks

Post-condition checking after critical operations. Not error handling (that's `try/catch`) — this is "verify the operation actually did what we expected."

```stash
// After a deployment, verify the result:
fn deploy(version) {
    $(kubectl set image deployment/app app=myapp:${version})

    ensure {
        let pods = json.parse($(kubectl get pods -o json).stdout)
        let running = arr.filter(pods.items, (p) => p.status.phase == "Running")
        assert.isTrue(len(running) >= 3, "Expected at least 3 running pods")
        assert.isTrue(arr.every(running, (p) => p.spec.containers[0].image == "myapp:${version}"),
            "Not all pods updated to ${version}")
    } timeout 2m retry 5 {
        log.warn("Deploy verification failed, retrying...")
    }
}
```

This combines retry + assertion + timeout into a verification block that's distinct from error handling. It's "make sure reality matches intent" rather than "catch when things blow up."
