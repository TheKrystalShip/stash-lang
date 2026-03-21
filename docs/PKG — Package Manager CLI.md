# PKG — Package Manager CLI

> **Status:** v1.0 — Complete
> **Created:** March 2026
> **Purpose:** User guide for the `stash pkg` CLI — installing, publishing, searching, and managing Stash packages.
>
> **Companion documents:**
>
> - [Language Specification](Stash%20—%20Language%20Specification.md) — language syntax, module/import system
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions
> - [Registry — Package Registry](Registry%20—%20Package%20Registry.md) — self-hosted registry server (REST API, configuration, deployment)

---

## Table of Contents

1. [Overview](#1-overview)
2. [Quick Start](#2-quick-start)
3. [Package Manifest — `stash.json`](#3-package-manifest--stashjson)
4. [Command Reference](#4-command-reference)
5. [Dependency Resolution](#5-dependency-resolution)
6. [Lock File — `stash-lock.json`](#6-lock-file--stash-lockjson)
7. [Project Layout](#7-project-layout)
8. [Registry Management](#8-registry-management)
9. [Git Dependencies](#9-git-dependencies)
10. [Package Cache](#10-package-cache)
11. [`.stashignore`](#11-stashignore)
12. [Version Ranges](#12-version-ranges)
13. [Security](#13-security)

---

## 1. Overview

The Stash package manager is built into the `stash` CLI. All package management commands live under `stash pkg` (alias `stash p`):

```bash
stash pkg <command> [options]
stash p <command> [options]      # short alias
```

The package manager handles:

- **Dependency management** — declare, install, update, and remove packages
- **Registry interaction** — search, publish, and download packages from self-hosted registries
- **Git dependencies** — install packages directly from Git repositories
- **Integrity verification** — SHA-256 hashes on all tarballs
- **Caching** — downloaded tarballs are cached locally to avoid redundant downloads

No separate tool needs to be installed — the package manager ships as part of the `stash` binary.

---

## 2. Quick Start

### Create a new project

```bash
mkdir my-project && cd my-project
stash pkg init
```

This creates a `stash.json` manifest interactively. Use `--yes` (or `-y`) to accept all defaults:

```bash
stash pkg init --yes
```

### Connect to a registry

```bash
stash pkg login --registry https://registry.example.com/api/v1
```

The first login automatically sets the default registry. All subsequent commands use it unless overridden with `--registry`.

### Install a package

```bash
stash pkg install http-utils           # latest version
stash pkg install http-utils@1.2.0     # specific version (adds ^1.2.0 constraint)
stash pkg install http-utils@^2.0.0    # explicit constraint
```

### Use the installed package

```stash
import { get, post } from "http-utils";
import "http-utils/lib/request" as req;
```

Installed packages live in the `stashes/` directory and are importable by name.

### Publish a package

```bash
stash pkg publish
```

---

## 3. Package Manifest — `stash.json`

Every Stash project can have a `stash.json` at its root. This file defines the project metadata, dependencies, and entry point.

### Minimal Example

```json
{
  "name": "my-project",
  "version": "1.0.0"
}
```

### Full Example

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

### Field Reference

| Field          | Required             | Description                                                                        |
| -------------- | -------------------- | ---------------------------------------------------------------------------------- |
| `name`         | Yes (for publishing) | Package name ([naming rules](#package-naming-rules) below)                         |
| `version`      | Yes (for publishing) | SemVer string: `MAJOR.MINOR.PATCH` (pre-release tags allowed, e.g. `1.0.0-beta.1`) |
| `description`  | No                   | One-line description (shown in search results)                                     |
| `author`       | No                   | Author name                                                                        |
| `license`      | No                   | SPDX license identifier (`MIT`, `Apache-2.0`, etc.)                                |
| `main`         | No                   | Entry point file (default: `index.stash`)                                          |
| `repository`   | No                   | Source repository URL                                                              |
| `keywords`     | No                   | Array of strings for registry search                                               |
| `stash`        | No                   | Compatible Stash version range (e.g. `>=1.0.0`)                                    |
| `dependencies` | No                   | Map of package name to version constraint or git source                            |
| `files`        | No                   | Whitelist of files/directories to include when publishing                          |
| `registries`   | No                   | Map of registry aliases to URLs                                                    |
| `private`      | No                   | If `true`, `stash pkg publish` is blocked                                          |

### Package Naming Rules

- **Maximum length:** 64 characters
- **Unscoped names:** Must match `^[a-z][a-z0-9-]*$` — lowercase, starts with a letter, alphanumeric and hyphens only
- **Scoped names:** Must match `^@[a-z][a-z0-9-]*/[a-z][a-z0-9-]*$` — format `@scope/package-name`

Examples of valid names: `http-utils`, `config-loader`, `@myorg/deploy-tools`

---

## 4. Command Reference

### `stash pkg init`

Create a new `stash.json` interactively.

```bash
stash pkg init          # interactive prompts
stash pkg init --yes    # accept all defaults
stash pkg init -y       # short form
```

Prompts for: name (defaults to directory name), version (`1.0.0`), description, author, license (`MIT`), and entry point (`index.stash`). The directory name is sanitized to a valid package name automatically — lowercased, non-alphanumeric characters replaced with hyphens.

Exits with an error if `stash.json` already exists.

---

### `stash pkg install [specifier]`

Install dependencies. Alias: `stash pkg i`.

**Without arguments** — install all dependencies from `stash.json`:

```bash
stash pkg install
```

This resolves the full dependency tree (including transitive dependencies), writes `stash-lock.json`, and installs packages to `stashes/`.

**With a package specifier** — install a specific package and add it to `stash.json`:

```bash
stash pkg install http-utils             # latest version (constraint: *)
stash pkg install http-utils@1.2.0       # adds constraint ^1.2.0
stash pkg install http-utils@^2.0.0      # explicit constraint
stash pkg install git:https://github.com/user/repo.git#v1.0.0   # git source
```

When a bare version number is given (e.g. `@1.2.0`), the caret range `^1.2.0` is used as the constraint. Otherwise the version part is used as-is.

**Flags:**

| Flag               | Description                 |
| ------------------ | --------------------------- |
| `--registry <url>` | Use a specific registry URL |

**Output:**

```
Registry: https://registry.example.com/api/v1
Resolving dependencies...
Installing http-utils@1.2.4
Installing config-loader@0.5.3
Installing json-schema@0.2.1 (transitive, via config-loader)
Dependencies installed.
```

Already-installed packages (matching version) are skipped automatically.

---

### `stash pkg uninstall <name>`

Remove a package and its entry from `stash.json`. Alias: `stash pkg remove`.

```bash
stash pkg uninstall http-utils
stash pkg remove http-utils       # alias
```

This removes the package directory from `stashes/`, deletes the entry from `stash.json` dependencies, and updates `stash-lock.json`.

---

### `stash pkg update [name]`

Update dependencies to the latest versions that satisfy their constraints.

```bash
stash pkg update              # update all dependencies
stash pkg update http-utils   # update only http-utils
```

When updating a specific package, only that package's lock entry is cleared. When updating all, the entire `stash-lock.json` is deleted. Dependencies are then re-resolved from scratch.

**Flags:**

| Flag               | Description                 |
| ------------------ | --------------------------- |
| `--registry <url>` | Use a specific registry URL |

---

### `stash pkg list`

List installed packages as a dependency tree. Alias: `stash pkg ls`.

```bash
stash pkg list
```

**Output:**

```
my-project@1.0.0
├── config-loader@0.5.3
├── http-utils@1.2.4
├── internal-tools@(git)
└── json-schema@0.2.1 (transitive)
```

Transitive dependencies are marked with `(transitive)`. Git dependencies show `(git)` instead of a version number. Packages are sorted alphabetically.

---

### `stash pkg outdated`

Show packages with their current installed versions and declared constraints.

```bash
stash pkg outdated
```

**Output:**

```
Package                        Current         Constraint
------------------------------------------------------------
config-loader                  0.5.3           ~0.5.0
http-utils                     1.2.4           ^1.2.0
internal-tools                 (git)           git:https://...
```

If all dependencies are current: `"All dependencies are up to date."`

---

### `stash pkg search <query>`

Search the registry for packages.

```bash
stash pkg search http
stash pkg search deploy --page 2
```

**Flags:**

| Flag               | Description                 |
| ------------------ | --------------------------- |
| `--registry <url>` | Use a specific registry URL |
| `--page <number>`  | Page number (default: 1)    |

**Output:**

```
Registry: https://registry.example.com/api/v1
Found 3 packages (page 1/1):

  http-utils                     1.2.4        HTTP client utilities for Stash
  http-server                    0.3.1        Simple HTTP server framework
  http-mock                      1.0.0        HTTP request mocking for tests
```

---

### `stash pkg info <name>`

Display detailed package metadata from the registry.

```bash
stash pkg info http-utils
```

**Flags:**

| Flag               | Description                 |
| ------------------ | --------------------------- |
| `--registry <url>` | Use a specific registry URL |

**Output:**

```
http-utils
Latest: 1.2.4
Description: HTTP client utilities for Stash
License: MIT
Repository: https://github.com/example/http-utils
Owners: alice, bob

Versions:
  1.2.4           2026-03-15T10:30:00Z
  1.2.3           2026-03-01T08:00:00Z
  1.1.0           2026-02-10T14:00:00Z

README:
  # http-utils

  HTTP client utilities for the Stash scripting language.
  ...
  ... (15 more lines)
```

The README is truncated to the first 10 lines, with a count of remaining lines shown.

---

### `stash pkg publish`

Publish the current package to a registry.

```bash
stash pkg publish
stash pkg publish --registry https://registry.example.com/api/v1
```

**Flags:**

| Flag               | Description                 |
| ------------------ | --------------------------- |
| `--registry <url>` | Use a specific registry URL |

**Requirements:**

- `stash.json` must exist with `name` and `version` fields
- `private` must not be `true`
- You must be logged in to the target registry

**What happens:**

1. The manifest is validated
2. A tarball is created from the project (respecting `.stashignore`)
3. A SHA-256 integrity hash is computed
4. The tarball is uploaded to the registry with the integrity hash
5. Output: `Published deploy-toolkit@2.1.0 to https://registry.example.com/api/v1.`

Published versions are immutable — you cannot republish the same version. To release a fix, bump the version in `stash.json` and publish again. The user who first publishes a package becomes its owner. Only owners and registry admins can publish new versions.

---

### `stash pkg pack`

Create a tarball of the package locally without publishing. Useful for inspecting what would be published.

```bash
stash pkg pack
```

**Output:**

```
Packed 12 files into deploy-toolkit-2.1.0.tar.gz
lib/deploy.stash
lib/rollback.stash
index.stash
stash.json
README.md
LICENSE
...
Total size: 4.2 KB
```

The tarball is written to the current directory. File sizes are displayed as B, KB, or MB.

---

### `stash pkg unpublish <name>@<version>`

Remove a published version from the registry. Subject to the registry's unpublish window (default: 72 hours after publishing).

```bash
stash pkg unpublish http-utils@1.2.3
```

**Flags:**

| Flag               | Description                 |
| ------------------ | --------------------------- |
| `--registry <url>` | Use a specific registry URL |

Requires login. The specifier format is `name@version` — both parts are required.

---

### `stash pkg login`

Authenticate with a package registry.

```bash
stash pkg login --registry https://registry.example.com/api/v1
```

The `--registry` flag is **required** — there is no fallback for authentication commands.

You are prompted for username and password (password input is hidden). On success, the JWT token is stored in `~/.stash/config.json`. If no default registry is configured, it is set automatically.

---

### `stash pkg logout`

Remove stored credentials for a registry.

```bash
stash pkg logout --registry https://registry.example.com/api/v1
```

The `--registry` flag is **required**. If the logged-out registry was the default, the default is cleared.

---

### `stash pkg owner <action> <package> [username]`

Manage package owners.

```bash
stash pkg owner list http-utils                   # list owners
stash pkg owner ls http-utils                     # alias for list
stash pkg owner add http-utils carol              # add an owner
stash pkg owner remove http-utils bob             # remove an owner
```

**Flags:**

| Flag               | Description                 |
| ------------------ | --------------------------- |
| `--registry <url>` | Use a specific registry URL |

The `add` and `remove` subcommands require login.

---

## 5. Dependency Resolution

### Algorithm

Stash uses a flat resolution strategy with version compatibility checking:

1. **Collect constraints** — BFS traversal starting from the root manifest's dependencies. For each package, all version constraints from all dependents are accumulated.
2. **Detect cycles** — DFS-based cycle detection on the dependency graph. Circular dependencies are rejected with a clear error message (e.g. `"Circular dependency detected: A → B → A"`).
3. **Resolve versions** — For each package, find the latest version that satisfies all accumulated constraints. If no version satisfies all constraints, a version conflict error is reported.
4. **Validate** — Verify that all transitive dependencies are present in the resolved set.
5. **Generate lock file** — Record exact resolved versions, download URLs, and integrity hashes.

### Flat Installation

All packages are installed at the same level in `stashes/` — there is no nesting. Each package name can only appear once in the dependency tree, at a single version. If two packages require incompatible versions of a shared dependency, the resolver reports a conflict.

### Error Messages

**Version conflict:**

```
Version conflict for "json-parser"
  http-utils requires json-parser@^2.0.0
  config-loader requires json-parser@^1.0.0
  No version satisfies both constraints.
```

**Circular dependency:**

```
Circular dependency detected: A → B → C → A
```

**Package not found:**

```
Package 'nonexistent' not found in any configured source.
```

---

## 6. Lock File — `stash-lock.json`

The lock file pins every dependency (direct and transitive) to an exact version. It is auto-generated by `stash pkg install` and should be committed to version control.

### Format

```json
{
  "lockVersion": 1,
  "stash": null,
  "resolved": {
    "config-loader": {
      "version": "0.5.3",
      "resolved": "/api/v1/packages/config-loader/0.5.3/download",
      "integrity": "sha256-abc123...",
      "dependencies": {
        "json-schema": "^0.2.0"
      }
    },
    "http-utils": {
      "version": "1.2.4",
      "resolved": "/api/v1/packages/http-utils/1.2.4/download",
      "integrity": "sha256-def456..."
    },
    "json-schema": {
      "version": "0.2.1",
      "resolved": "/api/v1/packages/json-schema/0.2.1/download",
      "integrity": "sha256-ghi789..."
    }
  }
}
```

### Fields

| Field         | Description                                   |
| ------------- | --------------------------------------------- |
| `lockVersion` | Lock file format version (currently `1`)      |
| `stash`       | Stash interpreter version used for resolution |
| `resolved`    | Map of package name to resolved entry         |

Each resolved entry contains:

| Field          | Description                                |
| -------------- | ------------------------------------------ |
| `version`      | Exact resolved version                     |
| `resolved`     | Download URL or git source string          |
| `integrity`    | `sha256-<base64>` hash of the tarball      |
| `dependencies` | Transitive dependency constraints (if any) |

### Behavior

- Keys in the lock file are sorted alphabetically (deterministic output)
- If `stash-lock.json` exists and is up to date, `stash pkg install` uses it directly without re-resolving
- `stash pkg update` clears the lock file (or specific entries) to force re-resolution
- Git dependencies appear with an empty version and the git URL as the resolved value

---

## 7. Project Layout

After installing dependencies, a typical project looks like this:

```
my-project/
├── stash.json               ← package manifest
├── stash-lock.json           ← auto-generated lock file (commit this)
├── stashes/                  ← installed packages (add to .gitignore)
│   ├── http-utils/
│   │   ├── stash.json
│   │   ├── .stash-version    ← version marker (e.g. "1.2.4")
│   │   ├── index.stash
│   │   └── lib/
│   ├── config-loader/
│   │   ├── stash.json
│   │   ├── .stash-version
│   │   └── index.stash
│   └── json-schema/          ← transitive dependency
│       ├── stash.json
│       ├── .stash-version
│       └── index.stash
├── index.stash
└── lib/
    └── deploy.stash
```

### Key Directories and Files

| Path                            | Purpose                                                         |
| ------------------------------- | --------------------------------------------------------------- |
| `stash.json`                    | Package manifest — dependencies, metadata, entry point          |
| `stash-lock.json`               | Pinned dependency versions — commit to version control          |
| `stashes/`                      | Installed packages — add to `.gitignore`                        |
| `stashes/{name}/.stash-version` | Version marker file — prevents re-extraction if already correct |
| `~/.stash/config.json`          | User credentials and registry configuration                     |
| `~/.stash/cache/`               | Downloaded tarball cache                                        |

### Importing Installed Packages

Packages installed in `stashes/` are importable by name:

```stash
// Import from the package's main entry point (default: index.stash)
import { get, post } from "http-utils";

// Import a specific file within the package
import "http-utils/lib/request" as req;

// All .stash files in the package are importable by path
import { validate } from "config-loader/lib/validator";
```

---

## 8. Registry Management

### Credential Storage

Registry credentials are stored in `~/.stash/config.json` with restricted permissions (mode `0600` on Unix):

```json
{
  "defaultRegistry": "https://registry.example.com/api/v1",
  "registries": {
    "https://registry.example.com/api/v1": {
      "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    },
    "https://internal.corp/api/v1": {
      "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    }
  }
}
```

### Default Registry

The default registry is used when no `--registry` flag is provided. It is managed automatically:

- **Set on first login:** The first `stash pkg login --registry <url>` sets `defaultRegistry` if none is configured
- **Cleared on logout:** Logging out of the current default clears the `defaultRegistry` field
- **Manual override:** Edit `~/.stash/config.json` directly to change the default

### Registry Resolution Order

1. If `--registry <url>` is provided → use that registry
2. If `--registry` is omitted → use `defaultRegistry` from `~/.stash/config.json`
3. If no default is configured → error:

```
No default registry configured. Run 'stash pkg login --registry <url>' to set one.
```

**Important:** The CLI never searches multiple registries. Each command targets exactly one registry.

### Which Commands Need Authentication?

| Command        | Auth Required? | Notes                                     |
| -------------- | -------------- | ----------------------------------------- |
| `install`      | No             | Read endpoints are public                 |
| `search`       | No             | Search is public                          |
| `info`         | No             | Package metadata is public                |
| `publish`      | Yes            | Requires `publish` or `admin` token scope |
| `unpublish`    | Yes            | Requires `publish` or `admin` token scope |
| `owner add`    | Yes            | Requires admin privileges                 |
| `owner remove` | Yes            | Requires admin privileges                 |
| `owner list`   | No             | Package metadata is public                |

---

## 9. Git Dependencies

Packages can be installed directly from Git repositories using the `git:` prefix:

```json
{
  "dependencies": {
    "my-lib": "git:https://github.com/user/my-lib.git#v1.0.0"
  }
}
```

### Format

```
git:<repository-url>#<ref>
```

- `<repository-url>` — any URL that `git clone` accepts
- `<ref>` — optional tag, branch, or commit SHA

Examples:

```
git:https://github.com/user/repo.git#v2.0.0        # tag
git:https://github.com/user/repo.git#main           # branch
git:https://github.com/user/repo.git#abc1234         # commit
git:https://github.com/user/repo.git                 # default branch (no ref)
```

### How Git Dependencies Work

1. The repository is cloned to a temporary directory
2. If a ref is specified, it is checked out
3. The contents (excluding `.git/`) are copied into `stashes/{name}/`
4. The repository must contain a valid `stash.json`

### Limitations

- Git dependencies do not participate in version resolution — they are installed as-is
- They appear in `stash pkg list` as `(git)` instead of a version number
- The `git` command must be available on PATH

---

## 10. Package Cache

Downloaded tarballs are cached locally to avoid redundant downloads across projects and installs.

### Cache Location

```
~/.stash/cache/{package-name}/{version}.tar.gz
```

The cache directory is created automatically on first use.

### Behavior

- When installing a package, the cache is checked first
- If a cached tarball exists and its integrity hash matches, it is used directly
- Otherwise, the tarball is downloaded and stored in the cache
- The cache is shared across all projects on the system

### Managing the Cache

There are no dedicated CLI commands for cache management. The cache can be managed manually:

```bash
# View cache contents
ls ~/.stash/cache/

# Clear the entire cache
rm -rf ~/.stash/cache/

# Clear a specific package's cache
rm -rf ~/.stash/cache/http-utils/
```

---

## 11. `.stashignore`

The `.stashignore` file controls which files are excluded when creating tarballs (via `pack` and `publish`). It follows `.gitignore` syntax.

### Default Ignore Patterns

These patterns are **always** applied, even without a `.stashignore` file:

```
.git/
stashes/
stash-lock.json
.env
```

### Syntax

```
# Comments start with #
*.log                  # Glob pattern — matches any .log file
build/                 # Directory pattern — excludes entire directory
/secrets.json          # Anchored pattern — only matches at root
!important.log         # Negation — re-includes a previously excluded file
**/test/               # Double-star — matches in any directory
```

### Pattern Rules

| Pattern    | Meaning                                     |
| ---------- | ------------------------------------------- |
| `*.ext`    | Match files with extension in any directory |
| `dir/`     | Match a directory and all its contents      |
| `/file`    | Match only at the project root (anchored)   |
| `!pattern` | Negate a previous exclusion (re-include)    |
| `**`       | Match zero or more path segments            |
| `?`        | Match a single character (except `/`)       |

Rules are processed in order — the last matching rule wins.

### Example `.stashignore`

```
# Test files
*.test.stash
test/

# Build artifacts
*.log
tmp/

# IDE files
.vscode/
.idea/

# Keep this specific test fixture
!test/fixtures/sample.stash
```

---

## 12. Version Ranges

Dependency constraints in `stash.json` use SemVer range syntax:

### Range Operators

| Syntax           | Meaning                  | Resolves to                 |
| ---------------- | ------------------------ | --------------------------- |
| `1.2.3`          | Exact version            | Only `1.2.3`                |
| `^1.2.3`         | Compatible (same major)  | `>=1.2.3` and `<2.0.0`      |
| `~1.2.3`         | Approximate (same minor) | `>=1.2.3` and `<1.3.0`      |
| `>=1.0.0`        | Minimum version          | `1.0.0` or higher           |
| `>=1.0.0 <2.0.0` | Range (space = AND)      | Between `1.0.0` and `2.0.0` |
| `*`              | Any version              | Latest available            |

### 0.x Version Behavior

For pre-1.0 packages, the caret operator `^` is more conservative:

| Constraint | Range                  | Rationale                           |
| ---------- | ---------------------- | ----------------------------------- |
| `^1.2.3`   | `>=1.2.3` and `<2.0.0` | Major version is breaking           |
| `^0.2.3`   | `>=0.2.3` and `<0.3.0` | Minor version is breaking for 0.x   |
| `^0.0.3`   | `>=0.0.3` and `<0.0.4` | Patch version is breaking for 0.0.x |

### Pre-release Versions

Pre-release versions (e.g. `1.0.0-beta.1`) are **opt-in only**:

- `^1.0.0` does **not** match `1.1.0-beta.1` — pre-releases are excluded from normal ranges
- `^1.0.0-beta.1` **does** match `1.0.0-beta.2` and `1.0.0` — explicit pre-release opts in
- Wildcard `*` matches all versions including pre-releases

This prevents users from accidentally receiving unstable versions.

---

## 13. Security

### Integrity Verification

Every package tarball has a SHA-256 integrity hash in the format `sha256-<base64>`. The hash is:

- Computed by the CLI before upload and sent to the registry
- Verified by the registry on publish
- Stored in the lock file for each dependency
- Verified against cached tarballs before extraction

### Tarball Safety

The tarball extraction process includes path traversal protection:

- Entries containing `..` path components are rejected
- Leading `/` and `./` are stripped from entry paths
- All extracted paths are verified to remain within the target directory

### Credential Storage

- Credentials stored in `~/.stash/config.json` with restricted permissions (see [Section 8](#8-registry-management))
- Tokens can be revoked server-side at any time via the registry API
