# RFC: {{Feature Name}}

> **Status:** Draft
> **Owner:** {{user}}
> **Created:** {{YYYY-MM-DD}}
> **Slug:** {{feature-slug}}

## Summary

One or two paragraphs describing the user-visible change.

## Motivation

What problem does this solve? Who feels the pain today? What is the cost of doing nothing?

## Goals

- ...

## Non-Goals

- ...

## Design

Describe the intended end state. Keep this focused on decisions that future agents must preserve.

### Surface

Syntax, API, CLI flags, config, or examples.

### Semantics

Behavior in normal cases and edge cases.

### Implementation Path

Write the big-picture path that must stay intact across phases.

Example:

Parser recognizes syntax -> analysis records meaning -> compiler lowers it -> VM/runtime enforces it -> CLI/LSP/DAP entrypoints all use the same behavior.

## Acceptance Criteria

- End-to-end behavior that proves the feature works.
- Error behavior that proves the failure path works.
- Cross-entrypoint behavior, if relevant.

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Each phase must have a concrete `done_when` list there.

## Open Questions

- ...

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| {{YYYY-MM-DD}} | ... | ... |
