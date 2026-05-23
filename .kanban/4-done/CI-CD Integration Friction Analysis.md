## CI/CD Integration — Friction Analysis

I compared the current Stash registry auth flow against npm, PyPI (twine), Cargo, Docker Hub, GitHub Packages, and NuGet. Here are the friction points ranked by impact:

### High Impact — Would meaningfully reduce CI/CD setup friction

**1. No `STASH_TOKEN` environment variable support**

- **Friction:** Every CI/CD pipeline must create `~/.stash/config.json` with a heredoc + shell substitution + `chmod 600`. That's 10 lines of boilerplate per pipeline. If the JSON is malformed (missing comma, wrong quote), the publish silently fails.
- **What others do:** npm reads `NPM_TOKEN` env var. Cargo reads `CARGO_REGISTRY_TOKEN`. PyPI/twine reads `TWINE_PASSWORD`. NuGet reads `NUGET_API_KEY`. Docker reads `DOCKER_PASSWORD` for `docker login --password-stdin`.
- **Fix:** Have `RegistryClient` / `UserConfig` check for `STASH_TOKEN` (and optionally `STASH_REGISTRY_URL`) before reading config.json. Zero-config CI: just set two env vars. This is the single highest-impact improvement.

**2. No `--token` CLI flag**

- **Friction:** Related to #1. Even if env vars aren't set, `stash pkg publish --token $SECRET` would be a simple alternative. Currently the only path is through the config file.
- **What others do:** `cargo publish --token $TOKEN`, `dotnet nuget push --api-key $KEY`, `npm publish --token $TOKEN`.
- **Fix:** Add `--token <value>` to `PublishCommand`, `UnpublishCommand`, `OwnerCommand`. Lower priority than env var (env vars are more natural in CI), but both should exist.

**3. No token listing endpoint (`GET /auth/tokens`)**

- **Friction:** If you create a token and lose the `tokenId`, there's no way to audit or revoke it until it expires. Admins can't see which CI/CD tokens exist for a user. You can't script "rotate all my tokens" because you can't discover them.
- **What others do:** npm has `npm token list`. GitHub has `GET /user/tokens`. PyPI has a token management UI.
- **Fix:** Add `GET /api/v1/auth/tokens` (returns `tokenId`, `scope`, `description`, `createdAt`, `expiresAt` — never the token value itself). Also useful for the future web dashboard.

### Medium Impact — Nice-to-have for mature CI/CD setups

**4. No custom expiry on API token creation**

- **Friction:** All API tokens get the same lifetime (90 days). A short-lived deploy token for a one-off release can't be created. A long-running bot can't get a 1-year token without changing the global config.
- **What others do:** GitHub PATs let you pick expiry at creation. PyPI tokens have no expiry but can be scoped. npm granular tokens support custom expiry.
- **Fix:** Add optional `"expiresIn": "30d"` field to the `POST /auth/tokens` request body. Fall back to the global `auth.apiTokenExpiry` when omitted.

**5. No `stash pkg token create` CLI command**

- **Friction:** Creating an API token currently requires two curl commands (login → create token) or logging in interactively and then using curl. There's no CLI workflow for it.
- **What others do:** `npm token create`, `gh auth token`, `cargo login`.
- **Fix:** Add `stash pkg token create [--scope publish] [--description "CI token"] [--registry <url>]`. Could also add `stash pkg token list` and `stash pkg token revoke <id>`.

**6. No IP or CIDR scoping on API tokens**

- **Friction:** A leaked CI/CD token can be used from anywhere. If your runners have known IPs, you can't restrict the token to those IPs.
- **What others do:** npm granular tokens support CIDR allowlists. GitHub fine-grained PATs have IP policies. PyPI doesn't have this.
- **Fix:** Lower priority — adds meaningful security but significant implementation cost. Consider for a future release.

### Low Impact — Polish items

**7. No package-scoped tokens**

- **Friction:** A `publish`-scoped token can publish to any package the user owns. If you have 10 packages across 10 repos, one leaked token exposes all of them.
- **What others do:** npm granular tokens can be scoped to specific packages. PyPI project-scoped tokens. GitHub PATs can be scoped to specific repos.
- **Fix:** Future consideration. Add an optional `"packages": ["my-pkg"]` array to token creation. The middleware would check both scope and package name.

**8. No `STASH_REGISTRY_URL` environment variable for default registry**

- **Friction:** Minor complement to #1. Currently the registry URL must be in config.json or `--registry`. An env var default would simplify CI further.
- **What others do:** `NPM_CONFIG_REGISTRY`, `CARGO_REGISTRIES_<NAME>_INDEX`.

**9. No "dry run" publish mode**

- **Friction:** CI pipelines can't validate that a publish _would_ succeed (auth, version check, integrity) without actually publishing.
- **What others do:** `npm publish --dry-run`, `cargo publish --dry-run`.

---

### Recommendation Priority

| #   | Improvement                                   | Effort | Impact                                                 |
| --- | --------------------------------------------- | ------ | ------------------------------------------------------ |
| 1   | `STASH_TOKEN` + `STASH_REGISTRY` env vars     | Small  | **High** — eliminates config.json boilerplate entirely |
| 2   | `--token` CLI flag on publish/unpublish/owner | Small  | **High** — standard escape hatch                       |
| 3   | `GET /auth/tokens` listing endpoint           | Small  | **Medium** — enables audit and scripted rotation       |
| 4   | Custom expiry on `POST /auth/tokens`          | Small  | **Medium** — flexibility for different use cases       |
| 5   | `stash pkg token create/list/revoke` CLI      | Medium | **Medium** — eliminates curl requirement               |
| 6   | IP/CIDR scoping on tokens                     | Large  | **Low** — nice security feature, complex to implement  |
| 7   | Package-scoped tokens                         | Large  | **Low** — matters at scale, significant design work    |
| 8   | `STASH_REGISTRY_URL` env var                  | Tiny   | **Low** — minor complement to #1                       |
| 9   | `--dry-run` publish                           | Medium | **Low** — helpful but not auth-specific                |

Items 1–2 are the clear priority — they eliminate the most common complaint about package registry CI/CD setups (the config file dance) with minimal implementation effort. Item 3–5 round out the token management story. The rest can wait.

The analysis above covers 9 friction points compared against npm, PyPI, Cargo, Docker Hub, GitHub Packages, and NuGet, ranked by impact and effort. The top two priorities — `STASH_TOKEN` env var support and `--token` CLI flag — would eliminate the config.json boilerplate that is the biggest CI/CD pain point.
