# Registry Authorization Filter — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `83fd890..8ded1fb` on `main`
**Brief:** ./brief.md
**Generated:** 2026-05-30

Baseline at HEAD on the curated filter: 9525 passed, 0 failed, 101 skipped (the two
transient failures reported are documented parallel-execution flakies and not feature
regressions). The new `AuthzDispatchCoverageMetaTests` family and the new
`RegistryAuthzAuditMutationTests` rows are green.

The filter, attribute, shared principal factory, deny-helper, resource resolver, and
dispatch-coverage meta-test are well-shaped and largely faithful to the brief. The four
imperative endpoints (`PublishPackage`, `DeleteOrg`, `ClaimScope`, `DeleteScope`) carry
`[ImperativeAuthz]`, the principal-helper duplication is gone, DI lifetimes are correct,
and the meta-test has genuine teeth (production assertion + fail-path fixture + pinned
imperative set).

Two real findings — both centered on the brief's "byte-identical body shape" promise — and
two minor maintainability notes follow.

---

## F01 — [IMPORTANT] PDP-deny body shape regressed on GetVersion / DownloadVersion (no longer "Version 'v' of package 'pkg' not found.")

**Status:** open
**Files:** `Stash.Registry/Controllers/PackagesController.cs:132-181`, `Stash.Registry/Auth/Authorization/RegistryAuthorizeFilter.cs:92-99`
**Phase:** P2
**Commit:** f6d4deb

### Observation

Before the refactor `PackagesController.GetVersion` and `DownloadVersion` mapped a PDP
`VisibilityHidden` / `PackageNotFound` denial to a **version-scoped** 404 body:

```csharp
return StatusCode(DenyReasonToStatus(decision.Reason),
    new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });
```

(see baseline `83fd890:Stash.Registry/Controllers/PackagesController.cs:185-188` and `:213-217`).

After the refactor those endpoints carry `[RegistryAuthorize(ReadPackageVersion)]` /
`[RegistryAuthorize(DownloadPackageVersion)]` and the deny path is rendered exclusively by
the filter, which builds the not-found message from the `PackageResource` alone:

```csharp
// RegistryAuthorizeFilter.cs:94-99
if (resource is PackageResource pkg &&
    (decision.Reason == AuthzDenyReason.VisibilityHidden ||
     decision.Reason == AuthzDenyReason.PackageNotFound))
{
    notFoundMessage = $"Package '{pkg.FullName}' not found.";
}
```

The `{version}` segment is lost — denied callers now see `"Package '@scope/name' not
found."` on routes that used to say `"Version 'v' of package '@scope/name' not found."`.

`RegistryAuthzMatrixTests.VisibilityAxis_Matrix` does not catch this because the matching
matrix rows (`vis.private.anon.deny`, `vis.internal.anon.deny`, etc.) pass
`expectedBody = null` and the test only asserts the body when `expectedBody != null`
(`RegistryAuthzMatrixTests.cs:103-107, 152-156, 212-214`).

### Why this matters

The brief's headline Acceptance Criterion is "Every covered (action × principal) row
produces the same status code AND the same `ErrorResponse` body shape as before the
refactor" and the Decision Log records the uniform-audit change as the *sole* sanctioned
behavior change. This silent body-shape regression on two read endpoints is not covered by
that exception. Any client UX (CLI or web) that surfaces the `Error` string verbatim now
shows a less informative message for version-not-found denials and may also surface a
misleading message when the version exists but is hidden by visibility.

### Suggested fix

Either (a) widen the deny-helper to accept a version-aware not-found message and have the
filter pass it for `ReadPackageVersion`/`DownloadPackageVersion`, or (b) drop
`[RegistryAuthorize]` from those two endpoints and keep their inline PDP block (they were
already filling in the version-scoped body manually). Option (a) is cleaner — extend the
resource resolver to carry the route `{version}` into a small `PackageVersionResource`
(or extend `PackageResource` with an optional `Version`), and have the filter format
`"Version 'v' of package '@scope/name' not found."` when the version is present.

Whichever path is chosen, add a matrix row that pins the body literal (not just status)
for at least one visibility-hidden denial on `GetVersion`/`DownloadVersion`, so the next
refactor cannot drop this again.

### Verify

```
dotnet test --filter "FullyQualifiedName~RegistryAuthzMatrixTests"
dotnet test --filter "FullyQualifiedName~Registry&FullyQualifiedName!~BasePathIntegrationTests&Category!=SqliteConcurrencyStress"
```

---

## F02 — [MINOR] PDP-deny body now carries a `Message` field on package 404 paths that previously omitted it

**Status:** open
**Files:** `Stash.Registry/Auth/Authorization/AuthzDenyResponse.cs:39-60`, `Stash.Registry/Controllers/PackagesController.cs:80-130, 132-152, 154-181`
**Phase:** P2
**Commit:** f6d4deb

### Observation

Original `PackagesController` PDP-deny bodies on read endpoints set only `Error`:

```csharp
new ErrorResponse { Error = $"Package '{packageName}' not found." }
```

`AuthzDenyResponse.For(decision, notFoundMessage)` always sets both fields:

```csharp
var body = new ErrorResponse { Error = errorText, Message = decision.Detail };
```

So a `VisibilityHidden` denial on `GetPackage` / `GetVersion` / `DownloadVersion` now
ships an extra `Message` field carrying `decision.Detail` (typically a non-empty PDP
explanation string), where before that field was absent / null. `RegistryAuthzMatrixTests`
doesn't observe this — same `expectedBody = null` shortcut as F01.

### Why this matters

Strictly speaking this is a wire-shape change on every package 404 deny — extra field in
the JSON response. For most clients this is harmless (extra JSON fields are ignored) but
it does count against the brief's "byte-identical body shape" promise and is the kind of
silent shape drift `AuthzMatrixData` was meant to gate.

### Suggested fix

Either drop `Message` from `AuthzDenyResponse.For` when `notFoundMessage != null` (so the
package 404 body matches its pre-refactor shape and only the non-404 paths gain
`Message`), or accept the change and add an explicit decision-log entry beside the
"uniform deny-audit" entry in `brief.md` acknowledging the additive `Message` field.

### Verify

```
dotnet test --filter "FullyQualifiedName~RegistryAuthzMatrixTests"
```

---

## F03 — [MINOR] `CreateOrg` PDP now runs before body validation, flipping 400→403 on bad payloads from insufficient-ceiling callers

**Status:** open
**Files:** `Stash.Registry/Controllers/OrganizationsController.cs:58-109`
**Phase:** P2
**Commit:** f6d4deb

### Observation

Original `OrganizationsController.CreateOrg` performed body parsing and name validation
(`Invalid JSON body.`, `Organization name is required.`, `Organization name must be 1-39
characters...`, `The name '{name}' is reserved...`) *before* calling
`_authorizer.AuthorizeAsync(principal, CreateOrg, new OrgResource(name))`
(baseline `83fd890:OrganizationsController.cs:71-110`).

After the refactor the filter runs before the action body, so the PDP fires first using
`OrgResource("")` (the route has no `{org}` param — `RegistryActionResourceResolver` falls
through to an empty string). For a caller whose ceiling is insufficient *and* whose body
is malformed:

- Before: HTTP 400 (`Invalid JSON body.` / validation message)
- After:  HTTP 403 `TokenScopeInsufficient`

The PDP decision itself is unchanged (the `CreateOrg` PDP rule ignores the resource —
see `RegistryAuthorizer.cs:203-205`) and an authenticated allow path is identical. Only
the *deny* path on a malformed body changes precedence.

### Why this matters

This is a behavioral change not called out anywhere in the brief, Decision Log, or
acceptance criteria. It is unlikely to break real clients (a malformed payload to an
endpoint they cannot call is always an error), but the brief's strict "no behavior change
beyond uniform-audit" framing should either acknowledge this precedence change or the
implementation should preserve the old order. The same precedence shift applies to any
other PDP-then-validate endpoint converted in P2 (`AddOrgMember`, `CreateTeam`,
`AddTeamMember`, `AssignRole`, `RevokeRole`, `SetVisibility`, etc.), but `CreateOrg` is
the most observable because the resource the PDP saw used to be the body-supplied name
and is now empty.

### Suggested fix

Pick one:

1. **Acknowledge:** add a one-line Decision-Log entry to `brief.md` recording that the
   filter inverts the validate-then-PDP order on `CreateOrg` (and friends) — i.e. callers
   that fail both auth and body validation now see the auth failure first, which is the
   typical filter-pipeline convention anyway.
2. **Preserve:** drop `[RegistryAuthorize]` from `CreateOrg` and keep an inline PDP call
   after body validation, marking it `[ImperativeAuthz("PDP requires body-supplied org
   name; folded in registry-authz-pdp-completion")]` — this would, however, expand the
   pinned imperative set.

Option 1 is cheaper and matches normal MVC pipeline expectations.

### Verify

```
dotnet test --filter "FullyQualifiedName~RegistryAuthzMatrixTests|FullyQualifiedName~RegistryAuthzAuditMutationTests"
```

---

## F04 — [MINOR] Resource resolver carries unreachable entries for `Login` / `Register`; minor dead-code risk

**Status:** open
**Files:** `Stash.Registry/Auth/Authorization/RegistryActionResourceResolver.cs:79-84`
**Phase:** P1
**Commit:** c2ffa61

### Observation

The resolver maps `RegistryAction.Login` and `RegistryAction.Register` to
`new PrincipalSelfResource()`, but no controller decorates an action with
`[RegistryAuthorize(RegistryAction.Login)]` or `[RegistryAuthorize(RegistryAction.Register)]`
— `AuthController.Login`/`Register` are `[PublicEndpoint]`-only. The two enum arms in the
switch are unreachable as wired today.

### Why this matters

Pure cleanup. The unreachable arms aren't harmful, but they suggest a coverage check the
implementation does not have — a future reader might assume Login/Register flow through
the filter. If you intentionally want the filter to be invokable on those actions later
(e.g. anti-abuse PDP rules on Login), document that; otherwise drop the entries to make
the resolver match the actual dispatch surface.

### Suggested fix

Either delete the two arms from `RegistryActionResourceResolver.Resolve` (cheapest), or
leave them and add a comment that they are reserved for future PDP wiring of the auth
endpoints. No test change required either way.

### Verify

```
dotnet build Stash.Registry
dotnet test --filter "FullyQualifiedName~AuthzDispatchCoverageMetaTests|FullyQualifiedName~RegistryAuthzMatrixTests"
```

---
