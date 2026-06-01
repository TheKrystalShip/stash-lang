# `ExecOptions` optional `stdin` string field

Status: deferred
Created: 2026-06-01
Discovery context: Surfaced by the `checkpoint-scripts-stash-port` feature (phase P4). The `difftest_runner.stash` oracle needed to pipe a YAML blob into a `python3 -c` normalization helper, but `process.exec`'s `ExecOptions` struct has no way to supply the child's stdin.

## What

Add an optional `stdin` string field to `ExecOptions` so `process.exec(cmd, args, ExecOptions { stdin: data, ... })` feeds `data` to the child process's standard input, then closes it. Today the only ways to send data to a child are (a) write a temp file and pass its path as an arg, or (b) the more verbose `process.spawn` + `process.write` dance.

## Why deferred

The temp-file workaround is clean and fully correct for the checkpoint port — no blocker. This is an ergonomics gap, not a correctness one. Filing so the convenience isn't lost.

## When to pick this up

If repeated `process.exec` call-sites accumulate temp-file boilerplate just to feed stdin. Routes through the full `language-changes.md` checklist (stdlib impl + `[StashParam]`/struct metadata regen + completion snapshot + example + tests). Confirm the field interacts correctly with `strict`, `cwd`, and `env`, and that large stdin payloads don't deadlock against a child that fills its stdout pipe before draining stdin.

## Related

- Worked around in `scripts/checkpoint/difftest_runner.stash` (`stdout_yaml_equal` axis writes py stdout to a temp file before the normalization exec). *(That oracle was retired in Milestone A.5 with the Python originals; the underlying stdlib gap remains open.)*
