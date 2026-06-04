# stash-hosting-mvp — Review (pass 2)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.
>
> Pass-1 history (8 findings, all fixed) is preserved in git at commit `24d12ba1`.

**Scope reviewed:** commits `07ac0e61..119c7f6c` on branch `feature/stash-hosting-mvp` (pass 2 focuses on fix commits `dd465dc6`, `ec797725`, `e4cc8c03`, `035a96cd`, `faf4a453`)
**Brief:** ../brief.md
**Baseline tests:** PASS (failed=0, passed=13424, skipped=6)
**Generated:** 2026-06-04

**Verdict in one line.** All 8 pass-1 fixes hold up empirically and structurally. F01's
collection unification is airtight (1 CollectionDefinition + 12 Collection refs, zero
dangling old names tree-wide). F02's chunk cache is sound (`??=` set, internals reused under
the per-host semaphore, `Statements` unmutated, legacy `Run` path intentionally untouched).
F03/F04/F06 surface the structured-parse-error contract via `KindParseError` /
`KindStepLimitExceeded` consts, with no stranded `InvalidOperationException` callers. F05's
linked CTS disposes on every exit (`using` scoped to the race block, `Cancel()` on the
success branch) — no `TaskCanceledException` leaks, no `UnobservedTaskException` (Canceled
≠ Faulted). F07's chokepoint is restored — every remaining `StashValue.FromObject` /
`FromObj` / `StashTypeConverter.*` reference in `Stash.Hosting/` is inside
`Marshalling/`, plus one comment in `StashHost.cs:366`. No VM-core file was touched again.

The single new finding below is a 50%-coverage gap on F08's loud-throw path, not a
correctness defect.

---

## F01 — [MINOR] `JsonStashBridge` loud-throw path is reachable but untested

**Status:** fixed
**Fixed in:** 24c89bd2
**Files:** `Stash.Hosting/Marshalling/JsonStashBridge.cs:159-164`, `Stash.Tests/Embedding/HostMarshallerTests.cs:200-217`
**Phase:** P3 (pass-2 fix follow-up)
**Commit:** faf4a453

### Observation

`faf4a453` shipped two new behaviors in one fix:

1. `byte[]` round-trips to base64.
2. Any other `StashValueTag.Obj` runtime type throws `InvalidOperationException` (replacing
   the prior silent `WriteNullValue()`).

```csharp
case StashValueTag.Obj when value.AsObj is byte[] bytes:
    writer.WriteBase64StringValue(bytes);
    break;

case StashValueTag.Obj:
    throw new InvalidOperationException(
        $"JsonStashBridge cannot serialize Stash Obj value of runtime type " +
        $"'{value.AsObj?.GetType().FullName ?? "null"}' to JSON; " +
        $"supported Obj types are string, array (List<StashValue>), " +
        $"StashDictionary, and byte[].");
```

The commit title — "JsonStashBridge round-trips byte[] as base64, throws loudly on
unsupported Obj" — names both behaviors. The added test
`JsonStashBridge_ByteArrayInDict_RoundTripsAsBase64` covers only the base64 half;
no test asserts the throw fires (or the message shape) for an unsupported Obj type.

The throw is reachable through ordinary use, not exotica. The pass-1 review noted a resolver
caveat suggesting `HostMarshaller.ToStash` "rejects unsupported CLR types first" — but
`ToStash` guards `CallAsync` **inputs**, not `CallAsync<JsonElement>` **return** values.
A script that returns an Obj-tagged value the bridge does not recognize (e.g. a struct
instance, a `StashFuture`, a `Range`, or any future Obj-typed runtime value the VM grows
later) requested as `JsonElement` hits the new throw with no guard upstream:

```csharp
fn f() { return SomeStruct{ ... }; }
await host.CallAsync<JsonElement>("f");   // → InvalidOperationException
```

### Why this matters

Half of a shipped fix is uncovered. The throw is the contract for "unsupported but
defined" — flipping silent-null to loud-throw is the entire point of F08 — and silently
weakening it later (someone broadens the silent fallback again) would not regress any test
today. The defect is in coverage, not in the behavior. MINOR because the fix itself is
correct and a script returning a non-string/list/dict/byte[] Obj as `JsonElement` is a
secondary use case, not the warm path.

The pass-1 reviewer flagged the verify command as
`JsonStashBridge_ByteArrayInDict_RoundTripsAsBase64_OrThrows` (note the suffix); the
resolver dropped the `_OrThrows` half.

### Suggested fix

Add a second test in the same `// ── byte[] marshalling through JSON bridge ──` block,
exercising the throw on an Obj-tagged value with no JSON mapping. The simplest reachable
trigger from a fully-public surface is a Stash script returning a struct instance:

```csharp
[Fact]
public async Task JsonStashBridge_UnsupportedObj_ThrowsInvalidOperationException()
{
    await using var host = new StashHost();
    var s = await host.CompileAsync(@"
        struct S { x: int }
        fn f() { return S{ x: 1 }; }
    ");
    await host.RunAsync(s);

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => host.CallAsync<JsonElement>("f"));
    Assert.Contains("JsonStashBridge cannot serialize", ex.Message);
}
```

(Adjust the trigger if structs do not survive to the JSON bridge through `CallAsync<JsonElement>`
in practice — any other ordinary Obj type the VM produces that is not string/list/dict/byte[]
is equivalent. The point is to lock in "this throws, here, with this message shape.")

### Verify

```
dotnet test --filter "FullyQualifiedName~HostMarshallerTests"
```

The new test must pass; the existing `RoundTripsAsBase64` test must stay green.

---

## Summary

| Severity | Count |
| --- | --- |
| CRITICAL | 0 |
| IMPORTANT | 0 |
| MINOR | 1 |

### Verdict on each pass-1 fix

| Pass-1 finding | Fix commit | Verdict |
| --- | --- | --- |
| F01 (HIGH) — collection unification | `dd465dc6` | **sound** — exactly 1 `[CollectionDefinition("ProcessGlobalSlots")]` + 12 `[Collection("ProcessGlobalSlots")]` refs, zero dangling old names tree-wide |
| F02 (MEDIUM) — chunk cache | `ec797725` | **sound** — `??=` set, internals visible to tests, `Assert.Same` confirms identity, `Statements` unmutated, legacy `Run` path intentionally untouched per commit message |
| F03 (MEDIUM) — kind const | `e4cc8c03` | **sound** — `KindStepLimitExceeded` declared once + 3 use sites; `KindCancelled` and `KindParseError` follow the same shape |
| F04 (MEDIUM) — structured parse error | `e4cc8c03` | **sound** — throws `StashScriptException(StashError { Kind = KindParseError, Span = null, CallStack = empty })`; no stranded `InvalidOperationException` callers |
| F05 (LOW) — linked CTS | `035a96cd` | **sound** — `using` disposes on every exit, `Cancel()` on the success branch, Canceled (not Faulted) task → no UnobservedTaskException, registration to outer `ct` removed on dispose |
| F06 (LOW) — CompileAsync ct | `e4cc8c03` | **sound** — `ct.ThrowIfCancellationRequested()` pre-flight + a test for a pre-cancelled token |
| F07 (LOW) — chokepoint | `035a96cd` | **sound** — `HostMarshaller.FromStashObject<T>` added; every `StashValue.FromObject` / `FromObj` / `StashTypeConverter.*` reference in `Stash.Hosting/` is inside `Marshalling/` (plus one harmless comment at `StashHost.cs:366`) |
| F08 (LOW) — byte[] + loud throw | `faf4a453` | **sound on behavior, incomplete on coverage** — base64 path tested, loud-throw path is reachable via an ordinary script-returns-unsupported-Obj scenario but untested; filed as F01 above |

The three VM-core files (`VirtualMachine.Debug.cs`, `VirtualMachine.cs`,
`VirtualMachine.Functions.cs`) were **not** touched by any of the 5 fix commits.
`StashEngine.Run`/`Evaluate` swallowing behavior is unchanged. The public surface
(`IStashHost`, `StashError`, `CompiledScript`, `HostMarshaller`) is consistent with the
brief's Design section. The baseline suite passes with `failed=0, passed=13424, skipped=6`.
