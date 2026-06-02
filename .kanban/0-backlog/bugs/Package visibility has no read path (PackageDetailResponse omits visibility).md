# Package visibility has no read path (`PackageDetailResponse` omits `visibility`)

**Status:** Backlog — Bug
**Created:** 2026-06-02
**Discovery context:** Surfaced while re-speccing `pkg-cli-api-parity` (2026-06-02). Verifying the read paths the CLI would call revealed that visibility can be *written* but never *read back* — the original spec's claim that `GetVisibility` could be "derived from the package detail endpoint" is false: there is no `visibility` field in that response.

---

## Problem

A package's visibility tier (`public` / `internal` / `private`) can be set via
`PATCH /api/v1/packages/{scope}/{name}/visibility`, but there is no way to read it
back over HTTP. The public package-detail response
(`GET /api/v1/packages/{scope}/{name}` → `PackageDetailResponse`) carries no
`visibility` field, and there is no dedicated `GET .../visibility` route. A caller
who flips a package to `private` has no API-level confirmation of the current tier
— the value is only observable indirectly (whether an anonymous read is rejected).

This blocks a `stash pkg visibility get` command: there is nothing to read. The
`pkg-cli-api-parity` feature therefore ships `visibility set` only and defers
`visibility get` to this fix.

## Reproduction

```bash
# With a running registry and a publish-scoped token:
stash pkg visibility set @alice/widget private      # 200 OK — succeeds
curl -s http://localhost:5000/api/v1/packages/alice/widget | jq 'has("visibility")'
# Expected: true
# Actual:   false   — no "visibility" key in the JSON
```

## Blast radius

- Any caller (CLI or HTTP) that needs to confirm or display a package's current
  visibility tier. There is no read path at all.
- Latent today (no `visibility get` ships without this), but it is a visible UX
  hole the moment the visibility commands land: a user can set a tier and never
  read it back through the API.
- Does not compound, but it is a permanent asymmetry (write-only field) until closed.

## Root cause

`Stash.Registry/Contracts/PackageContracts.cs` `PackageDetailResponse` (lines
10–67) defines no `visibility` property, and
`Stash.Registry/Controllers/PackagesController.cs` `GetPackage` (response built
at ~line 111) never surfaces one. The underlying `PackageRecord` does carry a
`Visibility` column (default `public`), so the value exists in the DB — it is
simply not projected into any read response.

## Suggested fix

- (A) **Add `visibility` to `PackageDetailResponse`**, populated from
  `package.Visibility` in `GetPackage`. Public read, consistent with the rest of
  the detail body. Trade-off: the tier becomes visible to anyone who can already
  read the package detail — but for `private`/`internal` packages the detail
  endpoint is itself gated by the visibility PDP, so a hidden package's tier never
  leaks to a caller who couldn't already see the package. Then wire
  `stash pkg visibility get` to read this field.
- (B) **Add a dedicated `GET /api/v1/packages/{scope}/{name}/visibility`** route
  (publish-scoped, `RegistryAction.ReadPackageVisibility` or reuse
  `ListPackageRoles`-style publish ceiling). Keeps the tier behind a publish
  token. Trade-off: a new route + action + PDP entry for a single scalar.

Recommend **(A)** — one field, zero new routes, and the detail endpoint's existing
visibility gate already provides the right disclosure boundary.

## Verification

```bash
dotnet test --filter "FullyQualifiedName~PackageDetailResponseTests"
# After the fix: a test asserts GET package detail JSON contains "visibility"
# matching the value last set via PATCH .../visibility.
```

End-to-end: `stash pkg visibility set @alice/widget internal` followed by
`stash pkg visibility get @alice/widget` prints `internal`.

## Related

- `pkg-cli-api-parity` — this re-spec deferred `visibility get` and ships
  `visibility set` only; closing this bug unblocks the `get` half.
- `registry-scope-foundation` — introduced the three visibility tiers and the
  `PATCH .../visibility` write route.
- `Stash.Registry/Contracts/PackageContracts.cs:10` — `PackageDetailResponse`.
- `Stash.Registry/Controllers/PackagesController.cs:493` — the visibility PATCH action.
