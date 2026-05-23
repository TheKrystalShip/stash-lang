# Test Suite — Parallel Execution Flake from cwd Mutation

> **Status:** Backlog
> **Created:** 2026-04-29
> **Discovery context:** Found during review of `REPL Prompt Customization — Themes, Colors, and Status.md`.

## Problem

Running `dotnet test` with default xUnit parallelism produces **non-deterministic failures** in tests that construct a `VirtualMachine` (or otherwise call `Environment.CurrentDirectory` / `Sys.GetCwd()`). Failure counts have been observed ranging from 4 to ~1900 in the same suite. Running with `xUnit.ParallelizeTestCollections=false` produces a clean 7299/0 pass.

Sample symptom (cascading):

```
System.IO.DirectoryNotFoundException : Could not find a part of the path '/tmp/stash_dirstack_*'.
   at System.Environment.set_CurrentDirectory(String value)
   at Stash.Bytecode.VMContext..ctor(...)  // line 74
```

…repeated across hundreds of unrelated tests once the process cwd has been deleted.

## Root cause

Several test fixtures mutate `Environment.CurrentDirectory` (a process-global) inside `[Fact]` bodies and revert it in `Dispose` / `finally`:

- `Stash.Tests/Bytecode/DirStackTests.cs` (11 occurrences)
- `Stash.Tests/Bytecode/GlobExpansionTests.cs` (`TempDir` helper)
- `Stash.Tests/Cli/ShellSugarIntegrationTests.cs` (3)
- `Stash.Tests/Cli/RcFileLoaderTests.cs` (1)
- `Stash.Tests/Interpreting/ProcessBuiltInsTests.cs` (1)

When two tests in different `Collection`s run in parallel, one test can:

1. cd into a temp dir `/tmp/X`,
2. delete `/tmp/X` (or fail mid-test),
3. leave the process cwd dangling,

and then any other concurrent test that calls `Environment.CurrentDirectory` (including the `VMContext` constructor at line 74, which does `CurrentDirectory = CurrentDirectory` for path resolution) throws `DirectoryNotFoundException`. Because `_cwd` mutation is process-global, no `[Collection]` attribute can isolate it.

## Suggested fix

Two viable approaches:

1. **Forbid parallel collections that mutate cwd.** Add a single `[CollectionDefinition("CwdMutating", DisableParallelization = true)]` and apply it to every fixture in the list above. Cheap, but easy to forget when adding new tests.

2. **Stop mutating process cwd.** Refactor every `Environment.CurrentDirectory = X` site to:
   - Pass the working directory as an argument to the SUT (preferred for `process.cwd`, `path.resolve`, etc.), or
   - Spawn a helper process when the test really needs a different cwd (slower but isolated).

Approach 2 is the structural fix; approach 1 is a one-line patch per test class that buys time.

## Reproduction

```bash
# Flaky:
dotnet test

# Always green:
dotnet test -- xUnit.ParallelizeAssembly=false xUnit.ParallelizeTestCollections=false
```

## Affected tests (so far observed)

- `Stash.Tests.Bytecode.GlobExpansionTests.*` (3 distinct tests)
- `Stash.Tests.Interpreting.CliExecutionTests.Stdin_SimplePrint_PrintsOutput`
- Cascading: any test that constructs a fresh `VirtualMachine` while a parallel test holds a deleted cwd (~1900 in the worst run observed)

## Out of scope

Fixing this flake is **not** required to ship the prompt-customization feature. The prompt subsystem itself does not introduce new cwd mutation; it only reads `Environment.CurrentDirectory` (in `BuildPromptContext`, the same way every other VM operation does). The flake is pre-existing and unrelated to the prompt code changes.
