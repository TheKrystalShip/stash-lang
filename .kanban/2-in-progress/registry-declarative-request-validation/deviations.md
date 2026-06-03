# Implementer deviations — for the reviewer to adjudicate

This file accumulates **plan deviations and ratification requests** reported by the implementer
agents during the phase loop. The reviewer reads only the diff + `brief.md`; these deviations
otherwise live only in the driver's context. They are surfaced here (and in the `/feature-review`
dispatch) so the review adjudicates them rather than rubber-stamping pre-rationalized code.

**Driver note on a review trap:** some deviations are pre-rationalized *in code comments*
(e.g. `// inlined here to keep Stash.Registry.Contracts dependency-free`). A diff-only reviewer
reads those as already-settled. Treat each item below as an OPEN question to confirm or refute,
not as a justified decision.

---

## P1 — Roslyn meta-test lands RED with pinned exemptions (commit 82627ac3, +d7f22643)

- **P1-D1 — third detection sink added (`Request.Query`).** `done_when` named two sinks (manual
  `DeserializeAsync`, Contracts-typed param missing a binding attr), but the 12-entry pin includes
  2 query-string actions (`Admin.GetAuditLog`, `Search.Search`) that neither named sink catches.
  Implementer added sink (c) for direct `Request.Query` access so the live violation set == 12.
  **Driver verdict: sound** — the `done_when` observable contract (live set == 12-pin) was the real
  spec; the mechanism was under-specified. Confirm the third sink is correctly scoped (doesn't
  over-flag legitimate query reads outside controller actions).
- **P1-D2 — `Mvc.Core` metadata reference added** so the Roslyn semantic model can bind `[FromBody]`
  in sink (b)'s attribute resolution; binding-floor probe now also asserts `FromBodyAttribute`
  resolves. **Driver verdict: necessary and correct.**
- **P1-D3 — sink (b) positive self-test added** (`d7f22643`, after the chore commit) — `done_when`
  only mandated a sink-(a) positive + clean-`[FromBody]` negative; sink (b) had no teeth-proof.
  **Driver verdict: strengthens the test.** Note it landed as a separate commit *after* the P2
  checkpoint chore — harmless (tree clean, P1 recorded commit reachable) but worth noting.

## P2 — Contracts DTOs gain DataAnnotations + factory aggregates (commit 4df0db13)

- **P2-D1 — custom attributes REIMPLEMENT instead of WRAP (MOST SIGNIFICANT).** `done_when` says
  `ScopeGrammarAttribute` "wraps `PackageManifest.IsValidScopeName`" and `TokenExpiryAttribute`
  "wraps `AuthHelper.ParseTokenExpiry`". Literal wrapping is **impossible**: `Stash.Registry.Contracts`
  is deliberately dependency-free, but `IsValidScopeName` lives in `Stash.Core` and `ParseTokenExpiry`
  in `Stash.Registry` — referencing either would create a forbidden/circular dep. So both were
  inlined (duplicated).
  - **Driver discriminating check (done):** the inlined scope regex `^[a-z][a-z0-9-]{0,38}$` is
    **byte-for-byte identical** to the source-generated `[GeneratedRegex]` on
    `PackagingRegexes.ScopeSegment()` (Stash.Core/Common/PackageManifest.cs:32). So it has **not**
    diverged — currently correct. Severity = **single-source-of-truth duplication (medium)**, NOT a
    live bug. The defect is *future drift*: two copies of a bounded grammar (CLAUDE.md doctrine:
    "duplicated closed set == inline literal").
  - **Durable fix to weigh:** relocate the canonical grammar (and the `d/h/m/int` parse) INTO
    `Stash.Registry.Contracts` so the controllers' existing call sites AND the attributes share ONE
    definition. **Caveat for the resolver (advisor-flagged):** this is NOT a pure dedup —
    `TokenExpiryAttribute` intentionally ADDS a `>=1h` floor that `AuthHelper.ParseTokenExpiry` does
    not have. The *parse* can be shared; the floor is validation-only. A naive "move the helper"
    that routes the existing token-creation call sites through the floored version would CHANGE
    token-creation behavior. Share parse, keep floor in the attribute.
  - **Minimum acceptable resolution if relocation is rejected:** a cross-check test asserting
    `ScopeGrammarAttribute` agrees with `IsValidScopeName` over a corpus (incl. boundary/invalid
    cases), so drift fails CI.
- **P2-D2 — OpenAPI snapshot regenerated.** Brief implied "no snapshot bump." Forced because
  `AddOrgMemberRequest` is already `[FromBody]`-bound in `OrganizationsController.AddMember`, so
  `[Required]` on `Username` surfaces as `"required": ["username"]`. Diff = 3 lines, one required
  field on one schema; no 400-shape / `ErrorResponse` $ref change; AuthzMatrix green. **Driver
  verdict: legitimate** — the brief's "no bump" was about the factory/ErrorResponse change and
  didn't account for `[Required]` on an already-bound DTO. Confirm the snapshot diff is exactly that.
- **P2-D3 — `plan.yaml` P2 verify command corrected.** Was `dotnet build A B C` (multi-project,
  which `dotnet build` rejects) → `dotnet build`. Within the "stale verify command" correction
  allowance, but note the implementer edited a checkpoint artifact (`plan.yaml`).
- **P2-D4 — factory adds `.Where(m => !string.IsNullOrEmpty(m))`** before the `"; "` join, beyond
  the literal `done_when` formula, to avoid spurious delimiters. **Driver verdict: defensible.**
- **P2-D5 — AOT/trim cleanliness unverified at phase level.** `StringLength`/`Range` carry
  `[UnconditionalSuppressMessage]` per the `DeprecatePackageRequest.Message` precedent; `[Required]`
  + the two custom attributes have no `[RequiresUnreferencedCode]` path. `final_verify` runs
  `dotnet test`, NOT the AOT publish — so trim cleanliness of the new attributes is NOT gated by
  `/done`. The AOT enum self-test lives in `build.stash` (manual). Reviewer: confirm whether an AOT
  publish smoke is warranted before merge, or accept the precedent-mirroring as sufficient.
