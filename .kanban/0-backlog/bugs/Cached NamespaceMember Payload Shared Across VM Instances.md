# Cached `NamespaceMember` Payload Shared Across VM Instances

**Status:** Backlog — Bug
**Created:** 2026-05-23
**Discovery context:** Found while writing regression tests for F01 (frozen-array unwrap in `SvArgs.StashList`) during review of `stdlib-namespace-members`. Reported by the Resolver as an "adjacent note" out of F01's scope.

---

## Bug Description

`NamespaceMemberPayload._cachedValue` (in [Stash.Stdlib/Models/NamespaceMemberPayload.cs](../../../Stash.Stdlib/Models/NamespaceMemberPayload.cs)) is process-scoped, not VM-scoped. For `Stability.Cached` members, the first access from any `VirtualMachine` instance writes the cache; every subsequent access — from the same VM **or any other VM constructed later in the same process** — returns that first cached value.

The payload object lives on the `StashNamespace` returned by `StdlibDefinitions.CreateVMGlobals()`. Whether that namespace is itself shared across VM instances is the seam: if it is (or any of its slots are), the `_cachedValue` field on the payload acts as a hidden global.

## Concrete effect

Where this surfaces today:

- **xUnit test isolation under parallel execution.** Tests that construct their own `VirtualMachine` with different `ScriptArgs` see whichever args the *first-to-run* test installed. `cli.argv` from VM₂ returns VM₁'s args because the payload's `_cachedValue` was already populated. The Resolver hit this while building three new regression tests in `CliMembersTests.cs` for F01 — they had to use the same args as earlier tests in the class to stay green.
- **Embedded scenarios.** Any application that spins up a fresh Stash VM per request / per task (e.g., a script-runner service) will see the *first* request's `cli.argv` / `env.user` / etc. for the rest of the process's lifetime. The "Cached" contract was designed for "set once at process start" — it doesn't survive contexts where multiple logical processes share a CLR process.

The 7 of 8 v1 members that are `Stability.Cached` are all affected: `cli.argc`, `cli.argv`, `env.home`, `env.user`, `env.hostname`, `env.os`, `env.arch`. `env.cwd` is `Live` and unaffected.

## Why this is latent, not catastrophic

The v1 members happen to be process-identity reads that are genuinely process-stable in practice — `env.user`, `env.os`, `env.arch` don't change at runtime, so caching the wrong VM's value is indistinguishable from caching the right one. **`cli.argc` / `cli.argv` are the real exposure**: they depend on `IInterpreterContext.ScriptArgs`, which is per-VM, not per-process.

The brief's stated motivation for `Stability.Cached` is "identity-stable across the process lifetime, modelled on JS ES module `const` exports." That framing implicitly assumed one VM per process, which is the common case for `stash` CLI invocations. Embedded and multi-tenant scenarios were not considered.

## Root cause

Three coupled design choices:

1. The payload's `_cachedValue` field is instance-state on a long-lived object (the payload).
2. The payload is stored on the `StashNamespace`, which is constructed once by `StdlibDefinitions.CreateVMGlobals()`.
3. The `StashNamespace` is reachable across VM instances when `StdlibDefinitions` or its containers are reused (which happens by default in long-running hosts).

Any one of the three could be relaxed to fix the bug.

## Suggested approaches (any one fixes it; pick during design)

- **(A) Cache on the VM context, not the payload.** The payload becomes stateless (just `getter` + `stability` + `returnType`). The VM holds a `Dictionary<NamespaceMemberPayload, StashValue>` for cached results. Tears down with the VM. **Cleanest fix; biggest scope.**
- **(B) Drop caching for `Cached` members; rename the modes.** Re-evaluate every access for all members; treat `Cached` as a documentation-only hint that "the value is unlikely to change." This is what Python `sys.argv` effectively does (no caching, just convention). **Smallest fix; weakest semantics.**
- **(C) Make the payload object per-VM.** Each VM constructs its own `NamespaceMemberPayload` instances when building its `StashNamespace`. Today this is shared because `StdlibDefinitions.CreateVMGlobals()` returns shared structures. **Localised fix; requires per-VM materialisation cost.**

Recommend (A): it preserves the brief's stated semantics (cache once per logical context), survives embedded / multi-tenant scenarios, and the VM is already the natural owner of per-context state.

## Verification when fixed

```bash
# A test that exercises the seam:
# Construct two VMs with different ScriptArgs, read cli.argv from each, assert they differ.
dotnet test --filter "FullyQualifiedName~CrossVmCachedMemberIsolationTests"

# Plus existing regression — must still pass after the fix:
dotnet test --filter "FullyQualifiedName~CliMembersTests|FullyQualifiedName~EnvMembersTests"
```

## Related

- Surfaced during F01 resolution in `stdlib-namespace-members` (commit `9fe6347`).
- Brief: `.kanban/2-in-progress/stdlib-namespace-members/brief.md` — "Stability annotation" subsection documents Cached semantics.
- Test infrastructure: see [Flaky Test Classes — Root Cause Stabilization.md](Flaky%20Test%20Classes%20%E2%80%94%20Root%20Cause%20Stabilization.md) for the broader pattern of order-dependent xUnit tests in this repo.
