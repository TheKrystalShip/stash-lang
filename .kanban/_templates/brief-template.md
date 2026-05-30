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

### Cross-Cutting Concerns

> Any logic, vocabulary, or decision shared by **more than one phase** — one decision point, one validator, one dispatch path, one bounded set of values. For each, name its single source of truth and how a future participant is *prevented from silently skipping it*. Prefer making omission **impossible** (a forgotten participant fails to compile, or fails closed at runtime) over a meta-test that merely **detects** it; fall back to a meta-test only when the type system / architecture cannot express the invariant, and never rely on prose instructions alone. A single-subsystem or one-file feature has none — write "None." See the architect's "Designing Out Cross-Cutting Omission" guidance for the Construct / Detect / Instruct ladder.

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| _e.g. every endpoint's authz decision_ | `RegistryAuthorizeFilter` | **Construct** — global fail-closed default policy denies any endpoint that declares no decision; dispatch-coverage meta-test adds fast feedback |
| _e.g. the closed set of visibility values_ | `Visibilities` enum | **Construct** — illegal value will not compile |

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
