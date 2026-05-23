# Backlog: Fuzz host crash in FuzzCorpus_PipelineOnAndOff_IdenticalOutput

## Summary

`FuzzHarnessTests.FuzzCorpus_PipelineOnAndOff_IdenticalOutput` crashes the xUnit
test host after the test itself passes. The crash is pre-existing and unrelated to
the `readable-disassembly` feature.

## Context

During the `readable-disassembly` P3 implementation the clean verify command:

    dotnet test --filter "FullyQualifiedName~Bytecode"

was replaced with a grep-based workaround in `plan.yaml`:

    dotnet test --filter "FullyQualifiedName~Bytecode" ... | grep "Failed: +0,"

This avoids the test host crash causing a non-zero exit code that would
incorrectly fail the verify step.

## Risk

The grep workaround masks any future case where the same fuzz crash appears
alongside a *different, real* failure in the same run — `Failed: +0,` will still
match in the output even when other regressions exist, as long as the host crashes
before the final summary updates.

## Suggested resolution

1. Quarantine `FuzzHarnessTests.FuzzCorpus_PipelineOnAndOff_IdenticalOutput` with
   `[Skip("Host crash under investigation — see backlog/fuzz-host-crash-pipeline-on-off.md")]`
   and a link to this file, OR
2. Investigate and fix the actual fuzz-host crash.

After the quarantine or fix, restore the clean verify command for future features
(i.e. remove the grep pipeline from any new plan.yaml verify lines).

## References

- Feature: `readable-disassembly`
- Phase: P3 (commit `3b6d3c5`)
- Review finding: F05 in `.kanban/2-in-progress/readable-disassembly/review.md`
