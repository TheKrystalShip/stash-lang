# FuzzCorpus_PipelineOnAndOff crashes the test host process

**Status:** Backlog — Bug (confirmed, quarantined)
**Created:** 2026-06-01
**Discovery context:** Surfaced during the Test Suite Green-Up when `FuzzHarnessTests.FuzzCorpus_PipelineOnAndOff_IdenticalOutput` was un-skipped and run. The skip had been mislabelled "non-determinism"; it actually crashes the host.

---

## Problem

`Stash.Tests/Bytecode/FuzzHarnessTests.cs` → `FuzzCorpus_PipelineOnAndOff_IdenticalOutput` is the only test in its class. When un-skipped and run (even in complete isolation), it **crashes the `dotnet test` host process**: `The active test run was aborted. Reason: Test host process crashed`. Because the crash is in the test host, it also aborts every *other* test in the same run — so un-skipping it makes the whole suite unrunnable.

This is a real, uncatchable crash, NOT the "pre-existing non-determinism in some examples" the old skip reason claimed. The earlier belief that the skip was "stale / exits 0" was wrong: that check used `dotnet test --filter ~FuzzHarnessTests`, which keeps a `[Fact(Skip)]` test **skipped** (filters don't un-skip), so it trivially exited 0 without ever executing.

## Reproduction

```bash
# Temporarily remove the [Fact(Skip=...)] on FuzzCorpus_PipelineOnAndOff_IdenticalOutput, then:
dotnet test Stash.Tests/ --filter "FullyQualifiedName~FuzzHarnessTests"
# => "The active test run was aborted. Reason: Test host process crashed"  (confirmed 2026-06-01, isolation)
```

## Blast radius

- The test cannot be enabled as-is: it takes down the entire `dotnet test` run, masking all other results. This is exactly why it must stay quarantined until fixed.
- The crash is in the VM executing a corpus example (the test compiles + runs every `examples/*.stash` twice, pipeline on vs off, comparing output). It iterates the example corpus, so the trigger is one (or more) example scripts.

## Root cause

Not yet pinned. The test executes example scripts through the full VM pipeline; one example crashes the VM with an **uncatchable** failure — most likely a `StackOverflowException` (uncatchable in .NET → immediate process termination), or a native/AccessViolation crash. The test already carries a `ShouldSkip` filename allow-list for known-bad patterns (async/network/stdin/etc.); the crashing example is either newly added or crashes via a path the allow-list doesn't cover. The specific example is unidentified because the crash prevents the harness from reporting which file it was on.

## Suggested fix

**Isolate execution so a crash fails soft, then identify and fix the crashing example.**
- (A) Run each corpus example in a **child process** (spawn `stash <file>` and compare stdout) so an uncatchable crash becomes a non-zero child exit the harness can attribute to a specific file and report — instead of taking down the test host. This both unblocks the test and pinpoints the offending example. **Recommended.**
- (B) Once identified, fix the underlying VM crash (e.g. add a recursion-depth guard that throws a catchable `RuntimeError` instead of overflowing the native stack), or exclude the example via the `ShouldSkip` list with a tracking note.

## Verification

```bash
# After the fix: un-skip FuzzCorpus_PipelineOnAndOff_IdenticalOutput and confirm the full
# suite completes without "Test host process crashed".
dotnet test Stash.Tests/ --filter "FullyQualifiedName~FuzzHarnessTests"
```

## Related

- Quarantined at `Stash.Tests/Bytecode/FuzzHarnessTests.cs` with an accurate skip reason (Test Suite Green-Up, 2026-06-01).
- The old repo.md note ("host-process crash during the SKIPPED test causes `dotnet test` to exit non-zero") was directionally right about a crash but wrong about the mechanism (it's the test executing, not merely being skipped). repo.md's "Known Issues" should be corrected to point here.
- Green-up plan: `Flaky Test Classes — Root Cause Stabilization.md` (now "Test Suite Green-Up").
