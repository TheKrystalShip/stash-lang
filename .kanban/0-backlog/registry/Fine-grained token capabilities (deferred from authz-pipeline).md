# Fine-grained token capabilities (deferred from registry-authz-pipeline)

**Status:** Backlog
**Created:** 2026-05-30
**Discovery context:** Spun out of `registry-authz-pipeline` during architect
revision. The original design carried THREE token concepts in parallel — a
coarse `read`/`publish`/`admin` ceiling, a `TokenCapabilities` shape with
per-package / per-action `(selector, actions[])` rules, and an
`IsUnrestricted` legacy bypass flag. The user reviewed the design and
concluded the three-concept token model was confusing and added complexity
that wasn't pulling its weight relative to the security guarantee the
coarse ceiling already delivers. The fine-grained layer was carved off
into this stub.

## What this feature is

A second, additive token dimension on top of the coarse ceiling shipped in
`registry-authz-pipeline`. Inspired by npm granular access tokens, NuGet
scoped API keys, and crates.io endpoint-scoped tokens. The shape is
approximately:

```jsonc
POST /api/v1/auth/tokens
{
  "name": "ci-publish-acme-widgets",
  "ceiling": "publish",
  "expires_in": "30d",
  "capabilities": [
    { "selector": { "kind": "single_package",
                    "scope": "acme", "name": "widgets" },
      "actions": ["PublishVersion", "DeprecateVersion"] }
  ]
}
```

with selectors `AllPackages` | `ScopePackages(scope)` |
`SinglePackage(scope, name)`. The PDP would gain a third intersection
step between the ceiling check and the resource-side check:

3. If the principal is not "unrestricted," then some `Capabilities.Rules`
   element must match `(resource, action)` else
   `TokenCapabilityScopeMiss`.

Storage shape (JSON column on `TokenRecord` vs a normalized
`token_capabilities` table) is an open implementation question for that
feature.

## Why it was deferred (and why the deferral is safe)

The fine-grained layer is **purely additive**: it intersects further
inside the PDP after the ceiling check has already passed. Removing it
from this phase reopens no security bug, because:

- **D6 (the security-critical decision) is fully satisfied by the coarse
  ceiling alone.** The login response is a `read`-ceiling token; any
  publish requires an explicit `auth/tokens` call with `ceiling=publish`.
  An attacker who phishes a default login token cannot publish. That is
  the blast-radius collapse the original RFC argued for.
- **Bug A is closed by the role / scope / policy axis**, not by
  capability rules. Even an unrestricted publish-ceiling token cannot
  squat a scope it does not own — the resource-side check denies first.
- **Bug B is closed by the route-authoritative `PackageResource`**, not
  by capability rules. Manifest fields never drive a write key.

The fine-grained layer is what lets a CI principal say "this token can
publish ONLY `@acme/widgets`, nothing else in `@acme`." That is a real
defense-in-depth improvement worth shipping — but it is a follow-up, not a
prerequisite, of the authz-pipeline redesign.

## Sketch of work

- Add `TokenCapabilities`, `TokenCapabilityRule`, `TokenResourceSelector`
  records under `Stash.Registry/Auth/Authorization/`.
- Extend `UserPrincipal` with a `Capabilities` field; reconstruct it from
  a new JWT claim (and a `token_capabilities` row in `TokenRecord` — pick
  JSON column vs normalized table at design time).
- Re-introduce `AuthzDenyReason.TokenCapabilityScopeMiss`.
- Extend `POST /api/v1/auth/tokens` request shape with a `capabilities[]`
  array; reject as today if the shape is malformed.
- Add a third PDP intersection step (between ceiling check and
  resource-side check) per the design above.
- Extend `RegistryAuthzMatrixTests` (or add a companion suite) with
  capability cells: a token scoped to `single_package @acme/widgets`
  ALLOWED on that package and DENIED with `TokenCapabilityScopeMiss` on a
  sibling package and on a non-listed action.
- Document the new shape under `docs/Registry — Package Registry.md`,
  including the npm-granular-token analogy.

## Acceptance shape

- `POST /auth/tokens` with a `capabilities` array round-trips correctly
  and the resulting JWT decodes back to the same shape.
- A token with `[single_package @acme/widgets, [PublishVersion]]`:
  - ALLOWS `PUT /api/v1/packages/acme/widgets`.
  - DENIES `PUT /api/v1/packages/acme/other` with
    `403 TokenCapabilityScopeMiss`.
  - DENIES `DELETE /api/v1/packages/acme/widgets/1.0.0` with
    `403 TokenCapabilityScopeMiss` (action not in the allow-list).
- An "unrestricted" token (the default when no `capabilities` array is
  supplied) behaves exactly as today's coarse-ceiling-only tokens.
- The conformance matrix grows a capability axis; every existing cell
  continues to pass.

## Related

- Prerequisite feature: `registry-authz-pipeline` (the PDP, the closed
  action enum, the coarse ceiling, the route-authoritative
  `PackageResource`).
- Adjacent backlog: `Registry Feature Gaps - Self-Hosted Registry
  Roadmap.md` (this is one of the gaps listed there).
- Decision D8 in `.kanban/2-in-progress/registry-authz-pipeline/brief.md`
  records the deferral.
