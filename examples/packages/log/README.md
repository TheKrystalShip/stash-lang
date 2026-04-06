# @stash/log

Structured, level-filtered logging for [Stash](https://github.com/TheKrystalShip/stash-lang). Supports configurable log levels, custom format templates, and optional file output — all in pure Stash.

> **This package replaces the former built-in `log` namespace.** See the [migration guide](#migration-from-built-in-log) below.

## Installation

```bash
stash pkg install @stash/log
```

## Quick Start

```stash
import "@stash/log" as log;

log.logger.info("Server started");
log.logger.warn("Disk usage above 80%");
log.logger.error("Connection refused");
```

Output (to stderr by default):

```
[2026-04-06T12:00:00Z] [INFO] Server started
[2026-04-06T12:00:00Z] [WARN] Disk usage above 80%
[2026-04-06T12:00:00Z] [ERROR] Connection refused
```

## Usage

### Namespace-style import

```stash
import "@stash/log" as log;

// Set minimum level — only WARN and above will be emitted
log.logger.setLevel(log.types.LogLevel.WARN);

log.logger.debug("This is suppressed");
log.logger.info("This is also suppressed");
log.logger.warn("This gets through");
log.logger.error("So does this");
```

### Destructured import

```stash
import { info, warn, error, setLevel } from "@stash/log/lib/logger.stash";
import { LogLevel } from "@stash/log/lib/types.stash";

setLevel(LogLevel.DEBUG);
info("Verbose mode enabled");
```

### Custom format template

```stash
import "@stash/log" as log;

log.logger.setFormat("{level}: {msg}");
log.logger.info("Compact format");
// INFO: Compact format
```

Available placeholders:

| Placeholder | Description                         |
| ----------- | ----------------------------------- |
| `{time}`    | ISO 8601 timestamp (`time.iso()`)   |
| `{level}`   | Severity label (`DEBUG`, `INFO`, …) |
| `{msg}`     | The log message                     |

### Log to file

```stash
import "@stash/log" as log;

log.logger.toFile("/var/log/my-app.log");
log.logger.info("Logging to file now");

// Revert to stderr
log.logger.toFile(null);
log.logger.info("Back to stderr");
```

### Full example

```stash
import "@stash/log" as log;

fn main() {
    log.logger.setLevel(log.types.LogLevel.DEBUG);
    log.logger.setFormat("[{time}] [{level}] {msg}");

    log.logger.debug("Initialising application");
    log.logger.info("Listening on port 8080");

    let ok = false;
    if (!ok) {
        log.logger.warn("Feature flag disabled");
    }

    log.logger.error("Unhandled exception — shutting down");
}

main();
```

## API Reference

All functions are exported from `lib/logger.stash` and re-exported through `index.stash` as `logger.*`.

### Logging functions

| Function | Signature    | Description                    |
| -------- | ------------ | ------------------------------ |
| `debug`  | `debug(msg)` | Log a message at `DEBUG` level |
| `info`   | `info(msg)`  | Log a message at `INFO` level  |
| `warn`   | `warn(msg)`  | Log a message at `WARN` level  |
| `error`  | `error(msg)` | Log a message at `ERROR` level |

All message arguments are coerced to string via `conv.toStr()`.

### Configuration functions

| Function    | Signature                | Description                                                                                                               |
| ----------- | ------------------------ | ------------------------------------------------------------------------------------------------------------------------- |
| `setLevel`  | `setLevel(level)`        | Set the minimum log level. Messages below this level are silently discarded. Throws if `level` is not a `LogLevel` value. |
| `setFormat` | `setFormat(template)`    | Set the message format template. Supports `{time}`, `{level}`, `{msg}` placeholders.                                      |
| `toFile`    | `toFile(path)`           | Redirect log output to a file (append mode). Pass `null` to revert to stderr.                                             |
| `getLevel`  | `getLevel() -> LogLevel` | Returns the current minimum log level.                                                                                    |

## Types Reference

### `LogLevel` enum

Defined in `lib/types.stash`. Values listed from lowest to highest severity:

| Value            | Description                                             |
| ---------------- | ------------------------------------------------------- |
| `LogLevel.DEBUG` | Fine-grained diagnostic information                     |
| `LogLevel.INFO`  | General informational messages (default minimum)        |
| `LogLevel.WARN`  | Potentially harmful situations                          |
| `LogLevel.ERROR` | Error events that may allow the application to continue |
| `LogLevel.OFF`   | Disable all logging                                     |

### `LogEntry` struct

Defined in `lib/types.stash`. Represents a single log entry before formatting.

| Field       | Description               |
| ----------- | ------------------------- |
| `level`     | `LogLevel` value          |
| `message`   | The string message        |
| `timestamp` | ISO 8601 timestamp string |

## Migration from Built-in `log`

This package replaces the former built-in `log` namespace. The built-in functions map directly to the package equivalents:

### Before (stdlib)

```stash
log.debug("Starting up");
log.info("Listening on :8080");
log.warn("Retry attempt 3");
log.error("Fatal: connection lost");
log.setLevel(log.DEBUG);
```

### After (package)

```stash
import "@stash/log" as log;

log.logger.debug("Starting up");
log.logger.info("Listening on :8080");
log.logger.warn("Retry attempt 3");
log.logger.error("Fatal: connection lost");
log.logger.setLevel(log.types.LogLevel.DEBUG);
```

Or with destructured imports for a closer one-to-one mapping:

```stash
import { debug, info, warn, error, setLevel } from "@stash/log/lib/logger.stash";
import { LogLevel } from "@stash/log/lib/types.stash";

debug("Starting up");
info("Listening on :8080");
warn("Retry attempt 3");
error("Fatal: connection lost");
setLevel(LogLevel.DEBUG);
```

The package adds two new capabilities not available in the built-in namespace:

- `setFormat(template)` — custom message format templates
- `toFile(path)` — redirect output to a log file

## License

GPL-3.0-only — see [LICENSE](./LICENSE).
