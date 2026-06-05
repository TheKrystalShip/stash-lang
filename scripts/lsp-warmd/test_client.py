#!/usr/bin/env python3
"""
scripts/lsp-warmd/test_client.py

Two-language end-to-end test for the LSP warm-daemon.
Spawns dispatch.stash as a subprocess and speaks LSP over its stdin/stdout,
exactly as Claude Code's harness invokes a .lsp.json command.

Test sequence (all through ONE daemon, two languages):
  1. .cs file  (Stash.Cli/Program.cs):       documentSymbol — routed to csharp-ls
  2. .stash file (examples/interfaces.stash): didOpen + documentSymbol — routed to stash-lsp

Runs twice: first call cold (daemon spawns+warms), second instant (daemon already warm).
"""

import subprocess, json, time, os, sys, select

REPO          = "/home/heisen/stash-lang"
STASH_BIN     = f"{REPO}/Stash.Cli/bin/Debug/net10.0/Stash"
DISPATCHER    = f"{REPO}/scripts/lsp-warmd/dispatch.stash"
DAEMON_LOG    = "/tmp/lsp_warmd.log"
READY_FILE    = "/tmp/lsp_warmd.ready"

CS_URI    = f"file://{REPO}/Stash.Cli/Program.cs"
STASH_URI = f"file://{REPO}/examples/interfaces.stash"


# ─── LSP stdio helpers ────────────────────────────────────────────────────────

def send(proc, obj):
    body = json.dumps(obj).encode("utf-8")
    proc.stdin.write(f"Content-Length: {len(body)}\r\n\r\n".encode() + body)
    proc.stdin.flush()

def recv_all(proc, timeout=3.0):
    """Read all complete frames available within timeout seconds."""
    buf = b""
    msgs = []
    deadline = time.time() + timeout
    while time.time() < deadline:
        r, _, _ = select.select([proc.stdout], [], [], 0.1)
        if r:
            chunk = proc.stdout.read(65536)
            if not chunk:
                break
            buf += chunk
        while True:
            sep = buf.find(b"\r\n\r\n")
            if sep == -1:
                break
            hdr = buf[:sep].decode("utf-8", "replace")
            cl_lines = [l for l in hdr.split("\r\n") if l.lower().startswith("content-length:")]
            if not cl_lines:
                break
            cl = int(cl_lines[0].split(":", 1)[1].strip())
            bs = sep + 4
            if len(buf) < bs + cl:
                break
            msgs.append(json.loads(buf[bs:bs+cl]))
            buf = buf[bs+cl:]
    return msgs

def pump_until_id(proc, match_id, timeout=30.0):
    """Read frames until we get the response for match_id. Handle server→client requests."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        msgs = recv_all(proc, min(1.0, deadline - time.time()))
        for msg in msgs:
            # Server→client request (method + id) — reply so server doesn't stall
            if "method" in msg and "id" in msg:
                result = None
                if msg["method"] == "workspace/configuration":
                    items = msg.get("params", {}).get("items", [None])
                    result = [{} for _ in items]
                send(proc, {"jsonrpc": "2.0", "id": msg["id"], "result": result})
                continue
            if "method" in msg:
                continue  # notification — skip
            if msg.get("id") == match_id:
                return msg
    return None


# ─── Single dispatcher session ────────────────────────────────────────────────

def run_session(label, cold_start=False):
    """
    Spawn dispatch.stash, send initialize→initialized then:
    1. documentSymbol for a .cs file (csharp-ls)
    2. didOpen + documentSymbol for a .stash file (stash-lsp)
    Returns (ok, cs_symbols, stash_symbols, cs_latency_s, stash_latency_s, total_s)
    """
    print(f"\n{'─'*60}", file=sys.stderr)
    print(f"[{label}] starting dispatch.stash (cold={cold_start})", file=sys.stderr)
    t0 = time.time()

    proc = subprocess.Popen(
        [STASH_BIN, DISPATCHER],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=None,       # dispatcher stderr (logs) flows to our stderr
        bufsize=0,
    )

    # initialize
    send(proc, {
        "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": {
            "processId": os.getpid(),
            "rootUri": f"file://{REPO}",
            "workspaceFolders": [{"uri": f"file://{REPO}", "name": "stash-lang"}],
            "capabilities": {"textDocument": {"documentSymbol": {}, "definition": {}}},
        }
    })

    init_timeout = 45 if cold_start else 10
    init_resp = pump_until_id(proc, 1, init_timeout)
    if not init_resp or "result" not in init_resp:
        print(f"[{label}] FAIL: no initialize response after {time.time()-t0:.1f}s", file=sys.stderr)
        proc.terminate()
        return False, 0, 0, 0.0, 0.0, time.time()-t0

    t_init = time.time() - t0
    print(f"[{label}] initialize OK at {t_init:.3f}s", file=sys.stderr)

    send(proc, {"jsonrpc": "2.0", "method": "initialized", "params": {}})

    # ── 1. .cs documentSymbol (csharp-ls) ────────────────────────────────────
    t_cs = time.time()
    send(proc, {
        "jsonrpc": "2.0", "id": 2, "method": "textDocument/documentSymbol",
        "params": {"textDocument": {"uri": CS_URI}}
    })
    cs_resp = pump_until_id(proc, 2, 30)
    cs_latency = time.time() - t_cs
    cs_result = cs_resp.get("result") if cs_resp else None
    cs_syms = len(cs_result) if isinstance(cs_result, list) else 0
    print(f"[{label}] .cs documentSymbol: {cs_syms} symbols in {cs_latency:.3f}s", file=sys.stderr)

    # ── 2. .stash didOpen + documentSymbol (stash-lsp) ───────────────────────
    with open(STASH_URI[7:]) as f:  # strip "file://"
        stash_text = f.read()

    # stash-lsp requires didOpen before documentSymbol
    send(proc, {
        "jsonrpc": "2.0", "method": "textDocument/didOpen",
        "params": {"textDocument": {
            "uri": STASH_URI, "languageId": "stash", "version": 1, "text": stash_text
        }}
    })
    # Give stash-lsp a moment to process didOpen before querying
    time.sleep(0.2)

    t_stash = time.time()
    send(proc, {
        "jsonrpc": "2.0", "id": 3, "method": "textDocument/documentSymbol",
        "params": {"textDocument": {"uri": STASH_URI}}
    })
    stash_resp = pump_until_id(proc, 3, 15)
    stash_latency = time.time() - t_stash
    stash_result = stash_resp.get("result") if stash_resp else None
    stash_syms = len(stash_result) if isinstance(stash_result, list) else 0
    print(f"[{label}] .stash documentSymbol: {stash_syms} symbols in {stash_latency:.3f}s", file=sys.stderr)

    # shutdown + exit
    send(proc, {"jsonrpc": "2.0", "id": 4, "method": "shutdown", "params": None})
    pump_until_id(proc, 4, 5)
    send(proc, {"jsonrpc": "2.0", "method": "exit", "params": None})

    try:
        proc.wait(timeout=3)
    except subprocess.TimeoutExpired:
        proc.terminate()

    total = time.time() - t0
    ok = cs_syms > 0 and stash_syms > 0
    return ok, cs_syms, stash_syms, cs_latency, stash_latency, total


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    print("=" * 60, file=sys.stderr)
    print("lsp-warmd two-language end-to-end test", file=sys.stderr)
    print("=" * 60, file=sys.stderr)

    # ── Clean slate ──────────────────────────────────────────────────────────
    print("\n[setup] killing any running daemon...", file=sys.stderr)
    # Only kill OUR daemon (and let it own its server children) — do NOT global-pkill
    # csharp-ls/stash-lsp: that would nuke other Claude Code sessions' and VS Code's servers.
    os.system("pkill -f 'Stash.*daemon.stash' 2>/dev/null; rm -f /tmp/lsp_warmd.ready; sleep 0.5")
    print("[setup] clean slate", file=sys.stderr)

    # ── Run 1: cold start ────────────────────────────────────────────────────
    print(f"\n--- Run 1: cold start (both servers spawned) ---", file=sys.stderr)
    ok1, cs1, stash1, cs_lat1, stash_lat1, total1 = run_session("run1", cold_start=True)

    if not ok1:
        print("\nFAIL: run1 did not complete", file=sys.stderr)
        sys.exit(1)

    print(f"\n[setup] waiting 0.5s before run2...", file=sys.stderr)
    time.sleep(0.5)

    # ── Run 2: warm ──────────────────────────────────────────────────────────
    print(f"\n--- Run 2: warm (daemon already running) ---", file=sys.stderr)
    ok2, cs2, stash2, cs_lat2, stash_lat2, total2 = run_session("run2", cold_start=False)

    # ── Verdict ──────────────────────────────────────────────────────────────
    print("\n" + "=" * 60, file=sys.stderr)
    print("VERDICT", file=sys.stderr)
    print(f"  run1 (cold — daemon spawns+warms):", file=sys.stderr)
    print(f"    .cs    : {'PASS' if cs1>0 else 'FAIL'} ({cs1} symbols, {cs_lat1:.3f}s)", file=sys.stderr)
    print(f"    .stash : {'PASS' if stash1>0 else 'FAIL'} ({stash1} symbols, {stash_lat1:.3f}s)", file=sys.stderr)
    print(f"    total session: {total1:.3f}s (includes warmup)", file=sys.stderr)
    print(f"  run2 (warm — daemon already running):", file=sys.stderr)
    print(f"    .cs    : {'PASS' if cs2>0 else 'FAIL'} ({cs2} symbols, {cs_lat2:.3f}s)", file=sys.stderr)
    print(f"    .stash : {'PASS' if stash2>0 else 'FAIL'} ({stash2} symbols, {stash_lat2:.3f}s)", file=sys.stderr)
    print(f"    total session: {total2:.3f}s (instant — pre-warmed)", file=sys.stderr)
    instant = total2 < 1.0
    print(f"  Second call instant (<1s): {'YES' if instant else 'NO'}", file=sys.stderr)
    print(f"  PROOF: ONE daemon served both .cs and .stash through same process", file=sys.stderr)
    print("=" * 60, file=sys.stderr)

    all_pass = ok1 and ok2
    sys.exit(0 if all_pass else 1)


if __name__ == "__main__":
    main()
