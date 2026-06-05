# Concurrency Upgrade Plan — serial → full concurrent multi-client daemon

**Status:** Draft / future work (blocked on a language fix)
**Created:** 2026-06-05
**Context:** The shipped `lsp-warmd` daemon is **serial** — it serves one dispatcher connection at a
time (`while (true) { tcp.listen(...) }`). For the LSP tool's **short-lived per-call connections** this
is invisible (each connect→query→disconnect is sub-100ms; a parallel session's calls just interleave).
This doc is the plan to make it a **genuinely concurrent multi-client** daemon once the blocking language
constraint is lifted.

---

## Why it's serial today (the root constraint)

A single Stash process cannot service two connections simultaneously, for three compounding reasons —
all verified during the LSP warm-daemon dogfood (2026-06-05):

1. **`tcp.listen` is synchronous** — it runs the accept+handle loop inline and blocks; you can't also be
   reading another connection.
2. **`process.read` blocks on an empty-but-open pipe** (docs said "non-blocking"; it isn't — bug
   `.kanban/0-backlog/bugs/process-read-blocks-on-empty-pipe.md`). So you can't poll a server's stdout
   without committing to block on it.
3. **Task-VM isolation disconnects `Process` handles** — the obvious escape (`tcp.listenAsync` +
   `event.loop()` to accept concurrently) fails because the async handler runs in an isolated VM where
   the spawned server's `Process` handle is dead (`process.read` there returns null), and
   `task.run(async …)` currently crashes outright (bug `.kanban/0-backlog/bugs/task-run-async-lambda-crash.md`).

Net: the only shape that can bridge a socket to a subprocess is the **synchronous, single-main-VM,
request-driven** loop — which is inherently one-connection-at-a-time.

## Prerequisite (the unblock)

**Land a genuinely non-blocking process read.** Minimum: `process.read` (and `process.readBytes`) returns
immediately with `null`/empty when no data is buffered (fixing the filed bug). With non-blocking reads on
**both** the server pipes and the client sockets, a single main VM can round-robin over everything without
ever blocking — no tasks, no cross-VM handles needed, so the isolation constraint is sidestepped entirely.

> **Shipped 2026-06-05:** the byte-level companion `process.readBytes(handle) -> buffer` landed (raw bytes,
> no decode — fixed the UTF-8-split corruption and unblocked the *serial* stash path). But it is **blocking**,
> exactly like `process.read`, so it does **not** unblock concurrency by itself. The remaining prerequisite
> is the *non-blocking* read semantics (plus a non-blocking TCP accept/recv on the main VM).

> If instead the runtime later makes `Process` handles cross task-VM boundaries (or integrates subprocess
> I/O into `event.loop()`), an async-accept design also becomes viable. The non-blocking-read path is
> preferred — it keeps all state in one VM and needs no new concurrency semantics.

## Target design (single-VM non-blocking event loop)

Replace the serial accept loop with one cooperative event loop in the main VM:

```
state:
  clients      : list of { conn, recvBuf, routedServer }      // open dispatcher connections
  servers      : map (lang,root) -> { proc, sendBuf }         // warm LSP servers (unchanged registry)
  pending      : map daemonReqId -> { client, originalId }    // for response routing + id de-collision

loop (never blocks):
  1. non-blocking ACCEPT  -> append any new connection to clients
  2. for each client: non-blocking RECV -> parse frames ->
        - intercept initialize/shutdown/exit (reply locally, as today)
        - else route by textDocument.uri extension -> server;
          REWRITE the request id to a fresh unique daemonReqId, record pending[daemonReqId]
          = {client, originalId}; write frame to server.sendBuf
  3. for each server: non-blocking READ -> parse frames ->
        - response: look up pending[id] -> restore originalId -> write to that client; drop pending
        - server->client request (workspace/configuration…): daemon answers locally
        - notification: route to the owning client(s)
  4. flush queued writes (servers' stdin, clients' sockets)
  5. brief sleep if the whole tick was idle
```

### What changes vs. the serial daemon

| Concern | Serial (today) | Concurrent (target) |
| --- | --- | --- |
| Accept | blocking `tcp.listen`, one at a time | non-blocking accept into a client set |
| Client read | blocking per-connection loop | non-blocking poll across all clients |
| Server read | request-driven (read only after a forward) | continuous non-blocking poll of all servers |
| Request ids | pass-through (safe — one client) | **id-remap** (multiple clients' ids collide on one server) → `pending` table |
| Server→client notifications | effectively undeliverable (no idle read) | delivered (continuous server poll) |
| Registry / routing / intercept | **reused unchanged** | **reused unchanged** |

The **lsp-agnostic server registry, the content-routing-by-extension, and the
initialize/shutdown/exit interception are all carried over verbatim** — only the I/O scheduling layer is
rewritten. That's the whole reason to keep those concerns cleanly separated in the serial build.

### New risks to handle in the concurrent version

- **Id-remap correctness** — every forwarded request must get a unique daemon-side id and be restored on
  the way back; leaked `pending` entries = hangs. Add a TTL/cleanup.
- **Fairness / starvation** — round-robin must not let one chatty client starve others; cap frames
  processed per client per tick.
- **Per-connection partial-frame buffering** — each client needs its own `recvBuf` (the serial version
  has one); a frame split across recvs must not bleed between clients.
- **Backpressure** — if a server is slow, queue to its `sendBuf` rather than blocking the loop.

## Sequencing

1. ~~Add `process.readBytes`~~ ✅ **done 2026-06-05** (byte-exact; fixed the serial stash path). Remaining:
   make `process.read`/`readBytes` **non-blocking** (return null/empty on an idle pipe). *(language change; separate PR)*
2. Add a non-blocking TCP accept/recv path usable from the main VM (confirm `tcp.*` async recv works
   without an isolated handler, or add a main-VM poll variant).
3. Rewrite only the daemon's I/O scheduling layer per the loop above; keep registry/routing/intercept.
4. Re-verify: drive **two** clients concurrently (e.g. csharp + stash, and two csharp) and confirm both
   get correct interleaved responses with no id cross-talk — the test the serial version can't pass.

## Until then

The serial daemon is correct and sufficient for short per-call connections. Re-validate the
short-connection assumption at `.lsp.json` wire-up time with a **held-open multi-query** client test; if
the harness turns out to hold long-lived connections, this upgrade becomes load-bearing rather than
optional.
