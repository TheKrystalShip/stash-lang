# `task.run` with an `async` lambda crashes: "Index was outside the bounds of the array"

**Status:** Fixed — 2026-06-05 (commit 54615769)
**Created:** 2026-06-05
**Discovery context:** Surfaced during the LSP warm-daemon dogfood (increment 2, `/tmp/lsp_warmd.stash`) while exploring whether a background task could poll a socket concurrently with the main VM. Passing an `async` lambda to `task.run` crashed; the daemon worked around it by using the synchronous `tcp.listen` in the main VM instead. Independently reproduced and confirmed 2026-06-05.

---

## Problem

`task.run(fn)` is documented as "Runs a function asynchronously in a new task and returns a Future." When `fn` is an **`async` lambda** (a function that itself returns a `Future`), the returned Future fails: awaiting it throws `RuntimeError: Future failed: Index was outside the bounds of the array.` A **plain** (non-async) lambda passed to the same `task.run` works correctly. So the combination `task.run(async () => ...)` is broken, while both `task.run(() => ...)` and a directly-awaited `async fn` work.

## Reproduction

```stash
// control — works, prints 2
let f1 = task.run(() => 1 + 1);
io.println(conv.toStr(task.await(f1)));

// trigger — crashes on await
let f2 = task.run(async () => 2 + 2);
io.println(conv.toStr(task.await(f2)));   // RuntimeError: Future failed: Index was outside the bounds of the array.
```

```
$ dotnet run --project Stash.Cli/ -- repro.stash
control: 2
RuntimeError: Future failed: Index was outside the bounds of the array.
  at <main> (repro.stash:8:46)   # the task.await(f2) call
exit: 70
```

Deterministic — reproduces every run, Debug build, Linux.

## Blast radius

Latent today (no shipped Stash code combines `task.run` with `async` lambdas), but a natural foot-gun: `task.run(async () => await http.get(...))` is an obvious thing to write, and it fails with an opaque internal error that gives the author no hint they should have dropped the `async`. Compounds as more concurrent Stash is written (the whole point of the embedding/daemon direction). The error message ("Index was outside the bounds of the array") leaks a C# internal and points nowhere useful.

## Root cause

Partially known. `TaskBuiltIns.Run` (`Stash.Stdlib/BuiltIns/TaskBuiltIns.cs:34-47`) does:

```csharp
var dotnetTask = Task.Run<object?>(() => {
    IInterpreterContext child = ctx.Fork(cts.Token);
    return child.InvokeCallbackDirect(fn, ReadOnlySpan<StashValue>.Empty).ToObject();
});
return StashValue.FromObj(new StashFuture(dotnetTask, cts));
```

Two interacting problems:
1. **Double-wrapping:** invoking an `async fn` returns a `Future`; `.ToObject()` then yields a `StashFuture`, so `task.run` wraps a Future whose result is *itself* a Future (Future-of-Future). `task.run` does not flatten the inner Future.
2. **The actual throw** is an `IndexOutOfRangeException` raised *inside* the `Task.Run` fork, surfaced as "Future failed: …". An `async fn` body itself forks a child VM on a thread-pool thread (per the spec); nesting that async-child fork inside `task.run`'s own `ctx.Fork` + `InvokeCallbackDirect(fn, Empty)` (empty arg span) appears to index a parameter/results array that the nested async path expects to be populated. Exact site needs runtime tracing through `InvokeCallbackDirect` → async-fn invocation → child-VM setup.

## Suggested fix

- (A) **Flatten** — detect that `fn` returned a `Future` and chain it (await the inner Future inside `dotnetTask`, return its eventual result), so `task.run(async () => x)` behaves like `task.run(() => x)`. Recommended: it makes the natural code Just Work and matches the documented "returns a Future representing the running task." Must first fix the underlying `IndexOutOfRange` in the nested-async-fork path — flattening alone won't help if invocation throws before returning the inner Future.
- (B) **Reject** — make `task.run` throw a clear `TypeError` ("pass a non-async function; async functions already return a Future — call it directly and await") when handed an `async` callable. Cheaper, but pushes a papercut onto users.

Recommend (A), with (B)'s clear message as the fallback if flattening the nested async-child fork proves too invasive.

## Verification

```bash
dotnet test --filter "FullyQualifiedName~TaskRunAsyncLambda"
# Regression test asserting: task.await(task.run(async () => 2 + 2)) == 4
# Today: fails (RuntimeError IndexOutOfRange). After fix: passes.
```
Cross-cutting: the existing `task.*` tests and `async fn` / `await` tests must stay green.

## Related

- Surfaced during: LSP warm-daemon dogfood (`/tmp/lsp_warmd.stash`, increment 2). The daemon avoided it by using synchronous `tcp.listen` in the main VM — see also the task-VM isolation findings.
- Same surface: `task.run` / `task.await` (`Stash.Stdlib/BuiltIns/TaskBuiltIns.cs`), `async fn` child-VM forking (Language Spec §"Async-child global isolation").
- Sibling finding (separate bug): `process.read` blocks on an empty pipe — `.kanban/0-backlog/bugs/process-read-blocks-on-empty-pipe.md`.
- Follow-up (not done here): `arr.parMap` / `arr.parFilter` / `arr.parForEach` invoke their callbacks via the same forked-child path, so the `CallClosureDirect` guard fixes the *crash* for an async callback there too — but they do **not** flatten (an async element callback would yield a `Future`, not its value). Flattening the parallel `arr.*` helpers for async callbacks is a separate, lower-priority enhancement.

## Resolution (2026-06-05)

Fixed with both halves of suggested fix (A): the underlying `IndexOutOfRange` *and* the Future-of-Future double-wrap.

**What changed**

1. **Root crash — `CallClosureDirect` frameless guard** (`Stash.Bytecode/VM/VirtualMachine.Functions.cs`). The forked-child callback path (the sole caller is `VMContext.InvokeCallbackDirect`'s Branch 3, used by `task.run`/`task.timeout`/`arr.parMap`/exit callbacks/TCP accept) unconditionally called `Run()` after `CallValue`. For an async fn, `SpawnAsyncFunction` returns the Future immediately *without pushing a frame*, so `Run()` → `RunInner` indexed `_frames[_frameCount - 1]` = `_frames[-1]` → "Index was outside the bounds of the array." The fix returns the Future directly when `_frameCount == 0`, mirroring the guard already present in the same-thread sibling `ExecuteVMFunctionInlineDirect`. This alone removes the crash for every Branch-3 caller.

2. **Flatten — narrow, async-only** (`Stash.Stdlib/BuiltIns/TaskBuiltIns.cs` `Run` + `Timeout`). With the crash gone, an async callback returned a `StashFuture`, so `task.await(task.run(async () => x))` yielded a Future-of-Future, not `x`. `task.run`/`task.timeout` now unwrap that inner Future (via `inner.GetResult()`) **only when the callable is async**, so `task.run(async () => x)` behaves exactly like `task.run(() => x)`. A plain lambda that explicitly returns a Future is left untouched (no surprise auto-flatten) — verified by `Run_PlainLambdaReturningFuture_IsNotFlattened`.

3. **`IStashCallable.IsAsync`** (`Stash.Core/Runtime/IStashCallable.cs`, default `false`; overridden by `VMFunction` → `Chunk.IsAsync`). The async-ness needed for the narrow flatten lived in `Stash.Bytecode` (`VMFunction.Chunk`), a layer above `Stash.Stdlib` where `TaskBuiltIns` lives — so the callable abstraction in `Stash.Core` had to expose it. Built-ins and plain lambdas inherit `false`.

**Verification**

- `dotnet test --filter FullyQualifiedName~TaskBuiltInsTests` → 67 passed (8 new: async-lambda/async-fn-ref/inner-await flatten, async-throws-propagates-cleanly asserting the `IndexOutOfRange` leak is gone, plain-lambda-not-flattened, + 3 `task.timeout` async cases).
- `AsyncAwaitTests`, `StandardLibraryReferenceTests`, `CompletionSurfaceSnapshotTests` → green.
- Docs regenerated (`dotnet run --project Stash.Docs/`) for the `task.run`/`task.timeout` `<summary>` clarifications.
- Example: `examples/async_await.stash` §10 demonstrates `task.run(async ...)` and `task.timeout(async ...)`, verified with the freshly-built binary (both flatten to 42).
- Tooling-compat checklist (LSP/DAP/Playground/VS Code/Analysis): **N/A** — runtime-behavior fix only; no new syntax, keyword, namespace, or signature change (only `<summary>` text), so no tokenizer/grammar/completion surface is affected.
