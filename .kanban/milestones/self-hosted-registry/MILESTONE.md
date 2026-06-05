# Milestone: Self-Hosted Registry Feature Maturity

> **Status:** Active
> **Created:** 2026-06-05
> **Slug:** self-hosted-registry

A living charter for evolving the Stash package registry from a working v1 package-feed
("store and fetch packages") into a **mature, self-hostable registry** that can *govern,
observe, and trust* packages — the backend product features that distinguish npm / PyPI /
NuGet / GitHub Packages from a bare artifact store. Run `/milestone self-hosted-registry`
for the derived ledger.

**Authoritative decision source.** This charter is the *program tracker*; it does **not**
re-decide anything. The locked feature decisions (mandatory `@scope/name`, polymorphic scope
owners, clean-break migration, visibility-with-orgs, IP-hashing defaults, advisory ownership,
lifecycle install semantics, …) live in
**`.kanban/0-backlog/registry/Registry Feature Gaps - Self-Hosted Registry Roadmap.md`** —
specifically its **Locked Decisions (2026-05-29)** addendum (D1–D20) and **Revised roadmap
(post-decision)** table, which are authoritative. Where this charter and that doc ever
disagree on a *decision*, that doc wins (its D18 makes it the master index). This charter
owns only the *program shape*: what's done, what's next, and why.

---

## Charter (living — edit freely)

### Vision

Make the self-hosted Stash registry credible for the environments people actually run a
private registry in — teams, CI systems, internal platform groups, security-conscious orgs.
The v1 mechanics (auth, publish, download, search, owners, basic audit, rate limiting) are
solid; the gaps are the *maturity* layer mature registries add **around** the feed: usage
metrics, operator-grade audit, supply-chain trust (provenance, signatures, trusted
publishing), vulnerability advisories, and release/lifecycle management. This matters both
on its own (self-hosting credibility) and as the **backing roadmap the optional website
milestone feature-detects against** — each backend feature here flips a flag in the
registry discovery `features{}` map (`GET /api/v1/.well-known/registry`), which is the seam
the website lights up against as capabilities land.

### Definition of Done (finite & checkable)

The registry surfaces the mature-registry capability set defined by the roadmap's **revised
P1–P6 ladder plus its cross-cutting items**, with each unit landed in `.kanban/4-done/`
tagged `milestone: self-hosted-registry`:

1. **P1 — Org/scope/visibility foundation** (orgs, teams, package roles, mandatory `@scope/name`,
   visibility, `RequireReadScope`) — *already shipped; see ledger note.*
2. **P2 — Download metrics + expanded admin stats.**
3. **P3 — Audit Log v2** (filters, event coverage, export, retention, tamper-evidence).
4. **P4 — Trusted publishing + provenance** (OIDC token exchange, attestation metadata).
5. **P5 — Vulnerability advisories + `stash pkg audit`** (spec from the dedicated security doc, D16).
6. **P6 — Dist-tags + lifecycle states** (yank/unlist/quarantine/block + resolver semantics, D17).
7. **Cross-cutting** (slot opportunistically): S3/MinIO storage, backup/restore, webhooks,
   operational metrics (`/metrics`, `/healthz`, `/readyz`).

The program converges — it is this enumerated ladder, not "improve the registry" forever.
Each unit's *decisions* are pre-locked in the roadmap doc; this milestone tracks their
*landing*.

### Unit Definition of Done

One unit is complete when its child `/spec` feature is in `4-done/` tagged
`milestone: self-hosted-registry`, with:

- the registry building and the **full `dotnet test` suite green** (`final_verify`);
- **registry change checklists honored** — OpenAPI coverage gate, `[PublicEndpoint]` /
  `[RegistryAuthorize]` / `[ImperativeAuthz]` classification on every new action, no magic
  auth strings (`NoMagicAuthStringsMetaTests`), declarative `[FromBody]`/`[FromQuery]` model
  binding (`RequestModelBindingMetaTests`);
- **new wire DTOs in `Stash.Registry.Contracts`** (dependency-free; no EF entities/view models
  leak), and any new **closed set** given a named home (`BoundedDomains` enum / value converter),
  never an inline literal;
- **read-path visibility preserved** on every new read endpoint — the PDP-backed predicate
  (anonymous → public only; hidden/unauthorized → 404, never an existence leak) replicated, not
  re-invented;
- the relevant **discovery `features{}` flag flipped** to `true` where the unit implements a
  pinned-`false` capability (a discovery-flags snapshot re-baseline is *expected*, not a regression);
- decisions taken **from the roadmap doc**, not relitigated.

### Rough order & next up

The revised post-decision roadmap (P1 done; the riskiest foundation went first). Detail the
next unit; the rest is a loose ordering redrawn as each unit teaches us something.

- **P1 — Org/scope/visibility foundation · DONE (untagged).** Shipped before this milestone
  existed (live evidence: `DiscoveryEndpoint.cs` pins `Organizations = true`,
  `PrivatePackages = true`; the registry ships `OrganizationsController`, `ScopesController`,
  `ScopeRecord`, `PackageRoleEntry`, the `Visibilities` enum, and scoped `{scope}/{name}`
  routes). Predates the milestone tag — see ledger note.
- **P2 — Download metrics + expanded admin stats · NEXT.** `registry-download-metrics`:
  non-blocking download counting (append-only raw events + daily/hourly rollups + retention job,
  D8), package/version metrics endpoints on scoped routes with the visibility gate (D9), expanded
  `GET /admin/stats` + `GET /admin/metrics/downloads` (storage bytes from a DB column per D10,
  top-packages via the shared `PagedResponse<T>`), operator-configurable IP handling (default
  hashed, D11), and flipping the `Metrics` discovery flag. **Explicitly defers** search
  ranking-by-downloads (a Bucket-B search straddle — do not fold in).
- **P3 — Audit Log v2 · LATER.** Cheap relative to its value — the `AuditEntry` schema already
  carries the needed columns (D12); v2 = query plumbing + new action strings + export/retention/
  tamper-evidence. Reuses the IP-handling config introduced by P2 (D11).
- **P4 — Trusted publishing + provenance · LATER.** Provider abstraction, GitHub Actions first
  (D14); provenance advisory-by-default + enforce knob (D15). Builds on P1 orgs.
- **P5 — Advisories + `stash pkg audit` · LATER.** Spec from
  `Registry Security Reports and Advisories - Vulnerability Handling Roadmap.md` (D16), not from
  the gap doc's §7 pointer.
- **P6 — Dist-tags + lifecycle states · LATER.** Install-by-tag; lifecycle install semantics per D17.
- **Cross-cutting · OPPORTUNISTIC.** S3/MinIO storage (current stub throws), backup/restore,
  webhooks, operational metrics — independent of the namespace work; slot when convenient.

### Decisions & learnings (append as you go)

| Date | Decision / learning | Why it changed the plan |
| --- | --- | --- |
| 2026-06-05 | **Milestone formalized** from the backlog roadmap doc, on the pivot after the `website` milestone reached its last unblocked phase (P3 shipped). The website charter explicitly disclaims backend ownership, so these features needed their own tracked program rather than `milestone: website` tags. | Turns the roadmap's P1–P6 ladder into a tracked program with a *derived* ledger; keeps the website ledger clean of backend work it doesn't own. |
| 2026-06-05 | **Decisions stay in the roadmap doc (D18 discipline).** This charter points at the Locked Decisions addendum instead of copying D1–D20, to avoid two drifting sources of truth. | A copied decision table rots; the single authoritative doc does not. |
| 2026-06-05 | **The discovery `features{}` map is the program's runtime seam.** Six flags are pinned `false` (`Metrics`, `Advisories`, `Provenance`, `Signatures`, `TrustedPublishing`, `VerifiedPublishers`); two are `true` (`Organizations`, `PrivatePackages`). Each unit that ships a capability flips its flag. | Makes "what's left" machine-checkable and gives the website client a feature-detection contract instead of guesswork. |

### Open questions

- **Count semantics** (P2): count a download on successful response *start* vs. stream
  *completion* — interacts with the "bytes served" metric (which wants completion). To be
  reconciled in the `registry-download-metrics` spec, not pre-locked here.
- **Scope-claim endpoints** are noted as "not yet defined anywhere" in the roadmap (§8) — a TODO
  if/when org/scope self-service is revisited; P1's shipped surface covers the foundation.
- **Metadata denormalization** (P2): whether to denormalize a `downloads` count onto
  summary/detail rows or keep metrics to dedicated endpoints — lean minimal-first; let the P2
  architect classify.

---

## Ledger (DERIVED — do not edit by hand)

Completion is computed from feature dirs, not asserted here. Each child feature's `plan.yaml`
carries `milestone: self-hosted-registry`; the status script groups them across all worktrees:

```bash
stash scripts/checkpoint/checkpoint.stash milestone-status self-hosted-registry
```

- **Done** = features in `.kanban/4-done/` tagged with this milestone.
- **In-flight** = features in `.kanban/2-in-progress/` tagged with this milestone.

**Ledger note — P1 is untagged.** The org/scope/visibility foundation shipped *before* this
milestone existed, so its feature dir(s) have no `milestone: self-hosted-registry` tag and the
derived ledger counts from **P2 onward** — it will read `done:0` until `registry-download-metrics`
promotes, even though P1 is genuinely shipped (recorded in the charter's ladder above, evidenced
by the live discovery flags). If anything written here disagrees with the command above for
*tagged* features, the command wins.
