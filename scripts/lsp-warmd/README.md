# lsp-warmd — LSP Warm-Daemon

A content-routing LSP daemon that pre-warms multiple language servers and bridges
them to Claude Code's LSP harness via a stateless per-call dispatcher. One daemon
process serves all registered language servers; routing is by file extension.

> ## Status (2026-06-05) — both languages working, NOT yet wired to `.lsp.json`
> - **csharp-ls AND stash-lsp: both work end-to-end.** One daemon serves both —
>   `dispatch.stash → daemon → content-route → {csharp-ls, stash-lsp}` returns full nav for
>   `.cs` and `.stash`. End-to-end test (`test_client.py`): cold run `.cs` 31 symbols + `.stash`
>   31 symbols (after ~17s warm); warm run both in ~1s. The architecture is validated.
> - **The stash-lsp fix shipped: `process.readBytes`.** Previously stash-lsp's responses for
>   non-ASCII files (e.g. `examples/interfaces.stash`, `·`/`─`/`—`) corrupted the daemon's frame
>   parser — `process.read` decodes stdout into *char* chunks, and that chunked decode→re-encode
>   isn't byte-faithful for non-ASCII (a multibyte seq split across chunks can decode lossily), so
>   the byte `Content-Length` never matched. The new `process.readBytes` (raw bytes, no decode)
>   keeps the accumulator byte-exact by construction; both server pumps now use it. See
>   `.kanban/0-backlog/bugs/process-read-blocks-on-empty-pipe.md`.
> - **Serial** (one connection at a time) — fine for short per-call connections. The concurrent
>   upgrade (`CONCURRENCY-PLAN.md`) is still gated on a *non-blocking* read (readBytes, like read,
>   still blocks on an idle pipe) plus the task-VM fixes — readBytes alone does NOT unblock it.
> - **Do NOT wire `.lsp.json` yet** — the daemon hardcodes this repo's root/solution
>   (per-project generalization is future work), and a parallel session needs the LSP tool.
> - Manage the daemon by PID (a `Stash` process running `daemon.stash`); don't global-`pkill`
>   `csharp-ls`/`stash-lsp` (kills other sessions' + VS Code's servers).

## Problem

Claude Code's LSP tool cold-spawns a fresh language server per call. For csharp-ls,
this means a ~15–25s Roslyn solution load on every call — making nav effectively
unusable. The warm-daemon fixes this by keeping one persistent, already-indexed
csharp-ls (and stash-lsp) process alive, serving requests to it in sub-100ms.

## Architecture

```
  Claude Code harness (LSP tool call)
       │  stdin/stdout LSP frames
       ▼
  dispatch.stash              ← stateless shim, one per call
  (detect-or-spawn + socat relay)
       │  TCP 127.0.0.1:8787
       ▼
  daemon.stash                ← single long-lived process, serial bridge
  (warm, registry, routing)
       │  textDocument.uri extension
       ├──(.cs)──► csharp-ls  ← workspace-indexed, no didOpen needed
       └──(.stash)► stash-lsp ← open-document, client sends didOpen
```

**N dispatchers → 1 daemon → N servers.** The dispatcher is generic (no language
argument); any future `.lsp.json` entry would point at the same `dispatch.stash`.
The daemon routes each request to the right backend by extracting the file extension
from `textDocument.uri`.

## Files

| File | Role |
|------|------|
| `daemon.stash` | Long-lived routing daemon — warm both servers, serve connections |
| `dispatch.stash` | Per-call dispatcher — detect-or-spawn daemon, then socat relay |
| `CONCURRENCY-PLAN.md` | Future concurrent upgrade design (serial → full multi-client) |

## Server registry

Defined in `daemon.stash` as `REGISTRY` — a list of server specs:

```stash
{
    "name":         "csharp",
    "extensions":   [".cs", ".csx"],
    "spawnCommand": "csharp-ls --solution Stash.sln",
    "warmupKind":   "poll-documentsymbol",
    "warmupUri":    "file:///…/Stash.Cli/Program.cs"
}
{
    "name":         "stash",
    "extensions":   [".stash"],
    "spawnCommand": "stash-lsp",
    "warmupKind":   "initialize-only",
    "warmupUri":    null
}
```

**warmupKind** controls the startup strategy:

- `"poll-documentsymbol"` — workspace-backed server (csharp-ls). Send `initialize`,
  poll `textDocument/documentSymbol` on a known `.cs` file until non-empty. This
  triggers the ~15–25s Roslyn solution load. No `didOpen` needed — csharp-ls indexes
  the whole workspace from disk and answers any file.

- `"initialize-only"` — open-document server (stash-lsp). `initialize` + `initialized`
  is sufficient; ready in ~0.5s. Clients send `didOpen` per document; the daemon
  forwards those transparently.

## Client session protocol (what the daemon intercepts)

| Method | Daemon action |
|--------|--------------|
| `initialize` | Reply **synthetic** InitializeResult (superset capabilities); do not forward |
| `initialized` | Swallow — backends already got one during warmup |
| `shutdown` | Reply `{id, result: null}`; keep backends alive |
| `exit` | Close client connection; keep backends alive; accept next client |
| anything else | Route by `textDocument.uri` extension → forward to matched backend |

The synthetic `InitializeResult` advertises a superset of capabilities covering both
backends, including `textDocumentSync: {openClose: true}` so LSP clients know to send
`didOpen`/`didChange` (required by stash-lsp). Note: stash-lsp itself reports a full
capability set; the synthetic result is a union for the two-backend case.

## Runtime artifacts (not in repo)

| Path | Content |
|------|---------|
| `/tmp/lsp_warmd.ready` | Written by daemon when warm; polled by dispatcher |
| `/tmp/lsp_warmd.log` | Daemon stdout/stderr (redirected by dispatcher's daemonize) |

## How to run

**Manually (testing):**
```bash
# Start daemon in foreground
stash scripts/lsp-warmd/daemon.stash [/path/to/project/root]
# Or via dispatch (auto-spawns daemon)
stash scripts/lsp-warmd/dispatch.stash
```

**Via the test client:**
```bash
python3 scripts/lsp-warmd/test_client.py
```

**Verifying warm behaviour:**
```bash
# First call: cold (dispatcher spawns+warms daemon, ~15–25s)
python3 scripts/lsp-warmd/test_client.py

# Second call: warm (dispatcher detects existing daemon, relays instantly)
python3 scripts/lsp-warmd/test_client.py
```

**Prerequisites:**
- `csharp-ls` on PATH (C# language server)
- `stash-lsp` on PATH (Stash language server, v0.5.0+)
- `socat` on PATH (bidirectional byte relay for dispatcher)
- Stash CLI built at `Stash.Cli/bin/Debug/net10.0/Stash`

## Limitations (current — serial)

The daemon serves **one dispatcher connection at a time**. For the LSP tool's
short-lived per-call connections (each connect → query → disconnect is sub-100ms)
this is invisible: calls interleave naturally between connections.

It is NOT safe if the harness holds a long-lived connection open and issues
concurrent requests from multiple threads. See **`CONCURRENCY-PLAN.md`** for the
full design of the concurrent upgrade — blocked on `process.read` becoming
genuinely non-blocking (filed bug).

Other known limitations (documented in source comments):
- `process.readBytes` (and `tcp.recv`) block on an idle source (gotcha filed — does
  not affect the bridge because we only read after forwarding a request, so the
  source always has pending data)
- Server stdout is byte-exact (`process.readBytes`); the client→daemon read still
  uses `tcp.recv` (string), so a multibyte LSP request body round-trips only as
  faithfully as the UTF-8 decode/re-encode (didn't bite in testing; a byte-exact
  client read is the follow-up)

## Do not wire `.lsp.json` yet

`dispatch.stash` is ready to be the `"command"` in Claude Code's `.lsp.json`, but
that step is deferred pending:
1. Validating held-connection behaviour with a real harness multi-query test
2. The concurrent upgrade (or confirmation that serial is sufficient in practice)

The dispatcher-as-stdio-shim has been verified end-to-end: spawning it as a
subprocess and speaking LSP over its stdin/stdout delivers nav results for both
`.cs` and `.stash` files through the one daemon process.
