# stash-author gotcha memory

Working notes for the `stash-author` agent: **doc/reality mismatches and known
sharp edges in the installed `stash` binary.** This file is a HINT list — *where
to be suspicious* — not a source of truth. The docs (`docs/Stash — Language
Specification.md`, `docs/Stash — Standard Library Reference.md`) are authoritative;
always read the relevant sections first.

## Rules for this file (read before editing)

- **Mismatches only.** Record something here only when the binary's behavior
  diverges from the docs, or a documented capability is missing/broken. Behavior
  that is correctly documented does NOT belong here — you learn that by reading.
- **Every entry is backed by a `Category=Gotcha` test.** When you find a bug,
  add an xUnit test in `Stash.Tests/Interpreting/GotchaTests.cs` that asserts the
  *current buggy* behavior (green now, red when fixed), plus a backlog stub under
  `.kanban/0-backlog/`. The test — not this note — is the validity check.
- **Verify by running the test, don't trust the note.** Before relying on a
  gotcha, run its test (`dotnet test --filter "<filter>"`). If it's red, the bug
  was fixed: flip the test to assert correct behavior, then **delete this entry.**
- **One fact per entry.** Keep entries dated and link the test + backlog stub.

---

## Active gotchas

### `process-read-blocks-empty-pipe`
- **Observed:** 2026-06-05 (lsp_warmd.stash increment 2 authoring)
- **Mismatch:** `process.read(handle)` is documented as "Non-blocking — returns null if no data is ready." In practice, when the child's stdout pipe buffer is empty, `process.read` **blocks** indefinitely waiting for the child to produce output, instead of returning null.
- **Impact:** Any poll-based read loop (`while(chunk = process.read(p); chunk == null) { ... }`) hangs forever when the child goes quiet. The daemon workaround: only call `process.read(lsp)` inside `pumpLsp`/`pumpLspBridge` AFTER forwarding a client request to csharp-ls (which will produce a response), so the pipe always has pending data.
- **Note:** `process.read(p)` from inside `task.run` returns immediately (cross-VM handle is disconnected from the new VM's scope), so the blocking is specific to the spawning VM's main execution context.
- **Test:** `dotnet test --filter "FullyQualifiedName~GotchaTests.ProcessRead_EmptyPipe_CurrentlyBlocks"` (green = bug still present, red = fixed)
- **Backlog:** `.kanban/0-backlog/bugs/process-read-blocks-on-empty-pipe.md`

---

### `fs-move-copy-file-only`
- **Observed:** 2026-06-01 (build with `path.match`, post-A.6)
- **Mismatch:** `fs.move` / `fs.copy` are **file-only** — there is no
  directory-capable variant in the `fs` namespace. Moving/copying a directory
  throws `IOError` ("Could not find file '<dir>'", despite the dir existing).
- **Work around:** shell out — `$!(mv ${src} ${dst})` / `$!(cp -r ${src} ${dst})`.
  (`mv`/`cp` are tools; calling them is correct, not a reimplementation.)
- **Test:** `dotnet test --filter "FullyQualifiedName~GotchaTests.FsMove_Directory_CurrentlyThrows"` (green = bug still present)
- **Backlog:** `.kanban/0-backlog/stdlib/fs-move-copy-file-only.md`

---

## Investigated — NOT gotchas (kept as docs-first teaching anchors)

These looked like bugs but are correct, documented behavior. They are recorded
ONLY to stop a future agent (or me) from re-filing them. The lesson each time:
**reading the doc entry would have answered it.**

- **`str.replace` replaces only the first occurrence** — *intended.*
  `str.replace(s, old, new, count=1)` takes an optional count (default 1), and
  `str.replaceAll(s, old, new)` does the global replace. (Standard Library
  Reference, `str.replace` / `str.replaceAll`.)
- **Running external commands** — use the command-expression sugar, not a
  reimplementation: `$(cmd)` (capture → struct `.stdout`/`.stderr`/`.exitCode`),
  `$!(cmd)` (strict, throws `CommandError`), `$>(cmd)`/`$!>(cmd)` (stream live),
  `try $!(cmd)` to branch on failure, `${var}` for injection-safe args. (Language
  Specification, "Shell Integration".) `process.exec` exists but the sugar is
  preferred.
