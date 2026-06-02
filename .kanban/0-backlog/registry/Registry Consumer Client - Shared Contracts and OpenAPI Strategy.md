# Registry Consumer Client - Shared Contracts and OpenAPI Strategy

> **Status:** Backlog
> **Created:** 2026-05-30
> **Priority:** Medium
> **Discovery context:** Design session on the registry website / API-readiness effort. While scoping `registry-web-api-readiness`, the question arose of how to keep the consumer side (the `stash pkg` CLI) in sync with the evolving registry HTTP API without hand-maintaining two parallel copies of the wire contract. The original idea — publish OpenAPI, generate a client from it, wire the generated client into the CLI — was evaluated and refined. This document records the decision and the implementation plan.

## Executive Summary

**Decision:** Eliminate the duplicated request/response contracts between `Stash.Registry` and `Stash.Cli` by introducing a single, contracts-only project — **`Stash.Registry.Contracts`** — referenced by every in-repo C# consumer (the registry, the CLI, and the future `Stash.Registry.Web`). Continue to **publish an OpenAPI document** from the registry as the public contract for third parties / non-C# consumers. Do **not** generate the CLI's client from OpenAPI.

This is a *refinement* of the original "OpenAPI → generated client → CLI" idea, not a rejection of it. "Publish OpenAPI" and "generate the CLI's client from OpenAPI" are separable decisions. The first stays. Only the second changes — and only for the CLI.

### Why the shared-contracts path wins here

Two facts about the current codebase flip the usual "just generate a client from OpenAPI" default:

1. **The CLI is Native AOT.** `Stash.Cli` ships as a native binary. It uses synchronous `HttpClient` wrapping (`.GetAwaiter().GetResult()`) and a **source-generated** `System.Text.Json` `JsonSerializerContext` (`PackageManager/CliJsonContext.cs`) precisely because reflection-based serialization is hostile to Native AOT. Generated OpenAPI clients (Kiota, NSwag, openapi-generator) historically rely on reflection-based serialization and would force ongoing work to verify and patch their AOT/trimming story. Shared POCOs, by contrast, slot into the CLI's existing source-gen context trivially — the strategy *sidesteps* the AOT question rather than fighting it.

2. **Every in-repo consumer is C#.** The CLI today, and the website (chosen as a server-rendered ASP.NET Razor app, see `Registry Website - Optional Web Client and API Readiness.md`). OpenAPI codegen earns its keep for *polyglot* consumers; for two C# consumers in the same solution, a shared project gives **compile-time** parity (a breaking wire change fails the build at the point of change, not in production) with far less machinery. There is no anticipated near-term non-C# consumer; if one appears, the published OpenAPI document already serves it.

### What the CLI keeps hand-written (by design)

The transport layer stays hand-written — it is genuine logic, not mechanical mapping, and a generator would force overrides on exactly the hard endpoints:

- AOT-driven synchronous `HttpClient` usage.
- Streaming tarball **upload** (`PUT` raw gzip body + `X-Integrity` header).
- Streaming tarball **download** with **in-flight SHA-256** integrity verification against the `X-Integrity` response header (no whole-file buffering).
- Token storage (`~/.stash/config.json`, mode 0600), automatic refresh, and machine-fingerprint binding (`X-Machine-Id`).

Codegen's "you don't have to write transport" promise is largely illusory here: the binary/streaming endpoints would need hand-written overrides regardless.

## Current State (the duplication being removed)

- **No shared contract reference exists today.** `Stash.Registry/Contracts/` and `Stash.Cli/PackageManager/CliJsonContext.cs` define **parallel, structurally-equivalent, textually-distinct** DTO hierarchies for the same wire formats.
- `Stash.Cli/PackageManager/RegistryClient.cs` (~1,126 lines) is a hand-rolled client covering **13 endpoint patterns** (package get/version/download/publish/unpublish/deprecate, search, auth login/register/whoami, token create/list/revoke/refresh, role assign).
- Some CLI responses are **not typed at all** — they are parsed ad hoc with `JsonDocument`. Adopting shared contracts therefore means *typing those for the first time*, not merely deleting duplicates.
- **The CLI inlines bounded-domain magic strings** (`Role = "owner"`, `PrincipalType = "user"` in `RegistryClient.cs` / `CliJsonContext.cs`). It has **no** equivalent of the registry's `Auth/RegistryAuthConstants.cs` and is **not** under the `NoMagicAuthStringsMetaTests` regime. It would fail that check today.

## Target Architecture

```
              Stash.Registry.Contracts      ← wire DTOs + wire-visible enums ONLY (no EF)
                 ▲           ▲           ▲
      Stash.Registry    Stash.Cli   Stash.Registry.Web      (all C#, compile-time parity)

      Stash.Registry  ──emits──►  openapi.json  ──►  external / non-C# consumers
```

`Stash.Registry.Contracts` contains **only**:

- The wire request/response DTOs (the API surface the CLI and website consume).
- The **wire-visible** bounded-domain enums / constant sets: package roles, token scopes, package visibility, principal types, search sort keys, and the discovery-document feature-flag keys.

It contains **no** EF Core entities, no database access, no server-internal types. This is the "DTO-only, no EF Core entities" coupling explicitly sanctioned by the website readiness doc.

## The Central Hazard: wire-visible vs server-internal split

This is the real work of the feature and its main failure mode. `RegistryAuthConstants.cs` and `Contracts/` today **mix** two categories:

| Wire-visible → move to `Stash.Registry.Contracts` | Server-internal → stays in `Stash.Registry` |
| --- | --- |
| Package roles (`owner`/`maintainer`/`publisher`/`reader`) | Authorization policy names (`RequireReadScope`, …) |
| Token scopes (`read`/`publish`/`admin`) | `AuthzDenyReason` enum |
| Package visibility (`public`/`private`/`internal`) | The `NoMagicAuthStringsMetaTests` sink list |
| Principal types (`user`/`team`/`org`) | EF entity models, DbContext config |
| Search sort keys, discovery feature-flag keys | Reserved-scope policy internals, ceiling logic |

Pulling the whole file into the shared project would **leak the registry's internals into the CLI** and couple it to things it has no business seeing. The split must be deliberate and reviewed.

## Bonus Win: the CLI comes under the no-magic-strings regime for free

Once the CLI references the shared enums, its current `"owner"` / `"user"` literals become references to the **same source of truth the registry enforces** — satisfying the project's bounded-domain rule (treated as a hard failure). This unlocks extending the meta-test regime to cover `Stash.Cli`: a sink-targeted scan over the CLI for bounded-domain literals reaching known sinks, shipped with its own self-test proving it has teeth (mirroring `NoMagicAuthStringsMetaTests`).

## Suggested Phases (to be turned into `plan.yaml` by `/spec` when promoted)

1. **Create `Stash.Registry.Contracts` + migrate the registry.** New contracts-only class library. Move the **wire-visible** request/response DTOs out of `Stash.Registry/Contracts/` into it; `Stash.Registry` references it and compiles green. EF models and server-internal contracts stay put.
2. **Extract wire-visible bounded-domain enums.** Move the wire-visible closed sets (roles, scopes, visibility, principal types, sort keys, feature-flag keys) into the shared project as the single source of truth. Update the registry to reference them. **Keep `NoMagicAuthStringsMetaTests` green** — its sink list must point at the new accessor locations (the list is append-only; re-home the accessors carefully).
3. **Migrate the CLI to the shared contracts.** Replace `CliJsonContext`'s duplicated DTOs with the shared types; register shared types in the CLI's source-gen `JsonSerializerContext` for AOT; type the ad-hoc `JsonDocument` parsing against shared DTOs; replace inline `"owner"`/`"user"` with shared enum references. **Transport in `RegistryClient.cs` stays hand-written.**
4. **Bring the CLI under the no-magic-strings regime.** Add a sink-targeted meta-test scanning `Stash.Cli` for bounded-domain literals reaching sinks, with a self-test and a floor guard against a vacuous pass.
5. **OpenAPI alignment + drift test.** Ensure the published OpenAPI document emits the shared enums as `enum` schemas (so an external generated client mirrors the bounded domains), and add a snapshot test of `openapi.json` so contract drift is visible in review — fits the existing `CompletionSurfaceSnapshotTests` snapshot-test pattern.
6. **Docs + AOT verification.** Update registry / contracts docs. **Critical gate:** verify the Native AOT publish of `Stash.Cli` still builds and runs with zero trim/AOT warnings after adopting shared POCOs + source-gen context. Full `dotnet test` green.

## Dependencies and Sequencing

Start **after** both of the following land, because both reshape the wire surface this project freezes into shared types:

1. **`registry-authz-pipeline` P5–P7.** P5 redesigns token issuance (login mints a `read`-ceiling token; publish requires an explicit token) — this changes the CLI's login/token DTOs and auth UX. That auth-UX change is hand-written and happens regardless of this feature; doing it before the contracts freeze avoids reworking shared types immediately after creating them.
2. **`registry-web-api-readiness`.** Adds new endpoints/DTOs (discovery document, dedicated per-version README, enriched metadata `links`, expanded search params) and the high-quality OpenAPI document. The shared contracts should be extracted once these settle so they are captured once.

Timing model: the CLI keeps working throughout (readiness changes are additive). This is **not** a big-bang migration at a mythical "API complete" milestone — it is a one-time extraction performed once the auth surface and readiness DTOs are stable, after which the CLI rides future additive changes by recompiling against the updated shared contracts (continuous, compiler-enforced parity).

## Hook into the current `registry-web-api-readiness` spec

This feature does **not** change the locked 7-item readiness scope. It only **sharpens the done-criteria of the readiness spec's "OpenAPI in prod" item**, which should already include:

- Stable `operationId`s on every operation.
- Bounded domains surfaced as OpenAPI `enum` schemas (not bare `string`).
- A snapshot test of the published `openapi.json` for drift visibility.

These are good practice regardless and are **not** on this feature's critical path under the shared-contracts approach — they make a *future* external generated client high-fidelity.

## Non-Goals

- Generating the CLI's HTTP client from OpenAPI (rejected above for the C# / Native AOT case).
- Moving EF Core entities or any server-internal type into the shared project.
- Rewriting `RegistryClient.cs` transport, streaming, integrity, or auth/token logic — these stay hand-written.
- Building a non-C# SDK now (served by the published OpenAPI document if/when needed).

## References

- Website / API-readiness concept: `.kanban/0-backlog/registry/Registry Website - Optional Web Client and API Readiness.md`
- Registry reference: `docs/Registry - Package Registry.md`
- Bounded-domain rule + meta-test pattern: `CLAUDE.md`, `Stash.Tests/Registry/Authz/NoMagicAuthStringsMetaTests.cs`
- Current CLI client: `Stash.Cli/PackageManager/RegistryClient.cs`, `Stash.Cli/PackageManager/CliJsonContext.cs`
- Registry contracts: `Stash.Registry/Contracts/`, `Stash.Registry/Auth/RegistryAuthConstants.cs`

---

## Investigation Addendum — 2026-06-02 (orchestrator, pre-spec)

A pre-spec investigation verified this doc against the current `main` and surfaced concrete details to fold into `plan.yaml` when this is promoted via `/spec`. The design below is unchanged; this addendum adds verified specifics and the coordination with `pkg-cli-api-parity`.

> **Sequencing (superseded — read latest):** An initial 2026-06-02 call deferred this until `pkg-cli-api-parity` landed (parity was about to reshape the wire surface). **Reversed later the same day:** the parity spec was re-specced to match the *current* registry API contract (it adds **no new server DTOs** — every role/scope/org/visibility DTO already exists in `Stash.Registry/Contracts/`). The wire surface is therefore settled, so this feature is being **specced and built FIRST** as the foundation; the parity spec is then updated to **consume** the shared project instead of declaring CLI "mirror DTOs."

### Coordination with `pkg-cli-api-parity`

`pkg-cli-api-parity` is now a downstream **consumer**, not a blocker. Its re-specced plan (`.kanban/2-in-progress/pkg-cli-api-parity/`) is a CLI-surface feature whose P2 currently plans to hand-declare CLI mirror DTOs (`PackageRoleResponse`, `PackageRolesListResponse`, `ScopeDetailResponse`, `ScopeChallengeBody`, `CreateOrgResponse`, `OrgDetailResponse`, `CreateTeamResponse`, `ClaimScopeRequest`, `CreateOrgRequest`, `AddOrgMemberRequest`, `CreateTeamRequest`, `AddTeamMemberRequest`) and register them in `CliJsonContext` — **exactly the duplication this project removes**. After this lands, the parity session updates that plan to consume the shared types.

Two concrete coordination points:

1. **`PackageContracts.cs` is shared between both features.** This feature **moves** `PackageContracts.cs` into the shared project unchanged (it keeps the derived `Owners` field). Parity P1 ("Drop owners field from `PackageDetailResponse`") then edits the file **in its new location** (`Stash.Registry.Contracts/`). This feature does **not** drop `Owners` (out of scope — that's parity's contract cleanup). Whoever lands second adjusts the file path.
2. **Implementation serializes on the CLI files.** Both touch `RegistryClient.cs` and `CliJsonContext.cs`. This feature lands first (creates the project, migrates the CLI's *existing* duplicates to shared types, re-points `CliJsonContext`); parity rebases onto the shared types for its *new* methods/commands. A CLI no-magic-strings meta-test landed here (see scope decision) then enforces the regime over parity's new CLI code from day one.

Upstream deps (from "Dependencies and Sequencing" above): `registry-authz-pipeline` is **DONE** (`.kanban/4-done/registry-authz-pipeline`); `registry-web-api-readiness` is still backlog and additive (a *soft* dep — its future DTOs are added to the shared project when they land).

### Verified extraction blockers (handle in the migration phase)

1. **`AdminContracts.AuditLogResponse` embeds `AuditEntry`, an EF database entity** (`Stash.Registry/Database/Models/AuditEntry.cs`). A dependency-free DTO project cannot carry it. Add a net-new `AuditEntryResponse` DTO (wire fields: action, package, version, user, target, ip, timestamp, decision, denyReason) and map DB→DTO in `AdminController`. Expected to be the **only** net-new DTO (no speculative Razor DTOs — move what exists).
2. **`OrganizationContracts.cs` carries a stale `using Stash.Registry.Database.Models`** (no DB type actually used). Drop it during the move.

### Verified CLI duplicate map (CLI re-declares ↔ registry owns) + drift

- `LoginRequest`↔`LoginRequest` (nullability differs); `AssignRoleRequest`/`RevokeRoleRequest` identical; `TokenRefreshRequest`↔`RefreshTokenRequest` (same shape, different name); `DeprecatePackageCliRequest`↔`DeprecatePackageRequest` and `DeprecateVersionCliRequest`↔`DeprecateVersionRequest` (renamed; CLI dropped `[MinLength(1)]` validation); `TokenCreateRequest`↔`TokenCreateRequest` (CLI **missing** `name` + `capabilities`); `TokenCreateResult`↔`TokenCreateResponse`; `TokenListResult`/`TokenListItemResult`↔`TokenListResponse`/`TokenListItem`; `SearchResults`/`SearchResultPackage`↔`SearchResponse`/`PackageSummaryResponse` (CLI missing `pageSize`/`keywords`). Adopting shared names = renaming CLI call sites.
- CLI-only projections that **stay CLI-side** (not wire DTOs): `LoginResult` (client-side `MachineId`), `WhoamiInfo`, `UserConfig`/`RegistryEntry`.
- **Honest tiering:** sharing the *request* DTOs (Tier 1) does **not** fix the runtime drifts above — those live in the CLI's raw `JsonDocument` response parsing (`Login`, `Whoami`, `GetManifest`, versions/latest, integrity, deprecation). Typing those against shared response DTOs is a separable **Tier 2** phase. The brief must not claim Tier 1 "eliminates drift."

### Behavior decision to resolve at spec time

**whoami `email` is dead code.** `RegistryClient.WhoamiDetailed()` reads an `email` field, but `WhoamiResponse` returns only `{username, role}` and `UserRecord` has no email column — it is always null today. Recommended: the CLI **drops** the field when it adopts shared `WhoamiResponse`. Alternative (larger, likely a separate feature): add `email` to the contract + the server plumbing (UserRecord column + auth provider) to populate it.

### Scope decision (reconciled with this doc)

Take this doc's **fuller** scope — extract the wire-visible bounded-domain **constant sets** too and bring the CLI under the no-magic-strings regime (the project's #1 doctrine, "absolute failure if violated") — **not** a minimal DTO-only extraction. This does not violate the dependency-free constraint: `const string` sets carry no dependencies and DTO properties stay `string`. Phase it so pure extraction lands first; the CLI no-magic-strings meta-test arrives as a **later** phase that goes **RED with an exemption list** as the shared constants land, shrinking to empty as call sites migrate (per `.claude/agents/architect.md` — never schedule a meta-test as the final phase).

### Locked architecture (confirmed against `main`)

Namespace stays `Stash.Registry.Contracts` (zero churn across ~6 controllers that `using` it); **`git mv`, not copy** (a copy duplicates type defs in the same namespace and won't compile); new `Stash.Registry.Contracts.csproj` with **zero project references** (BCL + `System.Text.Json` + `System.ComponentModel.DataAnnotations` only — verified: no contract references a `Stash.Core` type); CLI keeps its source-gen `CliJsonContext` pointed at the shared types (cross-assembly `[JsonSerializable]` is supported); registry serialization stays reflection-based / untouched. Add the project to `Stash.sln`; confirm `Stash.Tests` recompiles via its transitive `Stash.Registry` reference.
