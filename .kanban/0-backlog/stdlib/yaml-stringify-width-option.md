# `yaml.stringify` optional `width` (fold) option

Status: deferred
Created: 2026-05-31
Discovery context: YAML round-trip spike for the checkpoint-script rewrite (see `path-match-predicate.md`).

## What

Add an optional `width` parameter to `yaml.stringify` matching PyYAML's `width=100` line-folding, so long scalars (e.g. checkpoint `notes`) wrap instead of emitting on one long line.

## Why deferred (decision 2026-05-31)

Stash's `yaml.stringify` is already **deterministic and key-order-preserving** (verified), so it is fully correct for the rewrite as-is — it just emits long scalars unfolded on a single line where the old Python `save_yaml` folded at width 100. That is a cosmetic/readability difference, not a correctness one. The readiness feature was deliberately kept to `path.match` only.

`sort_keys` was considered and rejected outright: Stash already preserves insertion order by default (== Python `sort_keys=False`), so the param would be redundant.

## When to pick this up

If, once the checkpoint scripts are rewritten in Stash and own the YAML files, the unfolded long `notes` lines prove annoying in real diffs/review. Routes through the full `language-changes.md` checklist (stdlib impl + metadata regen + completion snapshot + example + tests).

## Related

- `path-match-predicate.md` — the actual blocking gap for the rewrite.
