# @stash/ufw

Comprehensive UFW (Uncomplicated Firewall) wrapper for Stash — manage firewall rules, application profiles, default policies, and logging through a clean, structured API.

## Installation

```sh
stash pkg install @stash/ufw
```

Or add to your `stash.json`:

```json
{
  "dependencies": {
    "@stash/ufw": "^1.0.0"
  }
}
```

## Quick Start

```stash
import "@stash/ufw" as ufw;
import { UfwRule } from "@stash/ufw/lib/types.stash";

// Check if ufw is installed
if (!ufw.common.check_ufw()) {
    io.println("ufw is not installed");
    return;
}

// Enable the firewall
ufw.config.enable();

// Set default policies
ufw.config.set_default("deny", "incoming");
ufw.config.set_default("allow", "outgoing");

// Allow SSH and HTTP using string rules
ufw.rules.allow("22/tcp");
ufw.rules.allow("80/tcp");
ufw.rules.allow("443/tcp");

// Allow from a specific subnet using a UfwRule struct
ufw.rules.allow(UfwRule {
    port: 22, proto: "tcp", from: "10.0.0.0/8", to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: "Internal SSH"
});

// Check status
let status = ufw.status.show(true);
io.println(status.stdout);

// List parsed rules
let result = ufw.status.list_rules();
if (result.ok) {
    for (let rule in result.rules) {
        io.println($"[{rule.number}] {rule.to} {rule.action} {rule.from}");
    }
}
```

## Types

All structured data uses explicit structs defined in `lib/types.stash`:

```stash
import { UfwRule, UfwResult, UfwRuleEntry, UfwRuleList, UfwDefaults,
         UfwDefaultsResult, UfwLoggingResult, UfwAppInfo, UfwAppInfoResult,
         UfwAppListResult, UfwLinesResult } from "@stash/ufw/lib/types.stash";
```

### Rule Specification

```stash
/// Describes a UFW firewall rule. Fields set to null are omitted.
struct UfwRule {
    port,       // int, string, or array — destination port(s) or service name
    proto,      // string — "tcp", "udp", or null
    from,       // string — source address/subnet, or null (defaults to "any")
    to,         // string — destination address/subnet, or null (defaults to "any")
    port_from,  // int or string — source port, or null
    port_to,    // int or string — destination port override, or null
    direction,  // string — "in" or "out", or null
    on,         // string — interface name (e.g. "eth0"), or null
    comment     // string — rule description, or null
}
```

### Command Results

```stash
struct UfwResult { ok, stdout, stderr, exitCode }
struct UfwLinesResult { ok, lines, stderr, exitCode }
struct UfwRuleList { ok, rules, stderr, exitCode }
struct UfwDefaultsResult { ok, data, stderr, exitCode }
struct UfwLoggingResult { ok, level, stderr, exitCode }
struct UfwAppInfoResult { ok, data, stderr, exitCode }
struct UfwAppListResult { ok, apps, stderr, exitCode }
```

### Parsed Data

```stash
struct UfwRuleEntry { number, to, action, from }
struct UfwDefaults { incoming, outgoing, routed }
struct UfwAppInfo { profile, title, description, ports }
```

## Modules

### `common` — Shared Helpers

| Function                       | Description                                             |
| ------------------------------ | ------------------------------------------------------- |
| `check_ufw()`                  | Check if ufw is installed and available in PATH         |
| `exec(args)`                   | Run a ufw command, returns UfwResult                    |
| `exec_lines(args)`             | Run a command, returns UfwLinesResult with parsed lines |
| `build_rule(rule)`             | Build a UFW rule string from a UfwRule struct or string |
| `build_args(base, opts, spec)` | Build CLI argument string from options and spec         |
| `parse_lines(stdout)`          | Parse stdout into array of non-empty lines              |

#### Rule Builder

The `build_rule()` function converts a `UfwRule` struct into a UFW rule string:

```stash
import { UfwRule } from "@stash/ufw/lib/types.stash";

// Simple port rule
common.build_rule(UfwRule {
    port: 22, proto: "tcp", from: null, to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: null
});
// → "from any to any port 22 proto tcp"

// From a specific source with comment
common.build_rule(UfwRule {
    port: 22, proto: "tcp", from: "192.168.1.0/24", to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: "Allow SSH from LAN"
});
// → "from 192.168.1.0/24 to any port 22 proto tcp comment 'Allow SSH from LAN'"

// With direction and interface
common.build_rule(UfwRule {
    port: 80, proto: "tcp", from: null, to: null,
    port_from: null, port_to: null, direction: "in", on: "eth0", comment: null
});
// → "in on eth0 from any to any port 80 proto tcp"

// Multiple ports
common.build_rule(UfwRule {
    port: [80, 443], proto: "tcp", from: null, to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: null
});
// → "from any to any port 80,443 proto tcp"

// Raw string passthrough (no struct needed for simple rules)
common.build_rule("22/tcp");
// → "22/tcp"
```

### `rules` — Rule Management

| Function                       | Description                                             |
| ------------------------------ | ------------------------------------------------------- |
| `allow(rule, number?)`         | Add an allow rule                                       |
| `deny(rule, number?)`          | Add a deny rule                                         |
| `reject(rule, number?)`        | Add a reject rule                                       |
| `limit(rule, number?)`         | Add a rate-limiting rule (max 6 connections/30s)        |
| `delete(rule)`                 | Delete a rule by number (int) or specification (string) |
| `insert(number, action, rule)` | Insert a rule at a specific position                    |
| `prepend(action, rule)`        | Prepend a rule (add at the top)                         |
| `route(action, rule)`          | Add a forwarding (route) rule                           |
| `delete_route(action, rule)`   | Delete a forwarding (route) rule                        |

All rule functions accept either a **string** (raw rule like `"22/tcp"`) or a **UfwRule** struct. The optional `number` parameter on allow/deny/reject/limit inserts the rule at a specific position.

```stash
import { UfwRule } from "@stash/ufw/lib/types.stash";

// String rules (simple syntax)
rules.allow("22/tcp");
rules.deny("telnet");
rules.reject("25/tcp");
rules.limit("ssh");

// Struct rules (full control)
rules.allow(UfwRule {
    port: 80, proto: "tcp", from: null, to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: null
});

rules.deny(UfwRule {
    port: 22, proto: null, from: "10.0.0.5", to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: "Block attacker"
});

rules.allow(UfwRule {
    port: [80, 443], proto: "tcp", from: "192.168.1.0/24", to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: "Web access"
});

// Insert at position
rules.allow("3306/tcp", 3);

// Delete rules
rules.delete(1);                    // by rule number
rules.delete("allow 22/tcp");       // by specification

// Route rules (forwarding)
rules.route("allow", UfwRule {
    port: null, proto: null, from: "10.0.0.0/8", to: "192.168.1.0/24",
    port_from: null, port_to: null, direction: null, on: null, comment: null
});

// Prepend a rule
rules.prepend("deny", UfwRule {
    port: null, proto: null, from: "10.0.0.5", to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: null
});
```

### `status` — Status Queries

| Function                    | Description                                                                         |
| --------------------------- | ----------------------------------------------------------------------------------- |
| `show(verbose?, numbered?)` | Show firewall status, returns UfwResult. If both are true, verbose takes precedence |
| `verbose()`                 | Show verbose status (includes defaults and logging)                                 |
| `numbered()`                | Show numbered status (rules with position numbers)                                  |
| `is_active()`               | Check if the firewall is active (returns bool)                                      |
| `list_rules()`              | Parse rules into UfwRuleEntry structs, returns UfwRuleList                          |
| `defaults()`                | Parse default policies, returns UfwDefaultsResult                                   |
| `logging_level()`           | Parse the logging level, returns UfwLoggingResult                                   |

```stash
// Check if active
if (status.is_active()) {
    io.println("Firewall is active");
}

// Get parsed rules (each rule is a UfwRuleEntry struct)
let result = status.list_rules();
for (let rule in result.rules) {
    io.println($"Rule #{rule.number}: {rule.action} {rule.to} from {rule.from}");
}

// Get default policies (returns UfwDefaults struct in data field)
let defs = status.defaults();
if (defs.ok) {
    io.println($"Incoming: {defs.data.incoming}");
    io.println($"Outgoing: {defs.data.outgoing}");
    io.println($"Routed: {defs.data.routed}");
}

// Get logging level
let log = status.logging_level();
if (log.ok) {
    io.println($"Logging: {log.level}");
}
```

### `app` — Application Profiles

| Function                 | Description                                       |
| ------------------------ | ------------------------------------------------- |
| `list()`                 | List available profiles, returns UfwAppListResult |
| `info(name)`             | Get profile details, returns UfwAppInfoResult     |
| `default_policy(policy)` | Set default app policy, returns UfwResult         |
| `update(name)`           | Update a profile, returns UfwResult               |

```stash
// List profiles (apps is an array of strings)
let result = app.list();
if (result.ok) {
    for (let name in result.apps) {
        io.println(name);
    }
}

// Get profile info (data is a UfwAppInfo struct)
let info = app.info("Nginx Full");
if (info.ok) {
    io.println($"Profile: {info.data.profile}");
    io.println($"Title: {info.data.title}");
    io.println($"Ports: {arr.join(info.data.ports, ", ")}");
}

// Allow by application profile name (string, no struct needed)
rules.allow("Nginx Full");
rules.allow("OpenSSH");
```

### `config` — Firewall Configuration

| Function                          | Description                                                 |
| --------------------------------- | ----------------------------------------------------------- |
| `enable()`                        | Enable the firewall (skips confirmation), returns UfwResult |
| `disable()`                       | Disable the firewall, returns UfwResult                     |
| `reload()`                        | Reload the firewall rules, returns UfwResult                |
| `reset()`                         | Reset to defaults (skips confirmation), returns UfwResult   |
| `set_default(policy, direction?)` | Set default policy for a direction, returns UfwResult       |
| `set_logging(level)`              | Set logging level, returns UfwResult                        |

```stash
// Enable with default deny incoming
config.enable();
config.set_default("deny", "incoming");
config.set_default("allow", "outgoing");

// Set logging
config.set_logging("low");

// Reload after changes
config.reload();

// Reset everything (destructive!)
config.reset();
```

## Common Patterns

### Server Setup Script

```stash
import "@stash/ufw" as ufw;
import { UfwRule } from "@stash/ufw/lib/types.stash";

// Reset and configure from scratch
ufw.config.reset();
ufw.config.set_default("deny", "incoming");
ufw.config.set_default("allow", "outgoing");

// Essential services
ufw.rules.allow("ssh");
ufw.rules.allow(UfwRule {
    port: [80, 443], proto: "tcp", from: null, to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: "HTTP/HTTPS"
});

// Allow monitoring from internal network
ufw.rules.allow(UfwRule {
    port: 9090, proto: "tcp", from: "10.0.0.0/8", to: null,
    port_from: null, port_to: null, direction: null, on: null, comment: "Prometheus"
});

// Rate limit SSH to prevent brute force
ufw.rules.delete("allow ssh");
ufw.rules.limit("ssh");

// Enable logging and activate
ufw.config.set_logging("low");
ufw.config.enable();

// Verify
let result = ufw.status.show(true);
io.println(result.stdout);
```

### Batch Rule Management

```stash
import "@stash/ufw" as ufw;
import { UfwRule } from "@stash/ufw/lib/types.stash";

let allowed_ports = [
    UfwRule { port: 22, proto: "tcp", from: null, to: null, port_from: null, port_to: null, direction: null, on: null, comment: "SSH" },
    UfwRule { port: 80, proto: "tcp", from: null, to: null, port_from: null, port_to: null, direction: null, on: null, comment: "HTTP" },
    UfwRule { port: 443, proto: "tcp", from: null, to: null, port_from: null, port_to: null, direction: null, on: null, comment: "HTTPS" },
    UfwRule { port: 53, proto: "udp", from: null, to: null, port_from: null, port_to: null, direction: null, on: null, comment: "DNS" }
];

for (let rule in allowed_ports) {
    let r = ufw.rules.allow(rule);
    if (r.ok) {
        io.println($"Allowed port {rule.port}/{rule.proto}");
    } else {
        io.eprintln($"Failed to allow port {rule.port}: {r.stderr}");
    }
}
```

## Dependencies

- [`@stash/cli`](../cli/) — Shared CLI wrapper toolkit (exec, flags, parsing, tool detection)

## Notes

- UFW must be installed on the system (`apt install ufw` on Debian/Ubuntu)
- Use the `elevate` block when running commands that require root privileges
- `config.enable()` and `config.reset()` use `--force` to skip interactive confirmation prompts
- Rule numbers (from `status.list_rules()`) can change after insertions or deletions — re-query after modifications
- All structured data uses explicit structs — import from `lib/types.stash`
