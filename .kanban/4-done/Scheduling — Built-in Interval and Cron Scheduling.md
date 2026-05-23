# Scheduling — Built-in Interval and Cron Scheduling

**Status:** Backlog — Analysis
**Created:** 2025-04-11
**Origin:** Extracted from "Unique language concepts" §4
**Dependencies:** Duration literals (§1 from same doc) would enhance ergonomics but are NOT required

---

## 1. Problem Statement

Sysadmin scripts frequently need to run operations on a recurring basis: health checks, disk space monitoring, log rotation, metric collection, nightly backups. Today, every language requires either:

- External scheduling (cron, systemd timers, Task Scheduler)
- Ad-hoc while-loop + sleep patterns that drift over time
- Third-party libraries (Node's `node-cron`, Python's `APScheduler`, Elixir's `Quantum`)

Stash targets system administration as its primary domain. Recurring execution is a first-class concern for this audience. Making scheduling a built-in capability would eliminate significant boilerplate and remove the need for external schedulers in common use cases.

---

## 2. The Fundamental Design Question

**Stash is a scripting language with a run-to-completion execution model.** There is no event loop. Scripts execute bytecode sequentially, can spawn async tasks on the .NET ThreadPool via `async fn` / `task.run()`, and exit when the main execution path completes — even if background tasks are still running.

This creates an inherent tension with scheduling:

> If I write `every 30s { checkHealth() }`, does my script ever exit?

This is THE central question the spec must answer. Every other design decision flows from it.

---

## 3. Prior Art Analysis

### 3.1 Node.js / Deno — Event Loop Model

```javascript
const id = setInterval(() => checkHealth(), 30000);
// Script stays alive because the event loop has active handles
clearInterval(id);
// Now the script can exit
```

**Key insight:** Active timers keep the process alive implicitly. `timer.unref()` opts a timer out of keep-alive (process exits even if timer is active).

**Strengths:** Zero-ceremony long-running services. Natural for the common case.
**Weaknesses:** Requires an event loop, which Stash doesn't have. Users sometimes confused about why their script won't exit.

### 3.2 Go — Explicit Control Model

```go
ticker := time.NewTicker(30 * time.Second)
defer ticker.Stop()
for range ticker.C {
    checkHealth()
}
// If main goroutine exits, ALL goroutines die — no implicit keep-alive
```

**Key insight:** Tickers are just channels. The programmer must explicitly block (via `for range`, `select {}`, or `<-done`) to keep the process running. Background goroutines die when main exits.

**Strengths:** No surprises. The programmer explicitly chooses when to block and for how long.
**Weaknesses:** Boilerplate for the common "run forever" case.

### 3.3 Elixir / Quantum — Supervised Process Model

```elixir
# Schedule lives inside a supervised GenServer process
config :my_app, MyApp.Scheduler,
  jobs: [{"*/5 * * * *", {HealthCheck, :run, []}}]
```

**Key insight:** Scheduling is a supervised long-running process managed by OTP. It's inherently part of an application that runs forever (an Elixir app, not a script).

**Strengths:** Rock-solid reliability, automatic restarts, distributed scheduling.
**Weaknesses:** Not applicable to scripting — this is an application framework pattern.

### 3.4 Python sched — Manual Run Model

```python
s = sched.scheduler(time.time, time.sleep)
s.enter(30, 1, check_health)
s.run()  # Blocks until all events done (but doesn't repeat!)
```

**Key insight:** The scheduler doesn't loop by itself. `run()` blocks and processes queued events, then returns. For recurring tasks, you must re-queue the event inside the handler.

**Strengths:** Simple, explicit, library-level.
**Weaknesses:** No built-in repeating, no cron expressions, tedious for the common case.

### 3.5 Summary: What Stash Can Learn

| Approach | Keep-alive? | Repeating? | Cron?    | Complexity          |
| -------- | ----------- | ---------- | -------- | ------------------- |
| Node.js  | Implicit    | Built-in   | Library  | Event loop required |
| Go       | Explicit    | Built-in   | Library  | Channel + goroutine |
| Elixir   | Inherent    | Built-in   | Built-in | Supervision tree    |
| Python   | Manual run  | Manual     | Library  | Simple but tedious  |

**The Node.js implicit keep-alive is the most ergonomic, but requires an event loop Stash doesn't have.** The Go explicit-control model is closest to Stash's current architecture (ThreadPool-based async, no event loop).

---

## 4. Design Options

### Option A: Language Keywords (`every`, `schedule`)

New keywords parsed into dedicated AST nodes:

```stash
every 30s {
  checkHealth();
}

schedule "0 2 * * *" {
  runBackup();
}
```

**Pros:**

- Most novel and distinctive — no other language has this syntax
- Clean, readable, zero-ceremony
- Composable with `try` (`try every 30s { ... }`)
- Compiler can optimize and validate at parse time

**Cons:**

- Two new keywords consuming identifiers (`every`, `schedule`)
- New AST node types, new parser rules, new bytecode opcodes
- Still needs the keep-alive mechanism regardless
- Cron expression validation at parse time is complex
- Harder to evolve (changing keyword semantics is a breaking change)

**Assessment:** High reward, high cost. The syntax is beautiful but the semantics (keep-alive, error handling, job control) are the hard part, and keywords don't help with that. The cost of consuming two identifiers is also non-trivial — `every` and `schedule` are common variable names.

### Option B: Stdlib Functions in `task` Namespace

Functions in the existing `task` namespace (or a new `scheduler` namespace):

```stash
let job = task.every(30s, () => {
  checkHealth();
})

let backup = task.cron("0 2 * * *", () => {
  runBackup();
})

// Explicit keep-alive
task.keepAlive();
```

**Pros:**

- No parser/lexer changes — builds entirely on existing infrastructure
- Consistent with how `task.run()`, `task.delay()`, `task.timeout()` already work
- Returns handles naturally (just a function return value)
- Easy to evolve (adding options is just adding function parameters)
- With duration literals, `task.every(30s, fn)` is nearly as clean as `every 30s { fn }`

**Cons:**

- Less distinctive — "just another stdlib function"
- Lambda wrapping adds ceremony vs. a block keyword
- No special compiler validation or optimization opportunities

**Assessment:** Lower risk, faster to implement, nearly as ergonomic (especially with duration literals). The semantics are identical — only the syntax differs.

### Option C: Hybrid — Stdlib Now, Keywords Later

Start with Option B. If the feature proves popular and the patterns stabilize, consider promoting to keyword syntax in a future version. The keyword syntax could desugar to the same underlying mechanism.

**Assessment: This is the recommended approach.** It de-risks the design, gets the feature shipping faster, and preserves the option for syntax sugar later.

---

## 5. Recommended Design: Stdlib Functions + Explicit Keep-Alive

### 5.1 Core Semantics: Does Scheduling Prevent Exit?

**Decision: NO, by default. Active schedulers do NOT prevent script exit.**

**Rationale:**

- Stash's execution model is run-to-completion. Changing this implicitly would be a fundamental and surprising behavioral change.
- It's consistent with how `async fn` and `task.run()` already work: spawned tasks are orphaned if not awaited.
- Go uses the same model successfully: background goroutines/tickers don't prevent exit.
- The "run forever until Ctrl+C" behavior should be OPT-IN, not automatic.

**The opt-in mechanism is `task.keepAlive()`:** a blocking function that suspends the main execution thread until all tracked jobs are cancelled, or a signal (SIGINT/SIGTERM) is received.

```stash
// This script EXITS IMMEDIATELY — jobs fire once (maybe) then orphaned
task.every(30s, () => checkHealth());
task.every(5m, () => checkDiskSpace());

// This script RUNS FOREVER — explicit keep-alive
task.every(30s, () => checkHealth());
task.every(5m, () => checkDiskSpace());
task.keepAlive();  // blocks until all jobs cancelled or signal received
```

This makes intent crystal clear. A script that schedules + keeps alive is explicitly a long-running service. A script that schedules without keep-alive is using the scheduler as a "fire soon" mechanism.

> **Alternative considered:** Implicit keep-alive (Node.js model).
> Rejected because it would make debugging confusing ("why won't my script exit?"), breaks the fundamental run-to-completion contract, and would require an event loop or equivalent machinery that Stash doesn't have.

### 5.2 API Surface

#### `task.every(intervalSeconds, callback [, options])` → `Job`

Creates a recurring job that executes `callback` at fixed intervals.

```stash
// Basic usage
let job = task.every(30s, () => {
  let health = http.get("https://api.example.com/health");
  if (health.status != 200) {
    io.println("ALERT: API unhealthy");
  }
});

// With options struct
let job = task.every(60s, () => collectMetrics(), task.JobOptions {
  name: "metrics-collector",
  immediate: true,          // run once immediately before first interval
  errorPolicy: "continue",  // "continue" (default) | "stop" | "backoff"
  maxErrors: 10,            // stop after 10 consecutive errors (0 = unlimited)
  overlap: false            // skip tick if previous invocation still running
});
```

**Parameters:**

- `intervalSeconds` — `float` — Interval in seconds between executions. Must be > 0.
- `callback` — `fn()` — Zero-argument function to execute on each tick.
- `options` (optional) — `task.JobOptions` struct (see §5.3).

**Behavior:**

- First execution occurs after `intervalSeconds` elapses (unless `immediate: true`).
- Uses a timer-based approach, NOT sleep-based: interval is measured from tick start, not from previous execution end. This prevents drift.
- If `overlap: false` (default) and the previous execution hasn't finished when the next tick arrives, the tick is SKIPPED, not queued.
- Errors in `callback` are caught and handled per `errorPolicy`. They do NOT propagate to the caller or crash the script.

#### `task.cron(expression, callback [, options])` → `Job`

Creates a recurring job triggered by a cron expression.

```stash
// Nightly backup at 2 AM
let backup = task.cron("0 2 * * *", () => {
  io.println("Starting nightly backup");
  runBackup();
});

// Every 15 minutes during business hours
let metrics = task.cron("*/15 9-17 * * 1-5", () => {
  collectBusinessMetrics();
});
```

**Parameters:**

- `expression` — `string` — Standard 5-field cron expression (`minute hour day-of-month month day-of-week`). Supports standard cron syntax including `*`, ranges (`1-5`), steps (`*/15`), lists (`1,3,5`), and named values (`MON-FRI`).
- `callback` — `fn()` — Zero-argument function to execute on each trigger.
- `options` (optional) — `task.JobOptions` struct.

**Cron extensions supported:**

- `@yearly` / `@annually` — `0 0 1 1 *`
- `@monthly` — `0 0 1 * *`
- `@weekly` — `0 0 * * 0`
- `@daily` / `@midnight` — `0 0 * * *`
- `@hourly` — `0 * * * *`

**No seconds field.** Stash targets sysadmin use cases where sub-minute scheduling is rarely needed. The 5-field standard cron format is well-understood and sufficient.

#### `task.keepAlive([options])` → `void`

Blocks the main execution thread until:

1. All tracked jobs have been cancelled, OR
2. A signal (SIGINT/SIGTERM) is received, OR
3. The optional timeout expires

```stash
// Run forever (until Ctrl+C)
task.keepAlive();

// Run for 1 hour, then exit
task.keepAlive(task.KeepAliveOptions { timeout: 3600 });

// Run until explicitly signalled
task.keepAlive(task.KeepAliveOptions { signals: [sys.Signal.SIGTERM] });
```

**Behavior:**

- When a signal is received, all active jobs are cancelled gracefully (current executions are allowed to complete, no new executions are started).
- After signal, the function returns normally (does not throw).
- If no jobs are active when called, returns immediately.

### 5.3 Job Control

The `Job` struct returned by `task.every` and `task.cron`:

```stash
struct Job {
  name: string,       // user-provided or auto-generated
  status: string,     // "running" | "paused" | "cancelled" | "stopped"
  runCount: int,      // number of successful executions
  errorCount: int,    // number of failed executions
  lastRun: float,     // timestamp of last execution (0 if never run)
  lastError: string,  // message of last error (empty if no errors)
  nextRun: float      // timestamp of next scheduled execution
}
```

**Methods on `Job`:**

```stash
job.pause();           // Temporarily suspend execution (keep schedule)
job.resume();          // Resume after pause
job.cancel();          // Permanently stop the job
job.trigger();         // Execute the callback immediately (out of schedule)
```

### 5.4 Error Handling Policies

When a scheduled callback throws an error, behavior depends on `errorPolicy`:

| Policy                 | Behavior                                                                                |
| ---------------------- | --------------------------------------------------------------------------------------- |
| `"continue"` (default) | Log the error, continue scheduling. Increment `errorCount`.                             |
| `"stop"`               | Cancel the job after the first error.                                                   |
| `"backoff"`            | Exponentially increase the interval (2x, 4x, 8x... up to `maxDelay`). Reset on success. |

Errors are always captured in `job.lastError`. They are **not** propagated to the main script — scheduled jobs run in isolated contexts. If users need error notification, they should handle it within the callback:

```stash
task.every(60s, () => {
  try {
    let result = checkServer();
  }
  if (result is Error) {
    io.eprintln("Server check failed: ${result.message}");
  }
});
```

### 5.5 Complete Example: Monitoring Service

```stash
// monitoring.stash — A system monitoring script

let config = {
  healthUrl: env.get("HEALTH_URL", "https://api.example.com/health"),
  alertEmail: env.getOrThrow("ALERT_EMAIL"),
  diskThreshold: 90mb
}

// Check API health every 30 seconds
let healthJob = task.every(30s, () => {
  let resp = try http.get(config.healthUrl);
  if (resp is Error || resp.status != 200) {
    io.println("[WARN] API unhealthy: ${resp}");
  }
}, task.JobOptions { name: "health-check", immediate: true });

// Check disk space every 5 minutes
let diskJob = task.every(5m, () => {
  let usage = sys.diskUsage("/");
  if (usage.percent > config.diskThreshold) {
    io.println("[ALERT] Disk usage: ${usage.percent}%");
  }
}, task.JobOptions { name: "disk-check" });

// Nightly log rotation at 2 AM
let rotateJob = task.cron("0 2 * * *", () => {
    io.println("Rotating logs...");
    fs.move("/var/log/app.log", "/var/log/app.log.${time.date()}");
}, task.JobOptions { name: "log-rotate" });

io.println("Monitoring started. Press Ctrl+C to stop.");
task.keepAlive();  // blocks until SIGINT/SIGTERM
io.println("Shutting down monitoring.");
```

---

## 6. Implementation Architecture

### 6.1 Where This Lives

**All in `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs`** — extends the existing `task` namespace. No new projects, no new namespaces.

The cron expression parser should be a standalone internal class (`CronExpression.cs` in `Stash.Stdlib/`) since it's pure logic with no interpreter dependencies.

### 6.2 .NET Implementation Strategy

Under the hood, each `Job` wraps:

- A `System.Threading.PeriodicTimer` (for interval jobs) or a custom cron timer
- A `CancellationTokenSource` for cancellation
- A callback that runs on the ThreadPool
- State tracking (counters, timestamps, error info)

`task.keepAlive()` uses a `ManualResetEventSlim` or `SemaphoreSlim` that blocks until all jobs are done or a signal fires.

**Key implementation detail:** Jobs must be tracked in `VMContext` (alongside `TrackedProcesses` and `TrackedWatchers`) so they are cleaned up on script exit, Ctrl+C, or `process.exit()`.

### 6.3 Thread Safety

- Job state (counters, status) must be protected by locks or use `Interlocked` operations
- Callback execution happens on ThreadPool threads — same thread-safety considerations as `task.run()`
- The `Stash.Bytecode` VM's `SynchronizedTextWriter` already handles concurrent `io.println()` safely

### 6.4 No New AST Nodes, No New Opcodes

This is entirely a stdlib implementation. The parser, lexer, compiler, and VM require **zero changes**. This is a major advantage over the keyword approach.

---

## 7. What This Does NOT Include (Scope Boundaries)

- **No `every` / `schedule` keywords.** This spec is stdlib-only. Keywords can be considered later as syntax sugar if the stdlib API proves successful.
- **No distributed scheduling.** This is single-process, in-memory scheduling. For multi-node scheduling, users should use external tools (cron, systemd, Kubernetes CronJobs).
- **No persistence.** Jobs don't survive process restarts. This is a scripting language, not a job queue.
- **No seconds field in cron.** Standard 5-field cron is sufficient for sysadmin use cases.
- **No timezone support in cron.** Cron expressions execute in the system's local time zone. Explicit timezone support can be added later.

---

## 8. Prerequisite: Do We Need a Job Scheduling Mechanism First?

**No.** The .NET runtime provides everything needed:

- `System.Threading.PeriodicTimer` — precise interval timing without drift
- `Task.Run()` / ThreadPool — callback execution (already used by `task.run()`)
- `CancellationTokenSource` — graceful cancellation (already used by `StashFuture`)
- `ManualResetEventSlim` — blocking for `keepAlive()`

The cron expression parser is the only new component, and it's a well-understood algorithm (~200-300 lines of C#). There are also NuGet packages (Cronos) if we want to avoid writing our own, though a hand-rolled parser keeps dependencies minimal.

---

## 9. Open Questions for Discussion

### Q1: Should `task.every()` accept duration values or seconds?

```stash
task.every(30s, callback);    // duration literal
task.every(5m, callback);     // cleaner than task.every(300, callback)
```

**Recommendation:** Yes, we have duration literals in the language, so we use them.

### Q2: Should `task.keepAlive()` be a separate function, or should it be a flag on `task.every()`?

```stash
// Option A: Separate function (recommended)
task.every(30s, callback);
task.keepAlive();

// Option B: Flag on job creation
task.every(30s, callback, { keepAlive: true });
```

**Recommendation:** Separate function. It's a script-level concern ("this script should run forever"), not a per-job concern. Multiple jobs can be active simultaneously — the keep-alive applies to all of them.

### Q3: Should there be a `task.once(delaySeconds, callback)` for one-shot delayed execution?

This is basically `task.delay()` + `task.run()` combined:

```stash
// Today:
task.run(() => {
  task.delay(60s);
  doSomething();
});

// Proposed:
task.once(60s, () => doSomething());
```

**Recommendation:** Yes, include it. Completes the timer API naturally. But lower priority than `every` and `cron`.

### Q4: What happens to running job callbacks during shutdown?

When `task.keepAlive()` receives SIGINT/SIGTERM:

1. Stop scheduling new executions
2. Wait for currently-running callbacks to complete (with a timeout?)
3. Return

**Recommendation:** Wait up to 5 seconds for running callbacks, then force-cancel. The timeout should be configurable. This matches how `CleanupTrackedProcesses()` already works (3-second grace period).

### Q5: Naming — `task.every()` vs `task.interval()` vs `task.repeat()`?

| Name                     | Reads as...                     | Notes                                      |
| ------------------------ | ------------------------------- | ------------------------------------------ |
| `task.every(30s, fn)`    | "Every 30 seconds, do fn"       | Most natural for Stash's sysadmin audience |
| `task.interval(30s, fn)` | "Run fn at 30-second intervals" | More technically precise                   |
| `task.repeat(30s, fn)`   | "Repeat fn every 30 seconds"    | Clear but not as clean                     |

**Recommendation:** `task.every()` — reads most naturally and is closer to the original `every 5m {}` vision. If we ever add keyword syntax, `every 30s { fn }` maps directly to `task.every(30s, fn)`.

---

## 10. Risks

| Risk                                                            | Likelihood | Impact | Mitigation                                                                       |
| --------------------------------------------------------------- | ---------- | ------ | -------------------------------------------------------------------------------- |
| Users expect `every` to keep script alive without `keepAlive()` | High       | Medium | Clear documentation, helpful error/warning when jobs are orphaned at exit        |
| Cron expression parsing edge cases                              | Medium     | Low    | Use well-tested algorithm, comprehensive tests                                   |
| Thread safety issues in job state                               | Medium     | High   | Use `Interlocked` operations, leverage existing `SynchronizedTextWriter` pattern |
| Callback errors crashing the scheduler                          | Low        | High   | Every callback invocation is wrapped in try/catch                                |
| Memory leaks from uncancelled jobs                              | Low        | Medium | Track in VMContext, clean up on exit (same as watchers/processes)                |

---

## 11. Decision Log

| Date       | Decision                                        | Rationale                                                                                       |
| ---------- | ----------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| 2025-04-11 | Stdlib functions over language keywords         | Lower risk, no parser changes, nearly as ergonomic, preserves option for keyword syntax later   |
| 2025-04-11 | Explicit keep-alive over implicit               | Consistent with run-to-completion model, no surprises, matches Go's approach                    |
| 2025-04-11 | `task` namespace over new `scheduler` namespace | Scheduling is a specialization of task management; `task` already has `delay`, `timeout`, `run` |
| 2025-04-11 | 5-field cron only (no seconds)                  | Standard, well-understood, sufficient for sysadmin use cases                                    |
| 2025-04-11 | No persistence, no distributed scheduling       | Script-level feature, not an application framework                                              |

---

## 12. Beyond In-Process Scheduling: The Job Scheduler Question

> **Context:** Sections 1–11 describe in-process scheduling — `task.every()` and `task.cron()` running callbacks inside a single Stash process. This section addresses the harder question: **what does a production-grade, trust-worthy scheduling system look like for Stash?**

The in-process approach (§5) is a good foundation. But it has real limitations that a sysadmin will hit quickly:

1. **No persistence.** If the Stash process crashes or the machine reboots, all jobs are gone. No record of what was supposed to run.
2. **No survivability.** Jobs live and die with the process. There's no way to deploy a schedule and have it outlive the script that defined it.
3. **No visibility.** You can't query from another terminal "what's scheduled? what ran? what failed?" — the state is trapped inside the process.
4. **No multi-script coordination.** Two Stash scripts can't share a common schedule or avoid conflicting with each other's jobs.

These limitations are fine for "monitoring script that runs in a tmux session." They're NOT fine for "I'm managing 50 servers and I need to trust that my backup rotation runs at 2 AM, period."

The question is: how far up the reliability ladder should Stash climb?

---

## 13. Architecture Options for a Job Scheduler

### Option 1: Integrate with OS Schedulers (systemd / cron / launchd / Task Scheduler)

**Concept:** Stash doesn't build its own scheduler daemon. Instead, it provides stdlib functions and/or a CLI subcommand that generates and installs native OS scheduler entries.

```stash
// Approach A: stdlib functions
import "@stash/scheduler"

// Registers a systemd timer (Linux), launchd plist (macOS), or Task Scheduler entry (Windows)
scheduler.install("health-check", scheduler.Job {
    script: "./health_check.stash",
    schedule: "*/5 * * * *",-
    user: "deploy",
    workingDir: "/opt/myapp",
    logFile: "/var/log/myapp/health.log",
    onFailure: "restart"    // systemd Restart= equivalent
});

scheduler.list();              // show installed stash jobs
scheduler.remove("health-check");
scheduler.enable("health-check");
scheduler.disable("health-check");
scheduler.logs("health-check"); // tail the log
scheduler.status("health-check"); // running? last exit code? next run?
```

```bash
# Approach B: CLI subcommand
stash scheduler install health-check.stash --cron "*/5 * * * *" --user deploy
stash scheduler list
stash scheduler remove health-check
stash scheduler logs health-check
stash scheduler status
```

**What this generates on Linux:**

```ini
# /etc/systemd/system/stash-health-check.service
[Unit]
Description=Stash Job: health-check
After=network.target

[Service]
Type=oneshot
ExecStart=/usr/local/bin/stash /opt/myapp/health_check.stash
WorkingDirectory=/opt/myapp
User=deploy
StandardOutput=append:/var/log/myapp/health.log
StandardError=append:/var/log/myapp/health.log

# /etc/systemd/system/stash-health-check.timer
[Unit]
Description=Stash Timer: health-check

[Timer]
OnCalendar=*:0/5
Persistent=true

[Install]
WantedBy=timers.target
```

**On macOS:** Generates a `~/Library/LaunchAgents/com.stash.health-check.plist` with the equivalent `StartCalendarInterval`.
**On Windows:** Uses `schtasks.exe` or the COM-based Task Scheduler API.

#### Tradeoffs

| Dimension           | Assessment                                                                                                                                    |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| **Reliability**     | Excellent — OS schedulers are battle-tested, handle reboots, log rotation, crash recovery                                                     |
| **Persistence**     | Built-in — OS scheduler survives reboots, process crashes, updates                                                                            |
| **Visibility**      | Good — `systemctl status`, `journalctl`, native OS tooling all work                                                                           |
| **Cross-platform**  | Painful — three completely different scheduler APIs (systemd, launchd, Task Scheduler). Cron is simpler but less capable than systemd timers. |
| **Complexity**      | Medium for basic cases, high for feature parity across platforms                                                                              |
| **User experience** | Cold — users must understand the underlying OS scheduler. "Why isn't my job running?" sends them to `journalctl`, not Stash tooling.          |
| **Scope**           | Each job runs as a separate Stash process invocation. No shared state between runs. Script must be self-contained.                            |

**Verdict:** This is a good **deployment story** — Stash scripts absolutely should be easy to install as systemd services/timers. But it's NOT a job scheduler. It's a systemd/launchd/Task Scheduler code generator. The scheduling intelligence lives entirely in the OS, and Stash is just a thin wrapper. Users still need to understand the underlying platform. This should exist eventually as a `stash scheduler install` CLI command, but it doesn't solve the core scheduling problem.

---

### Option 2: Stash Scheduler Daemon (`stashd`)

**Concept:** Stash ships a dedicated long-running daemon process that manages job scheduling. Scripts register jobs with the daemon. The daemon persists state, handles cron evaluation, spawns Stash processes to execute jobs, tracks results, and survives reboots.

```stash
// register_jobs.stash — runs once, registers jobs with the daemon
import "@stash/scheduler"

scheduler.register("health-check", scheduler.Job {
    script: "./health_check.stash",
    schedule: "*/5 * * * *",
    timeout: 30,
    retries: 3,
    tags: ["monitoring", "prod"]
});

scheduler.register("nightly-backup", scheduler.Job {
    script: "./backup.stash",
    schedule: "0 2 * * *",
    timeout: 3600,
    exclusive: true,  // don't start if previous run still going
    tags: ["backup", "prod"]
});
```

```bash
# Daemon management
stash daemon start                  # start the scheduler daemon
stash daemon stop                   # graceful shutdown
stash daemon status                 # is it running? how many jobs?

# Job management
stash jobs list                     # show all registered jobs
stash jobs status health-check      # detailed status of one job
stash jobs trigger health-check     # run it NOW, out of schedule
stash jobs pause health-check       # temporarily suspend
stash jobs resume health-check      # resume after pause
stash jobs remove health-check      # unregister
stash jobs logs health-check        # show execution history
stash jobs logs health-check --last 5  # last 5 runs
```

**Architecture:**

```
┌─────────────────────────────────────────────────┐
│                  stashd (daemon)                │
│                                                 │
│  ┌──────────┐  ┌──────────┐  ┌──────────────┐   │
│  │  Cron    │  │  Job     │  │  State Store │   │
│  │  Engine  │  │  Runner  │  │  (SQLite)    │   │
│  │          │  │          │  │              │   │
│  │ Evaluates│  │ Spawns   │  │ Jobs, runs,  │   │
│  │ schedules│  │ stash    │  │ logs, errors │   │
│  │ every    │  │ processes│  │              │   │
│  │ minute   │  │ per job  │  │              │   │
│  └──────────┘  └──────────┘  └──────────────┘   │
│                                                 │
│  ┌──────────┐  ┌──────────────────────────┐     │
│  │  IPC     │  │  PID file + signal       │     │
│  │  (Unix   │  │  handling (SIGTERM,      │     │
│  │  socket  │  │  SIGHUP for reload)      │     │
│  │  or TCP) │  │                          │     │
│  └──────────┘  └──────────────────────────┘     │
└─────────────────────────────────────────────────┘
         ▲                        ▲
         │ IPC                    │ spawn
         │                        │
    ┌────┴─────┐            ┌─────┴───────┐
    │ stash    │            │ stash       │
    │ jobs ... │            │ script.stash│
    │ (CLI)    │            │ (worker)    │
    └──────────┘            └─────────────┘
```

**State store:** SQLite database at `~/.stash/scheduler.db` containing:

- `jobs` table: name, script path, schedule, options, status, created_at
- `runs` table: job_name, started_at, finished_at, exit_code, stdout_path, stderr_path
- `locks` table: job_name, pid, acquired_at (for exclusive jobs)

**IPC:** The CLI commands (`stash jobs list`, `stash jobs trigger`, etc.) communicate with the running daemon via a Unix domain socket (Linux/macOS) or named pipe (Windows) at `~/.stash/scheduler.sock`.

**Job execution:** Each job run spawns a new `stash <script>` process (not an in-process callback). This provides:

- Process isolation — a crashing job doesn't take down the scheduler
- Resource limits — can set timeouts, memory limits via OS mechanisms
- Clean environment — each run starts fresh, no leaked state
- Logging — stdout/stderr captured to per-run log files

#### Tradeoffs

| Dimension              | Assessment                                                                                                                                                     |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Reliability**        | Very good — process isolation, crash recovery, automatic retries                                                                                               |
| **Persistence**        | Excellent — SQLite survives crashes, reboots (if daemon auto-starts via systemd/launchd)                                                                       |
| **Visibility**         | Excellent — `stash jobs list`, `stash jobs logs`, structured history in SQLite                                                                                 |
| **Cross-platform**     | Good — daemon is pure Stash/C#, IPC can abstract over Unix sockets vs named pipes                                                                              |
| **Complexity**         | **HIGH** — daemon lifecycle, IPC protocol, state storage, process spawning, lock management, log rotation. This is essentially building a mini-Celery/Sidekiq. |
| **User experience**    | Great for power users. But now they must manage a daemon ("is stashd running?").                                                                               |
| **Development effort** | Very large — easily 2000-4000 lines of C# across new projects, plus CLI integration, plus tests                                                                |

**Verdict:** This is the "proper" solution and what a mature sysadmin tool would eventually want. But it's a massive scope escalation. It transforms Stash from a scripting language with scheduling into a job scheduling platform that happens to use Stash. The development cost is enormous, and it introduces operational complexity (running a daemon, managing its state, auto-starting on boot).

---

### Option 3: Self-Scheduling Scripts (Recommended Approach)

**Concept:** Stash provides the building blocks for scripts to be their own schedulers, without requiring a separate daemon. The key insight: **a Stash script with `task.keepAlive()` IS already a daemon — it just needs persistence and observability bolted on.**

This approach layers three capabilities:

#### Layer 1: In-Process Scheduling (Already Specified — §5)

`task.every()`, `task.cron()`, `task.keepAlive()` — the core API from the existing spec.

#### Layer 2: Self-Daemonization + Observability (`process` namespace extensions)

```stash
// monitoring.stash — a self-managing scheduled service

// Daemonize on first invocation, exit the parent
if (args.has("--daemon")) {
  process.daemonize("stash " + sys.scriptPath() + " --run");
  io.println("Monitoring daemon started. PID: ${process.lastPid()}");
  process.exit(0);
}

// Write PID file for management
let pidFile = "/var/run/stash-monitoring.pid";
fs.writeFile(pidFile, str.from(sys.pid()));

// Register cleanup
sys.onSignal(sys.Signal.SIGTERM, () => {
  io.println("Shutting down...");
  fs.remove(pidFile);
  process.exit(0);
})

// Core scheduling — the in-process API from §5
task.every(30s, () => checkHealth(), task.JobOptions {
  name: "health-check",
  immediate: true
})

task.every(5m, () => checkDiskSpace(), task.JobOptions { name: "disk-check" });

task.cron("0 2 * * *", () => rotateLogs(), task.JobOptions { name: "log-rotate" });

// Persist schedule state to disk for observability
task.onTick((job, result) => {
  let logLine = "${time.iso()} [${job.name}] ${result.status} (${result.durationMs}ms)";
  fs.appendFile("/var/log/stash-monitoring.log", logLine + "\n");
})

io.println("Monitoring service running.");
task.keepAlive();
```

#### Layer 3: OS Integration CLI (`stash service`)

A thin CLI layer that wraps the daemonization + OS integration:

```bash
# Install a Stash script as a system service
stash service install monitoring.stash --name "stash-monitoring" --user deploy

# This generates and installs:
# - Linux: systemd service unit (Type=simple, ExecStart=stash monitoring.stash)
# - macOS: launchd plist
# - Windows: NSSM service wrapper or native service

# Management — thin wrappers around OS commands
stash service start stash-monitoring
stash service stop stash-monitoring
stash service restart stash-monitoring
stash service status stash-monitoring
stash service logs stash-monitoring
stash service uninstall stash-monitoring

# List all installed Stash services
stash service list
```

The key difference from Option 1: the script itself contains the scheduling logic (via `task.every`/`task.cron`). The OS service just keeps the process alive and handles auto-start on boot. The scheduling intelligence is IN Stash, not delegated to OS timers.

#### What new stdlib functions does this need?

Beyond the §5 API (`task.every`, `task.cron`, `task.keepAlive`):

```stash
// Job execution hooks — for logging, metrics, alerting
task.onTick(callback)           // called after every job execution
                                // callback receives (job, result) where result has
                                // { status: "ok"|"error", durationMs, error? }

task.onError(callback)          // called only on job failures
                                // callback receives (job, error)

// Job state query — for building status endpoints or log output
task.jobs()                     // returns array of all active Jobs
task.find("health-check")       // find a job by name, returns Job or null

// Optional: health endpoint for monitoring systems
task.healthCheck(port)          // starts a tiny HTTP endpoint on `port`
                                // GET / → {"status":"ok","jobs":[...]}
                                // Prometheus-compatible metrics at /metrics
```

The `task.healthCheck(port)` is a stretch goal but extremely valuable. It would let users point Prometheus, Grafana, or a simple `curl` at their Stash service to see job status. Under the hood, it's a simple HTTP listener (requires adding a basic HTTP server capability — which Stash currently lacks but would be valuable independently).

#### Tradeoffs

| Dimension              | Assessment                                                                                                                                   |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| **Reliability**        | Good — OS service manager handles restarts. Script handles scheduling. Separation of concerns.                                               |
| **Persistence**        | Partial — schedule is in the script (always "persisted" as code). Run history requires explicit logging (or a future `task.store()` option). |
| **Visibility**         | Good — `stash service status/logs`, custom logging via `task.onTick()`, optional health endpoint                                             |
| **Cross-platform**     | Medium — `stash service install` must generate platform-specific service definitions. The script itself is cross-platform.                   |
| **Complexity**         | Moderate — mostly builds on existing infrastructure. The `stash service` CLI is the main new work (~500-800 lines).                          |
| **User experience**    | Natural — write a script, install it as a service. No separate daemon to manage. No new mental model.                                        |
| **Development effort** | Moderate — Layer 1 is §5 (already specified). Layer 2 is small stdlib additions. Layer 3 is the `stash service` CLI.                         |

**Verdict: This is the recommended approach.** It keeps Stash true to its identity as a scripting language while giving users a credible path to production-grade scheduling. The key insight is that `stash script.stash` + `task.keepAlive()` + systemd `Type=simple` is already 90% of what a dedicated daemon would give you, without the complexity of building and maintaining a separate daemon process.

---

## 14. Comparison Matrix

| Capability           | Option 1: OS Integration         | Option 2: Stash Daemon (`stashd`) | Option 3: Self-Scheduling (Recommended)         |
| -------------------- | -------------------------------- | --------------------------------- | ----------------------------------------------- |
| Schedule lives in... | OS scheduler config              | SQLite database                   | Stash script source code                        |
| Process model        | One stash process per run        | Daemon + worker processes         | Single long-running stash process               |
| Job isolation        | Excellent (separate processes)   | Excellent (separate processes)    | In-process (shared VM)                          |
| Crash recovery       | OS restarts the timer            | Daemon restarts, replays missed   | OS service manager restarts process             |
| State persistence    | None (stateless runs)            | Full (SQLite)                     | Code-persisted schedule + optional file logging |
| History/logging      | OS journal/logs                  | Built-in structured history       | Custom via `task.onTick()` hooks                |
| Implementation size  | ~800 lines (3 platform backends) | ~3000-4000 lines (new project)    | ~400 lines stdlib + ~600 lines CLI              |
| New dependencies     | Platform-specific APIs           | SQLite, IPC protocol              | None (beyond §5 core)                           |
| Development timeline | Medium                           | Very long                         | Short-medium                                    |
| Operational overhead | Low (OS manages everything)      | High (daemon to manage)           | Low (just a service)                            |

---

## 15. Recommended Implementation Roadmap

### Phase 1: In-Process Scheduling Core (§5)

**Scope:** `task.every()`, `task.cron()`, `task.keepAlive()`, job control
**Effort:** Small-medium
**Value:** Immediate — scripts can schedule recurring work

This is the foundation everything else builds on. Ship it first.

### Phase 2: Observability Hooks

**Scope:** `task.onTick()`, `task.onError()`, `task.jobs()`, `task.find()`
**Effort:** Small
**Value:** Scripts can log their own execution history, integrate with alerting

### Phase 3: `stash service` CLI

**Scope:** `stash service install|start|stop|status|logs|uninstall`
**Effort:** Medium (platform-specific service generation)
**Value:** One-command deployment of Stash scripts as production services

**Platform matrix:**

| Platform | Service System        | Unit File Location                                                   |
| -------- | --------------------- | -------------------------------------------------------------------- |
| Linux    | systemd               | `~/.config/systemd/user/` (user) or `/etc/systemd/system/` (root)    |
| macOS    | launchd               | `~/Library/LaunchAgents/` (user) or `/Library/LaunchDaemons/` (root) |
| Windows  | Task Scheduler + NSSM | NSSM wrapper or native Windows Service (requires .NET `ServiceBase`) |

### Phase 4 (Future): Health Endpoint

**Scope:** `task.healthCheck(port)` — HTTP endpoint exposing job status + metrics
**Effort:** Medium (requires basic HTTP server in stdlib)
**Value:** Integration with Prometheus, Grafana, uptime monitoring
**Pre-requisite:** HTTP server capability in `net` namespace

### Phase 5 (Future, Maybe): Full Daemon

**Scope:** `stashd` with SQLite persistence, IPC, structured history
**Effort:** Large
**Value:** Full job scheduling platform
**Gate:** Only pursue if Phase 1-3 prove insufficient for the user base. The self-scheduling model may be "good enough" for 90%+ of use cases.

---

## 16. Answers to Key Design Questions

### "Does a scheduler inside a script prevent it from ending?"

**No, by default.** Active jobs do not prevent exit. `task.keepAlive()` is the explicit opt-in for long-running behavior. This is the right answer for a scripting language.

For production deployment, `stash service install` wraps the script in an OS service manager that auto-restarts on crash, starts on boot, and provides `start/stop/status/logs` management.

### "Would the scheduled job run as a background or detached child process?"

**In the recommended approach (Option 3): Neither.** Scheduled callbacks run as in-process async tasks on the .NET ThreadPool — same as `task.run()` today. This is simpler, faster, and avoids IPC overhead. Process isolation is unnecessary for most use cases, and when it IS needed, the callback can use `process.spawn()` explicitly.

The OS service manager (systemd/launchd) handles the "keep the process running" concern. The Stash process doesn't need to daemonize itself.

### "Do we need to implement a job scheduling mechanism first?"

**No.** Phase 1 (`task.every()`, `task.cron()`, `task.keepAlive()`) needs only .NET's built-in `PeriodicTimer`, `ThreadPool`, and `ManualResetEventSlim` — all already available. A cron expression parser (~200-300 lines) is the only new algorithm.

Phase 3 (`stash service`) generates platform-native service definitions — this is template rendering, not infrastructure.

A full daemon (Phase 5) would require significant infrastructure but is explicitly **deferred** until there's proven demand the simpler approach can't satisfy.

### "Can developers trust this?"

**Yes, because the trust hierarchy is clear:**

1. **Scheduling accuracy** — `PeriodicTimer` + cron engine. Well-understood, testable.
2. **Process liveness** — systemd/launchd/Task Scheduler. Battle-tested OS infrastructure.
3. **Crash recovery** — OS service manager `Restart=always`. Not reinventing this.
4. **Observability** — `task.onTick()` hooks + OS journal + optional health endpoint.

Stash doesn't try to be Celery or Kubernetes CronJobs. It owns the scheduling logic (cron parsing, interval timing, error policies) and delegates process lifecycle to the OS. That boundary is exactly where a scripting language should draw the line.

---

## 17. Risk: What If Self-Scheduling Isn't Enough?

The main scenario where Option 3 falls short:

**Multi-script coordination.** If a user has 20 different Stash scripts that all need to be scheduled, they'd need 20 separate services. There's no central dashboard, no unified logging, no "pause all jobs" command.

**Mitigations:**

- A single Stash script CAN schedule multiple jobs (that's the whole point of the API). Users should write one `scheduler.stash` that imports and schedules everything, rather than 20 independent scripts.
- `stash service list` provides a unified view of all installed Stash services.
- If this pattern proves insufficient, THAT is the trigger for Phase 5 (full daemon). But we shouldn't build it speculatively.

---

## 18. Updated Decision Log

| Date       | Decision                                        | Rationale                                                                                       |
| ---------- | ----------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| 2025-04-11 | Stdlib functions over language keywords         | Lower risk, no parser changes, nearly as ergonomic, preserves option for keyword syntax later   |
| 2025-04-11 | Explicit keep-alive over implicit               | Consistent with run-to-completion model, no surprises, matches Go's approach                    |
| 2025-04-11 | `task` namespace over new `scheduler` namespace | Scheduling is a specialization of task management; `task` already has `delay`, `timeout`, `run` |
| 2025-04-11 | 5-field cron only (no seconds)                  | Standard, well-understood, sufficient for sysadmin use cases                                    |
| 2026-04-11 | Self-scheduling scripts over dedicated daemon   | Lower complexity, natural for scripting language, delegates process lifecycle to OS             |
| 2026-04-11 | `stash service` CLI for OS integration          | One-command deployment, leverages battle-tested OS service managers                             |
| 2026-04-11 | Defer full daemon to Phase 5                    | Speculative complexity; self-scheduling model likely sufficient for 90%+ of use cases           |
| 2026-04-11 | Observability hooks (`onTick`, `onError`)       | Critical for production trust — users need to know what ran, when, and whether it succeeded     |
