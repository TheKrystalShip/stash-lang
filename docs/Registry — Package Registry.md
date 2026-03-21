# Registry — Package Registry

> **Status:** v1.0 — Complete
> **Created:** March 2026
> **Purpose:** Source of truth for the Stash Package Registry — architecture, REST API, configuration, storage, authentication, and CLI integration.
>
> **Companion documents:**
>
> - [Language Specification](Stash%20—%20Language%20Specification.md) — language syntax, type system, interpreter architecture
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions
> - [LSP — Language Server Protocol](LSP%20—%20Language%20Server%20Protocol.md) — language server
> - [DAP — Debug Adapter Protocol](DAP%20—%20Debug%20Adapter%20Protocol.md) — debug adapter server

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [REST API Reference](#3-rest-api-reference)
4. [Authentication & Authorization](#4-authentication--authorization)
5. [Configuration Reference](#5-configuration-reference)
6. [Database](#6-database)
7. [Package Storage](#7-package-storage)
8. [Package Publishing Workflow](#8-package-publishing-workflow)
9. [Rate Limiting](#9-rate-limiting)
10. [Security](#10-security)
11. [Audit Logging](#11-audit-logging)
12. [CLI Integration](#12-cli-integration)
13. [Implementation Status](#13-implementation-status)
14. [Design Decisions](#14-design-decisions)

---

## 1. Overview

The Stash Package Registry is a self-hosted package registry server for distributing and managing Stash language packages. It is the server counterpart to the `stash pkg` CLI commands — publishing, installing, and managing packages all go through this service.

**Technology stack:**

- **Runtime:** ASP.NET Core on .NET 10
- **Database:** EF Core with SQLite (default) or PostgreSQL
- **Authentication:** JWT (HMAC-SHA256) with local, LDAP, and OIDC provider support
- **Storage:** Local filesystem (default) or AWS S3

**Design goals:**

- **Self-hosted first** — teams run their own registry with zero external dependencies using the SQLite + filesystem defaults
- **Public hosting capable** — PostgreSQL and S3 backends support large-scale deployments
- **CLI-native** — every operation maps directly to a `stash pkg` subcommand
- **Secure by default** — JWT token scopes, rate limiting, integrity verification, and path traversal protection out of the box

---

## 2. Architecture

### Project Structure

```
Stash.Registry/
├── Program.cs                    # Entry point: Kestrel config, TLS, logging
├── Startup.cs                    # DI registration, middleware pipeline
├── appsettings.json                 # Default configuration
├── Auth/                         # Authentication providers & JWT
│   ├── IAuthProvider.cs          # Interface: Authenticate, CreateUser, UserExists
│   ├── LocalAuthProvider.cs      # Built-in password auth (SHA-256)
│   ├── LdapAuthProvider.cs       # LDAP integration (stub)
│   ├── OidcAuthProvider.cs       # OpenID Connect (stub)
│   └── JwtTokenService.cs        # JWT token creation & validation
├── Configuration/                # Config models (JSON deserialization)
│   ├── RegistryConfig.cs         # Root config loader
│   ├── ServerConfig.cs           # Host, port, TLS
│   ├── DatabaseConfig.cs         # SQLite/PostgreSQL
│   ├── StorageConfig.cs          # Filesystem/S3
│   ├── AuthConfig.cs             # Auth type, token expiry
│   ├── SecurityConfig.cs         # Max package size, JWT key, unpublish window
│   ├── TlsConfig.cs              # TLS cert/key paths
│   └── RateLimitingConfig.cs     # Per-category rate limits
├── Controllers/                  # REST API endpoints
│   ├── AuthController.cs         # Login, register, tokens, whoami
│   ├── PackagesController.cs     # Get, publish, unpublish, download
│   ├── SearchController.cs       # Search with pagination
│   └── AdminController.cs        # Stats, user/owner mgmt, audit log
├── Contracts/                    # DTO classes
│   ├── AuthContracts.cs          # Login/Register/Token request/response
│   ├── PackageContracts.cs       # Package/version detail, publish/unpublish
│   ├── SearchContracts.cs        # Search results, pagination
│   ├── AdminContracts.cs         # User/owner management, stats, audit
│   └── CommonContracts.cs        # ErrorResponse, SuccessResponse, HealthCheck
├── Database/                     # EF Core data layer
│   ├── RegistryDbContext.cs      # DbContext with 6 DbSets
│   ├── IRegistryDatabase.cs      # 40+ CRUD methods
│   ├── StashRegistryDatabase.cs  # EF Core implementation
│   └── Models/                   # Entity models
│       ├── PackageRecord.cs
│       ├── VersionRecord.cs
│       ├── UserRecord.cs
│       ├── TokenRecord.cs
│       ├── OwnerEntry.cs
│       ├── AuditEntry.cs
│       └── SearchResult.cs
├── Services/                     # Business logic
│   ├── PackageService.cs         # Publish/unpublish workflows
│   ├── AuditService.cs           # Audit logging
│   └── DeprecationService.cs     # (stub)
├── Storage/                      # Package file storage
│   ├── IPackageStorage.cs        # Store, Retrieve, Delete, Exists, GetSize
│   ├── FileSystemStorage.cs      # Local disk with path traversal protection
│   └── S3Storage.cs              # AWS S3 (stub)
└── Middleware/
    └── RateLimitingMiddleware.cs  # Per-category rate limiting
```

### Request Flow

```
Client (stash pkg CLI)
    │
    ▼
┌─────────────────────────────┐
│  RateLimitingMiddleware     │ ← IP/user-based throttling
├─────────────────────────────┤
│  JWT Authentication         │ ← Bearer token validation + revocation check
├─────────────────────────────┤
│  Authorization Policies     │ ← RequirePublishScope, RequireAdmin, etc.
├─────────────────────────────┤
│  Controllers                │ ← Auth, Packages, Search, Admin
│    │                        │
│    ├── PackageService       │ ← Business logic (publish, unpublish)
│    ├── AuditService         │ ← Action logging
│    └── IAuthProvider        │ ← Local / LDAP / OIDC
├─────────────────────────────┤
│  IRegistryDatabase (EF Core)│ ← SQLite or PostgreSQL
│  IPackageStorage            │ ← Filesystem or S3
└─────────────────────────────┘
```

---

## 3. REST API Reference

### Endpoint Summary

| Method | Path                                         | Auth          | Description                              |
| ------ | -------------------------------------------- | ------------- | ---------------------------------------- |
| GET    | `/`                                          | None          | Health check                             |
| POST   | `/api/v1/auth/login`                         | None          | Authenticate and receive JWT             |
| POST   | `/api/v1/auth/register`                      | None          | Create account (if registration enabled) |
| GET    | `/api/v1/auth/whoami`                        | Bearer        | Get current user info                    |
| POST   | `/api/v1/auth/tokens`                        | Bearer        | Create scoped token                      |
| DELETE | `/api/v1/auth/tokens/{id}`                   | Bearer        | Revoke token                             |
| GET    | `/api/v1/packages/{name}`                    | None          | Get package metadata and all versions    |
| GET    | `/api/v1/packages/{name}/{version}`          | None          | Get specific version details             |
| GET    | `/api/v1/packages/{name}/{version}/download` | None          | Download tarball                         |
| PUT    | `/api/v1/packages/{name}`                    | publish scope | Publish a package version                |
| DELETE | `/api/v1/packages/{name}/{version}`          | publish scope | Unpublish version (within window)        |
| GET    | `/api/v1/search?q=...&page=...&pageSize=...` | None          | Search packages                          |
| GET    | `/api/v1/admin/stats`                        | admin         | Registry statistics                      |
| POST   | `/api/v1/admin/users`                        | admin         | Create user                              |
| DELETE | `/api/v1/admin/users/{username}`             | admin         | Delete user                              |
| PUT    | `/api/v1/admin/packages/{name}/owners`       | admin         | Add or remove package owners             |
| GET    | `/api/v1/admin/audit-log`                    | admin         | Query audit log                          |

---

### Health Check

**GET /**

Returns a minimal health check response. No authentication required.

```json
{
  "status": "ok",
  "version": "1.0.0"
}
```

---

### Auth Endpoints

#### POST /api/v1/auth/login

Authenticate with username and password. Returns a JWT.

**Request:**

```json
{
  "username": "alice",
  "password": "s3cr3tpassword"
}
```

**Response `200 OK`:**

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-06-20T12:00:00Z"
}
```

**Error responses:**

| Status | Description                  |
| ------ | ---------------------------- |
| 400    | Missing username or password |
| 401    | Invalid credentials          |
| 429    | Rate limit exceeded          |

---

#### POST /api/v1/auth/register

Create a new user account. Only available when `auth.registrationEnabled` is `true`.

**Request:**

```json
{
  "username": "alice",
  "password": "s3cr3tpassword"
}
```

**Response `201 Created`:**

```json
{
  "ok": true,
  "username": "alice"
}
```

**Error responses:**

| Status | Description                                             |
| ------ | ------------------------------------------------------- |
| 400    | Validation failed (username format, password too short) |
| 409    | Username already taken                                  |
| 403    | Registration is disabled                                |
| 429    | Rate limit exceeded                                     |

---

#### GET /api/v1/auth/whoami

Returns information about the currently authenticated user.

**Response `200 OK`:**

```json
{
  "username": "alice",
  "role": "user"
}
```

---

#### POST /api/v1/auth/tokens

Create a new named, scoped token. Useful for CI/CD pipelines.

**Request:**

```json
{
  "scope": "publish",
  "description": "CI deploy token"
}
```

Scope must be `"read"`, `"publish"`, or `"admin"`. Only admin users can create admin-scoped tokens.

**Response `201 Created`:**

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenId": "550e8400-e29b-41d4-a716-446655440000",
  "scope": "publish",
  "expiresAt": "2026-06-20T12:00:00Z",
  "description": "CI deploy token"
}
```

**Note:** The `token` value is only returned at creation time and cannot be retrieved again. The `tokenId` can be used to revoke the token later.

---

#### DELETE /api/v1/auth/tokens/{id}

Revoke a token by its ID. Takes effect immediately — the token record is removed from the database. Since the `OnTokenValidated` middleware checks the token's JTI against the database on every request, revocation takes effect on the next request.

**Response `200 OK`:**

```json
{
  "ok": true
}
```

**Error responses:**

| Status | Description                               |
| ------ | ----------------------------------------- |
| 403    | Token belongs to another user (non-admin) |
| 404    | Token not found                           |

---

### Package Endpoints

#### GET /api/v1/packages/{name}

Returns package metadata and the full version list.

**Response `200 OK`:**

```json
{
  "name": "stash-http",
  "description": "HTTP client utilities for Stash",
  "owners": ["alice", "bob"],
  "license": "MIT",
  "repository": "https://github.com/example/stash-http",
  "keywords": ["http", "client"],
  "readme": "# stash-http\n\nHTTP client utilities...",
  "versions": {
    "1.2.0": {
      "version": "1.2.0",
      "stashVersion": ">=1.0.0",
      "dependencies": {},
      "integrity": "sha256-abc123...",
      "publishedAt": "2026-03-10T14:00:00.000Z",
      "publishedBy": "alice"
    },
    "1.1.0": {
      "version": "1.1.0",
      "stashVersion": ">=1.0.0",
      "dependencies": {},
      "integrity": "sha256-def456...",
      "publishedAt": "2026-02-20T09:00:00.000Z",
      "publishedBy": "alice"
    }
  },
  "latest": "1.2.0",
  "createdAt": "2026-02-01T10:00:00.000Z",
  "updatedAt": "2026-03-10T14:00:00.000Z"
}
```

Note: `versions` is a dictionary keyed by version string, not an array.

**Error responses:**

| Status | Description       |
| ------ | ----------------- |
| 404    | Package not found |

---

#### GET /api/v1/packages/{name}/{version}

Returns details for a specific version.

**Response `200 OK`:**

```json
{
  "version": "1.2.0",
  "stashVersion": ">=1.0.0",
  "dependencies": {},
  "integrity": "sha256-abc123...",
  "publishedAt": "2026-03-10T14:00:00.000Z",
  "publishedBy": "alice"
}
```

**Error responses:**

| Status | Description                  |
| ------ | ---------------------------- |
| 404    | Package or version not found |

---

#### GET /api/v1/packages/{name}/{version}/download

Downloads the tarball for the specified version. Returns the raw `.tar.gz` binary.

**Response `200 OK`:** Binary tarball (`application/gzip`). The `X-Integrity` response header contains the SHA-256 integrity hash.

**Error responses:**

| Status | Description                  |
| ------ | ---------------------------- |
| 404    | Package or version not found |

---

#### PUT /api/v1/packages/{name}

Publish a new version of a package. Requires a token with `publish` or `admin` scope. The request body is the raw tarball binary.

**Request headers:**

```
Content-Type: application/gzip
X-Version: 1.2.0
X-Integrity: sha256-abc123...   (optional — server verifies if present)
Authorization: Bearer <token>
```

**Request body:** Raw `.tar.gz` tarball. Must contain a `stash.json` manifest and at least one `.stash` file.

**Response `201 Created`:**

```json
{
  "ok": true,
  "package": "stash-http",
  "version": "1.2.0",
  "integrity": "sha256-abc123..."
}
```

**Error responses:**

| Status | Description                                                                        |
| ------ | ---------------------------------------------------------------------------------- |
| 400    | Missing manifest, no `.stash` files, integrity mismatch, or version already exists |
| 403    | Not a package owner                                                                |
| 429    | Rate limit exceeded                                                                |

---

#### DELETE /api/v1/packages/{name}/{version}

Unpublish a version. The caller must be a package owner, and publication must be within the `UnpublishWindow` (default 72 hours).

**Response `200 OK`:**

```json
{
  "ok": true,
  "package": "stash-http",
  "version": "1.2.0"
}
```

**Error responses:**

| Status | Description                                        |
| ------ | -------------------------------------------------- |
| 400    | Unpublish window has expired, or version not found |
| 403    | Not a package owner                                |

---

### Search Endpoint

#### GET /api/v1/search

Search packages by name or description.

**Query parameters:**

| Parameter  | Type    | Default | Description                |
| ---------- | ------- | ------- | -------------------------- |
| `q`        | string  | —       | Search query (required)    |
| `page`     | integer | `1`     | Page number (1-based)      |
| `pageSize` | integer | `20`    | Results per page (max 100) |

**Response `200 OK`:**

```json
{
  "packages": [
    {
      "name": "stash-http",
      "description": "HTTP client utilities for Stash",
      "latest": "1.2.0",
      "keywords": ["http", "client"],
      "updatedAt": "2026-03-10T14:00:00.000Z"
    }
  ],
  "totalCount": 3,
  "page": 1,
  "pageSize": 20,
  "totalPages": 1
}
```

---

### Admin Endpoints

All admin endpoints require a token with `admin` scope and the `admin` role.

#### GET /api/v1/admin/stats

Returns aggregate registry statistics.

**Response `200 OK`:**

```json
{
  "users": 15
}
```

The current stats endpoint returns the total number of registered users.

---

#### POST /api/v1/admin/users

Create a user account bypassing registration settings.

**Request:**

```json
{
  "username": "carol",
  "password": "initialpassword",
  "role": "user"
}
```

**Response `201 Created`:**

```json
{
  "ok": true,
  "username": "carol",
  "role": "user"
}
```

---

#### DELETE /api/v1/admin/users/{username}

Delete a user account and all associated owner entries. Tokens are removed via foreign key cascade.

**Response `200 OK`:**

```json
{
  "ok": true
}
```

---

#### PUT /api/v1/admin/packages/{name}/owners

Add or remove package owners.

**Request:**

```json
{
  "add": ["carol"],
  "remove": ["bob"]
}
```

**Response `200 OK`:**

```json
{
  "owners": ["alice", "carol"]
}
```

---

#### GET /api/v1/admin/audit-log

Query the audit log with optional filters.

**Query parameters:**

| Parameter  | Type    | Description                |
| ---------- | ------- | -------------------------- |
| `package`  | string  | Filter by package name     |
| `action`   | string  | Filter by action type      |
| `page`     | integer | Page number (1-based)      |
| `pageSize` | integer | Results per page (max 100) |

**Response `200 OK`:**

```json
{
  "entries": [
    {
      "id": 42,
      "action": "publish",
      "package": "stash-http",
      "version": "1.2.0",
      "user": "alice",
      "target": null,
      "ip": "203.0.113.5",
      "timestamp": "2026-03-10T14:00:00Z"
    }
  ],
  "totalCount": 5,
  "page": 1,
  "pageSize": 50,
  "totalPages": 1
}
```

Audit log pagination defaults to `pageSize=50` with a maximum of `200`.

---

## 4. Authentication & Authorization

### JWT Tokens

The registry uses JWT (JSON Web Tokens) with HMAC-SHA256 symmetric signing. Every protected endpoint validates the `Authorization: Bearer <token>` header. On each request, the token's JTI (JWT ID) claim is checked against the database to support immediate revocation.

**Token claims:**

| Claim   | Description                                |
| ------- | ------------------------------------------ |
| `sub`   | Username                                   |
| `jti`   | Unique token ID (for revocation)           |
| `scope` | Token scope: `read`, `publish`, or `admin` |
| `role`  | User role: `user` or `admin`               |
| `exp`   | Expiry timestamp                           |

### Token Scopes

| Scope     | Description                                                |
| --------- | ---------------------------------------------------------- |
| `read`    | Read-only access (currently all read endpoints are public) |
| `publish` | Publish and unpublish packages                             |
| `admin`   | Full access, including admin endpoints                     |

### Authorization Policies

| Policy                | Requirement                                                |
| --------------------- | ---------------------------------------------------------- |
| `RequirePublishScope` | Token must have `publish` or `admin` scope claim           |
| `RequireAdminScope`   | Token must have `admin` scope claim                        |
| `RequireAdmin`        | Token must have `admin` scope claim AND `admin` role claim |

### Token Revocation

Token revocation works through the token records table. Each JWT contains a `jti` claim whose value matches the `id` column of the `tokens` table. When a token is deleted (via `DELETE /api/v1/auth/tokens/{id}`), the corresponding record is removed from the database. The `OnTokenValidated` event in the JWT bearer middleware looks up every incoming token's `jti` in the database — if the record is missing, the request is rejected. Revocation takes effect on the next request.

### Auth Provider Backends

The registry supports three authentication provider backends, configured via `auth.type`:

| Provider | Status | Description                                                      |
| -------- | ------ | ---------------------------------------------------------------- |
| `local`  | ✅     | Built-in password authentication. Passwords hashed with SHA-256. |
| `ldap`   | ❌     | LDAP/Active Directory integration. Stub — not yet implemented.   |
| `oidc`   | ❌     | OpenID Connect delegation. Stub — not yet implemented.           |

All providers implement `IAuthProvider`:

```csharp
public interface IAuthProvider
{
    bool Authenticate(string username, string password);
    void CreateUser(string username, string password);
    bool UserExists(string username);
}
```

### Registration

User self-registration is controlled by `auth.registrationEnabled`. When disabled, only admins can create accounts via `POST /api/v1/admin/users`. Disable registration in production environments where user provisioning is centrally managed.

---

## 5. Configuration Reference

The registry reads configuration from `appsettings.json` in the working directory at startup. All values have defaults — the server runs with zero configuration changes for local development.

### Full Default Configuration

```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 8080,
    "BasePath": "/api/v1",
    "Tls": { "Enabled": false, "Cert": "", "Key": "" }
  },
  "Storage": {
    "Type": "filesystem",
    "Path": "data/packages",
    "Bucket": "",
    "Region": "",
    "Endpoint": "",
    "AccessKey": "",
    "SecretKey": ""
  },
  "Database": {
    "Type": "sqlite",
    "Path": "data/registry.db",
    "ConnectionString": ""
  },
  "Auth": {
    "Type": "local",
    "RegistrationEnabled": true,
    "TokenExpiry": "90d",
    "LdapServer": "",
    "LdapBaseDn": "",
    "LdapUserFilter": "",
    "OidcAuthority": "",
    "OidcClientId": "",
    "OidcClientSecret": ""
  },
  "Security": {
    "MaxPackageSize": "10MB",
    "RequiredIntegrity": "sha256",
    "UnpublishWindow": "72h",
    "JwtSigningKey": null
  },
  "RateLimiting": {
    "Enabled": true,
    "Auth": {
      "MaxAttempts": 10,
      "WindowSeconds": 300,
      "MaxPerHour": 60,
      "MaxPerMinute": 10
    },
    "Publish": {
      "MaxAttempts": 5,
      "WindowSeconds": 300,
      "MaxPerHour": 30,
      "MaxPerMinute": 5
    },
    "Download": {
      "MaxAttempts": 100,
      "WindowSeconds": 300,
      "MaxPerHour": 1000,
      "MaxPerMinute": 120
    },
    "Search": {
      "MaxAttempts": 50,
      "WindowSeconds": 300,
      "MaxPerHour": 500,
      "MaxPerMinute": 60
    }
  }
}
```

---

### Server

| Property      | Type    | Default     | Description                                               |
| ------------- | ------- | ----------- | --------------------------------------------------------- |
| `Host`        | string  | `"0.0.0.0"` | Bind address. Use `"127.0.0.1"` to restrict to localhost. |
| `Port`        | integer | `8080`      | TCP port to listen on.                                    |
| `BasePath`    | string  | `"/api/v1"` | URL prefix for all API routes.                            |
| `Tls.Enabled` | bool    | `false`     | Enable TLS termination.                                   |
| `Tls.Cert`    | string  | `""`        | Path to PEM certificate file.                             |
| `Tls.Key`     | string  | `""`        | Path to PEM private key file.                             |

---

### Storage

| Property    | Type   | Default           | Description                                              |
| ----------- | ------ | ----------------- | -------------------------------------------------------- |
| `Type`      | string | `"filesystem"`    | Storage backend: `"filesystem"` or `"s3"`.               |
| `Path`      | string | `"data/packages"` | Root directory for filesystem storage.                   |
| `Bucket`    | string | `""`              | S3 bucket name.                                          |
| `Region`    | string | `""`              | AWS region (e.g. `"us-east-1"`).                         |
| `Endpoint`  | string | `""`              | Custom S3 endpoint URL for MinIO or compatible services. |
| `AccessKey` | string | `""`              | AWS access key ID.                                       |
| `SecretKey` | string | `""`              | AWS secret access key.                                   |

---

### Database

| Property           | Type   | Default              | Description                                                                |
| ------------------ | ------ | -------------------- | -------------------------------------------------------------------------- |
| `Type`             | string | `"sqlite"`           | Database backend: `"sqlite"` or `"postgresql"`.                            |
| `Path`             | string | `"data/registry.db"` | SQLite database file path. The `data/` directory is created automatically. |
| `ConnectionString` | string | `""`                 | PostgreSQL connection string. Required when `Type` is `"postgresql"`.      |

**PostgreSQL connection string example:**

```
Host=localhost;Port=5432;Database=stash_registry;Username=stash;Password=secret
```

---

### Auth

| Property              | Type   | Default   | Description                                                                       |
| --------------------- | ------ | --------- | --------------------------------------------------------------------------------- |
| `Type`                | string | `"local"` | Auth provider: `"local"`, `"ldap"`, or `"oidc"`.                                  |
| `RegistrationEnabled` | bool   | `true`    | Allow unauthenticated users to create accounts.                                   |
| `TokenExpiry`         | string | `"90d"`   | Default JWT lifetime. Accepts duration strings: `"30d"`, `"24h"`, `"3600s"`.      |
| `LdapServer`          | string | `""`      | LDAP server URI (e.g. `"ldap://ldap.example.com"`). Used when `Type` is `"ldap"`. |
| `LdapBaseDn`          | string | `""`      | LDAP base DN for user searches.                                                   |
| `LdapPort`            | int    | `389`     | LDAP server port.                                                                 |
| `LdapUserFilter`      | string | `""`      | LDAP filter template (e.g. `"(uid={0})"`).                                        |
| `OidcAuthority`       | string | `""`      | OIDC issuer URL. Used when `Type` is `"oidc"`.                                    |
| `OidcClientId`        | string | `""`      | OIDC client ID.                                                                   |
| `OidcClientSecret`    | string | `""`      | OIDC client secret.                                                               |

---

### Security

| Property            | Type   | Default    | Description                                                                                                                               |
| ------------------- | ------ | ---------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `MaxPackageSize`    | string | `"10MB"`   | Maximum tarball upload size. Accepts `"MB"` and `"GB"` suffixes.                                                                          |
| `RequiredIntegrity` | string | `"sha256"` | Integrity algorithm. Only `"sha256"` is currently supported.                                                                              |
| `UnpublishWindow`   | string | `"72h"`    | How long after publishing a version can be unpublished.                                                                                   |
| `JwtSigningKey`     | string | `null`     | HMAC-SHA256 signing key. Must be at least 32 characters. If `null`, a random key is generated at startup — tokens won't survive restarts. |

---

### RateLimiting

Applies to all four category types (`Auth`, `Publish`, `Download`, `Search`).

| Property                   | Type    | Description                               |
| -------------------------- | ------- | ----------------------------------------- |
| `Enabled`                  | bool    | Enable or disable rate limiting globally. |
| `{Category}.MaxAttempts`   | integer | Maximum requests per window.              |
| `{Category}.WindowSeconds` | integer | Sliding window duration in seconds.       |
| `{Category}.MaxPerHour`    | integer | Hard hourly cap.                          |
| `{Category}.MaxPerMinute`  | integer | Hard per-minute cap.                      |

---

## 6. Database

### Backends

| Backend    | When to use                                                                                                                              |
| ---------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| SQLite     | Default. Zero-configuration, single-file. Suitable for small teams and local registries. The `data/` directory is created automatically. |
| PostgreSQL | Production deployments with multiple concurrent writers, large package counts, or high traffic.                                          |

### Schema Overview

The database contains six tables managed by EF Core. Column names shown are the database column names (snake_case), as configured in `RegistryDbContext.OnModelCreating`.

#### packages

Stores one record per package name. The package name is the primary key — there is no auto-increment integer ID.

| Column        | Type     | Constraints | Description                        |
| ------------- | -------- | ----------- | ---------------------------------- |
| `name`        | string   | PK          | Package name (e.g. `"stash-http"`) |
| `description` | string   |             | Short description                  |
| `license`     | string   |             | SPDX license identifier            |
| `repository`  | string   |             | Source repository URL              |
| `readme`      | string   |             | Extracted README.md content        |
| `keywords`    | string   |             | JSON array stored as string        |
| `latest`      | string   | not null    | Latest published version tag       |
| `created_at`  | datetime |             | First publication timestamp        |
| `updated_at`  | datetime |             | Last modification timestamp        |

#### versions

One record per published version. Composite primary key on `(package_name, version)`.

| Column          | Type     | Constraints             | Description                        |
| --------------- | -------- | ----------------------- | ---------------------------------- |
| `package_name`  | string   | PK, FK → packages(name) | Owning package                     |
| `version`       | string   | PK                      | Semver version string              |
| `stash_version` | string   |                         | Required Stash interpreter version |
| `dependencies`  | string   |                         | JSON object of dependencies        |
| `integrity`     | string   | not null                | `sha256-<base64>` hash of tarball  |
| `published_at`  | datetime |                         | Publication timestamp              |
| `published_by`  | string   | not null                | Publishing username                |

Foreign key cascades on delete — removing a package removes all its versions.

#### users

Registry user accounts. Username is the primary key.

| Column          | Type     | Constraints                | Description                |
| --------------- | -------- | -------------------------- | -------------------------- |
| `username`      | string   | PK                         | Login name                 |
| `password_hash` | string   | not null                   | SHA-256 hash of password   |
| `role`          | string   | not null, default `"user"` | `"user"` or `"admin"`      |
| `created_at`    | datetime |                            | Account creation timestamp |

#### tokens

Scoped API tokens. Each token is identified by a GUID that also serves as the JWT's `jti` claim.

| Column        | Type     | Constraints                    | Description                         |
| ------------- | -------- | ------------------------------ | ----------------------------------- |
| `id`          | string   | PK                             | Token ID (matches JWT `jti` claim)  |
| `username`    | string   | FK → users(username), not null | Token owner                         |
| `token_hash`  | string   | not null                       | SHA-256 hash of the token           |
| `scope`       | string   | not null, default `"publish"`  | `"read"`, `"publish"`, or `"admin"` |
| `created_at`  | datetime |                                | Creation timestamp                  |
| `expires_at`  | datetime |                                | Expiry timestamp                    |
| `description` | string   |                                | Human-readable label                |

Foreign key cascades on delete — removing a user removes all their tokens.

#### owners

Package ownership mapping. Composite primary key on `(package_name, username)`.

| Column         | Type   | Constraints             | Description    |
| -------------- | ------ | ----------------------- | -------------- |
| `package_name` | string | PK, FK → packages(name) | Package        |
| `username`     | string | PK                      | Owner username |

Foreign key cascades on delete — removing a package removes all its ownership entries.

#### audit_log

Immutable audit trail of all registry state changes.

| Column      | Type     | Constraints        | Description                                       |
| ----------- | -------- | ------------------ | ------------------------------------------------- |
| `id`        | int      | PK, auto-increment | Internal identifier                               |
| `action`    | string   | not null           | Action type (see [Section 11](#11-audit-logging)) |
| `package`   | string   |                    | Affected package name                             |
| `version`   | string   |                    | Affected version                                  |
| `user`      | string   |                    | Actor username                                    |
| `target`    | string   |                    | Secondary subject (e.g. target username)          |
| `ip`        | string   |                    | Client IP address                                 |
| `timestamp` | datetime |                    | Event time (UTC)                                  |

---

## 7. Package Storage

### FileSystemStorage

The default backend. Stores tarballs on local disk under a configurable root directory (`data/packages/` by default).

**Storage path format:**

```
{rootDir}/{safeName}/{version}.tar.gz
```

Where `{safeName}` is the package name with all characters outside `[a-zA-Z0-9_-]` replaced, preventing directory traversal via the package name field.

**Path traversal protection:** Before any read or write, `FileSystemStorage` resolves the canonical path and verifies that it begins with the configured root directory. Requests with names that resolve outside the root are rejected.

### S3Storage

Stub — not yet implemented. The `IPackageStorage` interface is satisfied and the class compiles, but all methods throw `NotImplementedException`. The configuration keys (`Bucket`, `Region`, `Endpoint`, `AccessKey`, `SecretKey`) are accepted but unused.

### IPackageStorage Interface

```csharp
public interface IPackageStorage
{
    void Store(string packageName, string version, Stream tarball);
    Stream? Retrieve(string packageName, string version);
    bool Exists(string packageName, string version);
    bool Delete(string packageName, string version);
    long GetSize(string packageName, string version);
}
```

Swapping storage backends requires only changing `storage.type` in `appsettings.json` — no code changes.

---

## 8. Package Publishing Workflow

### Publish

1. Client sends a `PUT /api/v1/packages/{name}` request with the tarball as the request body. An optional `X-Integrity` header can be included for client-side verification. The version is read from the `stash.json` manifest inside the tarball.
2. Server validates tarball size against `security.maxPackageSize`. Returns `413` if exceeded.
3. Server extracts `stash.json` from the tarball and validates it is a well-formed Stash manifest.
4. Server verifies at least one `.stash` file exists in the tarball.
5. Server computes the SHA-256 integrity hash of the tarball content.
6. If the client sent an `X-Integrity` header, the server verifies the client's hash matches. Returns `400` on mismatch.
7. Server checks that the version does not already exist (versions are immutable). Returns `409` if it does.
8. Server creates a package record if this is the first version, then adds a version record.
9. Server extracts `README.md` from the tarball and stores its content in the package record if present.
10. Server stores the tarball in the configured storage backend.
11. Server writes an audit log entry for the publish action.

### Unpublish

1. Client sends `DELETE /api/v1/packages/{name}/{version}` with a `publish`-scoped token.
2. Server verifies the authenticated user is a package owner. Returns `403` if not.
3. Server checks that the version's publication timestamp is within the `UnpublishWindow` (default 72 hours). Returns `409` if the window has expired.
4. Server deletes the tarball from the storage backend.
5. Server removes the version record from the database.
6. If this was the last version, the package record remains in the database (it is not deleted — the name stays reserved).
7. Server writes an audit log entry for the unpublish action.

---

## 9. Rate Limiting

`RateLimitingMiddleware` applies per-category limits before requests reach any controller. Requests that exceed the configured threshold receive a `429 Too Many Requests` response with a `Retry-After` header indicating the earliest time to retry.

### Categories

| Category   | Key        | Applies to                    |
| ---------- | ---------- | ----------------------------- |
| `Auth`     | IP address | Login and register endpoints  |
| `Publish`  | Username   | Package publish and unpublish |
| `Download` | IP address | Package download              |
| `Search`   | IP address | Search endpoint               |

### Behavior

- Each category maintains a sliding-window bucket per key (IP or username).
- Buckets are stored in memory. Stale buckets (last activity older than the window) are cleaned up every 1000 requests to bound memory usage.
- `Enabled: false` in configuration disables the middleware entirely — no overhead.
- Limits are independently configurable per category via `rateLimiting.{category}.*` in `appsettings.json`.

### Rate Limit Response

```
HTTP/1.1 429 Too Many Requests
Retry-After: 47
Content-Type: application/json

{
  "error": "Rate limit exceeded. Try again in 47 seconds."
}
```

---

## 10. Security

### JWT Signing Key

The `security.jwtSigningKey` must be at least 32 characters long to satisfy HMAC-SHA256 requirements. If `null` or absent, the server generates a random key at startup and logs a warning:

```
WARN: No JwtSigningKey configured. A random key was generated.
      Tokens will not survive a server restart.
```

For production, always set an explicit key and protect it as a secret.

### Integrity Verification

Every published package has a `sha256-<base64>` integrity hash stored in the version record. The hash covers the raw tarball bytes. Clients can supply an `X-Integrity` header when publishing — the server verifies the client's hash matches its own computation before storing the tarball. The same hash is returned in the `X-Integrity` response header on downloads, allowing clients to verify the download was not corrupted or tampered with.

### Path Traversal Protection

`FileSystemStorage` resolves all storage paths to their canonical form using `Path.GetFullPath` and checks that the result is prefixed by the configured root directory before performing any file operation. Package names and version strings that would escape the root are rejected.

### Input Validation

| Field        | Constraint                                    |
| ------------ | --------------------------------------------- |
| Username     | 1–64 characters, `[a-zA-Z0-9_-]` only         |
| Password     | Minimum 8 characters                          |
| Package name | Validated against `stash.json` manifest rules |
| Version      | Semver string from `stash.json` manifest      |

### Known Limitations

- **Password hashing:** Passwords are currently hashed with SHA-256. This is not a memory-hard function and is vulnerable to GPU-accelerated brute-force attacks. Migration to bcrypt or Argon2id is tracked as PA-3 and listed in the implementation status table.

---

## 11. Audit Logging

All state-changing operations produce an immutable audit log entry. No read-only operations are logged.

### Logged Actions

| Action         | Trigger                     |
| -------------- | --------------------------- |
| `publish`      | Package version published   |
| `unpublish`    | Package version unpublished |
| `user.create`  | User account created        |
| `user.disable` | User account disabled       |
| `owner.add`    | Package owner added         |
| `owner.remove` | Package owner removed       |
| `token.create` | Token created               |
| `token.revoke` | Token revoked               |

### Entry Schema

Each audit entry records:

| Field       | Description                                             |
| ----------- | ------------------------------------------------------- |
| `action`    | Action type from the table above                        |
| `package`   | Affected package name (may be null for user actions)    |
| `version`   | Affected version string (may be null)                   |
| `user`      | Username of the actor performing the action             |
| `target`    | Secondary subject username (for user and owner actions) |
| `ip`        | Client IP address at time of request                    |
| `timestamp` | UTC timestamp of the action                             |

### Querying the Audit Log

The admin endpoint `GET /api/v1/admin/audit-log` supports filtering by `package` and `action`, with page-based pagination. Log entries are returned in descending chronological order (newest first).

---

## 12. CLI Integration

The `stash pkg` CLI commands map directly to registry API calls. Authentication tokens are stored in `~/.stash/config.json` with mode `0600` on Unix systems.

### Command Reference

| CLI Command           | HTTP Request                                     | Description                        |
| --------------------- | ------------------------------------------------ | ---------------------------------- |
| `stash pkg login`     | `POST /api/v1/auth/login`                        | Authenticate and store token       |
| `stash pkg logout`    | —                                                | Remove stored credentials          |
| `stash pkg publish`   | `PUT /api/v1/packages/{name}`                    | Upload tarball and publish version |
| `stash pkg unpublish` | `DELETE /api/v1/packages/{name}/{version}`       | Unpublish a version                |
| `stash pkg install`   | `GET /api/v1/packages/{name}/{version}/download` | Download and install packages      |
| `stash pkg update`    | `GET /api/v1/packages/{name}/{version}/download` | Re-resolve and update dependencies |
| `stash pkg search`    | `GET /api/v1/search`                             | Search for packages                |
| `stash pkg info`      | `GET /api/v1/packages/{name}`                    | Display package metadata           |
| `stash pkg owner`     | `PUT /api/v1/admin/packages/{name}/owners`       | Manage package owners              |

### Registry Resolution

Stash follows the npm/Cargo model: one default registry, explicit overrides. Every registry-facing command accepts `--registry <url>` to target a specific registry. When omitted, the CLI uses the `defaultRegistry` from `~/.stash/config.json`. The CLI always prints which registry is being used:

```
$ stash pkg search http-client
Registry: https://registry.example.com/api/v1
Found 3 packages (page 1/1):
  ...
```

**Resolution order:**

1. If `--registry <url>` is provided → use that registry exclusively
2. If `--registry` is omitted → use `defaultRegistry` from `~/.stash/config.json`
3. If no default is configured → error: `"No default registry configured. Run 'stash pkg login --registry <url>' to set one."`

**Important:** The CLI never searches multiple registries. Each command invocation targets exactly one registry — either the explicit `--registry` value or the configured default.

**Authentication commands** (`login`, `logout`) always require an explicit `--registry` flag — there is no fallback, since these commands establish the registry configuration itself:

```bash
stash pkg login --registry https://registry.example.com/api/v1    # required
stash pkg logout --registry https://registry.example.com/api/v1   # required
```

**Default registry lifecycle:**

- **Set automatically:** The first `stash pkg login --registry <url>` sets `defaultRegistry` if none is configured
- **Cleared on logout:** Logging out of the current default clears the `defaultRegistry` field
- **Manual override:** Edit `~/.stash/config.json` directly to change the default

### Credential Storage

```
~/.stash/config.json    (Unix mode 0600)
```

Registries are stored as a URL-keyed dictionary. Each entry holds an optional authentication token:

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

Logging in to a registry adds an entry and sets `defaultRegistry` if none is configured. Logging out removes the token and clears `defaultRegistry` if it pointed to the logged-out registry.

### Typical Workflow

```bash
# 1. Log in to a registry
stash pkg login --registry https://registry.example.com/api/v1

# 2. Initialize a new package
stash pkg init

# 3. Install dependencies (uses default registry)
stash pkg install http-client@1.2.0

# 4. Publish a package (uses default registry)
stash pkg publish

# 5. Search for packages on a different registry
stash pkg search http-client --registry https://other-registry.example.com/api/v1
```

---

## 13. Implementation Status

| Feature                              | Status         |
| ------------------------------------ | -------------- |
| Package publishing & unpublishing    | ✅             |
| Package search with pagination       | ✅             |
| JWT authentication with token scopes | ✅             |
| Local auth provider                  | ✅             |
| SQLite database backend              | ✅             |
| PostgreSQL database backend          | ✅             |
| Filesystem package storage           | ✅             |
| Rate limiting middleware             | ✅             |
| Audit logging                        | ✅             |
| Package ownership management         | ✅             |
| TLS support                          | ✅             |
| Configurable via JSON                | ✅             |
| LDAP authentication                  | ❌ stub        |
| OIDC authentication                  | ❌ stub        |
| S3 package storage                   | ❌ stub        |
| Package deprecation                  | ❌ not started |
| Memory-hard password hashing (PA-3)  | ❌ not started |

---

## 14. Design Decisions

| Decision            | Choice                                   | Rationale                                                                                                                                            |
| ------------------- | ---------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| Database ORM        | EF Core                                  | Type-safe queries, migration support, and transparent multi-provider (SQLite/PostgreSQL) via a single `DbContext`                                    |
| Auth tokens         | JWT                                      | Stateless verification, standard claims format, scope embedding without database round-trips on every read request                                   |
| Token revocation    | JTI database check on `OnTokenValidated` | Immediate revocation is required despite JWT being stateless — JTI lookup adds one indexed query per authenticated request                           |
| Config format       | JSON file (`appsettings.json`)              | Simple, no external dependencies, consistent with the `stash.json` package manifest format already familiar to Stash users                           |
| Storage abstraction | `IPackageStorage` interface              | Swap filesystem for S3 (or any other backend) via config alone, with no controller or service code changes                                           |
| Rate limiting       | Custom middleware                        | Lightweight, no external service dependencies, category-aware (different limits for publish vs. download), and trivially disableable for development |
| API style           | Controller-based REST                    | Clear route-to-controller mapping, attribute-based authorization, straightforward unit testability via `WebApplicationFactory`                       |
| API versioning      | URL prefix (`/api/v1/`)                  | Explicit, highly visible, no content negotiation complexity — the registry is a simple CRUD service, not an evolving multi-version API               |
