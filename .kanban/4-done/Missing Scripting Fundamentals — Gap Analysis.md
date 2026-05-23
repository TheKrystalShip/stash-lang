# Missing Scripting Fundamentals — Gap Analysis

> **Status:** Analysis
> **Created:** March 2026
> **Purpose:** Identify critical scripting language features that Stash lacks — features that are standard expectations in any language marketed for system administration, automation, or general-purpose scripting. This surfaced during the "Can Stash Become a Shell?" discussion when a review of basic shell builtins revealed that many fundamental features were either absent or buried in non-obvious namespaces.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Methodology](#2-methodology)
3. [Tier 1 — Embarrassing Omissions](#3-tier-1--embarrassing-omissions)
4. [Tier 2 — Expected Scripting Primitives](#4-tier-2--expected-scripting-primitives)
5. [Tier 3 — Sysadmin Power Features](#5-tier-3--sysadmin-power-features)
6. [Tier 4 — Language Ergonomics](#6-tier-4--language-ergonomics)
7. [What Stash Gets Right](#7-what-stash-gets-right)
8. [Cross-Reference Matrix](#8-cross-reference-matrix)
9. [Recommended Priority](#9-recommended-priority)

---

## 1. Motivation

Three weeks into development, during a discussion about whether Stash could function as a shell, the topic of basic shell builtins surfaced. It turned out that while `process.chdir()` and `process.withDir()` exist for changing directories, many other fundamental scripting features are genuinely absent — file permissions control, signal trapping, bitwise operators, structured error handling. The `cd` discovery was a canary: if a feature this basic needed investigation to find, what other gaps exist?

This document is a systematic audit. It compares Stash against the baseline feature set of established scripting languages (Bash, Python, Ruby, PowerShell, Perl, Lua) and identifies every gap that would make a user say: _"Wait, it can't do THAT?"_

The gaps are tiered by severity:

| Tier       | Meaning                       | User Reaction                                           |
| ---------- | ----------------------------- | ------------------------------------------------------- |
| **Tier 1** | Embarrassing omissions        | "This language is incomplete"                           |
| **Tier 2** | Expected scripting primitives | "How am I supposed to write real scripts without this?" |
| **Tier 3** | Sysadmin power features       | "I'll need to shell out for everything non-trivial"     |
| **Tier 4** | Language ergonomics           | "This is annoying but I can work around it"             |

> **Note:** This analysis is scoped to _scripting language_ expectations — features that Python, Ruby, or PowerShell users take for granted. It does NOT cover shell-specific features (bare commands, job control, streaming pipes) which are analyzed separately in [Can Stash Become a Shell? — Architectural Analysis](Can%20Stash%20Become%20a%20Shell%3F%20—%20Architectural%20Analysis.md).

---

## 2. Methodology

Each gap was evaluated against three questions:

1. **Would a Python/Ruby/PowerShell user expect this to exist?** If yes, its absence is a real gap.
2. **Can Stash work around it today?** If the workaround is `$(chmod 644 file)` (shelling out), that's not a real workaround for a cross-platform language — it fails on Windows.
3. **Does the gap block real-world scripting tasks?** Deploy scripts, config management, log processing, service monitoring, CI/CD automation.

---

## 3. Tier 1 — Embarrassing Omissions

These are features so fundamental that their absence undermines credibility as a scripting language. Every language in the comparison set has had these since v1.0.

### 3.1 ~~No `try`/`catch`/`finally`~~ ✅ IMPLEMENTED (Structured Error Handling)

> **Status:** Implemented. Stash now supports `try { } catch (e) { } finally { }` blocks alongside the existing `try expr` pattern. All four forms are valid: try-catch, try-finally, try-catch-finally, and bare try (error suppression). 25 tests cover the feature.

**What Stash has:** `try expr` prefix that converts errors into Error values, `throw` for raising errors, and **`try { } catch (e) { } finally { }` blocks** for structured error handling with guaranteed cleanup.

**What ~~is~~ was missing:** ~~There is no way to run cleanup code that is _guaranteed_ to execute, regardless of success or failure. There is no way to catch an error and resume execution in the same block scope.~~

```stash
// What you'd expect to write:
try {
    let handle = fs.open("/tmp/data.lock");
    doWork(handle);
} catch (e) {
    log.error("Work failed: " + e.message);
} finally {
    fs.delete("/tmp/data.lock");  // ALWAYS runs
}

// What you actually write in Stash:
let handle = try fs.open("/tmp/data.lock");
if (handle is Error) {
    log.error("Open failed: " + handle.message);
    // But what about cleanup?
}
let result = try doWork(handle);
// If doWork throws, does the cleanup line below execute?
// Only if you wrapped doWork in try. If you forgot, the script crashes.
fs.delete("/tmp/data.lock");  // NOT guaranteed to run
```

**Why it matters:**

- Resource cleanup (file handles, lock files, temp dirs, network connections) requires `finally`
- Every deploy script, daemon, and long-running process needs guaranteed cleanup
- The `try expr` pattern works for simple cases but breaks down when you need _scoped cleanup_
- `try` on every single expression is verbose and error-prone — miss one and the script crashes

**What every other language has:**

| Language   | Structured Error Handling                 |
| ---------- | ----------------------------------------- |
| Python     | `try`/`except`/`finally`                  |
| Ruby       | `begin`/`rescue`/`ensure`                 |
| PowerShell | `try`/`catch`/`finally`                   |
| JavaScript | `try`/`catch`/`finally`                   |
| Perl       | `eval { }` + `die` (+ `Try::Tiny` module) |
| Lua        | `pcall`/`xpcall`                          |

### 3.2 ~~No File Permissions Control (`chmod`/`chown`)~~ ✅ IMPLEMENTED

> **Status:** Implemented. Stash now provides cross-platform file permission control via `fs.getPermissions(path)`, `fs.setPermissions(path, permissions)`, `fs.setReadOnly(path, readOnly)`, and `fs.setExecutable(path, executable)`. The API uses `FilePermission` and `FilePermissions` built-in structs rather than Unix-specific `chmod`/`chown` patterns, ensuring cross-platform compatibility. On Unix, full `rwx` owner/group/others bits are supported. On Windows, the read-only attribute is controlled. 24 tests cover the feature.

**What Stash now has:** `fs.readable()`, `fs.writable()`, `fs.executable()` for checking permissions, plus `fs.getPermissions(path)` → `FilePermissions` struct, `fs.setPermissions(path, permissions)`, `fs.setReadOnly(path, readOnly)`, and `fs.setExecutable(path, executable)` for setting permissions cross-platform.

**~~What's missing:~~** ~~Any way to change file permissions, ownership, or access control.~~

```stash
// What you'd expect:
fs.chmod("/opt/app/run.sh", 0o755);
fs.chown("/var/log/app.log", "www-data", "www-data");

// What you actually have to do:
$(chmod 755 /opt/app/run.sh);     // Unix only — breaks on Windows
$(chown www-data:www-data /var/log/app.log);  // Unix only
```

**Why it matters:**

- Stash positions itself as a _cross-platform_ scripting language
- Shelling out to `chmod` fails on Windows entirely
- Every deploy script, every config management script, every security script needs permission control
- This is the most common operation in system administration after file read/write

**Cross-platform considerations:**

- Unix: `chmod`, `chown`, `chgrp` with numeric modes and symbolic modes
- Windows: ACLs via `icacls` or .NET `FileSecurity` — different model but same need
- .NET provides `File.SetUnixFileMode()` on Unix and `FileSystemAclExtensions` on Windows

### 3.3 ~~No `which` / Command Path Resolution~~ ✅ IMPLEMENTED

> **Status:** Implemented. Stash now provides cross-platform command path resolution via `sys.which(name)`. On Unix, it searches PATH directories and verifies execute permission. On Windows, it additionally searches PATHEXT extensions (.exe, .cmd, .bat, etc.). Returns the full absolute path or null if not found.

**What Stash now has:** `sys.which(name)` — searches the system PATH for an executable, returning the full absolute path or `null`. Cross-platform: on Unix checks execute permission; on Windows uses PATHEXT extensions.

**~~What's missing:~~** ~~No way to check if an external command exists before trying to run it.~~

```stash
// What you'd expect:
if (sys.which("docker") != null) {
    $(docker compose up -d);
} else {
    throw "Docker is not installed";
}

// What you actually have to do:
let result = try $(which docker 2>/dev/null);
// But this shells out to `which`, which itself might not exist on all platforms
// Windows uses `where.exe`, not `which`
```

**Why it matters:**

- Scripts that call external tools need to verify tool availability
- Error messages from "command not found" are platform-specific and hard to catch cleanly
- Cross-platform scripts need a unified API (Windows `where.exe` vs Unix `which`)
- Every installer, setup script, and CI pipeline does this

### 3.4 ~~No Signal Trapping~~ ✅ IMPLEMENTED

> **Status:** Implemented. Stash now provides cross-platform signal trapping via `sys.Signal` enum, `sys.onSignal(signal, handler)`, and `sys.offSignal(signal)`. The `sys.Signal` enum includes SIGHUP, SIGINT, SIGQUIT, SIGTERM, SIGUSR1, and SIGUSR2. On Linux/macOS all signals work. On Windows, SIGHUP/SIGINT/SIGQUIT/SIGTERM are supported; SIGUSR1/SIGUSR2 are no-ops. Signal handlers run in an isolated forked context.

**What Stash now has:** `sys.Signal` enum with SIGHUP, SIGINT, SIGQUIT, SIGTERM, SIGUSR1, SIGUSR2 members. `sys.onSignal(signal, handler)` registers a callback for a signal. `sys.offSignal(signal)` removes a handler. Handlers execute in an isolated context. Cross-platform: all signals work on Linux/macOS; SIGUSR1/SIGUSR2 are no-ops on Windows.

**What Stash has:** `process.signal(handle, sig)` and `process.kill(handle)` — can _send_ signals to child processes.

**~~What's missing:~~** ~~No way to _receive_ or _trap_ signals in the Stash process itself.~~

```stash
// What you'd expect:
sys.onSignal("SIGTERM", () => {
    log.info("Shutting down gracefully...");
    cleanup();
    exit(0);
});

sys.onSignal("SIGHUP", () => {
    log.info("Reloading configuration...");
    reloadConfig();
});

// What you actually have: nothing.
// If someone sends SIGTERM to your Stash script, it just dies.
```

**Why it matters:**

- Daemons must handle SIGTERM for graceful shutdown
- SIGHUP is the standard signal for config reload in Unix services
- Long-running scripts need cleanup on Ctrl+C beyond what `Console.CancelKeyPress` provides
- Container orchestrators (Docker, Kubernetes) send SIGTERM before SIGKILL — scripts that don't handle it lose data

### 3.5 ~~No C-style `for` Loop~~ ✅ IMPLEMENTED

**What Stash has:** `for (let x in collection)` — iteration over arrays, dicts, and ranges.

**What's missing:** The classic `for (init; condition; increment)` loop.

```stash
// What you'd expect:
for (let i = 0; i < 10; i++) {
    io.println(i);
}

// What you have to write:
for (let i in range(10)) {
    io.println(i);
}

// This works for simple cases, but breaks down for:
// - Non-integer iteration (floating point steps)
// - Complex conditions (iterate until something happens)
// - Multiple loop variables
// - Iterating backwards without range
```

**Why it matters:**

- `range()` covers 80% of cases, but the remaining 20% are awkward
- Developers coming from C, JavaScript, Java, Go, Rust, C# all expect this
- Complex iteration patterns require `while` loops with manual counter management, which is more error-prone
- This is the single most recognizable loop syntax in programming

---

## 4. Tier 2 — Expected Scripting Primitives

These are features that scripting language users expect to exist. Their absence doesn't break the language but creates constant friction.

### 4.1 ~~No Spread Operator / Rest Parameters~~ ✅ IMPLEMENTED

**What's missing:** No way to spread arrays into function arguments or collect variable arguments.

```stash
// What you'd expect:
fn log(level, ...messages) {
    for (let msg in messages) {
        io.println($"[{level}] {msg}");
    }
}

let args = ["hello", "world"];
someFunction(...args);

// What you have to do:
fn log(level, messages) {      // Must pass an explicit array
    for (let msg in messages) {
        io.println($"[{level}] {msg}");
    }
}
log("INFO", ["hello", "world"]);  // Callers must wrap in array
```

**Why it matters:**

- Variadic functions are extremely common in scripting (print, log, format, concat)
- Array spreading enables generic wrappers and decorators
- Without it, utility functions have awkward array-wrapping APIs

### 4.2 No `eval` / Dynamic Code Execution

**What's missing:** No way to construct and execute code at runtime.

```stash
// What you'd expect:
let code = 'io.println("Generated at runtime");';
eval(code);

// Or loading and executing a script fragment:
let script = fs.readFile("plugin.stash");
eval(script);
```

**Why it matters — and why it's complicated:**

- Plugin/extension systems need dynamic code loading (but `import` is static)
- Code generation and metaprogramming use cases
- Script-based configuration (e.g., load a `.stashrc` file)

**Counter-argument:** `eval` is a security footgun. Python and JavaScript both have it and both communities advise against using it. Stash's static import system is _safer_. However, the inability to `source` files at runtime (even if `eval` is rejected) is a real gap — see 4.3.

### 4.3 ~~No Runtime File Sourcing~~ ✅ IMPLEMENTED PARTIALLY (Imports now accept expressions that resolve at runtime, alongside literal strings)

**What's missing:** No way to execute a script file and bring its definitions into the current scope at runtime.

```stash
// What you'd expect:
source("~/.stashrc");           // Load user config
source("./config/" + env);      // Dynamic path

// Or at minimum:
importDynamic("plugins/" + pluginName + ".stash");
```

**Why it matters:**

- RC file loading for REPL customization (`~/.stashrc`)
- Plugin systems that discover and load modules at runtime
- Environment-specific configuration loading
- Post-install hooks that load newly-installed packages

**Note:** This is different from `eval` — it would execute a file, not an arbitrary string. The security model could restrict it to known paths.

### 4.4 No `exit` Code Access for Commands

**What Stash has:** `$(cmd)` returns a `CommandResult` with `.exitCode`. But there's no global `$?` or equivalent shorthand for the last command's exit code.

**What's missing:** A convenient way to branch on command success/failure.

```stash
// Verbose (current):
let result = $(grep -q "pattern" file.txt);
if (result.exitCode != 0) {
    io.println("Pattern not found");
}

// What you'd expect (terser):
$(grep -q "pattern" file.txt);
if ($? != 0) {  // or: if (!$ok)
    io.println("Pattern not found");
}

// Or even:
if ($(grep -q "pattern" file.txt).exitCode == 0) { ... }
// This works but is noisy
```

**Why it matters:**

- Command exit codes are the primary error signal in system scripting
- Every shell script checks `$?` constantly
- The verbose `result.exitCode` pattern discourages good error checking
- Command success/failure is a boolean question that shouldn't require 3 lines

### ~~4.5 No Bitwise Operators~~ ✅ IMPLEMENTED

> **Status:** Implemented. Stash now supports all six bitwise operators: `&` (AND), `|` (OR), `^` (XOR), `~` (NOT), `<<` (left shift), `>>` (right shift). Compound assignment variants (`&=`, `|=`, `^=`, `<<=`, `>>=`) are also supported. All operators work on integer (`long`) operands only. The `|` and `>>` tokens are context-sensitive — they function as shell pipe/redirect when the left operand is a command expression, and as bitwise operators otherwise. 82 tests cover the feature.

**What Stash now has:** Full integer bitwise operations with C-standard precedence, compound assignments, and integration with hex/octal/binary literals.

**~~What was missing:~~** ~~No `&` (AND), `|` (OR), `^` (XOR), `~` (NOT), `<<` (left shift), `>>` (right shift) operators.~~

```stash
// Now works as expected:
let permissions = 0o644;
let readable = (permissions & 0o444) != 0;
let flags = 0b0100 | 0b0001;
let masked = 0xDEAD_BEEF & 0xFF;
let shifted = 1 << 8;            // 256
let inverted = ~0xFF;             // bitwise NOT

// Compound assignment:
let bits = 0;
bits |= 0b0100;    // set bit 2
bits &= ~0b0100;   // clear bit 2
bits <<= 4;         // shift left by 4

// Context-sensitive | and >>:
$(ls) | $(grep ".txt")    // pipe (left is command)
let x = 0xFF | 0x0F;      // bitwise OR (left is integer)
```

### ~~4.6 No Octal / Hex / Binary Number Literals~~ ✅ IMPLEMENTED

> **Status:** Implemented. Stash now supports hexadecimal (`0xFF`), octal (`0o755`), and binary (`0b1010`) number literals with optional underscore digit separators (`0b1111_0000`). All produce integer (`long`) values. 73 tests cover the feature.

**What Stash now has:** Literals in all four bases — decimal, hexadecimal (`0x`/`0X`), octal (`0o`/`0O`), and binary (`0b`/`0B`) — with optional `_` separators between digits.

**~~What was missing:~~** ~~No way to write numbers in octal (`0o755`), hexadecimal (`0xFF`), or binary (`0b1010`) notation.~~

```stash
// Now works as expected:
let permissions = 0o755;    // octal: 493
let color = 0xFF00FF;       // hex: 16711935
let flags = 0b10110;        // binary: 22
let mask = 0xFF_FF_00_00;   // hex with underscore separators

// The old workaround (no longer needed):
// let permissions = 493;   // Who knows this is 0o755?
// let color = 16711935;    // Completely unreadable
// let flags = 22;          // Meaningless
```

### ~~4.7 No Regex Capture Groups~~ ✅ IMPLEMENTED

> **Status:** Implemented. Stash now provides structured regex capture groups via `str.capture(s, pattern)` and `str.captureAll(s, pattern)`. Both return `RegexMatch` structs containing `value`, `index`, `length`, `groups` (array of `RegexGroup` structs), and `namedGroups` (dict of name → string). Named capture groups use `(?<name>...)` syntax. The existing `str.match()` and `str.matchAll()` remain unchanged for backward compatibility. 23 tests cover the feature.

**What Stash now has:** `str.capture(s, pattern)` → `RegexMatch | null`, `str.captureAll(s, pattern)` → array of `RegexMatch`. Two built-in structs: `RegexMatch` (value, index, length, groups, namedGroups) and `RegexGroup` (value, index, length, name). Both positional and named capture groups are fully supported.

```stash
// Positional capture groups:
let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
io.println(m.groups[1].value);  // "1"
io.println(m.groups[2].value);  // "23"

// Named capture groups:
let line = "192.168.1.1 myhost";
let m = str.capture(line, "(?<ip>\\d+\\.\\d+\\.\\d+\\.\\d+)\\s+(?<host>\\S+)");
io.println(m.namedGroups["ip"]);   // "192.168.1.1"
io.println(m.namedGroups["host"]); // "myhost"

// Multiple matches with captureAll:
let matches = str.captureAll("alice@example.com bob@work.org", "(\\w+)@(\\w+)");
for (let m in matches) {
    io.println(m.groups[1].value + " at " + m.groups[2].value);
}
```

---

## 5. Tier 3 — Sysadmin Power Features

Features that power users and sysadmin-focused scripts need. Their absence pushes users back to Bash/Python.

### 5.1 ~~No File Watching (inotify / FSEvents)~~ ✅ IMPLEMENTED

**What's missing:** No way to watch files or directories for changes.

```stash
// What you'd expect:
fs.watch("/var/log/app.log", (event) => {
    if (event.type == "modified") {
        processNewLines(event.path);
    }
});

// Or for config reload:
fs.watch("/etc/app/config.toml", () => {
    reloadConfig();
});
```

**Why it matters:**

- Log tailing and processing (the "tail -f" use case)
- Config file hot-reloading without polling
- Build systems watching source files for changes
- Deployment watchers that trigger on artifact changes
- .NET has `FileSystemWatcher` — the infrastructure exists

### ~~5.2 No Raw Socket / TCP / UDP~~ ✅ IMPLEMENTED

> **Status:** Implemented. The `net` namespace now provides TCP socket operations (`tcpConnect`, `tcpSend`, `tcpRecv`, `tcpClose`, `tcpListen`) and UDP datagram operations (`udpSend`, `udpRecv`). TCP supports both client connections and single-connection server listeners. UDP supports sending and receiving individual datagrams with configurable timeouts. Three new structs: `TcpConnection` (host, port, localPort), `UdpMessage` (data, host, port), and `MxRecord` (priority, exchange). 15 tests cover the TCP/UDP features.

**What Stash now has:** `net.tcpConnect(host, port, timeout?)` for TCP connections, `net.tcpSend(conn, data)` / `net.tcpRecv(conn, maxBytes?)` for data transfer, `net.tcpClose(conn)` for cleanup, `net.tcpListen(port, handler)` for single-connection servers. `net.udpSend(host, port, data)` for sending datagrams, `net.udpRecv(port, timeout?)` for receiving. All operations use UTF-8 encoding and support configurable timeouts.

**What Stash has:** `http.*` for HTTP, `ssh.*` and `sftp.*` for SSH/SFTP.

**~~What's missing:~~** ~~Lower-level networking — TCP connections, UDP datagrams, raw sockets.~~

```stash
// What you'd expect:
let sock = net.connect("host", 5432);  // TCP connection
net.send(sock, "PING\r\n");
let response = net.recv(sock);
net.close(sock);

// UDP:
let udp = net.udp();
net.sendTo(udp, "host", 514, message);  // syslog
```

**Why it matters:**

- Health checks against non-HTTP services (databases, Redis, SMTP, DNS)
- Syslog forwarding (UDP port 514)
- Custom protocol communication
- Port scanning and service discovery
- Network diagnostic tools

### ~~5.3 No DNS Lookups~~ ✅ IMPLEMENTED

> **Status:** Implemented. The `net` namespace now provides `net.resolve(hostname)` → IP, `net.resolveAll(hostname)` → array of IPs, `net.reverseLookup(ip)` → hostname (these existed previously), plus new `net.resolveMx(domain)` → array of MxRecord structs and `net.resolveTxt(domain)` → array of strings. MX and TXT queries use raw DNS protocol via UDP to the system nameserver (falls back to 8.8.8.8). 4 tests cover the DNS features.

**What Stash now has:** Forward DNS (`net.resolve`, `net.resolveAll`), reverse DNS (`net.reverseLookup`), MX record resolution (`net.resolveMx` → array of `MxRecord` with priority and exchange fields), and TXT record resolution (`net.resolveTxt` → array of strings). Cross-platform: uses raw DNS queries via UDP, falling back to Google Public DNS if no system nameserver is configured.

**~~What's missing:~~** ~~No way to perform DNS resolution.~~

```stash
// What you'd expect:
let ips = net.resolve("example.com");           // A records
let mx = net.resolveMx("example.com");          // MX records
let txt = net.resolveTxt("example.com");         // TXT records (SPF, DKIM)
let reverse = net.reverseLookup("93.184.216.34");
```

**Why it matters:**

- Network diagnostics and monitoring
- DNS record validation (SPF, DKIM, DMARC checks)
- Service discovery
- Reverse DNS for log enrichment

### ~~5.4 No Date/Time Formatting and Parsing~~ ✅ IMPLEMENTED

> **Status:** Implemented. The `time` namespace now provides timezone-aware operations: `toTimezone(timestamp, timezone)`, `toUTC(timestamp, timezone)`, `timezone()`, `timezones()`, `offset(timestamp, timezone)`. Duration convenience helpers: `seconds(n)`, `minutes(n)`, `hours(n)`, `days(n)`, `weeks(n)`. Date utilities: `startOf(timestamp, unit)`, `endOf(timestamp, unit)`, `isLeapYear(timestamp?)`, `daysInMonth(timestamp?)`. Supports IANA timezone IDs on all platforms. 27 tests cover the feature.

**What Stash now has:** Full timezone conversion via IANA timezone IDs (`time.toTimezone`, `time.toUTC`), system timezone introspection (`time.timezone()`, `time.timezones()`), UTC offset queries (`time.offset`), duration convenience helpers (`time.seconds`, `time.minutes`, `time.hours`, `time.days`, `time.weeks`) for readable time arithmetic, and date truncation/rounding utilities (`time.startOf`, `time.endOf`, `time.isLeapYear`, `time.daysInMonth`).

**What Stash has:** `time.now()`, `time.format()`, `time.parse()`, `time.add()`, `time.diff()` — basic datetime operations covering the common cases.

**~~What's missing:~~** ~~Timezone-aware operations (all time functions work in UTC only — no timezone conversion API), duration convenience helpers (`time.hours()`, `time.minutes()` shorthands), and richer arithmetic.~~

```stash
// What sysadmin scripts constantly need that's missing:

// Timezone conversion (not available):
let eastern = time.toTimezone(now, "America/New_York");
let utcTime = time.toUTC(localTime);

// Duration convenience helpers (not available):
let oneHourAgo = time.add(now, -3600);       // Must use raw seconds
// No time.hours(1), time.minutes(5) shorthands exist
let isRecent = time.diff(fileTime, now) < 300;  // Magic numbers instead of time.minutes(5)
```

**Why it matters:**

- Log timestamps are the most common data format in sysadmin work
- Cron-like scheduling needs time comparison
- SLA monitoring needs duration arithmetic
- Cross-timezone operations for distributed systems

### 5.5 No Mutex / Lock Primitives

**What Stash has:** `task.run()`, `task.await()`, `task.all()` — future-based concurrency.

**What's missing:** Any synchronization primitives for shared state.

```stash
// What you'd expect with concurrent tasks:
let lock = sync.mutex();

let tasks = arr.map(servers, (server) => {
    return task.run(() => {
        let result = checkServer(server);
        sync.lock(lock, () => {
            results = arr.push(results, result);  // Safe concurrent append
        });
    });
});
```

**Why it matters:**

- Parallel scripts that aggregate results need safe shared state
- File locking for scripts that must not run concurrently
- Rate limiting concurrent operations (connection pools)
- Without synchronization, parallel task results can be corrupted

### 5.6 No Channels / Inter-Task Communication

**What's missing:** No way for concurrent tasks to communicate aside from shared mutable state.

```stash
// What you'd expect:
let ch = sync.channel();

task.run(() => {
    for (let line in readLogStream()) {
        sync.send(ch, line);
    }
    sync.close(ch);
});

// Consumer:
for (let line in sync.receive(ch)) {
    processLine(line);
}
```

**Why it matters:**

- Producer/consumer patterns (log processing, work queues)
- Pipeline architectures with concurrent stages
- Event-driven architectures
- Go popularized this, but Python (multiprocessing.Queue), Ruby (Queue), and PowerShell (thread-safe collections) all have equivalents

### ~~5.7 No Timeout Wrapper~~ ✅ IMPLEMENTED

> **Status:** Implemented. The `task` namespace now provides `task.timeout(ms, fn)` which executes a function with a millisecond timeout. Returns the function's result if it completes in time; throws a `TimeoutError` if it doesn't. The timeout is enforced externally via `Task.Wait()`, so it works even with non-cancellation-aware blocking operations like `time.sleep()`. 6 tests cover the feature.

**What Stash now has:** `task.timeout(ms, fn)` — executes any function with a hard timeout. Returns the function's result on success. Throws a `TimeoutError` (catchable via `try`) if the timeout expires. Works with any blocking operation including network I/O, file operations, and `time.sleep()`.

**~~What's missing:~~** ~~No generic way to add a timeout to an arbitrary operation.~~

```stash
// What you'd expect:
let result = try task.timeout(5000, () => {
    return http.get("https://slow-api.example.com/data");
});

if (result is Error) {
    io.println("Operation timed out");
}
```

**Why it matters:**

- Network operations can hang indefinitely
- Health checks must have bounded response times
- Scripts running in CI/CD have time budgets
- Without timeouts, a single slow service hangs the entire script

### 5.8 No User/Group Information

**What's missing:** No way to look up system users and groups.

```stash
// What you'd expect:
let user = sys.getUser("www-data");
io.println(user.uid);     // 33
io.println(user.home);    // /var/www
io.println(user.shell);   // /usr/sbin/nologin

let groups = sys.getGroups("heisen");  // ["heisen", "sudo", "docker"]
let currentUser = sys.currentUser();    // { name: "heisen", uid: 1000, ... }
```

**Why it matters:**

- Permission management requires knowing UIDs/GIDs
- Service configuration often references specific users
- Audit scripts need to enumerate users and group memberships
- Deployment scripts may need to create or verify user accounts

### 5.9 No Binary File I/O

**What's missing:** All `fs.*` file operations (`readFile`, `writeFile`, `appendFile`, `readLines`) are text-mode only. There is no `fs.readBytes`, `fs.writeBytes`, or any streaming/binary API.

```stash
// What you'd expect:
let bytes = fs.readBytes("/path/to/binary.bin");
let header = arr.slice(bytes, 0, 4);
fs.writeBytes("/output/file.bin", processedBytes);

// What you have to do:
$(xxd -p binary.bin);  // Shell out — Unix only, requires xxd
```

**Why it matters:**

- Parsing binary formats, processing deploy artifacts, or reading non-text data is impossible without shelling out
- Binary file I/O is a fundamental system programming operation
- Cross-platform binary processing cannot rely on `xxd` or `od` (not available on Windows)
- .NET provides `File.ReadAllBytes` and `File.WriteAllBytes` natively — trivial to expose

### 5.10 No Compression / Archive Support

**What's missing:** No zip, tar, gzip, or bz2 operations anywhere in the stdlib. Creating and extracting archives requires shelling out to platform-specific tools.

```stash
// What you'd expect:
let archive = fs.zip("/var/log/app.log", "/tmp/app.log.gz");
let files = fs.unzip("/tmp/release.zip", "/opt/app/");
fs.tar("/opt/app/", "/tmp/app.tar.gz");

// What you have to do:
$(tar -czf /tmp/app.tar.gz /opt/app/);    // Unix only
$(zip /tmp/release.zip file.txt);          // May not exist on minimal systems or Windows
```

**Why it matters:**

- Creating and extracting archives is ubiquitous in deployment, CI/CD, and backup scripts
- Shelling out to `tar` or `zip` fails on minimal containers (no coreutils) and on Windows
- .NET provides `ZipFile` and `GZipStream` natively — the infrastructure exists and is cross-platform

---

## 6. Tier 4 — Language Ergonomics

Nice-to-have features that improve developer experience. Their absence is annoying but rarely blocking.

### 6.1 ~~No String `.method()` Syntax~~ ✅ IMPLEMENTED

Stash uses namespace functions: `str.upper(s)` not `s.upper()`. While consistent, it's unfamiliar to developers from Python, Ruby, JavaScript, etc.

```stash
// What Stash does:
let upper = str.upper(str.trim(input));

// What most languages do:
let upper = input.trim().upper();
```

This is a deliberate design choice and not a bug. However, it means chaining operations is inside-out rather than left-to-right.

### 6.2 No Multi-Variable Declaration

```stash
// What you'd expect:
let a, b, c = 1, 2, 3;
// Or:
let [a, b, c] = [1, 2, 3];  // Stash HAS array destructuring

// Note: Stash does have destructuring, so this is partially addressed.
// But multi-declaration on one line (without destructuring) isn't supported.
```

### 6.3 No REPL Niceties

Covered separately in the shell analysis, but worth consolidating:

| Feature                                     | Status                                       |
| ------------------------------------------- | -------------------------------------------- |
| History file persistence                    | **Missing** — history lost on exit           |
| Tab completion (files, commands, variables) | **Missing**                                  |
| RC file loading (`~/.stashrc`)              | **Missing**                                  |
| Customizable prompt                         | **Missing** — hardcoded `stash> `            |
| Syntax highlighting in REPL                 | **Missing**                                  |
| Multi-line input editing                    | **Missing** — relies on `;` or `{` detection |

### 6.4 No Heredoc / Raw String Syntax

**What Stash has:** Triple-quoted strings (`"""..."""`) with auto-indent stripping.

**What's arguably still useful:** Raw strings for regex and paths.

```stash
// Triple-quoted covers most heredoc cases:
let sql = """
    SELECT * FROM users
    WHERE active = true
""";

// But raw strings would help with regex and Windows paths:
let pattern = r"C:\Users\heisen\\.stash";  // No double-escaping backslashes
// Stash does NOT support r"..." raw string literals — this syntax does not exist
// Triple-quoted strings are the closest alternative, but still process escape sequences
```

### 6.5 No Enum Values / Associated Data

Stash enums are C-style: named constants without associated data.

```stash
// What Stash has:
enum Color { Red, Green, Blue }

// What Rust/Swift/etc. offer:
// enum Result { Ok(value), Err(message) }  — algebraic data types
```

Not critical for scripting, but limits pattern matching expressiveness.

---

## 7. What Stash Gets Right

For context, these are areas where Stash meets or exceeds scripting language expectations. This is not just a list of problems.

| Area                    | Assessment                                                                                     |
| ----------------------- | ---------------------------------------------------------------------------------------------- |
| **Command execution**   | First-class `$(...)` and `$>(...)` with interpolation — better than Python/Ruby's `subprocess` |
| **File I/O**            | 27 `fs.*` functions including `glob`, `walk`, `tempFile`, `tempDir`, `symlink`                 |
| **String manipulation** | 38+ `str.*` functions with timeout-protected regex                                             |
| **Data formats**        | JSON, YAML, TOML, INI — unified `config.*` API for all four                                    |
| **SSH/SFTP**            | Built-in, no external dependencies — rare for any scripting language                           |
| **HTTP client**         | Full REST support with `http.*` — no `curl` needed                                             |
| **Async/concurrency**   | Future-based with `task.*` — better than Bash, comparable to modern Python                     |
| **Process management**  | Signal sending, PID tracking, spawn/wait — comprehensive                                       |
| **Path manipulation**   | `path.*` namespace covers all standard operations                                              |
| **Environment**         | Full env var management plus `env.cwd()`, `env.home()`, `env.os()`, `env.arch()`               |
| **Encryption**          | `crypto.*` with hash, hmac, encrypt, decrypt, randomBytes                                      |
| **Encoding**            | `encoding.*` with base64, hex, url, html encode/decode                                         |
| **Templating**          | Jinja2-style `tpl.*` engine built-in — unusual for a scripting language                        |
| **Testing**             | TAP framework with `test`/`assert` namespaces — built-in test runner                           |
| **Privilege elevation** | `elevate { }` block — unique to Stash, solves a real sysadmin pain point                       |
| **Error values**        | First-class errors with `try expr ?? default` — elegant for simple cases                       |
| **Module system**       | `import { } from` with package resolution — modern                                             |
| **Type checking**       | `is` operator, `typeof()`, `nameof()` — sufficient for dynamic language                        |
| **Structures**          | `struct`, `enum`, `interface` — more than most scripting languages offer                       |
| **Destructuring**       | Array and object destructuring — modern JS/Python-like                                         |

---

## 8. Cross-Reference Matrix

How Stash compares against the other languages for each gap:

| Gap                    | Bash              | Python                  | Ruby             | PowerShell             | Stash          |
| ---------------------- | ----------------- | ----------------------- | ---------------- | ---------------------- | -------------- |
| **try/catch/finally**  | `trap`            | ✅                      | ✅               | ✅                     | ✅ implemented |
| **chmod/chown**        | ✅ built-in       | ✅ `os.chmod`           | ✅ `File.chmod`  | ✅ `Set-Acl`           | ✅ implemented |
| **which**              | ✅ built-in       | ✅ `shutil.which`       | ✅ built-in      | ✅ `Get-Command`       | ✅ implemented |
| **Signal trapping**    | ✅ `trap`         | ✅ `signal`             | ✅ `Signal.trap` | ❌ limited             | ✅ implemented |
| **C-style for**        | ✅ `for((;;))`    | ❌ (by design)          | ❌ (by design)   | ✅ `for(;;)`           | ✅ implemented |
| **Spread/rest**        | ✅ `$@`           | ✅ `*args`              | ✅ `*args`       | ✅ `$args`             | ✅ implemented |
| **Eval/source**        | ✅ both           | ✅ both                 | ✅ both          | ✅ `Invoke-Expression` | ✅ partial     |
| **Bitwise ops**        | ✅                | ✅                      | ✅               | ✅ `-band`, `-bor`     | ✅ implemented |
| **Hex/octal literals** | ✅                | ✅                      | ✅               | ✅                     | ✅ implemented |
| **Named captures**     | ❌                | ✅                      | ✅               | ✅                     | ✅ implemented |
| **File watching**      | ✅ `inotifywait`  | ✅ `watchdog`           | ✅ `Listen`      | ✅ `FileSystemWatcher` | ✅ implemented |
| **TCP/UDP**            | ✅ `/dev/tcp`     | ✅ `socket`             | ✅ `Socket`      | ✅ `Net.Sockets`       | ✅ implemented |
| **DNS**                | ✅ `dig`/`host`   | ✅ `socket.getaddrinfo` | ✅ `Resolv`      | ✅ `Resolve-DnsName`   | ✅ implemented |
| **Timeouts**           | ✅ `timeout` cmd  | ✅ `signal.alarm`       | ✅ `Timeout`     | ✅ `Wait-Job -Timeout` | ✅ implemented |
| **User/group info**    | ✅ `id`, `getent` | ✅ `pwd`/`grp`          | ✅ `Etc`         | ✅ `Get-LocalUser`     | ❌             |
| **Mutex/locks**        | ✅ `flock`        | ✅ `threading.Lock`     | ✅ `Mutex`       | ✅ `[Threading.Mutex]` | ❌             |

---

## 9. Recommended Priority

Based on how frequently each gap would block real scripts, here's the recommended implementation order:

### Phase 1 — Critical Path (blocks basic scripting)

| #     | Feature                                               | Effort                                    | Impact                                                                                   |
| ----- | ----------------------------------------------------- | ----------------------------------------- | ---------------------------------------------------------------------------------------- |
| ~~1~~ | ~~**try/catch/finally**~~                             | ~~High — parser + interpreter~~           | ✅ **DONE** — implemented with 25 tests                                                  |
| ~~2~~ | ~~**Bitwise operators + hex/octal/binary literals**~~ | ~~Medium — lexer + parser + interpreter~~ | ✅ **DONE** (73 tests);                                                                  |
| ~~3~~ | ~~**fs.chmod / fs.chown**~~                           | ~~Low — stdlib addition~~                 | ✅ **DONE** — implemented with cross-platform `FilePermissions` struct API (24 tests)    |
| ~~4~~ | ~~**sys.which**~~                                     | ~~Low — stdlib addition~~                 | ✅ **DONE** — implemented as cross-platform `sys.which(name)` with PATH + PATHEXT search |

### Phase 2 — Scripting Power (blocks intermediate scripts)

| #     | Feature                        | Effort                                    | Impact                                                                                    |
| ----- | ------------------------------ | ----------------------------------------- | ----------------------------------------------------------------------------------------- |
| ~~6~~ | ~~**Signal trapping**~~        | ~~Medium — .NET PosixSignalRegistration~~ | ✅ **DONE** — implemented with `sys.Signal` enum + `sys.onSignal()` + `sys.offSignal()`   |
| ~~7~~ | ~~**Spread/rest parameters**~~ | ~~Medium — parser + interpreter~~         | ✅ **DONE** - Unblocks variadic APIs and wrappers                                         |
| ~~8~~ | ~~**C-style for loop**~~       | ~~Low-Medium — parser + interpreter~~     | ✅ **DONE**                                                                               |
| ~~9~~ | ~~**Named regex captures**~~   | ~~Low — stdlib enhancement~~              | ✅ **DONE** — `str.capture()` + `str.captureAll()` with `RegexMatch`/`RegexGroup` structs |
| 10    | **Timeout wrapper**            | Low — stdlib addition                     | ✅ **DONE** Unblocks reliable network scripts                                                         |

### Phase 3 — Sysadmin Power (blocks advanced scripts)

| #   | Feature                    | Effort                             | Impact                                                |
| --- | -------------------------- | ---------------------------------- | ----------------------------------------------------- |
| 11  | **File watching**          | Medium — FileSystemWatcher wrapper | ✅ **DONE** — Event-driven scripts                    |
| 12  | **TCP/UDP sockets**        | Medium-High — new namespace        | ✅ **DONE** Non-HTTP service communication                        |
| 13  | **DNS lookups**            | Low — Dns.GetHostAddresses         | ✅ **DONE** Network diagnostics                                   |
| 14  | **User/group info**        | Low-Medium — platform-specific     | Permission and audit scripts                          |
| 15  | **Mutex / file locking**   | Medium — new sync namespace        | Safe concurrent scripts                               |
| 16  | **Runtime file sourcing**  | Medium — interpreter enhancement   | ✅ **PARTIAL** — Import statements accept expressions |
| 17  | **Binary file I/O**        | Low — fs namespace addition        | Enables non-text file processing                      |
| 18  | **Compression / archives** | Low-Medium — new archive namespace | Deployment and backup scripts                         |

### De-prioritized

| Feature                       | Reason                                                 |
| ----------------------------- | ------------------------------------------------------ |
| **eval**                      | Security risk; static imports are a feature, not a bug |
| **Channels**                  | Future-based concurrency covers most use cases         |
| **Raw sockets**               | Extremely niche for scripting target audience          |
| **REPL niceties**             | Important but covered by the shell analysis document   |
| **String `.method()` syntax** | ✅ **DONE**                                            |

---

> **Key takeaway:** With try/catch/finally, hex/octal/binary number literals, and file permissions control now implemented, the remaining critical gap is missing bitwise operators. This means that Stash can now write fully cross-platform deploy scripts that manipulate file permissions, but binary data manipulation and bitmask operations still require shelling out. Phase 1 should continue to be treated as a priority above new features.
