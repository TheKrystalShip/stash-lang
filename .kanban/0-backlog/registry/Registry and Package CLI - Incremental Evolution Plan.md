# Registry and Package CLI - Incremental Evolution Plan

> **Status:** Backlog
> **Created:** 2026-05-28
> **Priority:** High
> **Discovery context:** Follow-up review after the self-hosted registry feature gap analysis and optional registry website spec. Focus: additional registry and `stash pkg` CLI improvements that make the package ecosystem safer, more automatable, and more pleasant without trying to ship every mature-registry feature at once.

## Background

The Stash registry and package CLI already have a strong v1 foundation:

- `stash pkg init`
- dependency install/update/uninstall/list/outdated
- package search/info
- publish/pack/unpublish
- login/logout/whoami
- owner management
- API token management
- lockfile generation
- integrity verification
- local tarball cache
- Git dependencies
- registry-side auth, owners, deprecation endpoints, audit log, rate limiting, and package metadata

The next step is not to add every mature ecosystem feature immediately. The right path is incremental: first close workflow gaps that make the package manager feel reliable in CI and day-to-day use, then add policy/security automation, then add larger ecosystem features such as workspaces, provenance, advisories, release channels, and advanced registry governance.

This spec collects the remaining registry and package CLI ideas and orders them into short-term, mid-term, and long-term phases.

## Key Observations

### CLI Documentation / Registry Capability Mismatch

The registry reference documents package and version deprecation endpoints. The package CLI reference does not document a `stash pkg deprecate` command.

There are two possibilities:

- The command exists but is missing from `docs/PKG - Package Manager CLI.md`.
- The command does not exist yet, despite the registry API supporting deprecation.

Either way, this is an early gap. Deprecation is one of the safest lifecycle tools because it warns users without deleting immutable artifacts.

### Registry Alias Ambiguity

The manifest reference includes a `registries` field, but registry selection is documented as:

1. `--registry <url>`
2. `STASH_REGISTRY_URL`
3. `defaultRegistry` in `~/.stash/config.json`

The relationship between manifest-level `registries` and CLI registry selection is unclear. This should be clarified before multiple registries, private scopes, mirrors, or air-gapped workflows become more complex.

### Cache UX Is Too Manual

The package CLI reference says there are no cache-management commands and users should delete files manually. That is fine for an early implementation but weak for a package manager that wants to be used in CI and long-lived developer machines.

### CI Reproducibility Needs a First-Class Command

`stash pkg install` currently uses the lockfile when current, but there is no explicit `ci` or `--locked` mode. CI needs a command that refuses to mutate dependency state.

### Automation Needs Machine-Readable Output

Several commands are useful to scripts and CI systems, but the docs describe human-oriented output only. `--json` should become a common package CLI affordance.

## Principles

- Keep the registry as the source of truth and `stash pkg` as a client.
- Add small, composable commands before large workflow systems.
- Prefer explicit CI behavior over hidden heuristics.
- Make safety checks cheap and routine.
- Make package authoring more inspectable before publish.
- Do not build compatibility shims for current pre-1.0 behavior; choose the clean end state.
- Avoid coupling CLI output formats to the future website; shared needs should be represented as registry API data and CLI `--json` output.

## Short-Term Phase - Reliability and Daily Workflow

These should come first because they are small-to-medium scope and directly improve CI, authorship, and troubleshooting.

### 1. Document or Implement `stash pkg deprecate`

**Problem:** Registry deprecation exists, but the package CLI reference does not document a deprecation command.

**Recommended command shape:**

```bash
stash pkg deprecate <name> "<message>"
stash pkg deprecate <name>@<version> "<message>"
stash pkg undeprecate <name>
stash pkg undeprecate <name>@<version>
```

Optional package-level replacement:

```bash
stash pkg deprecate <name> "<message>" --alternative <package>
```

**Acceptance criteria:**

- CLI docs include deprecate and undeprecate commands.
- Package-level and version-level deprecation are both supported or explicitly scoped.
- Auth, registry, and token selection match publish/unpublish behavior.
- Install and info can surface deprecation warnings in follow-up work.

### 2. Add `stash pkg ci` / `stash pkg install --locked`

**Problem:** CI needs deterministic dependency installation that fails rather than rewriting files.

**Recommended behavior:**

- Require `stash-lock.json`.
- Require lockfile to match `stash.json`.
- Install exactly the locked graph.
- Verify integrity.
- Fail if any dependency must be resolved.
- Fail if the lockfile would change.
- Do not modify `stash.json`.
- Do not modify `stash-lock.json`.

**Command shape:**

```bash
stash pkg ci
stash pkg install --locked
```

**Acceptance criteria:**

- Missing lockfile exits non-zero.
- Stale lockfile exits non-zero with clear remediation.
- Matching lockfile installs successfully.
- No dependency resolution occurs in locked mode.
- CI-friendly error messages are deterministic.

### 3. Add `stash pkg doctor`

**Problem:** Package failures can come from config, registry auth, cache corruption, lockfile drift, invalid manifests, missing `git`, or stale installed packages. Users need one diagnostic command.

**Checks:**

- `stash.json` exists and is valid.
- Manifest fields are valid.
- `stash-lock.json` is fresh.
- `stashes/` matches the lockfile.
- Cache entries referenced by the lockfile have matching integrity.
- Registry URL can be resolved.
- Current token is present and accepted, when auth is expected.
- `git` is available if Git dependencies are present.
- `~/.stash/config.json` permissions are acceptable.

**Command shape:**

```bash
stash pkg doctor
stash pkg doctor --json
stash pkg doctor --registry <url>
```

**Acceptance criteria:**

- Human output groups checks by status.
- `--json` returns structured check results.
- The command exits non-zero when at least one required check fails.
- Warnings do not necessarily fail unless `--strict` is later added.

### 4. Add Cache Management Commands

**Problem:** Manual `rm -rf ~/.stash/cache` is too blunt and not cross-platform-friendly guidance.

**Command shape:**

```bash
stash pkg cache list
stash pkg cache verify
stash pkg cache clean
stash pkg cache prune
stash pkg cache clean <package>
stash pkg cache clean <package>@<version>
```

**Recommended behavior:**

- `list`: show package, version, size, integrity if known.
- `verify`: recompute integrity for cached tarballs.
- `clean`: remove all cache entries or a selected package/version.
- `prune`: remove entries not referenced by any discovered project lockfile, or use age-based pruning if project discovery is too expensive.

**Acceptance criteria:**

- No docs recommend manual deletion as the primary path.
- Commands are safe across Linux, macOS, and Windows.
- `--json` is available for list/verify.

### 5. Add Publish Dry-Run and Pack Inspection

**Problem:** Package authors need to inspect exactly what will be shipped before publishing.

**Command shape:**

```bash
stash pkg publish --dry-run
stash pkg pack --dry-run
stash pkg pack --list-files
stash pkg pack --json
```

**Recommended behavior:**

- Validate manifest.
- Apply `files` and `.stashignore`.
- Print included files.
- Print ignored files when verbose.
- Compute tarball size.
- Compute integrity.
- Show package name/version/registry.
- Optionally check auth and ownership when online.
- Do not upload in dry-run mode.

**Acceptance criteria:**

- Dry-run performs the same file selection as real publish.
- Dry-run catches missing manifest, invalid version, private package, and missing files.
- Output includes package size and integrity.

### 6. Add `--json` to Automation-Oriented Commands

**Problem:** Human output is not enough for CI and automation.

**Initial command set:**

- `stash pkg search --json`
- `stash pkg info --json`
- `stash pkg list --json`
- `stash pkg outdated --json`
- `stash pkg whoami --json`
- `stash pkg token list --json`
- `stash pkg owner list --json`
- `stash pkg doctor --json`
- `stash pkg pack --json`

**Acceptance criteria:**

- JSON output is stable and documented.
- Human output remains the default.
- JSON mode avoids progress spinners or extra prose.
- Errors should have a structured JSON form when `--json` is requested.

### 7. Clarify Manifest `registries`

**Problem:** The manifest includes `registries`, but selection order does not reference it.

**Possible decisions:**

- Remove `registries` from the manifest until multi-registry support exists.
- Define it as alias-only metadata used by explicit CLI flags.
- Define it as package-scope routing, e.g. `@corp/*` to internal registry.
- Define it as mirror/fallback configuration.

**Recommendation:** Decide early. Because Stash is pre-1.0, remove or rename freely rather than carrying ambiguity.

**Acceptance criteria:**

- `docs/PKG - Package Manager CLI.md` and registry-related behavior agree.
- Tests cover registry selection precedence.
- Error messages explain which registry was selected and why when verbose mode is enabled.

## Mid-Term Phase - Security, Policy, and Resolver Insight

These build on the short-term reliability layer and prepare the ecosystem for larger registry features.

### 8. Add `stash pkg verify`

**Problem:** Users need a way to prove that installed dependencies match the lockfile and registry metadata.

**Command shape:**

```bash
stash pkg verify
stash pkg verify <package>
stash pkg verify --registry <url>
stash pkg verify --json
```

**Recommended checks:**

- Installed package exists.
- Installed version matches lockfile.
- Installed package tarball or extracted files match expected integrity, depending on available metadata.
- Cached tarball matches lockfile integrity.
- Registry metadata integrity matches lockfile integrity when online.
- Future: signatures and provenance verify successfully.

**Acceptance criteria:**

- Detects missing installed package.
- Detects version mismatch.
- Detects corrupted cache tarball.
- Detects registry/lockfile integrity mismatch.
- Produces machine-readable results.

### 9. Add `stash pkg audit`

**Problem:** Once registry advisories exist, users need a first-class command to detect vulnerable dependency versions.

**Command shape:**

```bash
stash pkg audit
stash pkg audit --json
stash pkg audit --registry <url>
stash pkg audit --fail-on moderate
```

**Recommended behavior:**

- Read the lockfile.
- Query registry advisories for locked package versions.
- Include direct and transitive dependencies.
- Include severity, affected range, patched versions, advisory IDs, and references.
- Exit non-zero based on configured severity threshold.

**Acceptance criteria:**

- Works without mutating the lockfile.
- Has deterministic CI output.
- Handles missing registry advisory support gracefully.
- Future policy file can configure severity thresholds.

### 10. Add Dependency Explanation Commands

**Problem:** Flat dependency resolution is simple, but users still need to understand why a transitive dependency exists.

**Command shape:**

```bash
stash pkg why <name>
stash pkg explain <name>
```

**Recommended output:**

- Which direct dependency introduced it.
- Full dependency paths.
- Constraints that applied.
- Resolved version.
- Whether the dependency is direct or transitive.

**Acceptance criteria:**

- Explains direct dependencies.
- Explains transitive dependencies.
- Reports when a package is not present.
- Supports `--json`.

### 11. Improve Conflict Diagnostics

**Problem:** Version conflicts should guide users toward a fix, not only state that no version satisfies all constraints.

**Recommended improvements:**

- Show all constraints by package path.
- Show candidate versions considered.
- Show why each candidate was rejected.
- Suggest likely fixes:
  - loosen root constraint
  - update dependent package
  - add override, once supported
  - pin direct dependency

**Acceptance criteria:**

- Conflict errors identify the minimal conflicting set when practical.
- Output remains concise by default.
- Verbose mode can show candidate analysis.

### 12. Add Policy File Support

**Problem:** Teams need package-manager rules that are enforced consistently in CI and local installs.

**Potential file:**

```json
{
  "requireLockfile": true,
  "forbidGitBranchDependencies": true,
  "allowRegistries": ["https://registry.example.com/api/v1"],
  "blockDeprecated": false,
  "blockVulnerabilitiesAt": "high",
  "allowedLicenses": ["MIT", "Apache-2.0", "BSD-3-Clause"],
  "requireProvenance": false
}
```

**Potential names:**

- `stash-policy.json`
- `.stash/policy.json`
- `policy` field in `stash.json`

**Recommendation:** Keep policy separate from package metadata if it is environment/team-specific.

**Acceptance criteria:**

- `stash pkg ci`, `install`, `audit`, `publish`, and `doctor` can enforce relevant policy.
- Policy violations produce clear diagnostics.
- `--json` includes policy rule IDs.

### 13. Token Hardening UX

**Problem:** API tokens currently have broad scopes: `read`, `publish`, `admin`. Future private packages and trusted publishing need narrower tokens.

**Recommended capabilities:**

- Package-scoped tokens.
- Org-scoped tokens, once organizations exist.
- Token last-used timestamp.
- Token last-used IP or coarse location, if privacy policy allows.
- Token rotation helper.

**Command shape:**

```bash
stash pkg token create --scope publish --package foo --expires-in 30d
stash pkg token create --scope read --package foo --expires-in 12h
stash pkg token rotate <token-id>
stash pkg token list --json
```

**Acceptance criteria:**

- Registry enforces package scope.
- CLI clearly displays token scope and expiry.
- Token value is still only printed once.

### 14. License and SBOM Commands

**Problem:** Stash's target audience includes sysadmin, CI, deployment, and infrastructure users. Compliance and inventory matter.

**Command shape:**

```bash
stash pkg licenses
stash pkg licenses --json
stash pkg sbom
stash pkg sbom --format cyclonedx
stash pkg sbom --format spdx
```

**Recommended behavior:**

- Read lockfile.
- Include direct and transitive dependencies.
- Include package name, version, license, repository, integrity, and dependency relationships.
- Include Git dependencies with best-effort source metadata.

**Acceptance criteria:**

- Human license summary exists.
- JSON license summary exists.
- At least one SBOM format is supported initially.

## Long-Term Phase - Ecosystem Scale and Advanced Workflows

These are high-value but larger features that should wait until the core package manager and registry workflows are stable.

### 15. Dist-Tags and Release Channels

**Problem:** A single `latest` channel is too limiting for beta, nightly, preview, and LTS workflows.

**Command shape:**

```bash
stash pkg tag list <package>
stash pkg tag add <package>@<version> beta
stash pkg tag remove <package> beta
stash pkg install <package>@beta
```

**Registry requirements:**

- Tag storage per package.
- Permission checks for tag mutation.
- Resolver support for tags.
- Audit events for tag changes.

**Acceptance criteria:**

- `latest` can be represented as a tag or cleanly mapped to existing metadata.
- Installing by tag writes exact version to lockfile.
- Tag changes do not mutate existing lockfiles.

### 16. Workspace / Monorepo Support

**Problem:** Larger Stash projects may contain multiple packages that should be developed, tested, versioned, and published together.

**Potential manifest field:**

```json
{
  "workspaces": ["packages/*"]
}
```

**Recommended capabilities:**

- Discover workspace packages.
- Link local workspace dependencies.
- Install all workspace dependencies.
- Single workspace lockfile or clearly defined per-package lockfiles.
- Publish changed packages.
- Version bump orchestration.

**Command shape:**

```bash
stash pkg workspace list
stash pkg install --workspaces
stash pkg publish --workspaces
stash pkg version --workspaces patch
```

**Acceptance criteria:**

- Local workspace dependencies do not require publishing before development.
- Publish order respects dependency graph.
- Lockfile semantics are documented.

### 17. Dependency Overrides / Resolutions

**Problem:** Teams sometimes need to force a transitive dependency version during emergency fixes.

**Potential manifest field:**

```json
{
  "overrides": {
    "transitive-lib": "1.2.3"
  }
}
```

**Recommended behavior:**

- Overrides are explicit and visible in lockfile.
- Resolver explains when an override changes a chosen version.
- Policy can forbid overrides in release builds if desired.

**Acceptance criteria:**

- Overrides affect transitive resolution.
- Overrides are documented in lockfile.
- `stash pkg why` reports override influence.

### 18. Offline, Mirror, and Air-Gapped Workflows

**Problem:** Stash is a sysadmin language. Air-gapped and restricted-network environments are realistic.

**Recommended capabilities:**

- `stash pkg install --offline`
- `stash pkg install --prefer-offline`
- Registry mirror configuration.
- Cache export/import.
- Vendoring packages into a project or artifact.

**Command shape:**

```bash
stash pkg install --offline
stash pkg vendor
stash pkg cache export packages.tar
stash pkg cache import packages.tar
```

**Acceptance criteria:**

- Offline install never contacts the network.
- Missing cache entries produce clear errors.
- Export/import preserves integrity metadata.

### 19. Trusted Publishing, Provenance, and Verification UX

**Problem:** Registry-side trusted publishing and provenance need CLI support.

**Command shape:**

```bash
stash pkg trusted-publisher list <package>
stash pkg trusted-publisher add <package> --provider github --repo owner/repo --workflow release.yml
stash pkg provenance <package>@<version>
stash pkg verify <package>@<version> --provenance
```

**Recommended behavior:**

- CI publish can exchange OIDC identity for a short-lived publish token.
- Published versions record source and build provenance.
- CLI can display and verify provenance status.

**Acceptance criteria:**

- No long-lived CI token is required for supported trusted publishers.
- Provenance data is visible through CLI and registry API.
- Verification failures are clear and policy-enforceable.

### 20. Lifecycle States in CLI

**Problem:** Registry roadmap includes possible states such as yanked, unlisted, quarantined, and blocked. CLI install behavior must be explicit.

**Recommended behavior:**

- `deprecated`: warn.
- `unlisted`: do not show in search, install exact locked versions normally.
- `yanked`: warn or fail unless already locked, depending on policy.
- `quarantined`: fail by default.
- `blocked`: fail always.

**Command additions:**

```bash
stash pkg yank <package>@<version> "<reason>"
stash pkg unyank <package>@<version>
stash pkg quarantine <package>@<version> "<reason>"
```

Admin-only commands may live under a future admin CLI group instead.

### 21. Website and API Client Readiness

**Problem:** The optional registry website will need API-friendly data and CLI consistency.

**CLI tie-ins:**

- `--json` output should match API field names where practical.
- Search filters should align between CLI and registry API.
- Trust signals should be consistent between CLI, website, and package metadata.
- Registry capability discovery should let CLI gracefully degrade when a registry lacks newer features.

**Acceptance criteria:**

- CLI can call registry discovery endpoint.
- CLI can report unsupported registry features clearly.
- Website and CLI consume the same documented API surfaces.

## Suggested Implementation Order

1. Fix/document `stash pkg deprecate`.
2. Add `stash pkg ci` / `install --locked`.
3. Add `stash pkg doctor`.
4. Add cache management commands.
5. Add publish dry-run and pack inspection.
6. Add `--json` across automation-oriented commands.
7. Clarify or remove manifest `registries`.
8. Add `stash pkg verify`.
9. Add better resolver explanation and `stash pkg why`.
10. Add registry advisories and `stash pkg audit`.
11. Add policy file enforcement.
12. Add package-scoped tokens.
13. Add license/SBOM commands.
14. Add dist-tags.
15. Add workspaces.
16. Add overrides.
17. Add offline/mirror workflows.
18. Add provenance/trusted publishing CLI.
19. Add lifecycle state commands.
20. Align CLI and optional website through discovery/OpenAPI.

## Cross-References

- Registry API reference: `docs/Registry - Package Registry.md`
- Package CLI reference: `docs/PKG - Package Manager CLI.md`
- Registry feature gap roadmap: `.kanban/0-backlog/registry/Registry Feature Gaps - Self-Hosted Registry Roadmap.md`
- Optional registry website spec: `.kanban/0-backlog/registry/Registry Website - Optional Web Client and API Readiness.md`

## Out of Scope

- Implementing these features in this spec.
- Choosing final command names where multiple reasonable options exist.
- Designing the optional registry website UI.
- Changing registry database schemas before each feature gets a concrete implementation card.
- Adding compatibility shims for pre-1.0 behavior that should be cleaned up instead.

