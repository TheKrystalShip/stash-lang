# RFC: Shared registry contracts project (Stash.Registry.Contracts)

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-02
> **Slug:** shared-registry-contracts
> **Milestone:** —

## Summary

Extract the registry's wire-facing DTOs and the wire-visible bounded-domain
constant sets out of `Stash.Registry/` into a new **dependency-free** class
library, `Stash.Registry.Contracts`. The registry continues to own them as
producer; the `Stash.Cli` package manager consumes them in place of its current
hand-declared duplicates; and a future Razor registry UI (no code in this
feature) can reference the same project without dragging in EF Core, the
DbContext, or any server-internal type.

Along the way, the CLI is brought under the project's **no-magic-strings**
regime: bounded-domain literals (`"owner"`, `"user"`, etc.) that the CLI
currently inlines become references to the shared `const string` sets, and a
sink-targeted Roslyn meta-test (modelled on the registry's
`NoMagicAuthStringsMetaTests`) starts enforcing the rule over `Stash.Cli`
during the migration — not after it.

After this feature lands, the registry contract is **one definition, two
compile-time consumers.** Deleting or renaming a field on a shared DTO
fails both the registry build and the CLI build at the point of change —
silent wire-format drift between server and client becomes impossible.

## Motivation

`Stash.Registry/Contracts/` and `Stash.Cli/PackageManager/CliJsonContext.cs`
today define **parallel, textually-distinct, structurally-equivalent** DTO
hierarchies for the same HTTP wire format. Concretely:

- The CLI hand-declares duplicates for `LoginRequest`, `AssignRoleRequest`,
  `RevokeRoleRequest`, `TokenCreateRequest`, `TokenCreateResult`,
  `TokenListResult`, `TokenListItemResult`, `TokenRefreshRequest`,
  `DeprecatePackageCliRequest`, `DeprecateVersionCliRequest`, and
  `SearchResults` / `SearchResultPackage` — at minimum the 11 types listed in
  the design-of-record's "Verified CLI duplicate map". Several already drift:
  `TokenCreateRequest` is missing the server's `name` + `capabilities` fields;
  `DeprecatePackageCliRequest` silently dropped the server's
  `[MinLength(1)]`; `SearchResults` is missing `pageSize`/`keywords`. These
  drifts are textbook duplicated-source-of-truth bugs.
- The CLI **inlines** bounded-domain magic strings —
  `RegistryClient.cs:820` literally constructs
  `new AssignRoleRequest { PrincipalType = "user", ..., Role = "owner" }`
  and `:849` does the same for `RevokeRoleRequest`. The registry, by
  contrast, has been under the `NoMagicAuthStringsMetaTests` regime since
  the `registry-authz-*` series and uses `PackageRoles.Owner` /
  `PrincipalTypes.User` end to end. The CLI would fail that check today.
- The forthcoming `pkg-cli-api-parity` feature is about to **add another
  layer of CLI duplicates** (`PackageRoleResponse`, `PackageRolesListResponse`,
  `ScopeDetailResponse`, etc. — see Coordination below). Without this
  feature, parity bakes the duplication regime deeper.

The cost of doing nothing is two diverging definitions of one wire contract
and a CLI that fails the project's #1 coding doctrine ("absolute failure if
violated") in production code that contacts the registry.

## Goals

- A new **dependency-free** assembly `Stash.Registry.Contracts` carries
  every wire-facing DTO the registry exposes today. Zero `ProjectReference`s;
  only BCL + `System.Text.Json` + `System.ComponentModel.DataAnnotations`.
- The wire-visible **bounded-domain constant sets** (package roles, token
  scopes, package visibility, principal types, scope-owner types) live in the
  same shared project as the single source of truth. Server-internal
  authz vocabulary (policy names, `AuthzDenyReason`, the
  `NoMagicAuthStringsMetaTests` sink list, EF/PDP internals) **stays in
  `Stash.Registry`** — the wire-visible/server-internal split is the central
  hazard and is performed deliberately.
- `Stash.Registry` references the new project; the public namespace stays
  `Stash.Registry.Contracts` so existing `using Stash.Registry.Contracts;`
  lines across ~6 controllers do not change.
- `Stash.Cli` references the new project; its hand-declared duplicate
  request/response DTOs are deleted; its inline bounded-domain literals are
  replaced with the shared `const string` references; `CliJsonContext`'s
  `[JsonSerializable]` entries are re-pointed at the shared types (no
  source-gen reflow needed — cross-assembly source-gen is supported).
- A new **CLI sink-targeted Roslyn meta-test** (mirror of
  `NoMagicAuthStringsMetaTests`) lands while CLI literals still exist —
  RED with a pinned exemption list — and shrinks the list to **empty** as
  the migration completes. No green-on-arrival meta-test theatre.
- The CLI keeps shipping as a Native-AOT binary with **zero new trim or AOT
  warnings** after the migration.
- `NoMagicAuthStringsMetaTests` stays green across every phase boundary
  (its sink list is append-only; re-homing constants must not break the scan).

## Non-Goals

- **No OpenAPI work** (publishing the doc, emitting enums as schemas, snapshot
  testing `openapi.json`, generated-client investigation). The design-of-record
  explicitly puts this off the critical path; it depends on the un-started
  `registry-web-api-readiness` and is captured as a future sharpening of that
  spec's done-criteria.
- **No EF Core entity, no server-internal type** moves into the shared
  project. `AuditLogResponse` currently embeds the EF entity `AuditEntry`;
  this feature adds a net-new `AuditEntryResponse` DTO and the DB→DTO
  mapping. That is the **only** net-new DTO; everything else is a move.
- **No rewrite of `RegistryClient.cs`** transport, streaming, integrity,
  AOT-driven sync `HttpClient` wrapping, refresh-token logic, or machine-id
  binding. The hand-written client stays hand-written.
- **No language / stdlib changes.** `.claude/language-changes.md`'s
  `Stash.Docs` regeneration, `Wave1/Wave2ThrowsCoverageTests`, and
  `CompletionSurfaceSnapshotTests` are stdlib-scoped and not affected.
- **No Razor / website code now.** The project is simply kept
  framework-agnostic so a future Razor app can `<ProjectReference>` it.
- **No changes to the authorization model**, the wire surface, or any
  endpoint's contract beyond the one cleanup the AuditEntry blocker forces
  (mapping the EF entity into a wire DTO is invisible on the wire — same
  field names).
- **No bounded-domain rewrite to real C# enums.** DTO properties stay
  `string`-typed (e.g. `string Role`, `string Visibility`); the
  bounded-domain *constant sets* are a separate type whose `const string`
  values feed those string properties. This is a deliberate tradeoff to
  keep the project dependency-free — see Cross-Cutting Concerns.

## Design

### End state

```
                Stash.Registry.Contracts        (no ProjectReference)
                          ^           ^
                Stash.Registry   Stash.Cli
                (producer)       (consumer)        future: Stash.Registry.Web
```

- `Stash.Registry.Contracts` carries the wire DTOs (all 7 existing files,
  moved by `git mv`) plus the wire-visible bounded-domain constant classes
  (`PackageRoles`, `TokenScopes`, `Visibilities`, `PrincipalTypes`,
  `ScopeOwnerTypes`, `OrgRoles`, `UserRoles`).
- `Stash.Registry` keeps `RegistryClaims`, server-internal types
  (`AuthzDenyReason`, policy names, EF config, `TokenCeilingConverter`,
  `ReservedScopes`), and the whole controller / service /
  database layer. See the Decision Log for the wire-visible /
  server-internal split rationale.
- `Stash.Cli` references the shared project; its duplicate DTOs are deleted;
  bounded-domain literals become named references; `CliJsonContext`
  `[JsonSerializable]` entries point at the shared types.

### Surface

The end-user surface (every HTTP request, every JSON wire field name) is
**unchanged**. This is a pure internal-refactor feature. The only
externally visible side-effects:

1. The CLI drops the `email` field from its `WhoamiInfo` projection (see
   Decision Log — it has always been `null` because `WhoamiResponse` carries
   no `email`, `UserRecord` has no `email` column, and no auth provider
   populates one).
2. `Stash.sln` gains one new project entry. `Stash.Registry.Contracts.csproj`
   is the new file.

### Semantics

- **Producer / consumer.** The registry serializes DTOs out of the shared
  project; the CLI deserializes the same types. A breaking wire change
  (rename a field, drop a property, change a JSON name) fails BOTH builds at
  compile time, at the point of change. Cross-process drift becomes
  impossible — the property of the design.
- **JSON strategy.** The shared project carries no
  `JsonSerializerContext` — it stays POCOs-only so the registry's
  reflection-based serializer keeps working unchanged and the CLI's
  `CliJsonContext` (source-gen) keeps owning AOT registration via
  cross-assembly `[JsonSerializable(typeof(...))]`.
- **Bounded-domain wire fields stay string-typed.** A field whose values are
  a closed set (e.g. `PackageRoleResponse.Role`) remains `public required
  string Role { get; set; }`. The closed set is named on the *value*
  side (`PackageRoles.Owner`, `PackageRoles.Maintainer`, ...) — the same
  const-string pattern the registry uses today. This is what lets the
  shared project be dependency-free: no enum, no converter, no JSON
  attribute beyond `[JsonPropertyName]`.
- **EF default columns.** Several EF column defaults (`PackageRecord.Visibility =
  Visibilities.Public`) and `RegistryDbContext.HasDefaultValue(...)` calls
  require **compile-time constants** — `const string`, not `static readonly`.
  Both today and after the move, the constants are `const string`. Moving
  them across an assembly boundary keeps them compile-time constants
  (C# inlines `const` values across assemblies); the EF defaults keep working.
- **AOT.** The CLI references a non-AOT-published library; only POCOs +
  attributes are exposed; no reflection or dynamic dispatch crosses the
  boundary. The CLI's source-gen `CliJsonContext` registers shared types
  via `[JsonSerializable(typeof(...))]` — supported across assemblies.
  Each phase whose work touches CLI serialization runs
  `dotnet publish Stash.Cli -c Release` as part of its verify; the final
  acceptance gate publishes the AOT binary end to end.

### Implementation Path

```
P1  new csproj + git-mv all 7 contract files into it
    + add to Stash.sln + Stash.Registry references it + handle the two
    extraction blockers (AuditEntry embed, stale OrganizationContracts using)
       |
       v
P2  move wire-visible bounded-domain const classes (PackageRoles,
    TokenScopes, Visibilities, PrincipalTypes, ScopeOwnerTypes, OrgRoles)
    out of Auth/RegistryAuthConstants.cs and into the shared project as
    the single source of truth; registry call sites continue to reference
    them by name; NoMagicAuthStringsMetaTests stays green
       |
       v
P3  CLI references the shared project; introduce the CLI sink-scan
    meta-test (RED with a pinned exemption list listing every existing CLI
    bounded-domain literal site) AT THE START of this phase; migrate the
    CLI duplicates and inline literals to the shared types / shared
    constants, shrinking the exemption list to empty; re-point
    CliJsonContext at shared types; AOT publish stays clean. The meta-test
    enforces the invariant *throughout* the migration, not after it.
       |
       v
P4  Docs (Stash.Registry/CLAUDE.md Contracts/ section, any CLI doc that
    refers to DTO locations, docs/PKG cleanup of stale owner/role
    references that still cite the old path) + repo.md pointer +
    final verification - full dotnet test suite runs, including
    NoMagicAuthStringsMetaTests, the new CLI sink-scan, and the AOT
    publish gate.
```

The boundary between every phase compiles green. There is no "throwaway
intermediate state" phase. CLI users see no behavior change at any
boundary (the wire is byte-identical).

### Cross-Cutting Concerns

Two genuine cross-cutting concerns exist in this feature. Their guards are
recorded below using the architect's Construct / Detect / Instruct ladder.

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| **Dependency-freedom of the shared assembly** (no future contributor may quietly drop in a `<ProjectReference>` to `Stash.Core` / `Stash.Registry` / EF) | `Stash.Registry.Contracts.csproj` (zero `<ProjectReference>`) | **Construct (primary)** - attempting to reference an EF or `Stash.Core` type fails to compile (no such reference exists in the new project's reference graph). The end-to-end proof is P3's `dotnet publish Stash.Cli -c Release` succeeding with no new AOT/trim warnings: a hidden non-AOT-safe dependency would surface there. **Detect (backstop)** - a tiny csproj-shape meta-test asserts the contracts project has **zero** `<ProjectReference>` elements, so a future contributor adding one breaks a test, not just code review. |
| **Bounded-domain single source of truth** for wire-visible closed sets (package roles, token scopes, visibility, principal types, scope-owner types, org roles) referenced by both the registry and the CLI | The `const string` sets in `Stash.Registry.Contracts` (moved out of `Stash.Registry/Auth/RegistryAuthConstants.cs` in P2) | **Detect (forced choice)** - the registry's existing `NoMagicAuthStringsMetaTests` (which scans known auth sinks for bare string literals) stays green across the move and continues to guard the registry. A **new** parallel CLI sink-scan (P3) guards the CLI for *its* sink set (assignments to wire-bounded DTO properties `PrincipalType`/`Role`/`Visibility`/`OwnerType`/`OrgRole` on shared-contract types), with its own pinned-list, fail-path self-test, and floor-guard. Construct (an `enum` for each domain, illegal value won't compile) was **deliberately rejected by Decision Log entry "DTO properties stay string-typed"** to keep the shared project dependency-free; the meta-tests are the forced consequence of that tradeoff. The CLI meta-test is **live during the migration**, not after - see P3 done_when. |

For each: prose in this brief (Instruct) is never the sole guard.

## Acceptance Criteria

End-to-end behavior (the test of the feature):

- **Compile-time single source of truth.** Deleting a field from the shared
  `PackageRoleResponse` (e.g. removing the `Role` property) breaks BOTH the
  `Stash.Registry` build AND the `Stash.Cli` build at the point of the
  removed property — there is no second definition for either side to fall
  back on. Same property under both consumers: the registry's
  `PackagesController` and the CLI's `RegistryClient` read/write the same
  `Stash.Registry.Contracts.PackageRoleResponse` type.
- **Dependency-free shared assembly.** `Stash.Registry.Contracts.csproj`
  contains zero `<ProjectReference>` elements. No type in the assembly
  references a `Stash.Core` type, an EF Core type, or any
  `Stash.Registry.Database.Models.*` type. A backstop meta-test fails if a
  future contributor adds a `<ProjectReference>`.
- **Native AOT clean.** `dotnet publish Stash.Cli -c Release` after the CLI
  migration succeeds with **zero new trim or AOT warnings** versus the
  pre-feature baseline.
- **CLI no-magic-strings invariant holds.** A new Roslyn meta-test scans
  `Stash.Cli/**/*.cs` for bare string-literal arguments assigned to or
  passed at wire-bounded sinks (assignment to `*.PrincipalType` / `*.Role` /
  `*.Visibility` / `*.OwnerType` / `*.OrgRole` properties of
  `Stash.Registry.Contracts` DTOs). It ships a fail-path self-test (a
  known-bad fixture trips it), a known-good self-test (a clean fixture
  passes), and a floor guard (`MinScannedFiles`) preventing a vacuous pass
  against an empty tree. Its pinned exemption list is **empty** by the end
  of P3.
- **Registry no-magic-strings invariant still holds.**
  `NoMagicAuthStringsMetaTests` (and the rest of the registry authz
  meta-test family) is green at every phase boundary and at final
  acceptance.

Error / failure path:

- **The wire surface is byte-identical.** Recorded request and response
  shapes from before this feature (the request bodies / response JSON of
  every existing controller) deserialize cleanly against the shared DTOs
  and re-serialize byte-identically; existing `RegistryRoutesTests`,
  `RegistryAuthzMatrixTests`, and `PackageServiceTests` stay green at
  every phase boundary.
- **One net-new DTO only.** Exactly one type (`AuditEntryResponse`) is
  added to the shared project. A diff of moved files shows only
  rename + minor cleanup (drop the stale `using
  Stash.Registry.Database.Models;` from `OrganizationContracts.cs`); no
  silent type duplication slipped in.

Cross-entrypoint behavior:

- **CLI and registry compile against one definition.** The C# type-symbol
  resolved by `Stash.Registry` for `PackageRoleResponse` is identical
  (same assembly, same namespace, same full name) to the one resolved by
  `Stash.Cli`. The "delete a field" criterion above is the operational
  proof.
- **Full `dotnet test` suite green.** The final-acceptance gate runs the
  whole suite — no namespace-exclusion filters — so the registry authz
  meta-tests, the new CLI sink-scan, and every contract-consuming test
  must all pass.

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Each phase has
concrete `files`, `verify`, and `done_when` lists there.

## Open Questions

- **Final placement of `OrgRoles`.** `OrgRoles` (`Owner`/`Member`) is wire-
  visible (it appears in `AddOrgMemberRequest.OrgRole`) so it belongs in the
  shared project, but the registry also consumes it server-internally. The
  plan moves it to the shared project; if any registry-internal sink
  emerges that would *not* be a wire concern (none today), it would be
  duplicated locally. The implementer should confirm there is no
  surprising server-internal use that wants to stay private.
- **`UserRoles` placement.** `UserRoles` (`User`/`Admin`) is wire-bound:
  `CreateUserRequest.Role` and `CreateUserResponse.Role` both carry these
  values over HTTP. It was moved to `Stash.Registry.Contracts/BoundedDomains.cs`
  (resolved by F04 review finding).
- **Coordination with `pkg-cli-api-parity` — file path post-merge.**
  `PackageContracts.cs` moves into `Stash.Registry.Contracts/` in P1
  *unchanged* (the derived `Owners` field stays). Parity P1 ("Drop the
  `Owners` field") then edits the file in its new location. Whichever lands
  second adjusts the file path in its plan — confirmed; documented in
  Coordination below.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-02 | **Dependency-free shared assembly** — `Stash.Registry.Contracts.csproj` has zero `<ProjectReference>`; BCL + `System.Text.Json` + `System.ComponentModel.DataAnnotations` only | The hard constraint that lets a Native-AOT CLI and a future Razor app both consume the same project without dragging in EF Core. Verified against `main`: no contract type references a `Stash.Core` type today, so the constraint is achievable without compromise. |
| 2026-06-02 | **Namespace stays `Stash.Registry.Contracts`** | Zero churn across the ~6 controllers that already `using Stash.Registry.Contracts;`. |
| 2026-06-02 | **Move files with `git mv`, not copy** | A copy duplicates type definitions in the same namespace and fails to compile. `git mv` preserves history and avoids the dup. |
| 2026-06-02 | **JSON strategy: shared project is POCOs only (no `JsonSerializerContext`)** | Lets the registry keep reflection-based JSON unchanged and the CLI keep its source-gen `CliJsonContext` — cross-assembly `[JsonSerializable]` is supported. Sharing a context would either force the registry to adopt source-gen (out of scope) or duplicate AOT registration in the CLI (defeats the purpose). |
| 2026-06-02 | **DTO wire properties stay `string`-typed** (e.g. `string Role`, `string Visibility`, `string PrincipalType`); the bounded-domain `const string` sets are a separate type whose values feed those properties | Real C# enums would force a `JsonStringEnumConverter` and enum-converter dependencies into the shared project, breaking the dependency-free constraint. This is the deliberate Construct→Detect downgrade recorded in Cross-Cutting Concerns. |
| 2026-06-02 | **Wire-visible / server-internal split:** wire-visible (`PackageRoles`, `TokenScopes`, `Visibilities`, `PrincipalTypes`, `ScopeOwnerTypes`, `OrgRoles`, `UserRoles`) → shared project; server-internal (`RegistryClaims`, `ReservedScopes`, `AuthzDenyReason`, policy names, the `NoMagicAuthStringsMetaTests` sink list, `TokenCeilingConverter`, EF config) → stays in `Stash.Registry` | Pulling the whole `RegistryAuthConstants.cs` over would leak server internals into the CLI. The wire/internal split is the central hazard of this feature and is performed deliberately. `UserRoles` IS wire-bound: `CreateUserRequest.Role` and `CreateUserResponse.Role` both carry `"user"`/`"admin"` values over HTTP — the original "never wire-bound" rationale was incorrect. `ReservedScopes` and `TokenCeilingConverter` are pure server policy and remain server-internal. |
| 2026-06-02 | **One net-new DTO: `AuditEntryResponse`** — `AdminController` maps `AuditEntry` (EF) → `AuditEntryResponse` (wire) inline; `AuditLogResponse.Entries` becomes `List<AuditEntryResponse>` | `AuditEntry` is an EF database entity and cannot live in a dependency-free DTO project. The wire field names match the existing JSON keys (action / package / version / user / target / ip / timestamp / decision / denyReason — verified against the EF entity), so the wire shape is unchanged. |
| 2026-06-02 | **Drop the stale `using Stash.Registry.Database.Models;` from `OrganizationContracts.cs`** | Verified unused in the file; clean removal during the move. |
| 2026-06-02 | **CLI `WhoamiInfo` drops the dead `email` field** when it adopts the shared `WhoamiResponse` | `RegistryClient.WhoamiDetailed()` reads an `email` field that always returns `null`: `WhoamiResponse` carries `{username, role}` only, `UserRecord` has no `email` column, no auth provider populates it. The CLI surfaces `null` to the user today. The alternative — add `email` to the contract + server plumbing — is a larger, separate feature; this one removes the dead surface. |
| 2026-06-02 | **CLI no-magic-strings meta-test lands at the START of P3, RED with a pinned exemption list listing every existing CLI bounded-domain literal site** | Per the architect's Cross-Cutting Omission doctrine: a meta-test scheduled as a final phase merges every prior phase with the invariant unenforced. The migration must run *under* the guard, not be retrofitted to satisfy it. The exemption list shrinks to empty as the migration completes; P3's `done_when` requires `len(exemptions) == 0`. |
| 2026-06-02 | **No OpenAPI work in this feature** | Off the critical path; depends on the un-started `registry-web-api-readiness`. Captured as a future sharpening of that spec's done-criteria in the design-of-record. |
| 2026-06-02 | **`Stash.Tests` re-compiles transitively** through its existing `Stash.Registry` reference and the new `Stash.Registry.Contracts` it now pulls in | No new explicit project reference is needed in `Stash.Tests.csproj`; the contracts surface arrives transitively. Verified by the P1 verify step. |

## Coordination with `pkg-cli-api-parity` (live sibling feature)

A separate session owns `.kanban/2-in-progress/pkg-cli-api-parity/` and is
**not edited by this feature**. The two coordinate as follows; the user
will relay this to the parity session for a re-spec after this feature
lands:

- **Parity becomes a consumer, not a blocker.** Its plan P2 currently calls
  for hand-declared CLI "mirror DTOs" (`PackageRoleResponse`,
  `PackageRolesListResponse`, `ScopeDetailResponse`, `ScopeChallengeBody`,
  `CreateOrgResponse`, `OrgDetailResponse`, `CreateTeamResponse`,
  `ClaimScopeRequest`, `CreateOrgRequest`, `AddOrgMemberRequest`,
  `CreateTeamRequest`, `AddTeamMemberRequest`) — exactly the duplication
  this feature removes. After this lands, parity references the shared
  types instead and registers them in `CliJsonContext`.
- **Shared file `PackageContracts.cs`.** This feature **moves**
  `PackageContracts.cs` unchanged into `Stash.Registry.Contracts/` —
  the derived `Owners` field remains. Parity P1 ("Drop `Owners` from
  `PackageDetailResponse`") then edits the file **in its new location**
  (`Stash.Registry.Contracts/PackageContracts.cs`). Dropping `Owners` is
  parity's contract cleanup, not this feature's; whichever lands second
  adjusts the file path in its plan.
- **Both touch `RegistryClient.cs` + `CliJsonContext.cs`.** Implementation
  serializes with this feature **FIRST** (creates the shared project,
  removes existing CLI duplicates, re-points `[JsonSerializable]` entries);
  parity's NEW DTOs are then added against the shared types from day one.
  The P3 CLI sink-scan meta-test enforces no-magic-strings over parity's
  new CLI code as it lands.
