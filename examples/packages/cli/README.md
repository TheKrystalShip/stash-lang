# @stash/cli

Shared CLI wrapper toolkit for Stash — provides declarative flag specs, exec helpers, output parsers, and tool detection.

**Purpose:** Eliminate boilerplate across CLI wrapper packages (`@stash/docker`, `@stash/podman`, `@stash/systemd`, etc.) by providing a shared foundation.

## Installation

```bash
stash pkg install @stash/cli
```

## Quick Start

```stash
import { exec, exec_json } from "@stash/cli/lib/exec.stash";
import { build_args, map_flags } from "@stash/cli/lib/flags.stash";
import { parse_table, parse_properties } from "@stash/cli/lib/parse.stash";
import { check_tool, require_tool } from "@stash/cli/lib/tools.stash";

// Run a command
let result = exec("docker", "ps -a");
if (result.ok) {
    io.println(result.stdout);
}

// Build flags from options using a spec
let spec = {
    name:   { flag: "--name" },
    detach: { flag: "-d", type: "bool" },
    env:    { flag: "-e", type: "map" },
    ports:  { flag: "-p", type: "list" }
};

let args = build_args("run", {
    name: "myapp",
    detach: true,
    env: { NODE_ENV: "production" },
    ports: ["8080:80"]
}, spec);
// → "run --name myapp -d -e NODE_ENV=production -p 8080:80"

exec("docker", args);
```

## Modules

### exec — Command Execution

Generic tool-agnostic functions for running CLI commands.

```stash
import { exec, exec_json, exec_json_lines, exec_lines, exec_table } from "@stash/cli/lib/exec.stash";
```

| Function | Returns | Description |
|---|---|---|
| `exec(tool, args)` | `{ok, stdout, stderr, exitCode}` | Run a command |
| `exec_json(tool, args)` | `{ok, data, stderr, exitCode}` | Run + parse JSON |
| `exec_json_lines(tool, args)` | `{ok, data[], stderr, exitCode}` | Run + parse NDJSON |
| `exec_lines(tool, args)` | `{ok, lines[], stderr, exitCode}` | Run + split into lines |
| `exec_table(tool, args)` | `{ok, rows[], stderr, exitCode}` | Run + parse table output |

```stash
// Get docker containers as JSON
let result = exec_json("docker", "inspect mycontainer");
if (result.ok) {
    io.println(result.data);
}

// List files as lines
let files = exec_lines("ls", "-1 /tmp");
for (let line in files.lines) {
    io.println(line);
}
```

### flags — Declarative Flag Specs

The core innovation. Replace repetitive `dict.has`/`dict.set` boilerplate with declarative flag specifications.

```stash
import { build_args, map_flags, with_defaults } from "@stash/cli/lib/flags.stash";
```

| Function | Returns | Description |
|---|---|---|
| `build_args(base, opts, spec)` | `string` | Build complete argument string |
| `map_flags(opts, spec)` | `array` | Map options to flag strings (composable) |
| `with_defaults(defaults, opts)` | `dict` | Merge defaults with user options |

#### Flag Spec Types

| Type | Behavior | Example Input | Output |
|---|---|---|---|
| `"value"` (default) | `--flag value` | `network: "bridge"` | `--network bridge` |
| `"bool"` | flag present or omitted | `detach: true` | `-d` |
| `"list"` | repeated `--flag item` | `ports: ["80:80", "443:443"]` | `-p 80:80 -p 443:443` |
| `"map"` | repeated `--flag key=val` | `env: {A: "1"}` | `-e A=1` |
| `"csv"` | single `--flag a,b,c` | `caps: ["NET_ADMIN"]` | `--cap-add NET_ADMIN` |

#### Before vs After

**Before** (55 lines per function):
```stash
fn run(image, opts = {}) {
    let parts = ["run"];
    if (dict.has(opts, "name") && !(opts.name is null)) {
        arr.push(parts, $"--name {opts.name}");
    }
    if (dict.has(opts, "detach") && opts.detach) {
        arr.push(parts, "-d");
    }
    if (dict.has(opts, "rm") && opts.rm) {
        arr.push(parts, "--rm");
    }
    // ... 12 more blocks ...
    if (dict.has(opts, "env") && !(opts.env is null)) {
        let env_flags = format_env(opts.env);
        for (let flag in env_flags) { arr.push(parts, flag); }
    }
    // ... ports, volumes, labels ...
    arr.push(parts, image);
    return exec(arr.join(parts, " "));
}
```

**After** (spec + 3 lines):
```stash
let run_spec = {
    name:        { flag: "--name" },
    detach:      { flag: "-d",          type: "bool" },
    rm:          { flag: "--rm",        type: "bool" },
    interactive: { flag: "-i",          type: "bool" },
    tty:         { flag: "-t",          type: "bool" },
    network:     { flag: "--network" },
    restart:     { flag: "--restart" },
    workdir:     { flag: "--workdir" },
    user:        { flag: "--user" },
    hostname:    { flag: "--hostname" },
    entrypoint:  { flag: "--entrypoint" },
    memory:      { flag: "--memory" },
    cpus:        { flag: "--cpus" },
    platform:    { flag: "--platform" },
    pull:        { flag: "--pull" },
    env:         { flag: "-e",          type: "map" },
    ports:       { flag: "-p",          type: "list" },
    volumes:     { flag: "-v",          type: "list" },
    labels:      { flag: "--label",     type: "map" }
};

fn run(image, opts = {}) {
    let args = build_args("run", opts, run_spec);
    return exec("docker", args + " " + image);
}
```

#### Auto-Detection Mode

Options without spec entries are auto-detected:
- Single-char keys → short flag (`-k`), multi-char → long flag (`--key-name`)
- Underscores converted to hyphens (`no_pager` → `--no-pager`)
- Bools → flag only, Arrays → repeated, Scalars → `--flag value`

```stash
// No spec needed for simple cases
let args = build_args("ps", { all: true, format: "json" });
// → "ps --all --format json"
```

#### Composable Flag Building

Use `map_flags` when you need to insert positional args between flag groups:

```stash
let flags = map_flags(opts, spec);
let parts = ["run"];
for (let f in flags) {
    arr.push(parts, f);
}
arr.push(parts, image);  // positional arg after flags
let cmd = arr.join(parts, " ");
```

### parse — Output Parsers

Generic parsers for common CLI output formats.

```stash
import { parse_table, parse_properties, parse_lines, format_pairs, join_pairs } from "@stash/cli/lib/parse.stash";
```

| Function | Returns | Description |
|---|---|---|
| `parse_table(stdout)` | `array` of dicts | Parse header + whitespace-separated rows |
| `parse_properties(stdout, delim)` | `dict` | Parse KEY=VALUE output (default `=`) |
| `parse_lines(stdout)` | `array` | Split into trimmed non-empty lines |
| `format_pairs(dict, sep)` | `array` | Dict → `["K=V", ...]` |
| `join_pairs(dict, sep)` | `string` | Dict → `"K=V K2=V2"` |

```stash
// Parse systemctl show output
let props = parse_properties("ActiveState=active\nLoadState=loaded");
// → {activestate: "active", loadstate: "loaded"}

// Parse table output
let rows = parse_table("NAME   STATUS\nnginx  Running\nredis  Stopped");
// → [{name: "nginx", status: "Running"}, {name: "redis", status: "Stopped"}]

// Format filter expressions for journalctl
let filters = join_pairs({ _SYSTEMD_UNIT: "nginx.service", PRIORITY: "3" });
// → "_SYSTEMD_UNIT=nginx.service PRIORITY=3"
```

### tools — Tool Detection

Functions for checking if CLI tools are installed.

```stash
import { check_tool, tool_version, tool_version_number, require_tool, check_tools } from "@stash/cli/lib/tools.stash";
```

| Function | Returns | Description |
|---|---|---|
| `check_tool(name)` | `bool` | Check if tool is in PATH |
| `tool_version(name, flag)` | `string` or `null` | Get full version string |
| `tool_version_number(name)` | `string` or `null` | Extract just the version number |
| `require_tool(name)` | `{ok, error}` | Structured availability check |
| `check_tools(names)` | `dict` | Check multiple tools at once |

```stash
// Guard a wrapper module
let check = require_tool("docker");
if (!check.ok) {
    io.println(check.error);
    exit(1);
}

// Get version number
let ver = tool_version_number("docker");
// → "24.0.7" (extracted from "Docker version 24.0.7, build afdd53b")

// Check multiple tools
let available = check_tools(["docker", "kubectl", "helm"]);
// → {docker: true, kubectl: true, helm: false}
```

## Building a Wrapper Package

Here's how to use `@stash/cli` to build a new CLI wrapper:

```stash
// mypackage/lib/common.stash
import { exec, exec_json } from "@stash/cli/lib/exec.stash";
import { build_args } from "@stash/cli/lib/flags.stash";
import { check_tool } from "@stash/cli/lib/tools.stash";

let TOOL = "mytool";

fn run(args) {
    return exec(TOOL, args);
}

fn run_json(args) {
    return exec_json(TOOL, args);
}

fn run_with_opts(base, opts, spec) {
    let args = build_args(base, opts, spec);
    return exec(TOOL, args);
}

fn check() {
    return check_tool(TOOL);
}
```

```stash
// mypackage/lib/containers.stash
import { run_with_opts } from "common.stash";

let start_spec = {
    name:    { flag: "--name" },
    detach:  { flag: "-d",    type: "bool" },
    env:     { flag: "-e",    type: "map" },
    ports:   { flag: "-p",    type: "list" },
    volumes: { flag: "-v",    type: "list" }
};

fn start(image, opts = {}) {
    return run_with_opts("start " + image, opts, start_spec);
}
```

## API Summary

| Module | Functions |
|---|---|
| `exec` | `exec`, `exec_json`, `exec_json_lines`, `exec_lines`, `exec_table` |
| `flags` | `build_args`, `map_flags`, `apply_spec`, `auto_flag`, `with_defaults` |
| `parse` | `parse_table`, `parse_properties`, `parse_lines`, `format_pairs`, `join_pairs` |
| `tools` | `check_tool`, `tool_version`, `tool_version_number`, `require_tool`, `check_tools` |

## License

MIT
