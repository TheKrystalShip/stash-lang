---
description: Record a deliberate HUMAN decision to ship a review finding without fixing it (sets Status: accepted). Human-only — the autopilot never self-accepts. CRITICAL findings can never be accepted.
argument-hint: [slug] <Fxx> <reason...>
---

You are recording a **human decision to accept a review finding as-is** — shipping the feature
without fixing it. This is a deliberate, audited override, **not** a default path:

- **Human-only.** The autopilot never self-accepts; when it can't fix a finding it STOPs and asks.
  `/accept` exists so a human can record "we're shipping without this fix, here's why" and let
  `/done` pass.
- **The promotion gate (`promote-gate.stash`) is the authoritative enforcement.** This command sets
  the status and pre-checks the rules for fast feedback, but the gate is what actually refuses an
  invalid accept at `/done`. The two must agree; the gate is the source of truth.

## Args from the user

$ARGUMENTS

Parse: optional `<slug>` (the first token IF it is not an `Fxx` id), a required `<Fxx>` finding id,
and a required free-text `<reason>` (everything after the id). If the first token matches
`^F[0-9][0-9]$`, infer the slug from the single active feature.

## Steps (run in the main conversation — script-only, no agent)

### 1. Parse args, resolve slug

```bash
set -- $ARGUMENTS
if [[ "$1" =~ ^F[0-9][0-9]$ ]]; then
  count=$(ls -1d .kanban/2-in-progress/*/ 2>/dev/null | wc -l)
  [ "$count" -eq 1 ] || { echo "pass a slug — active: $(ls -1 .kanban/2-in-progress/ 2>/dev/null | tr '\n' ' ')" >&2; exit 1; }
  SLUG=$(basename "$(ls -1d .kanban/2-in-progress/*/)"); FID="$1"; shift
else
  SLUG="$1"; FID="$2"; shift 2 2>/dev/null || true
fi
REASON="$*"
[[ "$FID" =~ ^F[0-9][0-9]$ ]] || { echo "invalid finding id: '$FID' (want Fxx)" >&2; exit 1; }
[ -n "$REASON" ] || { echo "a reason is required: /accept $SLUG $FID <reason>" >&2; exit 1; }
REVIEW=".kanban/2-in-progress/$SLUG/review.md"
[ -f "$REVIEW" ] || { echo "no review.md for $SLUG" >&2; exit 1; }
echo "slug: $SLUG  finding: $FID  reason: $REASON"
```

### 2. Refuse if the tree is dirty

```bash
[ -z "$(git status --porcelain)" ] || { echo "tree dirty; commit or stash before accepting" >&2; exit 1; }
```

### 3. Locate the finding and ENFORCE the accept rules (fail closed)

```bash
# Read severity + status through the SHARED parser (review-findings.stash) — the
# SAME parse promote-gate uses, so /accept's pre-check can never drift from the
# gate it mirrors. `--field` exits 1 when the finding id is absent.
SEV=$(stash scripts/checkpoint/checkpoint.stash review-findings "$SLUG" --id "$FID" --field severity) \
  || { echo "finding $FID not found in $REVIEW" >&2; exit 1; }
STATUS=$(stash scripts/checkpoint/checkpoint.stash review-findings "$SLUG" --id "$FID" --field status) \
  || { echo "finding $FID not found in $REVIEW" >&2; exit 1; }

[ "$STATUS" = "open" ] || { echo "refusing: $FID status is '$STATUS', not 'open' — only an open finding can be accepted" >&2; exit 1; }
[ "$SEV" != "CRITICAL" ] || { echo "refusing: $FID is CRITICAL — it must be fixed (/resolve) or the run stops; CRITICAL findings can never be accepted" >&2; exit 1; }
echo "OK to accept $FID  [severity: $SEV]"
```

If any check fails, stop and report it to the user — do not edit anything.

### 4. Edit `review.md` (use the Edit tool, targeting THIS finding's block)

Change **only** `<Fxx>`'s status line — there are other `**Status:** open` lines for other findings,
so target this one using the finding's surrounding context (its header/title). Set:

```
**Status:** accepted
**Accepted because:** <reason>
**Accepted by:** human — <YYYY-MM-DD>
```

i.e. flip `**Status:** open` → `**Status:** accepted` for `<Fxx>` and insert the two new lines
directly below it. Leave every other finding untouched.

### 5. Ensure the deferred work is tracked

Accepting a finding means the underlying issue still ships. Make sure it is tracked: if `<Fxx>` is
not already backed by a `.kanban/0-backlog/bugs/` stub (or a `4-done/` one if it's partially
addressed), create a stub from `.kanban/_templates/bug-template.md` describing the accepted-as-is
gap, and reference it in the **Accepted because:** line. Don't let an accepted finding vanish.

### 6. Chore-commit

```bash
git add .kanban/2-in-progress/$SLUG/review.md   # + any new backlog stub
git commit -m "chore($SLUG): accept $FID — <short reason>"
```

### 7. Report

- the accepted finding id + severity + reason + commit SHA
- the count of still-open findings: `grep -c '^\*\*Status:\*\* open' .kanban/2-in-progress/$SLUG/review.md`
- next action: `/resolve` the remaining open findings, or `/done` if none remain open (the gate now
  treats this finding as satisfied)

## What this command does NOT do

- It never accepts a **CRITICAL** finding (those are fix-or-stop).
- It is never invoked autonomously — the autopilot STOPs and hands the decision to a human, who runs
  `/accept` (or `/resolve`) and then re-runs the driver.
- It does not move the feature directory or run any verify — it only records a decision; `/done`'s
  `final_verify` still runs on whatever ships.
