# Stash.Scheduler — OS Service Abstraction Library

**Status:** Backlog — Design
**Created:** 2026-04-11
**Origin:** Design evolution from "Scheduling — Built-in Interval and Cron Scheduling" spec
**Related:** `Scheduling — Built-in Interval and Cron Scheduling.md` (§1–11 covers in-process scheduling via `task.every`, `task.cron`, `task.keepAlive`)

---

## 1. Purpose

`Stash.Scheduler` is a .NET class library that provides a **cross-platform abstraction layer over OS-native service/scheduling facilities** (systemd on Linux, launchd on macOS, Task Scheduler on Windows). It enables:

1. **The CLI** to expose `stash service install|start|stop|status|logs|uninstall` commands
2. **The stdlib** to expose `scheduler.install()`, `scheduler.list()`, etc. for programmatic service management from Stash scripts
3. **Test suites** to validate service definition generation deterministically, without root access or a running OS scheduler

The library does NOT implement its own scheduler, daemon, or IPC mechanism. The OS is the sole source of truth for scheduling state.

---

## 2. How We Got Here — Design History

This spec is the result of a design process that evaluated three architectural options for production-grade scheduling in Stash. Understanding the reasoning matters — an implementer who doesn't know WHY we rejected the daemon approach may accidentally reintroduce it.

### The Problem

Stash's in-process scheduling (`task.every()`, `task.cron()`, `task.keepAlive()` — specified in the companion spec §5) gives scripts recurring execution. But it has no persistence, no survivability across reboots, no visibility from other terminals, and no multi-script coordination. Sysadmins managing production servers need more.

### Options Evaluated

**Option A: Dedicated Daemon (`stashd`).** A long-running daemon with SQLite state store, IPC via Unix sockets, cron engine, and process spawning for job execution.

- **Rejected.** The daemon is operationally expensive (IPC protocol, state persistence, lock management, daemon lifecycle, process supervision) and introduces significant security surface (privileged persistent process, network-accessible socket, mutable state store). Most critically, because the OS already provides scheduling and process supervision, the daemon would be largely idle between job registrations — it's a stateful proxy adding attack surface for what is essentially a CRUD API over OS scheduler entries.
- The daemon approach could still be revisited as a future phase if the library-based approach proves insufficient, but it should not be built speculatively.

**Option B: Self-Scheduling Scripts.** Scripts use `task.keepAlive()` to become long-running services, with a thin `stash service install` CLI that generates OS service definitions.

- **Partially adopted.** The in-process scheduling story (`task.every` + `task.cron` + `task.keepAlive`) is the right Layer 1. But the OS integration layer needs more thought — it benefits from a standalone library project rather than being shoehorned into the CLI.

**Option C: OS Service Abstraction Library (this spec).** A dedicated library project that abstracts over systemd/launchd/Task Scheduler with a unified interface. The CLI integrates with it for `stash service` commands. The stdlib can expose it for programmatic use. No daemon, no IPC, no persistent process.

- **Adopted.** Lowest attack surface, leverages battle-tested OS infrastructure, enables both CLI and stdlib integration from a single codebase.

### Key Insight

A Stash script with `task.keepAlive()` is already a daemon in practice. What it needs is not another daemon managing it, but a clean way to install itself into the OS service manager so it survives reboots, gets restarted on crashes, and can be managed via a unified interface. The library exists to make that one-command simple.

---

## 3. Architecture

### 3.1 Project Layout

```
Stash.Scheduler/
├── Stash.Scheduler.csproj
├── IServiceManager.cs              → Platform abstraction interface
├── ServiceManagerFactory.cs        → Returns the right IServiceManager for the current OS
├── CronExpression.cs               → Cron parser (shared with task.cron() in Stash.Stdlib)
├── Models/
│   ├── ServiceDefinition.cs        → Cross-platform service description
│   ├── ServiceStatus.cs            → Unified status model
│   ├── ServiceInfo.cs              → Installed service metadata
│   └── ExecutionRecord.cs          → History entry (timestamp, exit code, duration)
├── Platforms/
│   ├── SystemdServiceManager.cs    → Linux backend
│   ├── LaunchdServiceManager.cs    → macOS backend
│   └── WindowsTaskServiceManager.cs → Windows backend
├── Validation/
│   └── InputValidator.cs           → Security-critical input validation
└── Logging/
    └── ServiceLogManager.cs        → Stash-managed log file abstraction
```

### 3.2 Dependency Position

```
Stash.Core (no dependencies)
  ↓
Stash.Scheduler (references Core only — same pattern as Stash.Tpl, Stash.Tap)
  ↓
Stash.Stdlib (references Scheduler for stdlib exposition)
Stash.Cli (references Scheduler for CLI commands)
Stash.Tests (references Scheduler for testing)
```

`Stash.Scheduler` follows the same minimal-dependency pattern as `Stash.Tpl` and `Stash.Tap`: it references only `Stash.Core`. It does NOT depend on `Stash.Stdlib` or `Stash.Bytecode`.

### 3.3 Capability Gating

The library needs OS process capabilities. In the `StdlibDefinitions` registry, the scheduler namespace should be gated behind `StashCapabilities.Process | StashCapabilities.FileSystem` — this prevents it from loading in the Playground (Blazor WASM) which cannot interact with OS services.

---

## 4. The `IServiceManager` Interface

```csharp
public interface IServiceManager
{
    /// <summary>Install a Stash script as an OS-managed service.</summary>
    ServiceResult Install(ServiceDefinition definition);

    /// <summary>Remove an installed service and its artifacts.</summary>
    ServiceResult Uninstall(string serviceName);

    /// <summary>Start a stopped/installed service.</summary>
    ServiceResult Start(string serviceName);

    /// <summary>Stop a running service.</summary>
    ServiceResult Stop(string serviceName);

    /// <summary>Restart a running service.</summary>
    ServiceResult Restart(string serviceName);

    /// <summary>Enable auto-start on boot.</summary>
    ServiceResult Enable(string serviceName);

    /// <summary>Disable auto-start on boot.</summary>
    ServiceResult Disable(string serviceName);

    /// <summary>Get the current status of a service.</summary>
    ServiceStatus GetStatus(string serviceName);

    /// <summary>List all Stash-managed services.</summary>
    IReadOnlyList<ServiceInfo> List();

    /// <summary>Get execution history from OS logs and Stash log files.</summary>
    IReadOnlyList<ExecutionRecord> GetHistory(string serviceName, int maxRecords = 20);

    /// <summary>Check whether the current environment supports this manager.</summary>
    bool IsAvailable();
}
```

`ServiceResult` is a simple success/failure type with an error message — not exceptions for expected failures (e.g., "service already exists", "service not found").

```csharp
public readonly record struct ServiceResult(bool Success, string? Error = null);
```

### 4.1 The Source of Truth Rule

Every method that returns state (`GetStatus`, `List`, `GetHistory`) MUST query the OS first, then augment with sidecar metadata. If the OS says a service doesn't exist but the sidecar file does, the service is orphaned — report it as such rather than pretending it exists.

If the OS says a service exists but there's no sidecar file, the service was likely installed manually or the sidecar was lost — report it as "unmanaged" (known to the OS, not tracked by Stash).

**The OS always wins on scheduling state.** The sidecar never overrides the OS.

---

## 5. The `ServiceDefinition` Model

```csharp
public sealed class ServiceDefinition
{
    // ── Portable fields (work on all platforms) ──

    /// <summary>Unique service name. Becomes part of the OS service identifier.</summary>
    public required string Name { get; init; }

    /// <summary>Absolute path to the Stash script to execute.</summary>
    public required string ScriptPath { get; init; }

    /// <summary>Human-readable description shown in OS service listings.</summary>
    public string? Description { get; init; }

    /// <summary>Cron expression for periodic execution (null = long-running service).</summary>
    public string? Schedule { get; init; }

    /// <summary>Working directory for the script.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Environment variables to set.</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>User to run the service as (null = current user).</summary>
    public string? User { get; init; }

    /// <summary>Whether to automatically start on boot.</summary>
    public bool AutoStart { get; init; } = true;

    /// <summary>Whether to restart on non-zero exit.</summary>
    public bool RestartOnFailure { get; init; } = true;

    /// <summary>Maximum restart attempts before giving up (0 = unlimited).</summary>
    public int MaxRestarts { get; init; } = 0;

    /// <summary>Seconds to wait between restart attempts.</summary>
    public int RestartDelaySec { get; init; } = 5;

    // ── Platform escape hatch ──

    /// <summary>
    /// Raw key/value pairs injected into the platform-specific service definition.
    /// systemd: injected into [Service] section. launchd: injected into top-level plist dict.
    /// Windows: injected as Task Scheduler settings.
    /// Keys are platform-specific. No validation beyond security sanitization.
    /// </summary>
    public IReadOnlyDictionary<string, string>? PlatformExtras { get; init; }
}
```

### 5.1 The Two Service Modes

The `Schedule` field determines the service mode:

| `Schedule` value                  | OS mapping                                                                                            | Behavior                                                                                                        |
| --------------------------------- | ----------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `null`                            | systemd `Type=simple` service / launchd `KeepAlive=true` / Windows Task triggered `AtStartup`         | Long-running service. Script uses `task.keepAlive()` internally for scheduling. OS supervises process liveness. |
| `"*/5 * * * *"` (cron expression) | systemd `Type=oneshot` + timer / launchd `StartCalendarInterval` / Windows Task with calendar trigger | Periodic execution. OS fires the script on schedule. Script runs, does work, exits.                             |

This distinction matters. A long-running Stash service with `task.keepAlive()` is supervised differently from a periodic script that runs and exits.

### 5.2 Stash Struct Equivalent

When exposed through the stdlib, the `ServiceDefinition` maps to a Stash struct:

```stash
struct ServiceDef {
    name: string,
    scriptPath: string,
    description: string,    // optional (default: "")
    schedule: string,       // optional (default: null — long-running mode)
    workingDir: string,     // optional (default: script's directory)
    env: dict,              // optional (default: {})
    user: string,           // optional (default: current user)
    autoStart: bool,        // optional (default: true)
    restartOnFailure: bool, // optional (default: true)
    maxRestarts: int,       // optional (default: 0)
    restartDelaySec: int,   // optional (default: 5)
    platformExtras: dict    // optional (default: {})
}
```

---

## 6. Security Architecture

This is the most critical section of the spec. Every decision here was made to minimize attack surface while preserving utility.

### 6.1 Threat Model

The library generates configuration files that OS service managers parse and execute, potentially with elevated privileges. The primary threats are:

| Threat                       | Vector                                                                                               | Impact                                                   |
| ---------------------------- | ---------------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| **Arbitrary code execution** | Malicious script path, injected unit file directives                                                 | OS executes attacker-chosen commands as the service user |
| **Path traversal**           | Service name containing `../` used in file paths                                                     | Write service files to unintended locations              |
| **Privilege escalation**     | User-level service that references a root-owned script, or `PlatformExtras` that injects `User=root` | Script runs with higher privileges than intended         |
| **Log injection**            | Malicious data in job output written to log files                                                    | Log file corruption, ANSI escape injection               |
| **Symlink attacks**          | Script path resolves to symlink pointing at sensitive file                                           | Information disclosure or execution of unintended code   |
| **Denial of service**        | Registering thousands of services, cron expression that fires every second                           | OS scheduler overloaded                                  |

### 6.2 Input Validation — The Security Boundary

ALL user-provided inputs pass through `InputValidator` before reaching any platform backend. Validation failures are hard errors, not warnings.

#### Service Name

```
Pattern: ^[a-zA-Z][a-zA-Z0-9_-]{0,63}$
```

- Must start with a letter (prevents `-` prefixed names that could be interpreted as flags)
- Alphanumeric, hyphens, underscores only — no dots, spaces, slashes, or special characters
- Max 64 characters (systemd unit name limit minus the `stash-` prefix)
- The name is used in file paths (`stash-{name}.service`), so it MUST be safe for all filesystems

#### Script Path

1. Resolve to absolute path via `Path.GetFullPath()`
2. Resolve symlinks via `File.ResolveLinkTarget(returnFinalTarget: true)` — the real path is what gets embedded in the service definition
3. Verify the file exists and is readable
4. Verify the file has a `.stash` extension (defense in depth — prevents `ExecStart=/bin/sh -c "evil"` via a non-Stash script path)
5. On Unix, verify the file is owned by the current user OR root (prevents another user from modifying the script after installation)

> **Design decision:** We resolve symlinks at install time. The service definition contains the real path, not the symlink. This prevents TOCTOU attacks where an attacker replaces the symlink target after installation.

#### Working Directory

1. Resolve to absolute path
2. Verify it exists and is a directory
3. Reject if it contains `..` segments after normalization (defense in depth)

#### Environment Variables

1. Keys must match `^[a-zA-Z_][a-zA-Z0-9_]*$` (POSIX env var naming)
2. Values are treated as opaque strings — no shell expansion, no variable interpolation
3. Each platform backend must properly escape values for its format (systemd `Environment=` quoting rules differ from launchd plist XML escaping)

#### Cron Expression

1. Must parse successfully through `CronExpression.Parse()` (strict mode, 5-field only)
2. Minimum interval enforcement: reject expressions that would fire more frequently than once per minute (this is already inherent in 5-field cron, but validated explicitly as a safeguard against future changes)
3. Must not contain shell metacharacters — the expression is only used for Stash's own cron-to-OS-calendar translation, never passed to a shell

#### Platform Extras

This is the escape hatch — intentionally less validated. But we still enforce safety bounds:

1. Keys and values are sanitized against injection for the target format:
   - **systemd:** No newlines in values (would break INI format). Keys must not be security-sensitive directives unless running as root: `User`, `Group`, `CapabilityBoundingSet`, `AmbientCapabilities`, `NoNewPrivileges`, `ProtectSystem`, `ProtectHome`, `PrivateTmp`, `RootDirectory` are **blocked for non-root installs**
   - **launchd:** XML-escaped. Keys must not override `ProgramArguments`, `Label`, or `UserName` (those are controlled by the portable fields)
   - **Windows:** Sanitized against XML injection. Cannot override `UserId`, `Command`, or `Arguments`
2. Blocked keys produce a clear error: `"PlatformExtra key 'User' is blocked: use the 'user' field in ServiceDefinition instead"`
3. A warning is emitted when any platform extras are used: `"Platform-specific extras reduce cross-platform portability"`

### 6.3 Privilege Model

The library operates in two modes:

#### User Mode (Default, No Elevation Required)

- Services are installed in user-scoped locations:
  - Linux: `~/.config/systemd/user/` (requires `loginctl enable-linger` for non-session survival)
  - macOS: `~/Library/LaunchAgents/`
  - Windows: Current user's Task Scheduler folder
- Can only run services as the current user
- Cannot set `User` field to a different user
- Maximum portability, minimum risk

#### System Mode (Requires Root/Admin)

Triggered by `--system` flag on CLI or `system: true` in stdlib options.

- Services installed in system-wide locations:
  - Linux: `/etc/systemd/system/`
  - macOS: `/Library/LaunchDaemons/`
  - Windows: `\Microsoft\Windows\stash\` in system Task Scheduler
- Can set `User` field to run as a specific user
- The CLI MUST verify it has the necessary privileges before attempting installation, and fail with a clear message if not: `"System-mode install requires root. Run with sudo or use user-mode (default)."`

#### No Escalation From Library

The library NEVER requests, acquires, or assumes elevated privileges on its own. It does not call `sudo`, does not write to locations requiring root without already having root, and does not prompt for passwords. Privilege decisions are made by the user at the CLI level.

### 6.4 File Permissions

All generated artifacts are created with restrictive permissions:

| Artifact          | Linux/macOS                              | Windows                 | Location                                            |
| ----------------- | ---------------------------------------- | ----------------------- | --------------------------------------------------- |
| Service unit file | `0644` (systemd requires world-readable) | ACL: owner full control | `~/.config/systemd/user/` or `/etc/systemd/system/` |
| Sidecar metadata  | `0600` (owner-only)                      | ACL: owner full control | `~/.config/stash/services/`                         |
| Log directory     | `0700` (owner-only)                      | ACL: owner full control | `~/.local/share/stash/logs/`                        |
| Log files         | `0600` (owner-only)                      | ACL: owner full control | `~/.local/share/stash/logs/{name}/`                 |

The sidecar and log directories are created by the library if they don't exist, with the permissions above set at creation time.

### 6.5 Service Definition Generation — Not String Concatenation

Each platform backend MUST use a structured builder approach for generating service definitions. **String interpolation/concatenation of user inputs into unit files is forbidden.** The risk is injection: a script description containing `\nExecStartPost=/bin/sh -c "evil"` could inject arbitrary directives into a systemd unit file if the description is naively interpolated.

Each backend should:

1. Build an in-memory model of the service definition
2. Serialize it using format-aware escaping (INI escaping for systemd, XML serialization for launchd/Windows)
3. Write the serialized output atomically (write to temp file, then rename — prevents partial writes)

### 6.6 Atomic File Operations

Service definition files must be written atomically to prevent partial writes that could leave the OS scheduler in an inconsistent state:

1. Write to a temp file in the same directory (e.g., `stash-health-check.service.tmp`)
2. `fsync` the temp file
3. Rename the temp file to the final name (atomic on all POSIX filesystems and NTFS)

This is especially important during `Uninstall` — the service must be stopped before the unit file is removed, and the operation should be all-or-nothing.

---

## 7. Sidecar Metadata

Each installed service gets a JSON sidecar file at `~/.config/stash/services/{name}.json`:

```json
{
  "name": "health-check",
  "scriptPath": "/home/deploy/monitoring/health_check.stash",
  "installedAt": "2026-04-11T14:30:00Z",
  "installedBy": "deploy",
  "mode": "user",
  "schedule": "*/5 * * * *",
  "description": "API health check every 5 minutes",
  "stashVersion": "1.0.0",
  "platformExtras": {}
}
```

### 7.1 What The Sidecar Is For

- **Discovery:** `stash service list` reads sidecar files to know which services Stash manages, then cross-references with the OS scheduler.
- **Context:** Stores the original script path, install time, description, and Stash version — things the OS doesn't track.
- **Reconciliation:** If a sidecar exists but the OS service doesn't, report it as orphaned (likely manually removed). Offer `stash service clean` to remove orphaned sidecars.

### 7.2 What The Sidecar Is NOT For

- **NOT the source of truth for active/inactive/running/failed status.** That comes from the OS.
- **NOT the source of truth for execution history.** That comes from log files.
- **NOT a database.** It's a flat JSON file per service. No queries, no transactions, no migrations.

---

## 8. Logging Architecture

**Decision: Stash-managed log files for cross-platform consistency.**

Each service gets a dedicated log directory at `~/.local/share/stash/logs/{name}/`:

```
~/.local/share/stash/logs/health-check/
├── current.log        → Active log file (stdout + stderr from most recent runs)
├── 2026-04-10.log     → Rotated log from April 10
├── 2026-04-09.log     → Rotated log from April 9
└── ...
```

### 8.1 Log Capture Mechanism

The generated service definition redirects stdout and stderr to the log file:

**systemd:**

```ini
StandardOutput=append:%h/.local/share/stash/logs/health-check/current.log
StandardError=append:%h/.local/share/stash/logs/health-check/current.log
```

**launchd:**

```xml
<key>StandardOutPath</key>
<string>/Users/deploy/.local/share/stash/logs/health-check/current.log</string>
<key>StandardErrorPath</key>
<string>/Users/deploy/.local/share/stash/logs/health-check/current.log</string>
```

**Windows Task Scheduler:** Task Scheduler doesn't support output redirection natively. The library wraps the Stash invocation:

```
cmd.exe /c "stash script.stash >> C:\Users\deploy\.local\share\stash\logs\health-check\current.log 2>&1"
```

### 8.2 Log Rotation

Phase 1 uses a simple daily rotation: when `stash service logs` (or the stdlib equivalent) detects that `current.log` was last modified on a previous day, it renames it to `{date}.log` and creates a fresh `current.log`.

This is lazy rotation (happens on access, not on a timer). It keeps the implementation simple and avoids requiring yet another scheduled task just for log management.

Future phases could add size-based rotation, compression, and retention policies.

### 8.3 `stash service logs` Output

```bash
$ stash service logs health-check
2026-04-11T14:30:00Z [health-check] API health check: 200 OK (45ms)
2026-04-11T14:35:00Z [health-check] API health check: 200 OK (38ms)
2026-04-11T14:40:00Z [health-check] API health check: 503 Service Unavailable (2001ms)
2026-04-11T14:45:00Z [health-check] API health check: 200 OK (41ms)

$ stash service logs health-check --follow
# (tails the current.log file)

$ stash service logs health-check --date 2026-04-10
# (reads 2026-04-10.log)
```

---

## 9. Platform Backends

### 9.1 Linux — `SystemdServiceManager`

**Discovery:** `stash-` prefixed units. `systemctl --user list-units 'stash-*'` (user mode) or `systemctl list-units 'stash-*'` (system mode).

**Install:**

1. Validate all inputs via `InputValidator`
2. Build service unit file and (if `Schedule` is set) timer unit file
3. Write both atomically to the target directory
4. Run `systemctl [--user] daemon-reload`
5. Run `systemctl [--user] enable [--now]` on the timer or service as appropriate
6. Write sidecar metadata
7. Create log directory

**Service type mapping:**

| ServiceDefinition                     | systemd                                                                               |
| ------------------------------------- | ------------------------------------------------------------------------------------- |
| `Schedule = null` (long-running)      | `Type=simple`, `ExecStart=stash <script>`, `Restart=on-failure`                       |
| `Schedule = "*/5 * * * *"` (periodic) | `Type=oneshot`, `ExecStart=stash <script>` + separate `.timer` unit with `OnCalendar` |

**Cron-to-OnCalendar translation:** The cron expression `*/5 * * * *` translates to systemd's `OnCalendar=*:0/5`. This requires a translator in the library. systemd's calendar syntax is different from cron — the library must handle the mapping correctly.

**Status mapping:** `systemctl show --property=ActiveState,SubState,ExecMainStartTimestamp,ExecMainExitTimestamp,ExecMainStatus,NRestarts` provides all needed fields for `ServiceStatus`.

**Requires:** `systemctl` on PATH. The `IsAvailable()` check verifies this.

### 9.2 macOS — `LaunchdServiceManager`

**Discovery:** `com.stash.` prefixed labels. `launchctl list | grep com.stash.`.

**Install:**

1. Validate all inputs
2. Generate plist XML
3. Write atomically to `~/Library/LaunchAgents/` or `/Library/LaunchDaemons/`
4. `launchctl load <plist>` (or `launchctl bootstrap` on newer macOS)
5. Write sidecar metadata
6. Create log directory

**Cron mapping:** launchd uses `StartCalendarInterval` dictionaries, not cron strings. The library must translate:

```
*/15 9-17 * * 1-5
→
Multiple StartCalendarInterval entries (one per hour 9–17, one per weekday 1–5, at minutes 0,15,30,45)
```

This expansion can produce many entries for complex cron expressions. The library should warn or reject cron expressions that would produce more than 100 `StartCalendarInterval` entries.

**Known limitation:** launchd's `StartCalendarInterval` cannot express "every N minutes" directly for arbitrary N — only for values that divide evenly into 60. `*/7 * * * *` (every 7 minutes) would need to be enumerated as minute 0, 7, 14, 21, 28, 35, 42, 49, 56. This is fine but produces a larger plist.

**Status mapping:** `launchctl list <label>` provides PID (if running), last exit status, and label. Less rich than systemd.

**Requires:** `launchctl` on PATH (always available on macOS).

### 9.3 Windows — `WindowsTaskServiceManager`

**Discovery:** All tasks under `\Stash\` folder in Task Scheduler.

**Install:**

1. Validate all inputs
2. Generate task XML or use `schtasks.exe /create` with parameters
3. Write sidecar metadata
4. Create log directory

**Cron mapping:** Windows Task Scheduler uses `<CalendarTrigger>` or `<TimeTrigger>` with `<Repetition>` elements. The library must translate cron expressions to the XML schema.

**Long-running services on Windows:** Task Scheduler is not ideal for long-running services. Options:

- Use Task Scheduler with `AtStartup` trigger and no timeout → technically works but Task Scheduler may report it as "running" indefinitely
- Use NSSM (Non-Sucking Service Manager) if available → proper Windows Service wrapper
- Document the limitation clearly

**Status mapping:** `schtasks.exe /query /tn \Stash\{name} /fo csv /v` provides last run time, result, status.

**Requires:** `schtasks.exe` on PATH (always available on Windows). NSSM is optional.

---

## 10. CLI Integration

### 10.1 Command Surface

```
stash service install <script> [options]    Install a Stash script as an OS service
stash service uninstall <name>              Remove an installed service
stash service start <name>                  Start a stopped service
stash service stop <name>                   Stop a running service
stash service restart <name>                Restart a service
stash service enable <name>                 Enable auto-start on boot
stash service disable <name>                Disable auto-start on boot
stash service status [name]                 Show status (all services or one)
stash service list                          List all Stash-managed services
stash service logs <name> [options]         Show service logs
stash service clean                         Remove orphaned sidecar files
```

### 10.2 `stash service install` Options

```
stash service install ./health_check.stash \
    --name health-check \
    --schedule "*/5 * * * *" \
    --description "API health check" \
    --user deploy \
    --workdir /opt/myapp \
    --env "API_URL=https://api.example.com" \
    --env "ALERT_EMAIL=ops@example.com" \
    --restart-on-failure \
    --max-restarts 5 \
    --restart-delay 10 \
    --system                        # system-mode (requires root)
    --platform-extra "MemoryMax=512M" \
    --platform-extra "CPUQuota=50%"
```

If `--name` is omitted, the service name is derived from the script filename (minus `.stash` extension), validated against the naming rules.

If `--schedule` is omitted, the service is installed as a long-running service (the script is expected to use `task.keepAlive()` internally).

### 10.3 `stash service status` Output

```
$ stash service status
NAME            STATUS    SCHEDULE        LAST RUN              NEXT RUN
health-check    active    */5 * * * *     2026-04-11 14:45:00   2026-04-11 14:50:00
nightly-backup  inactive  0 2 * * *       2026-04-11 02:00:00   2026-04-12 02:00:00
monitoring      running   (long-running)  2026-04-11 08:00:00   -

$ stash service status health-check
Service:          health-check
Status:           active
Schedule:         */5 * * * * (every 5 minutes)
Script:           /opt/myapp/health_check.stash
Working Dir:      /opt/myapp
User:             deploy
Last Run:         2026-04-11 14:45:00 (exit code 0)
Next Run:         2026-04-11 14:50:00
Installed:        2026-04-11 14:30:00
Mode:             user
Platform:         systemd (stash-health-check.timer)
```

### 10.4 CLI Integration Points

The CLI (`Stash.Cli/Program.cs`) currently uses ad-hoc if/else dispatch for subcommands (e.g., `stash pkg ...`). The `stash service` subcommand follows the same pattern:

```csharp
if (args.Length > 0 && args[0] == "service")
{
    return ServiceCommands.Run(args[1..]);
}
```

`ServiceCommands` is a thin dispatcher that parses arguments and calls `IServiceManager` methods. It lives in `Stash.Cli/` since it's CLI-specific presentation logic.

---

## 11. Stdlib Integration

The library can be exposed as a `scheduler` namespace in `Stash.Stdlib`:

```stash
import "@stash/scheduler"

// Install a service
scheduler.install(scheduler.ServiceDef {
    name: "health-check",
    scriptPath: "./health_check.stash",
    schedule: "*/5 * * * *",
    description: "API health check"
})

// Query
let services = scheduler.list()
let status = scheduler.status("health-check")

// Manage
scheduler.start("health-check")
scheduler.stop("health-check")
scheduler.uninstall("health-check")

// Logs
let logs = scheduler.logs("health-check", 20)  // last 20 lines
```

### 11.1 New Namespace or Existing?

**Decision: New `scheduler` namespace, NOT part of `task`.**

The `task` namespace handles in-process async operations (`task.run`, `task.delay`, `task.every`, `task.cron`). The `scheduler` namespace handles OS-level service management. These are fundamentally different concerns:

- `task.every(30s, callback)` → runs a callback inside the current process
- `scheduler.install(def)` → installs a persistent OS service that outlives the current process

Mixing them in one namespace would confuse the boundary between "things happening in my script" and "things happening in the OS."

---

## 12. Testing Strategy

### 12.1 Unit Tests (No OS Dependencies)

The core value of the `IServiceManager` interface is testability. Most tests validate the library without touching the OS:

**Input validation tests:**

- Valid/invalid service names (boundary cases, injection attempts)
- Path traversal attempts in script path, working directory
- Cron expression parsing and validation
- Environment variable key/value validation
- PlatformExtras blocked key detection

**Service definition generation tests:**

- Assert that `SystemdServiceManager.GenerateUnitFile(def)` produces exact expected output for known inputs
- Assert that `LaunchdServiceManager.GeneratePlist(def)` produces valid XML with correct structure
- Assert that cron-to-`OnCalendar` translation is correct for a comprehensive set of expressions
- Assert that cron-to-`StartCalendarInterval` expansion is correct and within size limits

**Reconciliation tests:**

- Mock the OS query methods, test that orphaned/unmanaged services are detected correctly
- Test status merging (OS state + sidecar metadata)

### 12.2 Integration Tests (Platform-Specific, Optional)

These require the actual OS scheduler and are marked with a test category/trait:

```csharp
[Trait("Category", "Integration")]
[Trait("Platform", "Linux")]
public class SystemdIntegrationTests
{
    // Requires: systemctl --user available, user session active
    // Uses: systemctl --user (user-mode only, no root needed)
}
```

Integration tests:

- Install a trivial service, verify it appears in `systemctl --user list-timers`
- Start/stop/restart and verify status transitions
- Uninstall and verify cleanup
- Run on CI only in environments that support it (Linux containers with systemd, macOS runners)

---

## 13. Implementation Phases

### Phase 1: Core Library + Linux Backend ✅ Complete

**Scope:**

- `Stash.Scheduler` project skeleton: `IServiceManager`, `ServiceDefinition`, `ServiceStatus`, `ServiceResult`, `InputValidator`
- `CronExpression` parser (5-field, strict validation) — shared between this library and `task.cron()` in Stash.Stdlib
- `SystemdServiceManager`: install, uninstall, start, stop, status, list
- Sidecar metadata read/write
- Log directory creation and stdout/stderr redirection in generated unit files
- `InputValidator` with full security validation
- Unit tests for validation, generation, and cron parsing
- Integration tests for systemd (optional, CI-gated)

**Does NOT include:** CLI commands, stdlib namespace, macOS/Windows backends.

**Rationale:** Linux is the primary sysadmin platform. Get the core abstractions right on one platform before tackling cross-platform concerns. The `CronExpression` parser is built here because it's needed early and is shared with in-process scheduling.

### Phase 2: CLI Integration ✅ Complete

**Scope:**

- `stash service install|uninstall|start|stop|restart|enable|disable|status|list|logs|clean` CLI commands
- Argument parsing and dispatch in `Stash.Cli/`
- `ServiceLogManager`: log reading, `--follow` (tail), `--date`, lazy daily rotation
- CLI output formatting (table for `list`, detailed for `status <name>`)
- End-to-end tests: CLI → library → systemd

**Rationale:** The CLI is the primary user interface. Users need to interact with services before the stdlib exposition matters.

### Phase 3: macOS Backend ✅ Complete

**Scope:**

- `LaunchdServiceManager`: full `IServiceManager` implementation
- Plist XML generation (structured, not string concatenation)
- Cron-to-`StartCalendarInterval` translation with expansion limit
- `launchctl` integration (load/unload/list)
- Platform-specific tests

**Rationale:** macOS is the second most common development platform for sysadmins. launchd is architecturally different enough from systemd that it deserves its own focused phase.

### Phase 4: Windows Backend ✅ Complete

**Scope:**

- `WindowsTaskServiceManager`: full `IServiceManager` implementation
- Task Scheduler XML generation via `System.Xml.Linq` and `schtasks.exe` integration
- Cron-to-Task Scheduler trigger translation (`CalendarTrigger` with `ScheduleByDay/Week/Month` and `Repetition` for interval crons)
- Long-running service strategy: `BootTrigger` + `PT0S` execution limit + `RestartOnFailure` settings
- Output redirection via `cmd.exe /c "stash script >> log 2>&1"` wrapper
- Environment variables injected as `set "KEY=VALUE" &&` prefixes in the cmd.exe command
- `ServiceManagerFactory` updated for all three platforms
- 51 unit tests for XML generation and cron-to-trigger translation

**Rationale:** Windows is the most different platform and has the most limitations (Task Scheduler is not designed for long-running services, output redirection requires wrapping). Doing it last means the interface is stable and well-tested before encountering Windows edge cases.

### Phase 5: Stdlib Integration ✅ Complete

**Scope:**

- `scheduler` namespace in `Stash.Stdlib/BuiltIns/SchedulerBuiltIns.cs`
- `scheduler.install()`, `scheduler.uninstall()`, `scheduler.start()`, `scheduler.stop()`, `scheduler.status()`, `scheduler.list()`, `scheduler.logs()`
- `ServiceDef` struct exposed to Stash scripts
- Capability gating (`StashCapabilities.Process | StashCapabilities.FileSystem`)
- Documentation in Standard Library Reference
- Example script in `examples/`

**Rationale:** The stdlib exposure should come after the CLI is stable and battle-tested — the CLI is the primary testing ground for the library's real-world behavior.

### Phase 6 (Future): Advanced Features

Candidates (not committed):

- Log rotation policies (size-based, retention period, compression)
- `stash service update` (modify schedule/options without uninstall + reinstall)
- Service groups (install multiple services from one config file)
- Health check integration (`task.healthCheck(port)` from companion spec)
- `stash service export` — generate the raw unit file/plist without installing (for review or manual deployment)

---

## 14. Open Questions

### Q1: Should `stash service install` validate that the script actually works before installing?

**Options:**

- **A) No validation.** Install the service definition regardless. If the script fails, the service will fail and the user can debug via `stash service logs`.
- **B) Dry run.** Execute `stash --check <script>` (static analysis only) before installing.
- **C) Trial run.** Execute the script once and verify it exits cleanly before installing.

**Answer:** Option A, the service installer is not responsible for validating the scripts the user created. It is the user's responsability to validate their own script.

### Q2: `loginctl enable-linger` on Linux — should the library handle it?

User-mode systemd services only run while the user has an active session, unless `enable-linger` is set. Should `stash service install` automatically enable linger if it's not already set?

**Answer:** Detect and warn, but don't auto-enable. Enabling linger changes system behavior beyond Stash's scope and may require elevated privileges on some distributions. Print a clear message: `"Warning: User lingering is not enabled. Services will stop when you log out. Run 'loginctl enable-linger' to fix this."`

### Q3: How should `stash service list` handle services that were installed by a different version of Stash?

The sidecar records the Stash version at install time. If the library format changes, old sidecars may be incompatible.

**Answer:** Keep the sidecar format minimal and stable. Version the format (a `"version": 1` field). Support reading all prior versions. Only increment the version when the schema changes in a backward-incompatible way, which should be extremely rare for what is essentially a metadata record.

---

## 15. Decision Log

| Date       | Decision                                       | Rationale                                                                                                     |
| ---------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| 2026-04-11 | Library over daemon                            | Daemon adds attack surface (IPC, state store, persistent process) for what is a CRUD API over OS schedulers   |
| 2026-04-11 | OS is source of truth for scheduling state     | No split-brain between daemon state and OS state; OS always wins                                              |
| 2026-04-11 | Stash-managed log files over OS-native logging | Cross-platform consistency; `journalctl` vs `log show` vs Event Log is too fragmented for a unified interface |
| 2026-04-11 | Portable + platform escape hatch               | Don't hide platform-specific features from power users; portable fields for common case, extras for the rest  |
| 2026-04-11 | Sidecar JSON files over SQLite                 | Simpler, no dependency, per-service files align with per-service OS entries, loss is non-critical             |
| 2026-04-11 | `scheduler` namespace over extending `task`    | In-process scheduling (`task`) and OS service management (`scheduler`) are different concerns                 |
| 2026-04-11 | New `.stash` extension check on script paths   | Defense in depth against `ExecStart` injection via non-Stash script paths                                     |
| 2026-04-11 | Symlink resolution at install time             | Prevents TOCTOU attacks where symlink target changes after service installation                               |
| 2026-04-11 | Structured builders over string interpolation  | Prevents injection in generated unit files/plists/XML                                                         |
| 2026-04-11 | User-mode default, system-mode opt-in          | Minimizes privilege requirements; root installation is an explicit choice                                     |
| 2026-04-11 | Blocked keys in PlatformExtras for non-root    | Prevents privilege escalation via `User=root` in platform extras on user-mode installs                        |
| 2026-04-11 | Linux-first implementation order               | Primary sysadmin platform, simplest backend, validates abstractions before cross-platform complexity          |

---

## 16. Relationship to Companion Spec

This spec and "Scheduling — Built-in Interval and Cron Scheduling" are complementary:

| Concern                                                                   | Where It Lives                               |
| ------------------------------------------------------------------------- | -------------------------------------------- |
| `task.every()`, `task.cron()`, `task.keepAlive()` — in-process scheduling | Companion spec §1–11                         |
| `CronExpression` parser                                                   | `Stash.Scheduler/CronExpression.cs` (shared) |
| `stash service install`, OS integration                                   | This spec                                    |
| `scheduler.install()`, stdlib exposure                                    | This spec, Phase 5                           |
| `task.onTick()`, `task.onError()` — observability hooks                   | Companion spec (future addition)             |

A typical production deployment uses both:

1. Write a script that uses `task.every()` + `task.cron()` + `task.keepAlive()` for scheduling logic
2. Install it as an OS service with `stash service install` for persistence and crash recovery
