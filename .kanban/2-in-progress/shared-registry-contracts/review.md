## shared-registry-contracts — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `975ffec8..310ba534` on branch `feature/shared-registry-contracts`
**Brief:** ./brief.md
**Generated:** 2026-06-02

**Verdict:** Feature is well-executed. The headline invariants all hold: `Stash.Registry.Contracts.csproj` has zero `<ProjectReference>`s, the shared types are the single source of truth (CLI consumes them; `CliContractsConsumptionTests` proves "one definition, two consumers" by `Assembly.GetName().Name` equality), `BoundedDomainPlacementTests` confirms all six const classes live in the shared assembly and every representative member is `const string` (not `static readonly`), `git mv` history is preserved, the `Stash.Registry/Contracts/` directory is gone on disk, both `OrganizationContracts.cs` AND `AdminContracts.cs` dropped the stale `using Stash.Registry.Database.Models;`, and `CliNoMagicWireStringsMetaTests` is genuinely sound after the `10bd8e5b` fix — TPA-based deterministic refs, `AssertBindingFloor` probes a real type (`AssignRoleRequest`) so a vacuous-pass scenario fails loudly, the sink is a semantic property-symbol bind (not a name proxy), self-tests construct real contracts types to prove the binding path works, `MinScannedFiles = 20` floor guard is present, and the exemption list is empty at phase close. The AOT publish is clean. Findings below are all LOW/MEDIUM, none CRITICAL.

---

## F01 — [MEDIUM] ContractsAssembly_ReferencesNoForbiddenAssemblies — assembly-name sub-check is dead for `Stash.Core`

**Status:** fixed
**Fixed in:** 3462af44
**Files:** `Stash.Tests/Registry/Contracts/ContractsAssemblyShapeTests.cs:88-100`
**Phase:** P1
**Commit:** b036d653

### Observation

`ContractsAssemblyShapeTests.ContractsAssembly_ReferencesNoForbiddenAssemblies` uses two parallel arrays — dotted namespace prefixes (defense-in-depth) and dotless assembly-name fragments (the assembly-reference check):

```csharp
var forbiddenPrefixes = new[] { "Stash.Registry.Database.Models", "Stash.Core" };
var forbiddenAssemblyNames = new[] { "StashRegistry", "StashCore" };
...
var forbiddenRefs = referencedNames
    .Where(name => forbiddenAssemblyNames.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
    .ToList();
Assert.Empty(forbiddenRefs);
```

The two assemblies that need to be forbidden in this project:

- `Stash.Registry` — overrides `<AssemblyName>StashRegistry</AssemblyName>` (verified in `Stash.Registry/Stash.Registry.csproj:9`), so the substring check `"StashRegistry".Contains("StashRegistry")` succeeds. **This half works.**
- `Stash.Core` — has no `AssemblyName` override (verified in `Stash.Core/Stash.Core.csproj`), so its assembly name is the default dotted `Stash.Core`. `"Stash.Core".Contains("StashCore")` is **false** (the dot breaks the substring). **This sub-check can never fire for Stash.Core.**

The inconsistency is visible in the test itself: the namespace-prefix array uses dotted `"Stash.Core"` while the assembly-name array dropped the dot. The defense-in-depth member-type scan immediately below uses the dotted prefixes, so a leaked `Stash.Core.X` field type would still be caught — but the *referenced-assembly* enforcement is half-vacuous.

### Why this matters

The brief's Cross-Cutting Concern #1 (dependency-freedom of the shared assembly) is real, and the `ContractsCsproj_ContainsZeroProjectReferences` text scan already catches the realistic attack vector (you cannot pull in `Stash.Core` without a `<ProjectReference>`). The member-namespace scan also has teeth. So the invariant holds today — the belt holds even though one tooth on this particular suspender is fake. The reason this is MEDIUM, not LOW: the test is presented in its summary as a backstop ("checked via reflected assembly metadata") and a future contributor reading the test for confidence about the dependency-free invariant will be misled about the strength of the assembly-name enforcement.

### Suggested fix

Replace the dotless fragments with the actual assembly names. The substring-with-OrdinalIgnoreCase pattern is the wrong shape; an exact case-sensitive match is more precise and self-documenting:

```csharp
var forbiddenAssemblyNames = new HashSet<string>(StringComparer.Ordinal)
{
    "StashRegistry",   // Stash.Registry/Stash.Registry.csproj overrides AssemblyName
    "Stash.Core",      // default assembly name (no override)
};
var forbiddenRefs = referencedNames
    .Where(name => forbiddenAssemblyNames.Contains(name))
    .ToList();
```

### Verify

```
dotnet test --filter "FullyQualifiedName~ContractsAssemblyShapeTests"
```

A small fail-path self-test that adds a fake `Stash.Core` referenced-assembly entry would also prove the sub-check has teeth — currently there's no fail-path fixture for any of the three assertions in this class.

---

## F02 — [LOW] `[UnconditionalSuppressMessage("Trimming","IL2026")]` is a consumer concern leaking into the shared contract

**Status:** fixed
**Fixed in:** 5581e0f0
**Files:** `Stash.Registry.Contracts/PackageContracts.cs:151-154`, `Stash.Registry.Contracts/PackageContracts.cs:170-173`
**Phase:** P3
**Commit:** e23c5d38

### Observation

Two trim-suppression attributes were added to `DeprecatePackageRequest.Message` and `DeprecateVersionRequest.Message` to silence IL2026 warnings that the AOT CLI publish surfaced after referencing the shared contracts:

```csharp
[MinLength(1)]
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "[MinLength] is server-side ASP.NET Core model-binding validation; " +
                    "AOT consumers (e.g. the CLI) serialise this DTO but never invoke " +
                    "the reflection-based validation path.")]
[JsonPropertyName("message")]
public required string Message { get; set; }
```

This pulls `using System.Diagnostics.CodeAnalysis;` into the shared, dependency-free, producer-owned POCO contract project to suppress a warning that originates in one specific consumer's AOT publish. The registry never needs the suppression; a future Razor UI never needs it; only `Stash.Cli`'s `PublishAot=true` publish does.

**Safety check:** I grepped `Stash.Cli/` for `Validator.`, `ValidateObject`, `ValidateValue`, and `TryValidateObject` — zero hits. The CLI never invokes the reflection-based DataAnnotations validation path. The P3/P4 AOT publish gate also passed clean. So the suppression is empirically safe today.

### Why this matters

This is a maintainability/cleanliness concern, not a runtime bug. Two issues with the current placement:

1. **Consumer concern in producer code.** A locked decision in the brief ("contracts project is dependency-free; POCOs only") is silently widened by a CLI-specific trim concern living in the shared header. Any future consumer (Razor UI, a third-party tool) inherits the suppression even though their consumption path may not even involve trimming.
2. **Causal claim is asserted, not proven.** The `Justification` string says "[MinLength] is server-side validation; AOT consumers never invoke it" as if that is a verified causal explanation for IL2026. I verified the CLI doesn't call `Validator`, and the AOT publish is empirically clean — but I did not reproduce the exact ILLink reasoning that flags `[MinLength]` as IL2026. The justification reads stronger than the evidence supports.

### Suggested fix

Pick one of the following — the second is preferable:

- **Tighten the justification** to match what was actually verified: "AOT publish gate (Stash.Cli) is empirically clean with this suppression; the CLI does not call `Validator.*`, so the reflection-based validation path is not reached at runtime." Keep the suppression in place.
- **Relocate the suppression CLI-side** via a `TrimmerRootDescriptor` XML or a `<TrimmerRootAssembly>`/suppression entry in `Stash.Cli.csproj`. This keeps the shared contract truly framework-agnostic and confines the AOT concern to the AOT consumer that needs it.

### Verify

After either fix, the AOT publish must remain clean:

```
dotnet publish Stash.Cli -c Release -o /tmp/stash-cli-aot-verify
```

---

## F03 — [LOW] Brief's "wire shape is unchanged" claim contradicts the dropped `AuditEntry.Id` wire field

**Status:** open
**Files:** `Stash.Registry.Contracts/AdminContracts.cs:80-119`, `Stash.Registry/Controllers/AdminController.cs:249-260`, `.kanban/2-in-progress/shared-registry-contracts/brief.md:334` (Decision Log row "One net-new DTO")
**Phase:** P1
**Commit:** b036d653

### Observation

Pre-feature, `AuditLogResponse.Entries` was `List<AuditEntry>` (the EF entity, `Stash.Registry/Database/Models/AuditEntry.cs`), which has `public int Id { get; set; }` as its first member. The default reflection-based JSON serializer emits all public properties, so the wire shape included `"id"` for every audit entry. The new `AuditEntryResponse` enumerates exactly 9 fields and intentionally omits `Id`; `AdminController.GetAuditLog` maps the same 9 fields in the projection (`AdminController.cs:249-260`). The Decision Log row asserts:

> "the wire field names match the existing JSON keys (action / package / version / user / target / ip / timestamp / decision / denyReason — verified against the EF entity), so the wire shape is unchanged."

The actual wire shape is changed (one fewer field). Also affects acceptance criterion "The wire surface is byte-identical."

### Why this matters

Operationally, the impact is **zero**:
- The registry is pre-release (no deployed instance — see memory `project_registry_pre_release.md`).
- No CLI code or test references `AuditLogResponse`, `AuditEntryResponse`, or `audit-log` (verified by grep across `Stash.Cli/` and `Stash.Tests/`).
- No test asserts the `id` field on the wire.
- The implementer flagged the omission explicitly in commit `b036d653` ("AuditEntryResponse omits the EF `Id` surrogate key (no test asserts it on the HTTP wire; no client documentation references it; flagged for reviewer)").

The finding is purely a precision/honesty fix in the Decision Log and acceptance-criterion language. The drop itself is defensible (internal surrogate key, not part of any documented public contract).

### Suggested fix

Either accept the drop and correct the Decision Log to acknowledge it explicitly, or restore `Id` on the wire. Recommended: accept the drop, sharpen the language. Add an `id` field to `AuditEntryResponse` only if a future audit-log consumer turns out to need it (none today).

Suggested Decision Log edit:

> "One net-new DTO: `AuditEntryResponse`. The previous wire shape (`List<AuditEntry>`) serialized the EF surrogate `id` column; the new DTO intentionally omits it (internal database surrogate, not part of any documented public contract, no consumer reads it). The remaining 9 fields are byte-identical — action / package / version / user / target / ip / timestamp / decision / denyReason."

### Verify

No code change required. After the brief edit, re-run:

```
dotnet test --filter "FullyQualifiedName~AdminController|FullyQualifiedName~AuditService"
```

---

## F04 — [LOW] Wire-visible `Role` domain split: `CreateUserRequest/Response.Role` carries `user`/`admin` but `UserRoles` is server-internal

**Status:** fixed
**Fixed in:** db9cfdad
**Files:** `Stash.Registry.Contracts/AdminContracts.cs:22`, `Stash.Registry.Contracts/AdminContracts.cs:40`, `Stash.Registry/Auth/RegistryAuthConstants.cs` (defines `UserRoles` server-internally)
**Phase:** P2
**Commit:** cf6f537d

### Observation

The brief's wire-visible / server-internal split decision (Decision Log row dated 2026-06-02) keeps `UserRoles` server-internal with the rationale:

> "`UserRoles` is a JWT *role* claim, never wire-bound in a request/response body."

But `CreateUserRequest.Role` and `CreateUserResponse.Role` (both in the shared `AdminContracts.cs` as of this feature) ARE wire-bound, and the doc comments explicitly bound their values to UserRoles values:

```csharp
// CreateUserRequest:22
/// <summary>The role to assign to the new account (e.g. <c>"user"</c> or <c>"admin"</c>).</summary>
[JsonPropertyName("role")]
public string? Role { get; set; }
```

`UserRoles` (the named const set for this exact closed domain) lives in `Stash.Registry/Auth/RegistryAuthConstants.cs` and is not accessible to any consumer of the shared contracts. The same brief asserts:

> "If the CLI ever needs it, it would be added to the shared project then; not speculatively now."

The CLI today does not expose an admin user-creation command, so this is currently latent. But it IS wire-bound in the contracts the CLI now references, so the brief's rationale ("never wire-bound") is incorrect for this domain.

### Why this matters

This is a single-source-of-truth gap that the wire-visible / server-internal split deliberately tried to prevent. The same architectural mistake the brief calls out for `OrgRoles` ("if a surprise server-internal-only use surfaces, never duplicate across the wire/internal split") applies here in mirror: a wire-visible domain whose const home isn't shared. If/when the CLI grows an admin user-create command (or a Razor UI does), the implementer will face a choice between (a) inlining `"admin"`/`"user"` literals (would trip `CliNoMagicWireStringsMetaTests` if `Role` is treated as a sink for this type), (b) re-deriving a local copy in `Stash.Cli` (duplication), or (c) moving `UserRoles` to the shared project at that point (correct fix, deferred).

### Suggested fix

Move `UserRoles` to `Stash.Registry.Contracts/BoundedDomains.cs` now, while the split is being established, rather than waiting for the first consumer. The brief's "not speculatively" framing was based on the (incorrect) premise that the domain is not wire-bound; the premise is empirically false (see `CreateUserRequest.Role` doc comment), so the speculation argument no longer applies.

Alternatively: explicitly acknowledge the gap in the brief (or in a follow-up backlog stub) so the next implementer adding an admin user-create CLI command doesn't repeat the duplicate-DTO mistake this whole feature was about removing.

### Verify

```
dotnet test --filter "FullyQualifiedName~BoundedDomainPlacementTests|FullyQualifiedName~NoMagicAuthStringsMetaTests"
```

After the move, the registry's existing `RegistryAuthConstants.UserRoles` callers should keep compiling (cross-assembly `const string` inlining preserves the EF-default and field-initializer invariants).

---

## F05 — [LOW] `Stash.Registry/CLAUDE.md` directory tree visually nests `Stash.Registry.Contracts/` inside `Stash.Registry/`

**Status:** open
**Files:** `Stash.Registry/CLAUDE.md:34-46`
**Phase:** P4
**Commit:** fad280b0

### Observation

The directory-tree block in the updated CLAUDE.md draws `Stash.Registry.Contracts/` under the `Stash.Registry/` root with a leading `│` continuation character, making it visually appear as a nested subdirectory of `Stash.Registry/`:

```
Stash.Registry/
├── Controllers/
│   └── AdminController.cs        → Stats, user mgmt, package role override, audit log
│   (Contracts/ removed — all wire DTOs live in Stash.Registry.Contracts/)
│
│   Stash.Registry.Contracts/     → Shared wire-contract assembly (dependency-free)
│   ├── AuthContracts.cs          → ...
```

`Stash.Registry.Contracts/` is a sibling project at repo root, not a subdirectory of `Stash.Registry/`. The `│   ` indentation makes a reader scanning the tree assume nesting.

### Why this matters

Cosmetic doc accuracy. CLAUDE.md is consulted by future implementers for orientation; an incorrect tree shape is a small but real friction (and would mislead an implementer searching `Stash.Registry/Stash.Registry.Contracts/` for the files).

### Suggested fix

Restructure the block so `Stash.Registry.Contracts/` appears as its own top-level entry, e.g. close out the `Stash.Registry/` tree first, then open a sibling tree:

```
Stash.Registry/
├── ...
└── Endpoints/
    └── AuthHelper.cs             → Shared auth utilities

Stash.Registry.Contracts/         → Shared wire-contract assembly (dependency-free)
├── AuthContracts.cs              → ...
├── PackageContracts.cs           → ...
└── BoundedDomains.cs             → Wire-visible bounded-domain const sets
```

### Verify

Visual inspection of the rendered Markdown.
