# `process.read` blocks on empty pipe instead of returning null

**Status:** Backlog — Bug
**Created:** 2026-06-05
**Discovery context:** Surfaced during `/tmp/lsp_warmd.stash` authoring (increment 2 of the LSP warm-daemon dogfood). The daemon spawns csharp-ls, warms it via documentSymbol polling (during which `process.read` works correctly because csharp-ls is actively sending `$/progress` notifications), then tries to drain remaining output. After csharp-ls quiets down, `process.read(lsp)` blocks indefinitely instead of returning null.

---

## Problem

`process.read(handle)` is documented as "Non-blocking read from a process's stdout. Returns a string chunk if data is available, or **null if no data is ready**." In practice, when the child process's stdout pipe buffer has no pending data, `process.read` **blocks** waiting for the child to produce output, instead of returning null immediately.

The behavior appears when:
1. Spawn a child process
2. Read all pending output (drain the pipe buffer)
3. Call `process.read` again — the pipe is empty; it should return null immediately

The blocking behavior makes any poll-based non-blocking read loop impossible in the main VM (e.g., `while(chunk = process.read(p); chunk == null) { ... }` never executes the loop body).

Note: calling `process.read(p)` from inside a `task.run` returns immediately (because the cross-VM handle is invalid/disconnected), so the bug is specific to the spawning VM's main execution context.

## Reproduction

```stash
let p = process.spawn("bash -c 'echo READY; sleep 30'");
// Drain the READY line
let deadline = time.now() + 2.0;
while (time.now() < deadline) {
    let c = process.read(p);
    if (c != null && str.contains(c, "READY")) { break; }
    if (c == null) { time.sleep(0.05); }
}
// Pipe is now empty — should return null (docs say non-blocking)
// ACTUALLY: blocks for ~30s until bash finishes sleeping
let r = process.read(p);
io.println(r == null ? "null (correct)" : "got data: " + r);
process.kill(p);
```

The script above hangs at `process.read(p)` for the remainder of the `sleep 30`, instead of returning null immediately.

## Blast radius

High for any Stash code that needs to concurrently read from a spawned process while doing other work (e.g., serving TCP clients, polling multiple processes). The documented non-blocking semantics make it appear safe to poll, but it isn't.

Workaround: always ensure the child is actively producing output before calling `process.read`. For batch processing (spawn → wait for completion) use `process.wait` instead.

## Root cause

**Confirmed** (read `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs` → `Read`): the guard is
`if (stream.Peek() == -1) return Null;`, but `StreamReader.Peek()` on a redirected pipe is **not**
non-blocking. When its internal buffer is empty, `Peek()` calls the underlying stream's blocking `Read`,
which waits for data or EOF. So `Peek()` *itself* hangs until the child produces output or the pipe
closes (then returns -1 → null). The intended non-blocking guard never fires while the pipe is
open-but-idle — `Peek()` is the blocker, not the safeguard.

## Suggested fix

`StreamReader.Peek()` cannot be the non-blocking check — it is the thing that blocks. Use a genuinely
non-blocking path on the underlying pipe: read `entry.Process.StandardOutput.BaseStream` via `ReadAsync`
with an already-cancelled / zero-timeout `CancellationToken` (return null on cancellation), or poll the
OS pipe's available-byte count before reading. Preserve the data-available fast path (return the chunk
without blocking). A `process.readBytes(handle) -> buffer` variant built the same way would **also** fix
a separate, now-CONFIRMED corruption defect (below). Recommend shipping both: a non-blocking
`read`/`tryRead` and a byte-level `readBytes`.

### RESOLVED sibling defect — multibyte corruption in byte-framed protocols (fixed 2026-06-05)

`process.read` returns a **decoded string**. Round-tripping a process's stdout through a *chunked* string
decode and re-encode (`buf.from(process.read(...))`) is **not guaranteed byte-preserving** for non-ASCII
content: a multibyte UTF-8 sequence split across read-chunk boundaries can decode lossily (e.g. to a `U+FFFD`
replacement char), changing the byte count and desynchronizing a byte-length-framed protocol. (For a *faithful*
decode→re-encode of valid UTF-8 the byte count is preserved — so the defect is specifically the **chunk-boundary
decode hop**, not `buf.from` itself; do not "fix" `buf.from`.) Reproduced in the `scripts/lsp-warmd` LSP daemon:
csharp-ls (ASCII payloads) parsed fine, but stash-lsp's response for a **non-ASCII** file
(`examples/interfaces.stash` — `·`/`─`/`—`, 580 multibyte bytes) never completed a frame — a byte-trace showed
the accumulator climbing 5788→18076+ bytes while the byte-count `Content-Length` never matched, so the parser
waited forever. **The precise corruption point was not pinned**; what's certain is that the string decode hop is
not byte-faithful here and `readBytes` removes it. This blocked the daemon's stash path; csharp worked only
because it's incidentally ASCII.

**FIXED 2026-06-05 by shipping `process.readBytes(handle) -> buffer`** (raw bytes, no decode/re-encode) in
`Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs`. It reads the underlying pipe (`StandardOutput.BaseStream`) rather
than the decoding `StreamReader`, so the byte stream is byte-exact and `parseAllFrames` slices on true bytes.
Covered by a byte-exact multibyte regression test (`ProcessBuiltInsTests.ReadBytes_MultibyteUtf8Output_PreservesExactBytes`
asserts `─—·` → `e29480e28094c2b7`) and a worked example (`examples/process_readbytes.stash`). The daemon's
two server pumps (`pumpServer`, `pumpServerBridge`) now use `readBytes`; end-to-end `test_client.py` confirms
`.stash` documentSymbol returns 31 symbols (was 0). **Note: only the multibyte-corruption half of this bug is
fixed — `readBytes`, like `read`, still BLOCKS on an idle-but-open pipe (the enhancement tracked by the rest
of this file and the `ProcessRead_EmptyPipe_CurrentlyBlocks` gotcha remains open).**

**Doc corrected 2026-06-05** (commit pending): the `<summary>` on `ProcessBuiltIns.Read` — and the
regenerated `docs/Stash — Standard Library Reference.md` — now describe the **actual blocking behavior**,
so callers aren't misled by a "non-blocking" claim while this enhancement is pending. The
`ProcessRead_EmptyPipe_CurrentlyBlocks` gotcha test now tracks the blocking→non-blocking **enhancement**
(flips red when a real non-blocking read lands) rather than a doc/reality mismatch.

## Verification

```stash
let p = process.spawn("bash -c 'echo READY; sleep 30'");
// drain READY...
let r = process.read(p);
// r should be null immediately (< 1ms), not after 30s
```

## Related

- `Stash.Tests/Interpreting/GotchaTests.cs` — `ProcessRead_EmptyPipe_CurrentlyBlocks` (green = bug present, red = fixed)
- `.claude/agents/stash-author.gotchas.md` — `process-read-blocks-empty-pipe`
- Surfaced during: `/tmp/lsp_warmd.stash` LSP warm-daemon (workaround: no post-warmup drain; bridge loop only calls `process.read` after forwarding a client request, so csharp-ls always has pending output)
