# Changelog

All notable changes to the **Debug Agent Tools** extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — 2026-04-15

### Added

- 8 Language Model Tools for AI-assisted debugging:
  - `debug_startSession` — launch debug sessions with stop-on-entry, exception breakpoints, and environment variable support
  - `debug_setBreakpoints` — set/replace breakpoints with conditions, hit counts, and logpoints
  - `debug_removeBreakpoints` — remove agent-managed breakpoints per-file or globally
  - `debug_continue` — resume execution with blocking timeout
  - `debug_step` — step over, into, or out of functions (up to 20 steps per call)
  - `debug_getSnapshot` — read current debug state with configurable variable depth and stack depth
  - `debug_evaluate` — evaluate expressions in the current stack frame context
  - `debug_stopSession` — terminate session with output capture and summary
- Debug snapshot format with source context, call stack, locals, closures, and captured output
- Automatic debug type inference from file extensions (14 languages supported)
- Agent-managed breakpoint tracking (never interferes with user-set breakpoints)
- Debug adapter capability negotiation and graceful degradation
- Environment variable safety blocklist
- Session auto-timeout after 5 minutes of inactivity
- Single-session constraint with automatic cleanup

[0.1.0]: https://github.com/TheKrystalShip/stash-lang/releases/tag/debug-agent-tools-v0.1.0
