# Milestone: Optional Package-Registry Website

> **Status:** Active
> **Created:** 2026-06-04
> **Slug:** website

A living charter for giving Stash an **optional** human-facing package-registry website — a
browse/discover/inspect surface like npmjs.com / NuGet.org / PyPI — *without* coupling the
registry API to a frontend. The registry stays a standalone API; the website is just another
client of it. The destination is fixed (below); the route is a 5-phase ladder whose later phases
unlock as their backing features ship. Run `/milestone website` for the derived ledger.

---

## Charter (living — edit freely)

### Vision

Make the Stash registry **browsable by humans** without making the website a privileged
backdoor. An optional `Stash.Registry.Web` client — another consumer of the documented REST API,
never a private endpoint set — lets users discover packages, read rendered READMEs, inspect
versions, and (in later phases) manage ownership and evaluate trust signals. Operators can run
the registry alone, the website pointed at an existing registry, both behind one reverse proxy,
or neither (CLI/API only). The registry remains the single source of truth: the website never
owns registry data, never imports EF entities, and exposes nothing the API doesn't.

This matters for ecosystem credibility and onboarding — a registry with no web surface feels
opaque. The expensive architectural constraint a website needs is **already satisfied today**:
wire DTOs live in a dependency-free `Stash.Registry.Contracts` (no EF, no view models), the same
types the CLI consumes, and OpenAPI is published at `/openapi/v1.json`. So this milestone is
about *building clients* (and the handful of features those clients surface), not retrofitting
the API.

### Definition of Done (finite & checkable)

An optional `Stash.Registry.Web` client exists and surfaces every registry capability that is
actually available, with each roadmap phase landed in `.kanban/4-done/` tagged
`milestone: website`:

1. **Phase 1 — API Readiness** *(shipped — Bucket A polish; see ledger note)*.
2. **Phase 2 — Public browse-only website** (search, package page, version page, README render;
   no login).
3. **Phase 3 — Authenticated maintainer website** (login, token mgmt, owned packages, deprecate,
   settings).
4. **Phase 4 — Admin website** (user/role mgmt, audit search, advisories, quarantine, webhooks).
5. **Phase 5 — Trust & safety UX** (provenance, signatures, verified/trusted publishers,
   lifecycle states).

The program converges — it is these five phases, not "make the website better" forever.
**External-dependency caveat:** Phases 4–5 (and parts of 3) *surface* Bucket-B product features
that do **not** exist yet and live on a **separate** roadmap
(`.kanban/0-backlog/registry/Registry Feature Gaps - Self-Hosted Registry Roadmap.md`). This
milestone owns the **web client**, not those backend features; a later phase becomes `/spec`-able
only once its backing Bucket-B feature has shipped. The discovery endpoint's `features{}` map is
the seam the website feature-detects against as they land.

### Unit Definition of Done

One roadmap phase is complete when its child `/spec` feature is in `4-done/` tagged
`milestone: website`, with:

- the web project building and tested (`final_verify` green for whatever stack the phase's
  architecture decision selects);
- **every page / workflow it adds mapped to a documented registry API endpoint** — no
  website-only backdoor endpoints, no EF entities or view models leaking into
  `Stash.Registry.Contracts`, no UI vocabulary in API response names (the core coupling rule);
- README and any package-authored content rendered **sanitized** — package content is hostile
  input (no remote scripts/iframes/inline handlers; unsafe links rewritten/blocked);
- read-path **visibility enforcement preserved** on every surface (anonymous sees only public;
  hidden/unauthorized → 404 — never a private-package existence leak);
- for any phase that *also* touches the registry API, the registry change checklists apply
  (OpenAPI coverage gate, `[PublicEndpoint]`/`[RegistryAuthorize]` attributes, no magic auth
  strings, declarative `[FromQuery]` binding).

### Rough order & next up

A real dependency chain (each phase consumes the prior), but Phases 3–5 are *also* gated on the
external Bucket-B roadmap. Detail the next unit; the rest is sketched.

- **Phase 1 — API Readiness · DONE (2026-06-04).** Shipped as `registry-api-readiness-phase1`
  (8 phases, 2 review passes, merged `--no-ff` to `main` @ `4bf9d8e4`, final_verify green
  13344/0/6). Delivered the Bucket-A slice: shared `PagedResponse<T>` envelope (+ CLI lockstep),
  paginated `GET …/versions`, dedicated `GET …/readme` (both ETag/Last-Modified/304), search v2
  column-backed filters+sorts (+`license`/`ownerCount` on summary rows), off-by-default CORS, and
  `GET /api/v1/.well-known/registry` with the Bucket-B `features{}` flags pinned `false`
  (`Organizations`/`PrivatePackages` `true`). **Predates this milestone tag** — see ledger note.
  In `.kanban/4-done/registry-api-readiness-phase1/`.

- **Phase 2 — Public browse-only website · NEXT.** New `Stash.Registry.Web` project: search page,
  package detail page, version detail page, sanitized README rendering. No login, no Bucket B.
  Generates a typed client off `/openapi/v1.json`. **Unblocked on the API side — needs only three
  architecture decisions first** (see Open questions): repo location, rendering model
  (static SPA vs server-rendered/BFF), and origin model (same-origin vs separate-origins — the
  latter is the only thing that switches the already-shipped CORS *on*). Routes are two-segment
  scoped (`@scope/name`), so the client must handle that.

- **Phase 3 — Authenticated maintainer site · LATER.** Login, API-token mgmt, owned-packages,
  deprecate, settings. Gated on the **browser-auth decision** — the evaluation recommends a
  **backend-for-frontend** (website owns the browser session, calls the registry with a token), so
  the registry needs **no** auth change. Only the direct-browser-to-registry alternative would
  force cookies/CSRF/session-revocation into the API.

- **Phase 4 — Admin site · BLOCKED (Bucket B).** User/role mgmt, audit search/export, advisory
  mgmt, reserved prefixes, quarantine/block, webhooks, operational metrics. Most backing features
  don't exist yet (self-hosted roadmap).

- **Phase 5 — Trust & safety UX · BLOCKED (Bucket B).** Provenance/signature verification,
  verified/trusted publishers, advisories, lifecycle (yank/quarantine/unlist). Entirely gated on
  unbuilt Bucket-B features.

### Decisions & learnings (append as you go)

| Date | Decision / learning | Why it changed the plan |
| --- | --- | --- |
| 2026-06-04 | **Milestone formalized** from the backlog doc's `## API Readiness Evaluation (2026-06-04)` (`Registry Website - Optional Web Client and API Readiness.md`), the same day Phase 1 (Bucket A) shipped via `/autopilot`. User chose *formalize-as-milestone* over going straight to a Phase-2 `/spec`. | Turns a 5-phase roadmap buried in a backlog doc into a tracked program; completion now **derives** from `4-done/` milestone tags instead of being asserted in prose. |
| 2026-06-04 | **Bucket A vs Bucket B is the load-bearing split.** Phase 2 needs **zero** new registry API work — Bucket A is done. Phases 3–5 gate on Bucket-B *product features* that live on a **separate** self-hosted-registry roadmap and are surfaced via the discovery `features{}` map. | Keeps this milestone from silently absorbing backend product work it doesn't own. The web client *lights up* phases as the other roadmap ships, rather than blocking on them inside one giant spec. |
| 2026-06-04 | **Browser auth is a Phase-3 concern, not a Phase-2 gate; BFF is the recommended strategy** (registry stays token-native, no auth change). | Anonymous browse needs no auth at all. Naming this prevents over-scoping Phase 2 with cookie/CSRF/session work it doesn't need. |

### Open questions

The immediate blockers for Phase 2 are three **architecture decisions** (the backlog doc's "Open
Design Questions") that are genuine user preference, not derivable from code:

- **Repo location** — `Stash.Registry.Web` as a project *in this repo*, or a *separate* repository?
- **Rendering model** — static SPA (browser calls the registry directly) vs. server-rendered /
  backend-for-frontend app (owns sessions, calls the registry server-side)?
- **Origin model** — same-origin behind one reverse proxy, or separate origins
  (`packages.example.com` ↔ `registry-api.example.com`, which switches CORS on)?

Deferred / later-phase:

- README rendering server-side (in the web app) or client-side?
- Clean public routes (`/packages/foo`) distinct from API routes (`/api/v1/packages/foo`)?
- One configured registry backend, or multiple?
- Should a self-hosted operator be able to disable public browsing while keeping authenticated
  browsing? Hide private package names from search entirely, or show redacted to authorized users?

---

## Ledger (DERIVED — do not edit by hand)

Completion is computed from feature dirs, not asserted here. Each child feature's `plan.yaml`
carries `milestone: website`; the status script groups them across all git worktrees:

```bash
stash scripts/checkpoint/checkpoint.stash milestone-status website
```

- **Done** = features in `.kanban/4-done/` tagged with this milestone.
- **In-flight** = features in `.kanban/2-in-progress/` tagged with this milestone.

**Ledger note — Phase 1 is untagged.** `registry-api-readiness-phase1` shipped (2026-06-04, in
`4-done/`) *before* this milestone existed, so its `plan.yaml` has no `milestone: website` tag and
`4-done/` is reference-only (never re-edit). The derived ledger therefore counts from **Phase 2
onward** — it will read `done:0` until Phase 2 promotes, even though Phase 1 is genuinely shipped
(recorded in the charter's phase list above). If anything written elsewhere in this doc disagrees
with the command above for *tagged* features, the command wins.
