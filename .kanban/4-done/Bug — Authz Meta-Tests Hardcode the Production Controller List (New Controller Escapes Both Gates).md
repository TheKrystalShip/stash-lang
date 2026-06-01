# Authz meta-tests hardcode the production controller list — a new controller silently escapes both coverage gates

**Status:** Fixed — 2026-06-01 (commit `9cb6d1df`)
**Created:** 2026-06-01
**Discovery context:** Surfaced by an adversarial refute pass while reviewing whether the bug *"AuthzCoverageMetaTests accepts class-level Authorize as PDP coverage (does not verify dispatch)"* (now in `4-done/`) was genuinely fixed. The dispatch fix (`registry-authz-filter` P3, `efa5f11b`) holds, but the refute pass found this adjacent residual gap in the gate itself.

---

## Problem

Both registry authorization meta-tests enumerate a **hardcoded list of six controller types** rather than discovering controllers by reflection:

- `Stash.Tests/Registry/AuthzCoverageMetaTests.cs:40-48` — default-deny (`[Authorize]`/`[PublicEndpoint]` classification)
- `Stash.Tests/Registry/Authz/AuthzDispatchCoverageMetaTests.cs:49-57` — PDP-dispatch classification (`[RegistryAuthorize]`/`[ImperativeAuthz]`)

The list is **identical and copy-pasted across both files**:

```csharp
private static readonly IReadOnlyList<Type> ProductionControllers =
[
    typeof(AuthController),
    typeof(PackagesController),
    typeof(OrganizationsController),
    typeof(ScopesController),
    typeof(SearchController),
    typeof(AdminController),
];
```

Because both gates iterate only this list, a **new `ControllerBase` subclass added to `Stash.Registry` but not appended to the list is scanned by neither gate**. A new controller can ship with an action carrying bare `[Authorize]` (or no attribute at all) and both meta-tests stay green — the precise class of silent omission these gates exist to prevent. This is a Construct-vs-Detect weakness: the gate *detects* missing classification on known controllers but does not *construct* the controller set in a way that makes omission impossible.

Secondary defect: the duplicated closed set violates the project's single-source-of-truth rule (CLAUDE.md, "a closed set duplicated across files … is the same defect as an inline literal — collapse it").

## Reproduction

Deterministic, today, against `HEAD` of `main`:

```bash
# 1. Add a new controller to Stash.Registry/Controllers/ with an unclassified action:
#
#    [ApiController]
#    [Route("api/v1/leak")]
#    public sealed class LeakController : ControllerBase
#    {
#        [Authorize]                       // authenticated but NOT PDP-routed
#        [HttpGet] public IActionResult Get() => Ok();
#    }
#
# 2. Do NOT add typeof(LeakController) to either ProductionControllers list.
# 3. Run both gates:
dotnet test --filter "FullyQualifiedName~AuthzCoverageMetaTests|FullyQualifiedName~AuthzDispatchCoverageMetaTests"
#    => BOTH PASS GREEN. LeakController.Get is never scanned — the [Authorize]-only,
#       non-PDP-routed endpoint escapes the dispatch gate entirely.
```

## Blast radius

- **Latent, defensive.** No production hole today — the current six controllers are all enumerated and compliant. The hole is in the *gate that prevents regressions*, identical in shape to the bug it was built to catch.
- **Becomes load-bearing the moment a seventh controller is added.** Registry growth (a new resource surface — tokens, webhooks, mirrors, metrics) is the foreseeable change that arms this. The author of a new controller gets *no signal* that their endpoints are unguarded; both authz gates report full compliance.
- Compounds with the no-magic-strings / move-enforcement-into-CI ethos: a gate that silently under-covers undercuts the guarantee the project leans on.

## Root cause

`AuthzCoverageMetaTests.cs:40-48` and `AuthzDispatchCoverageMetaTests.cs:49-57` each declare a static `ProductionControllers` array of explicit `typeof(...)` entries and iterate only it. Nothing cross-checks that array against the actual set of concrete `ControllerBase` subclasses in the `Stash.Registry` assembly, so the two can diverge silently. The set is maintained by hand in two places.

## Suggested fix

- (A) **Derive the set by reflection (Construct, recommended).** Replace both hardcoded arrays with a single shared source of truth that reflects over the `Stash.Registry` assembly for every concrete (non-abstract) public `ControllerBase` subclass. A new controller is then automatically held to both gates — omission becomes impossible, not merely detectable. Pair with a **floor guard** (`count >= 6`) so a vacuous/empty reflection result (assembly mismatch, predicate regression) fails loudly instead of passing on zero controllers — the same teeth pattern `NoMagicAuthStringsMetaTests` uses. Collapses the duplicated list as a side effect. Trade-off: loses the explicit human-readable roster; mitigated by a teeth test asserting the derived set contains the known controllers.
- (B) **Pin + divergence assertion (Detect).** Keep the explicit list (extracted to one shared constant) and add a test that reflects the assembly and fails if the discovered set ≠ the pinned list. Trade-off: still two things to keep in sync; a new controller fails the build until acknowledged (forcing function), but does not auto-cover — strictly weaker than (A) per the project's Construct > Detect > Instruct doctrine.

Recommend **(A)**: it makes the omission impossible rather than merely detected, and collapses the duplicated closed set in one move. Keep the floor guard to defend against a vacuous pass.

## Verification

```bash
# A new shared inventory + floor guard. The floor-guard/teeth test must FAIL if the
# reflection sweep returns fewer than the known production controllers (proving it has
# teeth and isn't a vacuous pass), and PASS for the current assembly.
dotnet test --filter "FullyQualifiedName~RegistryControllerInventory"

# Both existing gates must continue to pass, now consuming the derived set:
dotnet test --filter "FullyQualifiedName~AuthzCoverageMetaTests|FullyQualifiedName~AuthzDispatchCoverageMetaTests"

# Manual teeth check: temporarily add an [Authorize]-only LeakController (per Reproduction)
# WITHOUT touching any list — AuthzDispatchCoverageMetaTests must now go RED.
```

Cross-cutting checks that must continue to pass: `NoMagicAuthStringsMetaTests`, `RegistryAuthzMatrixTests`, and the full `FullyQualifiedName~Registry` filter.

## Related

- Sibling bug whose refute pass surfaced this: `4-done/Bug — AuthzCoverageMetaTests Accepts Class-Level Authorize as PDP Coverage (Does Not Verify Dispatch).md` (and its `## Resolution` → "Residual hardening gaps" #2).
- Same surface: `Stash.Tests/Registry/AuthzCoverageMetaTests.cs`, `Stash.Tests/Registry/Authz/AuthzDispatchCoverageMetaTests.cs`.
- Doctrine: `MEMORY.md` → "Prevent omission, don't detect it" (Construct > Detect > Instruct); CLAUDE.md → single-source-of-truth for closed sets.
- Pattern to mirror for the floor-guard teeth: `Stash.Tests/Registry/Authz/NoMagicAuthStringsMetaTests.cs`.

---

## Resolution (2026-06-01)

Fixed via **approach (A)** in commit `9cb6d1df` (on `main`). The duplicated hardcoded list was replaced with a single reflection-derived source of truth:

- **New `Stash.Tests/Registry/Authz/RegistryControllerInventory.cs`** — `Production` is computed by reflecting over the `Stash.Registry` assembly (`typeof(AuthController).Assembly`) for every concrete, top-level public `ControllerBase` subclass. A newly added controller is now held to both gates **automatically** — the omission is impossible, not merely detectable (Construct, not Detect). Scope is pinned to the registry assembly precisely so the deliberately-unclassified test fixtures (which live in `Stash.Tests`) are never swept in.
- **Both gates consume it.** `AuthzCoverageMetaTests` and `AuthzDispatchCoverageMetaTests` now reference `RegistryControllerInventory.Production` instead of an inline `typeof(...)` array — collapsing the duplicated closed set to one home.
- **Vacuous-pass defense.** `RegistryControllerInventory.FloorCount = 6` plus three teeth tests (`Production_MeetsFloorGuard`, `Production_ContainsKnownControllers`, `Production_ExcludesTestFixtureControllers`) ensure an empty/under-scoped reflection result fails loudly instead of satisfying every gate trivially.

**Verification performed:**

1. Unit/teeth + both gates: `dotnet test --filter "FullyQualifiedName~RegistryControllerInventory|FullyQualifiedName~AuthzCoverageMetaTests|FullyQualifiedName~AuthzDispatchCoverageMetaTests"` → 10/10 green.
2. **Injection proof (the decisive one).** A temporary `[Authorize]`-only `ZzzTempLeakController` was added under `Stash.Registry/Controllers/` *without touching any list*. `AuthzDispatchCoverageMetaTests.AllProductionEndpoints_HaveDispatchClassification` went **RED** naming `ZzzTempLeakController.Get`; `AuthzCoverageMetaTests` stayed **green** (bare `[Authorize]` satisfies default-deny). The probe was then removed. This proves the gate now catches a controller it was never told about — a green-only run would not have discriminated hole-open from hole-closed.
3. Cross-cutting: `NoMagicAuthStringsMetaTests` + `RegistryAuthzMatrixTests` + both gates + inventory → 88/88 green.

Drive-by: corrected a stale XML doc in `AuthzDispatchCoverageMetaTests` (pinned imperative set is `{ClaimScope}`, not `{PublishPackage, ClaimScope}` — `PublishPackage` had been folded into the shared filter).
