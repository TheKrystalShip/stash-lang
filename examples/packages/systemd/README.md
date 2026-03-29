# @stash/systemd

A comprehensive systemd CLI wrapper for [Stash](https://github.com/stash-lang/stash), providing idiomatic access to the full systemd toolchain from Stash scripts. Covers 13 modules and 135 functions spanning service control, journal querying, system analysis, time/locale/hostname configuration, networking, DNS resolution, login sessions, containers, transient units, and core dumps.

> **Linux only** — requires a Linux system running systemd. All functions wrap standard systemd CLI tools (`systemctl`, `journalctl`, `systemd-analyze`, `timedatectl`, `hostnamectl`, `localectl`, `networkctl`, `resolvectl`, `loginctl`, `machinectl`, `systemd-run`, `coredumpctl`).

## Installation

```bash
stash pkg install @stash/systemd
```

## Quick Start

```stash
import "@stash/systemd" as systemd;

// Check service status
let result = systemd.services.status("nginx.service");
if (result.ok) {
    io.println(result.stdout);
}

// Check if a service is running
if (systemd.services.is_active("sshd.service")) {
    io.println("SSH is running");
}

// Tail the journal for a unit
let logs = systemd.journal.logs({ unit: "nginx", lines: 20 });
io.println(logs.stdout);

// Show boot time breakdown
let t = systemd.analyze.time();
io.println(t.stdout);
```

You can also import individual modules directly:

```stash
import "lib/services.stash" as svc;
import "lib/journal.stash" as journal;
import "lib/analyze.stash" as analyze;
```

## Modules

| Module | CLI Tool | Functions | Description |
|---|---|---|---|
| `services` | `systemctl` | 29 | Start, stop, enable, disable, and inspect units |
| `journal` | `journalctl` | 13 | Query and manage the systemd journal |
| `analyze` | `systemd-analyze` | 13 | Boot timing, security analysis, and unit verification |
| `time` | `timedatectl` | 7 | System clock, timezone, and NTP configuration |
| `hostname` | `hostnamectl` | 6 | Hostname, chassis type, and deployment metadata |
| `locale` | `localectl` | 5 | System locale and console keymap |
| `network` | `networkctl` | 10 | systemd-networkd link management |
| `resolve` | `resolvectl` | 8 | DNS resolution via systemd-resolved |
| `sessions` | `loginctl` | 12 | User sessions, seats, and linger configuration |
| `machine` | `machinectl` | 13 | systemd-nspawn machine and image management |
| `run` | `systemd-run` | 5 | Transient services, scopes, and timers |
| `coredump` | `coredumpctl` | 5 | Core dump listing, inspection, and debugging |
| `common` | — | 9 | Shared helpers used internally by all modules |

## API Reference

### services

Full unit lifecycle management via `systemctl` — 29 functions.

| Function | Description |
|---|---|
| `start(unit, opts)` | Start a unit |
| `stop(unit, opts)` | Stop a unit |
| `restart(unit, opts)` | Restart a unit |
| `reload(unit, opts)` | Reload a unit's configuration without restarting |
| `reload_or_restart(unit, opts)` | Reload if supported, otherwise restart |
| `try_restart(unit, opts)` | Restart a unit only if it is currently running |
| `enable(unit, opts)` | Enable a unit to start on boot |
| `disable(unit, opts)` | Disable a unit from starting on boot |
| `reenable(unit, opts)` | Re-enable a unit (disable then enable) |
| `mask(unit, opts)` | Mask a unit, preventing it from being started |
| `unmask(unit, opts)` | Remove the mask from a unit |
| `status(unit, opts)` | Show the runtime status of a unit |
| `is_active(unit, opts)` | Return `true` if the unit is active |
| `is_enabled(unit, opts)` | Return `true` if the unit is enabled |
| `is_failed(unit, opts)` | Return `true` if the unit is in a failed state |
| `show(unit, opts)` | Show machine-readable properties (JSON) |
| `cat(unit)` | Print the unit file(s) as loaded by systemd |
| `list_units(opts)` | List loaded units (JSON) |
| `list_unit_files(opts)` | List installed unit files (JSON) |
| `list_dependencies(unit, opts)` | Show the dependency tree for a unit |
| `list_sockets(opts)` | List socket units (JSON) |
| `list_timers(opts)` | List timer units with next/last trigger times (JSON) |
| `daemon_reload(opts)` | Reload the systemd manager configuration |
| `daemon_reexec()` | Re-execute the systemd manager |
| `get_default()` | Show the default boot target |
| `set_default(target)` | Set the default boot target |
| `reset_failed(unit, opts)` | Reset the failed state of a unit |
| `isolate(target)` | Start a target and stop all units not in its dependencies |
| `kill_unit(unit, opts)` | Send a signal to the processes of a unit |

#### Common Options

Many `services` functions accept an `opts` dict with the following keys:

| Option | Type | Description |
|---|---|---|
| `user` | `bool` | Operate on the user systemd instance |
| `system` | `bool` | Operate on the system systemd instance (default) |
| `global` | `bool` | Apply to all user instances (for `enable`/`disable`) |
| `now` | `bool` | For `enable`: also start the unit immediately |
| `force` | `bool` | Force the operation (e.g. override symlinks) |
| `runtime` | `bool` | Make changes only until next reboot |
| `signal` | `string` | Signal to send (for `kill_unit`) |
| `no_block` | `bool` | Do not wait for the operation to complete |

---

### journal

Query and manage the systemd journal via `journalctl` — 13 functions.

| Function | Description |
|---|---|
| `logs(opts)` | Query journal entries; returns raw text |
| `logs_json(opts)` | Query journal entries; returns parsed JSON lines |
| `follow(unit, opts)` | Tail the journal for a unit (streaming) |
| `boot(id)` | Show logs from a specific boot by index or ID |
| `disk_usage()` | Show total journal disk usage |
| `vacuum_size(size)` | Reduce journal to at most the given size (e.g. `"500M"`) |
| `vacuum_time(time_str)` | Remove journal entries older than the given timespan |
| `rotate()` | Request journal file rotation |
| `flush()` | Flush `/run/log/journal` to `/var/log/journal` |
| `verify()` | Verify journal file integrity |
| `list_boots()` | List recorded boots with timestamps (JSON) |
| `field_values(field)` | List all unique values for a journal field |
| `catalog(identifier)` | Show catalog entry for a message identifier |

#### Common Options for `logs` / `logs_json`

| Option | Type | Description |
|---|---|---|
| `unit` | `string` | Filter by unit name |
| `since` | `string` | Show entries since a timestamp or keyword (e.g. `"yesterday"`) |
| `until` | `string` | Show entries up to a timestamp |
| `lines` | `int` | Limit output to the last N lines |
| `priority` | `string` | Filter by priority (e.g. `"err"`, `"warning"`) |
| `grep` | `string` | Filter entries matching a pattern |
| `boot` | `string` | Limit to a specific boot |
| `no_pager` | `bool` | Disable pager output |

---

### analyze

Boot timing and unit analysis via `systemd-analyze` — 13 functions.

| Function | Description |
|---|---|
| `time()` | Show time spent in firmware, loader, kernel, and userspace |
| `blame(opts)` | List units ordered by initialization time |
| `critical_chain(unit)` | Show the critical chain of events for a unit |
| `security(unit)` | Show the security exposure score for a unit |
| `verify(unit)` | Check unit files for errors |
| `calendar(expression)` | Parse and describe a calendar expression |
| `timestamp(ts)` | Parse and normalize a timestamp string |
| `timespan(ts)` | Parse and normalize a timespan string |
| `plot(output_file)` | Write an SVG boot timeline to a file |
| `dot(output_file)` | Write a Graphviz dependency graph to a file |
| `dump()` | Dump the full systemd state as text |
| `unit_paths()` | List the search paths for unit files |
| `exit_status()` | List known process exit status definitions |

---

### time

System clock, timezone, and NTP configuration via `timedatectl` — 7 functions.

| Function | Description |
|---|---|
| `status()` | Show current time, timezone, and NTP status |
| `show()` | Show machine-readable time properties (JSON) |
| `set_time(time_str)` | Set the system clock (e.g. `"2025-01-15 10:00:00"`) |
| `set_timezone(tz)` | Set the system timezone (e.g. `"Europe/London"`) |
| `list_timezones()` | List all available timezone names |
| `set_ntp(enabled)` | Enable or disable NTP synchronisation |
| `timesync_status()` | Show the current NTP synchronisation status |

---

### hostname

Hostname and machine identity management via `hostnamectl` — 6 functions.

| Function | Description |
|---|---|
| `status()` | Show hostname and machine metadata |
| `show()` | Show machine-readable hostname properties (JSON) |
| `set_hostname(name, opts)` | Set the system hostname |
| `set_icon_name(name)` | Set the icon name for the machine |
| `set_chassis(type_name)` | Set the chassis type (e.g. `"server"`, `"laptop"`, `"vm"`) |
| `set_deployment(env)` | Set the deployment environment (e.g. `"production"`, `"staging"`) |

---

### locale

System locale and keymap configuration via `localectl` — 5 functions.

| Function | Description |
|---|---|
| `status()` | Show current locale and keymap settings |
| `set_locale(locale)` | Set the system locale (e.g. `"en_GB.UTF-8"`) |
| `list_locales()` | List all available locales |
| `set_keymap(keymap)` | Set the console keymap (e.g. `"uk"`) |
| `list_keymaps()` | List all available console keymaps |

---

### network

systemd-networkd link management via `networkctl` — 10 functions.

| Function | Description |
|---|---|
| `list(opts)` | List managed network links (JSON) |
| `status(iface)` | Show detailed status for a network link |
| `up(iface)` | Bring a network link up |
| `down(iface)` | Take a network link down |
| `reload()` | Reload network configuration |
| `reconfigure(iface)` | Reconfigure a specific link |
| `lldp()` | Show received LLDP neighbour information |
| `delete_iface(iface)` | Delete a virtual network link |
| `renew(iface)` | Renew the DHCP lease for a link |
| `forcerenew(iface)` | Force-renew the DHCP lease for a link |

---

### resolve

DNS resolution via `resolvectl` — 8 functions.

| Function | Description |
|---|---|
| `query(hostname, opts)` | Resolve a hostname or IP address |
| `status(iface)` | Show DNS configuration for a link or globally |
| `statistics()` | Show resolver cache statistics |
| `reset_statistics()` | Reset resolver statistics |
| `flush_caches()` | Flush the DNS resolver cache |
| `dns(iface, servers)` | Set DNS servers for a link |
| `domain(iface, domains)` | Set search domains for a link |
| `revert(iface)` | Revert DNS configuration for a link to its defaults |

---

### sessions

User session, seat, and linger management via `loginctl` — 12 functions.

| Function | Description |
|---|---|
| `list_sessions()` | List active login sessions (JSON) |
| `show_session(id)` | Show machine-readable properties for a session (JSON) |
| `terminate_session(id)` | Terminate a login session |
| `lock_session(id)` | Lock a session's screen |
| `unlock_session(id)` | Unlock a session's screen |
| `list_users()` | List logged-in users (JSON) |
| `show_user(uid)` | Show machine-readable properties for a user (JSON) |
| `terminate_user(uid)` | Terminate all sessions belonging to a user |
| `enable_linger(user)` | Enable linger for a user (services run after logout) |
| `disable_linger(user)` | Disable linger for a user |
| `list_seats()` | List available seats (JSON) |
| `show_seat(seat)` | Show machine-readable properties for a seat (JSON) |

---

### machine

systemd-nspawn container and image management via `machinectl` — 13 functions.

| Function | Description |
|---|---|
| `list()` | List running machines (JSON) |
| `status(name)` | Show status information for a machine |
| `show(name)` | Show machine-readable properties for a machine (JSON) |
| `start(name)` | Start a container registered as a machine |
| `stop(name)` | Terminate a running machine |
| `login(name)` | Open a login prompt on a running machine |
| `shell(name, cmd)` | Execute a shell command in a running machine |
| `enable(name)` | Enable a machine to start at boot |
| `disable(name)` | Disable a machine from starting at boot |
| `remove(name)` | Remove a machine image |
| `image_list()` | List available machine images (JSON) |
| `pull_tar(url, name)` | Download and register a `.tar` machine image |
| `pull_raw(url, name)` | Download and register a raw disk image |

---

### run

Transient unit management via `systemd-run` — 5 functions.

| Function | Description |
|---|---|
| `run_service(command, opts)` | Run a command as a transient service unit |
| `run_scope(command, opts)` | Run a command inside a transient scope unit |
| `run_timer(command, opts)` | Schedule a command via a transient timer unit |
| `run_oneshot(command, opts)` | Run a command as a one-shot service unit |
| `run_shell(command, opts)` | Run a shell command as a transient unit |

#### Common Options for `run_*`

| Option | Type | Description |
|---|---|---|
| `unit` | `string` | Name for the transient unit |
| `description` | `string` | Human-readable description |
| `on_calendar` | `string` | Calendar expression for timer scheduling |
| `on_boot_sec` | `string` | Timer offset from boot (e.g. `"5min"`) |
| `user` | `bool` | Create the unit in the user session |
| `slice` | `string` | Place the unit in a specific slice |
| `property` | `array` | Additional unit properties |
| `wait` | `bool` | Wait for the unit to finish before returning |
| `collect` | `bool` | Unload the unit after it stops |

---

### coredump

Core dump listing and inspection via `coredumpctl` — 5 functions.

| Function | Description |
|---|---|
| `list(opts)` | List recorded core dumps (JSON) |
| `info(id)` | Show detailed metadata for a core dump |
| `dump(id, output)` | Write a core dump to a file |
| `debug_dump(id)` | Open a core dump in the default debugger |
| `gdb(id)` | Open a core dump in GDB |

---

### common

Shared internal helpers used by all other modules — 9 functions.

| Function | Description |
|---|---|
| `exec(cmd)` | Run a shell command and return a standard result dict |
| `exec_json(cmd)` | Run a command and parse stdout as JSON |
| `exec_json_lines(cmd)` | Run a command and parse stdout as newline-delimited JSON |
| `build_args(base, opts)` | Construct a CLI argument list from a base command and options dict |
| `parse_properties(stdout)` | Parse `KEY=value` property output into a dict |
| `check_systemd()` | Assert that systemd is available on the current system |
| `scope_flag(user_mode)` | Return `--user` or `--system` based on a boolean |
| `parse_table(stdout)` | Parse tabular text output into an array of dicts |
| `format_filters(filter_dict)` | Convert a dict of journal field filters to CLI arguments |

> The `common` module is intended for internal use. You can import it directly if you need low-level control, but most scripts should work entirely through the higher-level modules.

---

## Return Values

All functions return a result dict. There are two shapes depending on whether the function returns structured data:

**Standard result** (from `exec`):
```stash
{
    ok: true,        // true if exitCode == 0
    stdout: "...",   // raw stdout as a string
    stderr: "...",   // raw stderr as a string
    exitCode: 0,     // integer process exit code
}
```

**JSON result** (from `exec_json` / `exec_json_lines`):
```stash
{
    ok: true,        // true if exitCode == 0 and JSON parsed successfully
    data: {...},     // parsed JSON — object, array, or array of objects
    stderr: "...",
    exitCode: 0,
}
```

Functions whose names suggest structured output (e.g. `show`, `list_units`, `logs_json`, `list_sessions`) return the JSON form. All others return the standard form.

---

## Options Convention

Options are passed as dicts. Keys use `snake_case` and are automatically converted to `--kebab-case` CLI flags:

```stash
// { no_block: true, kill_who: "all" }
// becomes: --no-block --kill-who all
```

| Value Type | CLI Conversion |
|---|---|
| `true` | `--flag` (bare flag, no value) |
| `false` / `null` | Skipped entirely |
| `string` / `int` | `--flag value` |
| `array` | Repeated: `--flag val1 --flag val2` |

---

## Examples

### Service Management

Enable and start a service, then check its properties:

```stash
import "lib/services.stash" as svc;

// Enable and start a service atomically
svc.enable("nginx.service", { now: true });

// Restart a user service
svc.restart("pipewire.service", { user: true });

// View machine-readable service properties
let props = svc.show("nginx.service");
if (props.ok) {
    io.println($"Main PID: {props.data.mainpid}");
    io.println($"State:    {props.data.activestate}");
    io.println($"Uptime:   {props.data.activationrealtimetimestamp}");
}

// List all failed units
let units = svc.list_units({ state: "failed" });
if (units.ok) {
    for (let u in units.data) {
        io.println($"FAILED: {u.unit} — {u.description}");
    }
}

// Reload systemd after dropping a new unit file
svc.daemon_reload();
svc.enable("my-app.service", { now: true });
```

### Log Queries

Query the journal with filters and structured output:

```stash
import "lib/journal.stash" as journal;

// Get the last 50 lines from the nginx access log
let logs = journal.logs({ unit: "nginx", lines: 50 });
io.println(logs.stdout);

// Get structured JSON logs since yesterday
let data = journal.logs_json({ unit: "sshd", since: "yesterday" });
if (data.ok) {
    for (let entry in data.data) {
        io.println($"{entry.__REALTIME_TIMESTAMP}  {entry.MESSAGE}");
    }
}

// Filter by priority — only errors and above
let errors = journal.logs({
    priority: "err",
    since: "-1h",
    no_pager: true,
});
io.println(errors.stdout);

// List all recorded boots
let boots = journal.list_boots();
if (boots.ok) {
    for (let b in boots.data) {
        io.println($"Boot {b.index}: {b.first_entry} — {b.last_entry}");
    }
}

// Reclaim disk space
journal.vacuum_size("500M");
journal.vacuum_time("90days");
```

### System Analysis

Profile boot performance and audit unit security:

```stash
import "lib/analyze.stash" as analyze;

// Show overall boot time split
let t = analyze.time();
io.println(t.stdout);

// Show the 10 slowest units to start
let blame = analyze.blame({ lines: 10 });
io.println(blame.stdout);

// Show the critical path for a specific unit
let chain = analyze.critical_chain("graphical.target");
io.println(chain.stdout);

// Check security exposure score for a service
let sec = analyze.security("nginx.service");
io.println(sec.stdout);

// Verify a unit file for syntax errors
let check = analyze.verify("my-app.service");
if (!check.ok) {
    io.println("Unit file has errors:");
    io.println(check.stderr);
}

// Parse a calendar expression to see when it will fire
let cal = analyze.calendar("Mon..Fri *-*-* 08:00:00");
io.println(cal.stdout);

// Generate an SVG boot timeline
analyze.plot("/tmp/boot-timeline.svg");
```

### Network and DNS

Inspect interfaces and resolve hostnames:

```stash
import "lib/network.stash" as net;
import "lib/resolve.stash" as dns;

// List all managed network links
let ifaces = net.list();
io.println(ifaces.stdout);

// Show detailed status for a specific interface
let eth = net.status("eth0");
io.println(eth.stdout);

// Renew the DHCP lease on an interface
net.renew("eth0");

// Resolve a hostname
let result = dns.query("example.com");
io.println(result.stdout);

// Resolve with a specific record type
let mx = dns.query("example.com", { type: "MX" });
io.println(mx.stdout);

// Override DNS servers for an interface
dns.dns("eth0", ["1.1.1.1", "1.0.0.1"]);

// Add search domains
dns.domain("eth0", ["corp.example.com", "example.com"]);

// Flush the resolver cache
dns.flush_caches();

// Show resolver statistics
let stats = dns.statistics();
io.println(stats.stdout);
```

### Transient Units

Run commands as transient systemd services and timers:

```stash
import "lib/run.stash" as run;

// Run a one-shot cleanup task and wait for it to finish
run.run_oneshot("/usr/bin/find /tmp -mtime +7 -delete", {
    unit: "tmp-cleanup",
    description: "Clean old temp files",
    wait: true,
    collect: true,
});

// Run a command as a background service with resource limits
run.run_service("/usr/local/bin/heavy-job.sh", {
    unit: "heavy-job",
    description: "CPU-intensive background task",
    property: ["CPUWeight=50", "MemoryMax=1G"],
});

// Schedule a recurring task with a transient timer
run.run_timer("/usr/local/bin/backup.sh", {
    on_calendar: "daily",
    unit: "daily-backup",
    description: "Daily backup job",
});

// Run a task every 5 minutes after boot
run.run_timer("/usr/local/bin/health-check.sh", {
    on_boot_sec: "5min",
    unit: "health-check",
});

// Run a user-session service
run.run_service("/usr/bin/syncthing", {
    user: true,
    unit: "syncthing-transient",
    description: "Syncthing (transient)",
});
```

### Time and Locale Configuration

Configure the system clock and regional settings:

```stash
import "lib/time.stash" as dt;
import "lib/locale.stash" as loc;
import "lib/hostname.stash" as host;

// Check current date/time and NTP status
let ts = dt.status();
io.println(ts.stdout);

// Set the timezone
dt.set_timezone("America/New_York");

// Enable NTP synchronisation
dt.set_ntp(true);

// Check NTP sync status
let ntp = dt.timesync_status();
io.println(ntp.stdout);

// Set the system locale
loc.set_locale("en_US.UTF-8");

// Set the console keymap
loc.set_keymap("us");

// Configure hostname and machine metadata
host.set_hostname("web-01.prod.example.com");
host.set_chassis("server");
host.set_deployment("production");
```

### Core Dump Inspection

List and debug recorded core dumps:

```stash
import "lib/coredump.stash" as coredump;

// List all recorded core dumps
let dumps = coredump.list();
if (dumps.ok) {
    for (let d in dumps.data) {
        io.println($"{d.timestamp}  {d.exe}  PID {d.pid}");
    }
}

// Show detailed info for the most recent dump
let info = coredump.info("0");
io.println(info.stdout);

// Extract a core dump to a file for offline analysis
coredump.dump("0", "/var/tmp/core.dump");

// Open the most recent core dump in GDB
coredump.gdb("0");
```

---

## Error Handling

Check the `ok` field before using the result:

```stash
let result = systemd.services.start("nginx.service");
if (!result.ok) {
    io.println("Failed to start nginx: " + str.trim(result.stderr));
    io.println("Exit code: " + conv.toStr(result.exitCode));
}
```

For unexpected failures, use Stash's `try`:

```stash
let result = try systemd.services.show("nonexistent.service");
if (result is Error) {
    io.println("Unexpected error: " + result.message);
}
```

`is_active`, `is_enabled`, and `is_failed` return plain booleans and do not need an `ok` check:

```stash
if (!systemd.services.is_active("postgresql.service")) {
    io.println("PostgreSQL is not running — starting it now");
    systemd.services.start("postgresql.service");
}
```

---

## Requirements

- Linux with **systemd** (systemd 232 or later recommended)
- CLI tools must be present on `PATH`:
  - `systemctl` — for the `services` module
  - `journalctl` — for the `journal` module
  - `systemd-analyze` — for the `analyze` module
  - `timedatectl` — for the `time` module
  - `hostnamectl` — for the `hostname` module
  - `localectl` — for the `locale` module
  - `networkctl` — for the `network` module (requires `systemd-networkd`)
  - `resolvectl` — for the `resolve` module (requires `systemd-resolved`)
  - `loginctl` — for the `sessions` module (requires `systemd-logind`)
  - `machinectl` — for the `machine` module (requires `systemd-machined`)
  - `systemd-run` — for the `run` module
  - `coredumpctl` — for the `coredump` module (requires `systemd-coredump`)
- **Stash** >= 1.0.0
- Root or `sudo` privileges may be required for system-scope operations; user-scope operations pass `--user` automatically when `opts.user` is `true`

## License

MIT
