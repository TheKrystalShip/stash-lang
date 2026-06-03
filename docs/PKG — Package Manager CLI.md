# PKG - Package Manager CLI

> **Status:** Stable v1 CLI reference
> **Audience:** package authors, package consumers, registry operators, and CI maintainers
> **Purpose:** reference for `stash pkg`, the package-management command group built into the Stash CLI.

`stash pkg` manages Stash package manifests, dependency installation, lock files,
publishing, registry authentication, roles, visibility, scopes, organizations, and
API tokens. The short alias
`stash p` is equivalent to `stash pkg`.

**Companion documents:**

- [Language Specification](Stash%20%E2%80%94%20Language%20Specification.md) - imports and module semantics
- [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md) - `pkg` namespace and stdlib APIs
- [Registry - Package Registry](Registry%20%E2%80%94%20Package%20Registry.md) - registry server, REST API, auth, storage, and deployment

---

## Contents

1. [Overview](#overview)
2. [Quick Start](#quick-start)
3. [Project Files](#project-files)
4. [Manifest Reference](#manifest-reference)
5. [Command Reference](#command-reference)
6. [Dependency Resolution](#dependency-resolution)
7. [Lock File](#lock-file)
8. [Registry and Authentication](#registry-and-authentication)
9. [Git Dependencies](#git-dependencies)
10. [Publishing and Packaging](#publishing-and-packaging)
11. [Cache](#cache)
12. [Security](#security)
13. [Environment Variables](#environment-variables)

---

## Overview

The package manager is part of the `stash` executable.

```bash
stash pkg <command> [options]
stash p <command> [options]
```

It provides:

- project initialization with `stash.json`
- dependency installation into `stashes/`
- deterministic locking with `stash-lock.json`
- package search, info, publish, and unpublish
- registry login/logout, role / visibility / scope / organization management, and API token management
- Git dependency installation
- tarball packing, integrity verification, and local cache reuse

Package commands operate on the current working directory unless a command says
otherwise. Commands that contact a registry target exactly one registry.

## Quick Start

Create a project:

```bash
mkdir my-project
cd my-project
stash pkg init --yes
```

Log in to a registry:

```bash
stash pkg login --registry https://registry.example.com/api/v1
```

Install a package:

```bash
stash pkg install http-utils
stash pkg install http-utils@1.2.0
stash pkg install http-utils@^2.0.0
```

Use the package:

```stash
import { get, post } from "http-utils";
import "http-utils/lib/request" as req;
```

Publish the current package:

```bash
stash pkg publish
```

## Project Files

A typical package project contains:

```text
my-project/
├── stash.json
├── stash-lock.json
├── stashes/
│   └── http-utils/
│       ├── stash.json
│       ├── .stash-version
│       └── index.stash
├── index.stash
└── lib/
    └── helpers.stash
```

| Path                            | Purpose                                                 |
| ------------------------------- | ------------------------------------------------------- |
| `stash.json`                    | package manifest                                        |
| `stash-lock.json`               | exact resolved dependency graph; commit this file       |
| `stashes/`                      | installed packages; normally ignored by version control |
| `stashes/{name}/.stash-version` | installed package version marker                        |
| `.stashignore`                  | publish/pack exclusion rules                            |
| `~/.stash/config.json`          | user registry credentials and default registry          |
| `~/.stash/cache/`               | downloaded package tarball cache                        |

Packages installed under `stashes/` are importable by package name or package
subpath.

```stash
import { request } from "http-utils";
import "http-utils/lib/request" as req;
```

## Manifest Reference

`stash.json` describes a project or package.

Minimal manifest:

```json
{
  "name": "my-project",
  "version": "1.0.0"
}
```

Full manifest:

```json
{
  "name": "deploy-toolkit",
  "version": "2.1.0",
  "description": "Deployment automation utilities",
  "author": "Jane Doe",
  "license": "MIT",
  "main": "index.stash",
  "repository": "https://github.com/example/deploy-toolkit",
  "keywords": ["deploy", "automation", "devops"],
  "stash": ">=1.0.0",
  "dependencies": {
    "http-utils": "^1.2.0",
    "config-loader": "~0.5.0",
    "internal-tools": "git:https://github.com/corp/tools.git#v2.0.0"
  },
  "files": ["lib/", "index.stash", "README.md", "LICENSE"],
  "private": false
}
```

| Field          | Required            | Meaning                                    |
| -------------- | ------------------- | ------------------------------------------ |
| `name`         | required to publish | package name                               |
| `version`      | required to publish | SemVer package version                     |
| `description`  | no                  | one-line registry/search description       |
| `author`       | no                  | author name                                |
| `license`      | no                  | SPDX license identifier                    |
| `main`         | no                  | package entry point; default `index.stash` |
| `repository`   | no                  | source repository URL                      |
| `keywords`     | no                  | search keywords                            |
| `stash`        | no                  | supported Stash version range              |
| `dependencies` | no                  | package and Git dependency map             |
| `files`        | no                  | publish whitelist                          |
| `registries`   | no                  | registry aliases and URLs                  |
| `private`      | no                  | when `true`, publishing is blocked         |

Package names are case-sensitive and must follow one of these forms:

| Form     | Pattern                              | Example               |
| -------- | ------------------------------------ | --------------------- |
| unscoped | `^[a-z][a-z0-9-]*$`                  | `http-utils`          |
| scoped   | `^@[a-z][a-z0-9-]*/[a-z][a-z0-9-]*$` | `@myorg/deploy-tools` |

Names may be at most 64 characters.

## Command Reference

### `stash pkg init`

Creates `stash.json` in the current directory.

```bash
stash pkg init
stash pkg init --yes
stash pkg init -y
```

Interactive mode prompts for name, version, description, author, license, and entry
point. `--yes` accepts defaults. The default name is derived from the directory name
and sanitized into a valid package name. The command fails if `stash.json` already
exists.

### `stash pkg install [specifier]`

Installs dependencies. Alias: `stash pkg i`.

```bash
stash pkg install
stash pkg install http-utils
stash pkg install http-utils@1.2.0
stash pkg install http-utils@^2.0.0
stash pkg install git:https://github.com/user/repo.git#v1.0.0
```

Without a specifier, the command installs dependencies from `stash.json`. With a
specifier, it adds the dependency to `stash.json`, resolves the full dependency
tree, writes `stash-lock.json`, and extracts packages into `stashes/`.

When a bare version is provided, such as `http-utils@1.2.0`, the manifest
constraint becomes `^1.2.0`. Explicit ranges are preserved as written.

| Flag               | Meaning                                   |
| ------------------ | ----------------------------------------- |
| `--registry <url>` | use this registry for resolution/download |

Already installed packages with the resolved version are skipped.

### `stash pkg uninstall <name>`

Removes a direct dependency. Alias: `stash pkg remove`.

```bash
stash pkg uninstall http-utils
stash pkg remove http-utils
```

The command removes the dependency from `stash.json`, deletes its installed package
directory, and updates the lock file.

### `stash pkg update [name]`

Updates dependency versions within declared constraints.

```bash
stash pkg update
stash pkg update http-utils
```

Without a name, all lock entries are cleared and dependencies are re-resolved. With
a name, only that package's lock entry is cleared.

| Flag               | Meaning                                   |
| ------------------ | ----------------------------------------- |
| `--registry <url>` | use this registry for resolution/download |

### `stash pkg list`

Prints the installed dependency tree. Alias: `stash pkg ls`.

```bash
stash pkg list
```

Transitive dependencies are marked with `(transitive)`. Git dependencies show
`(git)` instead of a version. Packages are sorted alphabetically.

### `stash pkg outdated`

Prints installed versions and declared constraints.

```bash
stash pkg outdated
```

If all dependencies are current, the command prints `All dependencies are up to
date.`

### `stash pkg search <query>`

Searches the registry.

```bash
stash pkg search http
stash pkg search deploy --page 2
```

| Flag               | Meaning                  |
| ------------------ | ------------------------ |
| `--registry <url>` | use this registry        |
| `--page <number>`  | page number; default `1` |

### `stash pkg info <name>`

Displays package metadata, versions, and a truncated README. (Owner/role display
moved to `stash pkg role list`; see that command.)

```bash
stash pkg info http-utils
```

| Flag               | Meaning           |
| ------------------ | ----------------- |
| `--registry <url>` | use this registry |

### `stash pkg publish`

Publishes the current package.

```bash
stash pkg publish
stash pkg publish --registry https://registry.example.com/api/v1
```

Requirements:

- `stash.json` exists
- `name` and `version` are present
- `private` is not `true`
- the user is authenticated for the target registry

Publish flow:

1. validate manifest
2. create package tarball using `.stashignore` and `files`
3. compute SHA-256 integrity
4. upload tarball and metadata
5. print the published package and registry

Published versions are immutable. To release a fix, publish a new version. The
first publisher becomes an owner. Only owners and registry admins can publish later
versions. Manage per-package roles with `stash pkg role`.

| Flag               | Meaning                                        |
| ------------------ | ---------------------------------------------- |
| `--registry <url>` | use this registry                              |
| `--token <token>`  | auth token; overrides config and `STASH_TOKEN` |

### `stash pkg pack`

Creates a local tarball without publishing.

```bash
stash pkg pack
```

The command writes `{name}-{version}.tar.gz` to the current directory and prints
included files plus total size.

### `stash pkg unpublish <name>@<version>`

Removes a published package version, subject to the registry's unpublish policy.

```bash
stash pkg unpublish http-utils@1.2.3
```

The specifier must include both package name and exact version.

| Flag               | Meaning                                        |
| ------------------ | ---------------------------------------------- |
| `--registry <url>` | use this registry                              |
| `--token <token>`  | auth token; overrides config and `STASH_TOKEN` |

### `stash pkg login`

Authenticates to a registry.

```bash
stash pkg login --registry https://registry.example.com/api/v1
```

`--registry` is required. The command prompts for username and password, hides
password input, stores the returned token in `~/.stash/config.json`, and sets the
default registry if none exists.

### `stash pkg logout`

Removes stored credentials for a registry.

```bash
stash pkg logout --registry https://registry.example.com/api/v1
```

`--registry` is required. If the registry was the default, the default registry is
cleared.

### `stash pkg whoami`

Prints the authenticated username.

```bash
stash pkg whoami
stash pkg whoami --registry https://registry.example.com/api/v1
stash pkg whoami --verbose
stash pkg whoami -v
```

By default, output is a single script-friendly line. With `--verbose`, the command
also prints email, role, and registry URL. Missing optional fields are printed as
`(none)`.

| Flag               | Meaning                        |
| ------------------ | ------------------------------ |
| `--registry <url>` | use this registry              |
| `--verbose`, `-v`  | print extended account details |

The command exits non-zero when not logged in or when the registry is unreachable.

### `stash pkg role <action> <package> ...`

Manages per-package role grants. Replaces the former `stash pkg owner` command with a
principal-typed grammar: a role is granted to a **user**, **team**, or **org**, and the
role itself is explicit (`owner`, `maintainer`, `publisher`, or `reader`).

```bash
stash pkg role list   @myorg/widget
stash pkg role assign @myorg/widget user alice     maintainer
stash pkg role assign @myorg/widget team designers publisher
stash pkg role revoke @myorg/widget user alice
```

| Subcommand                                                    | Effect                                       |
| ------------------------------------------------------------- | -------------------------------------------- |
| `role list <package>`                                         | list every role grant on the package         |
| `role assign <package> <user\|team\|org> <principal> <role>`  | grant `<role>` to a principal                |
| `role revoke <package> <user\|team\|org> <principal>`         | remove whatever role the principal holds      |

The principal type (`user`, `team`, `org`) and the role (`owner`, `maintainer`,
`publisher`, `reader`) are required, explicit arguments — there is no implicit user-owner
default. `role revoke` takes **no** role argument: a principal holds at most one role per
package, and revoke removes whichever role they hold.

`role list`, `assign`, and `revoke` all run at the **publish** ceiling on the self-service
route `/packages/{scope}/{name}/roles` — a package owner manages their own package's roles
with a publish token; no admin token is required. Revoking the **last owner** of a package
is refused by the registry (the server's last-owner message is surfaced); revoking a
principal that holds no role reports a clear not-found error.

| Flag               | Meaning                                        |
| ------------------ | ---------------------------------------------- |
| `--registry <url>` | use this registry                              |
| `--token <token>`  | auth token; overrides config and `STASH_TOKEN` |

> The flat `owners` array was removed from `stash pkg info`; `stash pkg role list` is now
> the canonical, principal-typed reader of who can act on a package. (`info` is anonymous,
> whereas `role list` requires a publish token.)

### `stash pkg visibility set <package> <public|internal|private>`

Sets a package's visibility tier. **Set-only** — there is no `visibility get` subcommand
because the registry exposes no visibility read path (the package-detail response carries
no visibility field). The read gap is tracked as a deferred backlog item.

```bash
stash pkg visibility set @myorg/widget private
stash pkg visibility set @myorg/widget public
```

`visibility set` runs at the **publish** ceiling on `/packages/{scope}/{name}/visibility`
and is idempotent (setting a tier the package already has succeeds). An unknown tier is
rejected with a non-zero exit and the list of valid tiers.

| Flag               | Meaning                                        |
| ------------------ | ---------------------------------------------- |
| `--registry <url>` | use this registry                              |
| `--token <token>`  | auth token; overrides config and `STASH_TOKEN` |

### `stash pkg scope <action> <scope> ...`

Claims and inspects namespace scopes.

```bash
stash pkg scope claim myorg
stash pkg scope claim myorg --org acme
stash pkg scope info  myorg
```

| Subcommand                          | Effect                                                  |
| ----------------------------------- | ------------------------------------------------------- |
| `scope claim <scope> [--org <org>]` | claim a scope for the authenticated user, or for an org |
| `scope info <scope>`                | print the scope's owner (anonymous read)                |

`scope claim` posts to `/scopes`. By default the scope is claimed for the authenticated
user (`owner_type=user`); `--org <org>` claims it for an organization (`owner_type=org`).
When the registry runs in **Verified** ownership mode, `claim` prints a DNS-TXT challenge
(record name, record value, and expiry) to satisfy before the claim completes, instead of
reporting a finished claim. A second claim of an already-owned scope by a different user
fails with a clear error.

| Flag               | Meaning                                        |
| ------------------ | ---------------------------------------------- |
| `--org <org>`      | claim the scope for an organization            |
| `--registry <url>` | use this registry                              |
| `--token <token>`  | auth token; overrides config and `STASH_TOKEN` |

### `stash pkg org <action> ...`

Manages organizations and their teams.

```bash
stash pkg org create acme --display-name "Acme Corp"
stash pkg org info   acme
stash pkg org member add    acme bob --role member
stash pkg org member remove acme bob
stash pkg org team add        acme designers
stash pkg org team member add acme designers bob
```

| Subcommand                                            | Effect                                          |
| ----------------------------------------------------- | ----------------------------------------------- |
| `org create <org> [--display-name <name>]`            | create an organization                          |
| `org info <org>`                                      | print the org's flat metadata (anonymous read)  |
| `org member add <org> <user> [--role owner\|member]`  | add a member                                    |
| `org member remove <org> <user>`                      | remove a member                                 |
| `org team add <org> <team>`                           | create a team                                   |
| `org team member add <org> <team> <user>`             | add a user to a team                            |

`org info` prints only the organization's flat metadata (id, name, display name, creation
time, creator). It does **not** list members or teams — the registry exposes no membership
read path (tracked as a deferred backlog item). The write subcommands run at the
**publish** ceiling.

| Flag                     | Meaning                                          |
| ------------------------ | ------------------------------------------------ |
| `--display-name <name>`  | human-readable org name (on `create`)            |
| `--role <owner\|member>` | member role (on `member add`; default `member`)  |
| `--registry <url>`       | use this registry                                |
| `--token <token>`        | auth token; overrides config and `STASH_TOKEN`   |

### `stash pkg token`

Manages registry API tokens.

Create a token:

```bash
stash pkg token create --scope publish --description "CI deploy token"
stash pkg token create --scope publish --expires-in 30d
stash pkg token create --scope read --expires-in 12h
```

| Flag                      | Meaning                                                       |
| ------------------------- | ------------------------------------------------------------- |
| `--scope <scope>`         | `read`, `publish`, or `admin`; required                       |
| `--description <text>`    | human-readable token label                                    |
| `--expires-in <duration>` | lifetime such as `30d`, `12h`, or `90m`; min `1h`, max `365d` |
| `--registry <url>`        | use this registry                                             |
| `--token <token>`         | auth token for the request                                    |

The token value is printed once and cannot be retrieved again.

List tokens:

```bash
stash pkg token list
stash pkg token ls
```

Token list output includes token IDs and metadata, never token values.

Revoke a token:

```bash
stash pkg token revoke 550e8400-e29b-41d4-a716-446655440000
```

Revocation is immediate.

## Dependency Resolution

Stash uses flat dependency resolution. Each package name is installed once at one
version under `stashes/`.

Resolution steps:

1. collect direct and transitive constraints from the root manifest
2. detect dependency cycles
3. choose the latest version satisfying all constraints for each package
4. validate the resolved graph
5. write the lock file

If two packages require incompatible versions of the same dependency, resolution
fails with a version-conflict error.

```text
Version conflict for "json-parser"
  http-utils requires json-parser@^2.0.0
  config-loader requires json-parser@^1.0.0
  No version satisfies both constraints.
```

Circular dependencies are rejected.

```text
Circular dependency detected: A -> B -> C -> A
```

### Version Ranges

Dependency constraints use SemVer range syntax.

| Syntax           | Meaning                  |
| ---------------- | ------------------------ |
| `1.2.3`          | exact version            |
| `^1.2.3`         | `>=1.2.3 <2.0.0`         |
| `~1.2.3`         | `>=1.2.3 <1.3.0`         |
| `>=1.0.0`        | minimum version          |
| `>=1.0.0 <2.0.0` | range intersection       |
| `*`              | latest available version |

For `0.x` packages, caret ranges are conservative:

| Constraint | Range            |
| ---------- | ---------------- |
| `^0.2.3`   | `>=0.2.3 <0.3.0` |
| `^0.0.3`   | `>=0.0.3 <0.0.4` |

Pre-release versions are opt-in. Normal ranges do not match pre-releases unless the
range itself includes a pre-release. `*` matches all versions, including
pre-releases.

## Lock File

`stash-lock.json` pins every direct and transitive dependency to exact resolved
metadata. It is generated by `stash pkg install` and should be committed.

```json
{
  "lockVersion": 1,
  "stash": null,
  "resolved": {
    "http-utils": {
      "version": "1.2.4",
      "resolved": "/api/v1/packages/http-utils/1.2.4/download",
      "integrity": "sha256-def456..."
    }
  }
}
```

| Field         | Meaning                                                  |
| ------------- | -------------------------------------------------------- |
| `lockVersion` | lock format version; currently `1`                       |
| `stash`       | Stash interpreter version used for resolution, or `null` |
| `resolved`    | map of package name to resolved entry                    |

Resolved entries contain:

| Field          | Meaning                                   |
| -------------- | ----------------------------------------- |
| `version`      | exact resolved version                    |
| `resolved`     | download URL or Git source                |
| `integrity`    | `sha256-<base64>` package hash            |
| `dependencies` | transitive dependency constraints, if any |

Lock keys are sorted alphabetically. If the lock file is current, `install` uses it
without re-resolving. `update` clears lock entries to force resolution. Git
dependencies use the Git source string as `resolved`.

## Registry and Authentication

### Registry Selection

Registry commands target one registry.

Selection order:

1. `--registry <url>`
2. `STASH_REGISTRY_URL`
3. `defaultRegistry` in `~/.stash/config.json`

If no registry is available, commands that require one fail and suggest
`stash pkg login --registry <url>`.

### Credentials

Credentials are stored in `~/.stash/config.json`.

```json
{
  "defaultRegistry": "https://registry.example.com/api/v1",
  "registries": {
    "https://registry.example.com/api/v1": {
      "token": "eyJhbGciOiJIUzI1NiIs..."
    }
  }
}
```

On Unix, the file is written with restricted permissions. The first successful
login sets the default registry if no default exists. Logging out of the default
registry clears the default.

Auth token selection:

1. `--token <token>`
2. `STASH_TOKEN`
3. stored token for the target registry

### Authentication Requirements

| Command                       | Auth required |
| ----------------------------- | ------------- |
| `install`                     | no            |
| `search`                      | no            |
| `info`                        | no            |
| `scope info`                  | no            |
| `org info`                    | no            |
| `publish`                     | yes           |
| `unpublish`                   | yes           |
| `role list / assign / revoke` | yes (publish) |
| `visibility set`              | yes (publish) |
| `scope claim`                 | yes (publish) |
| `org create`                  | yes (publish) |
| `org member add / remove`     | yes (publish) |
| `org team add / member add`   | yes (publish) |
| `token create/list/revoke`    | yes           |
| `whoami`                      | yes           |

## Git Dependencies

Git dependencies use the `git:` source form.

```json
{
  "dependencies": {
    "my-lib": "git:https://github.com/user/my-lib.git#v1.0.0"
  }
}
```

Format:

```text
git:<repository-url>#<ref>
```

`<ref>` may be a tag, branch, or commit SHA. If omitted, the repository's default
branch is used.

Git dependencies are cloned to a temporary directory, optionally checked out at the
ref, copied into `stashes/{name}/`, and required to contain a valid `stash.json`.
They do not participate in version resolution and appear in `list` output as
`(git)`.

The `git` executable must be available on `PATH`.

## Publishing and Packaging

### File Selection

`pack` and `publish` create a tarball from the current package.

If the manifest has a `files` array, only those paths are included, subject to
ignore rules. `.stashignore` excludes files using `.gitignore`-style syntax.

Default ignore patterns always apply:

```text
.git/
stashes/
stash-lock.json
.env
```

`.stashignore` supports comments, globs, root-anchored paths, directory patterns,
negation, `**`, and `?`. Rules are processed in order; the last matching rule wins.

```text
*.test.stash
test/
build/
!/test/fixtures/sample.stash
```

### Tarball Safety

Tarball extraction rejects unsafe paths:

- entries containing `..`
- absolute paths
- paths that would escape the extraction directory

Leading `./` is stripped during extraction.

### Integrity

Every package tarball uses a SHA-256 integrity string in the form
`sha256-<base64>`.

The CLI computes integrity before upload, the registry verifies it on publish, the
lock file stores it, and install verifies cached/downloaded tarballs before
extracting.

## Cache

Downloaded tarballs are cached locally.

```text
~/.stash/cache/{package-name}/{version}.tar.gz
```

When installing, the cache is checked first. If a cached tarball exists and its
integrity matches the lock entry, it is reused. Otherwise it is downloaded again
and stored in the cache. The cache is shared across projects.

There are no dedicated cache-management commands. Delete cache files manually when
needed.

```bash
rm -rf ~/.stash/cache/
rm -rf ~/.stash/cache/http-utils/
```

## Security

Package-manager security properties:

- published versions are immutable
- publish/unpublish/role/visibility/scope/org/token commands require authenticated registry requests
- credentials are stored in the user config file with restricted permissions where
  supported by the platform
- `STASH_TOKEN` and `--token` allow CI usage without writing config files
- package tarballs are integrity checked
- extraction prevents path traversal
- `private: true` blocks accidental publishing

Tokens can be revoked server-side with `stash pkg token revoke`.

## Environment Variables

| Variable             | Meaning                                                          |
| -------------------- | ---------------------------------------------------------------- |
| `STASH_TOKEN`        | bearer token for authenticated commands when `--token` is absent |
| `STASH_REGISTRY_URL` | registry URL when `--registry` is absent                         |

Environment variables are the recommended CI configuration mechanism.

```bash
export STASH_TOKEN="$CI_SECRET_TOKEN"
export STASH_REGISTRY_URL="https://registry.example.com/api/v1"
stash pkg publish
```
