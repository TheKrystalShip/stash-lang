---
description: Diagnostic — print a long-term milestone's derived ledger (done / in-flight child features) and suggest the next unit to spec. No agent invocation.
argument-hint: [milestone-slug]
---

You are reporting the state of a long-term **milestone** (a charter spanning many feature cycles). **Do not invoke any agent.** This command is purely informational — it derives completion from feature dirs, it does not change anything.

## Slug from the user

$ARGUMENTS

## Steps (run in main conversation, no agent dispatch)

### 1. Print the derived ledger

```bash
python3 scripts/checkpoint/milestone-status.py "$ARGUMENTS"
```

(The script resolves the slug itself: omitted + exactly one milestone → that one; multiple → it lists them and asks for an explicit slug.)

### 2. Read the charter's "next up"

Open `.kanban/milestones/<slug>/MILESTONE.md` and read the **Charter → Rough order & next up** and **Decisions & learnings** sections. The script reports *facts* (what's done / in-flight); the charter reports *intent* (what's next and why).

### 3. Decide what's next

| Situation | Recommend |
| --- | --- |
| A child feature is **in-flight** (`/milestone` shows it under IN-FLIGHT) | Continue it: `/resume <feature-slug>` then the command it suggests. Don't start a new unit until it lands in `4-done/`. |
| No in-flight unit, charter "Next up" names a unit | `/spec <next-unit-slug>` — and remind the user the new feature's `plan.yaml` must carry `milestone: <slug>` so the ledger can see it, and its `brief.md` header a `Milestone: <slug>` line. |
| No in-flight unit, "Next up" is stale/empty | The road needs redrawing — suggest the user (or an architect session) update the charter's "Next up" before speccing. |
| Ledger shows all known work done & DoD met | Suggest marking the milestone `Status: complete` in the charter and archiving it. |

Print:

- A 2-3 line summary: milestone, done/in-flight counts, the next unit.
- The recommended next command.
- Any drift you notice between the charter's prose and the derived ledger (e.g. the charter implies a unit is done that isn't in `4-done/`) — surface it; the ledger is the authority.
