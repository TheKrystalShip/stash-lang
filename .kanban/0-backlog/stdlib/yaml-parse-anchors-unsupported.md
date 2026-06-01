# `yaml.parse` rejects YAML anchors/aliases (`&id` / `*id`)

**Status:** Backlog — Bug
**Created:** 2026-06-01
**Discovery context:** Surfaced by the `checkpoint-scripts-stash-port` feature (phase P4, `next-phase.stash` differential oracle). The Python reference `next-phase.py` emits its output via `yaml.safe_dump`, which produces anchors/aliases whenever the output structure shares object references (e.g. the same `scope`/`default_verify` list reused in multiple places). The Stash oracle parses both implementations' stdout via `yaml.parse` to deep-compare them, and `yaml.parse` choked on the Python side's anchors.

---

## Problem

`yaml.parse` cannot deserialize valid YAML that uses anchors (`&name`) and aliases (`*name`). Anchors/aliases are core YAML 1.1/1.2 syntax for sharing a node by reference — any conformant emitter (PyYAML's `safe_dump`, libyaml, etc.) produces them when the same object appears more than once in the serialized graph. Stash rejects such documents outright with a `ParseError`, and `yaml.valid` returns `false` for them.

## Reproduction

```bash
$ stash -c 'let y = "a: &x [1,2]\nb: *x\n"; io.println(yaml.valid(y)); io.println(json.stringify(yaml.parse(y)));'
false
ParseError: yaml.parse: invalid YAML — ... : Aliases are not supported when deserializing into object unless ReferenceHandling is Preserve.

# Control — same data, aliases expanded — parses fine:
$ stash -c 'let y = "a: [1,2]\nb: [1,2]\n"; io.println(yaml.valid(y)); io.println(json.stringify(yaml.parse(y)));'
true
{"a":[1,2],"b":[1,2]}
```

## Blast radius

Latent today for the checkpoint workflow: the real `checkpoint.yaml` / `plan.yaml` files are flat structures with no shared object references, so the bash/Python writers never emit anchors and the Stash ports read them fine. The limitation only bit inside the *test harness*, where the Python next-phase output (a transient in-memory structure with shared list refs) was anchor-bearing; the oracle works around it by round-tripping the Python side through Python to expand aliases before comparison.

Becomes load-bearing if: any Stash program must consume YAML authored by a tool that emits anchors (common in hand-written config, Kubernetes manifests, CI YAML, docker-compose). Stash would reject documents that every other YAML consumer accepts.

## Root cause

The error text ("Aliases are not supported when deserializing into object unless ReferenceHandling is Preserve") is YamlDotNet's. The underlying deserializer in the `yaml` builtin is constructed without alias/reference resolution enabled. Likely a one-line configuration on the `DeserializerBuilder` (enable alias resolution, or `WithReferenceHandling`/equivalent) in the `yaml.parse` implementation.

## Suggested fix

Configure the YamlDotNet deserializer used by `yaml.parse` to resolve aliases into their referenced values (expanding them into independent copies in Stash's value model, since Stash dicts/arrays are not reference-shared the way the YAML graph is). Add tests: a document with a scalar anchor, a sequence anchor, a mapping anchor, and a merge-key (`<<: *base`) case — confirm each expands to the same structure as the alias-free equivalent.

## Verification

`stash -c '... yaml.parse(<anchored doc>) ...'` returns the alias-expanded structure; `yaml.valid` returns `true`; new xUnit cases in the yaml-builtin test file pass; the `next-phase` oracle's Python-side anchor-normalization workaround can be removed and still pass 8/8.

## Related

- `.kanban/0-backlog/stdlib/yaml-stringify-width-option.md` (sibling yaml-builtin gap)
- Worked around in `scripts/checkpoint/difftest_runner.stash` (`stdout_yaml_equal` axis, Python round-trip normalization). *(That oracle was retired in Milestone A.5 with the Python originals; the underlying stdlib gap remains open.)*
