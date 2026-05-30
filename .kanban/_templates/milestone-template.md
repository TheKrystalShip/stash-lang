# Milestone: {{Milestone Name}}

> **Status:** Active
> **Created:** {{YYYY-MM-DD}}
> **Slug:** {{milestone-slug}}

A milestone is a **living charter for long-term work that spans many feature cycles** —
work too large for one `/spec`→`/done`. It holds the *destination* and your *current
thinking*; the road is built as you go (rolling-wave: detail the next unit, sketch the rest).

**Two layers, two different rules** — keep them separate:

- The **Charter** below is hand-maintained and evolves freely. It is the authority on the
  *future* (vision, end-goal, what's next, what you've learned).
- The **Ledger** is *derived, never hand-written*. It is the authority on the *past* (what's
  actually done) and is computed from `4-done/` by `scripts/checkpoint/milestone-status.py`.
  A living doc drifts; the completion record must not, so it is not written here.

Run `/milestone {{milestone-slug}}` to see the derived ledger + next-action advice.

---

## Charter (living — edit freely)

### Vision

What is the end state, and why does it matter? One or two paragraphs.

### Definition of Done (finite & checkable)

The destination is fixed even though the route is emergent. State a concrete, checkable
end-goal — "every project's cross-cutting concerns classified and resolved," not "make
things better." Without this, the program never converges.

### Unit Definition of Done

What makes one *unit* (one child `/spec` feature) complete. Reused across units so each spec
inherits the same bar.

### Rough order & next up

A revisable sketch, **not** a contract. Detail only the next unit (or two); the rest is a
loose ordering you redraw as each unit teaches you something. Analyzing one unit may split,
merge, or surface units you didn't know existed — that's expected.

- **Next up:** `{{next-unit-slug}}` — one line on scope / why it's next.
- Later (rough): ...

### Decisions & learnings (append as you go)

| Date | Decision / learning | Why it changed the plan |
| --- | --- | --- |
| {{YYYY-MM-DD}} | ... | ... |

### Open questions

- ...

---

## Ledger (DERIVED — do not edit by hand)

Completion is computed from feature dirs, not asserted here. Each child feature's
`plan.yaml` carries `milestone: {{milestone-slug}}`; the status script groups them:

- **Done** = features in `.kanban/4-done/` tagged with this milestone.
- **In-flight** = features in `.kanban/2-in-progress/` tagged with this milestone.

```bash
python3 scripts/checkpoint/milestone-status.py {{milestone-slug}}
```

If anything written here ever disagrees with that command's output, the command wins —
which is why this section stays empty.
