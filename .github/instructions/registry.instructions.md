---
description: "Use when: working on the Stash Package Registry server — REST API endpoints (auth, packages, search, admin), database layer (EF Core, SQLite/PostgreSQL), package storage (filesystem/S3), JWT authentication, rate limiting, audit logging, configuration, or registry tests."
applyTo: "Stash.Registry/**"
---

# Package Registry Guidelines

The Stash Package Registry is a self-hosted ASP.NET Core server that powers all `stash pkg` CLI commands — publishing, installing, searching, and managing packages. It uses EF Core for data access (SQLite or PostgreSQL), JWT for authentication, and local filesystem (or S3) for tarball storage. See `docs/Registry — Package Registry.md` for the full spec and `Stash.Registry/README.md` for setup and deployment.

## Project Structure

```
Stash.Registry/
├── Program.cs                    → Entry point: Kestrel config, TLS, logging
├── Startup.cs                    → DI registration, middleware pipeline
├── appsettings.json              → Default configuration
├── Auth/                         → Authentication providers & JWT
│   ├── IAuthProvider.cs          → Interface: Authenticate, CreateUser, UserExists
│   ├── LocalAuthProvider.cs      → Built-in password auth (SHA-256)
│   ├── LdapAuthProvider.cs       → LDAP integration (stub)
│   ├── OidcAuthProvider.cs       → OpenID Connect (stub)
│   └── JwtTokenService.cs        → JWT token creation & validation
├── Configuration/                → Config models (JSON deserialization)
│   ├── RegistryConfig.cs         → Root config loader
│   ├── ServerConfig.cs           → Host, port, TLS
│   ├── DatabaseConfig.cs         → SQLite/PostgreSQL
│   ├── StorageConfig.cs          → Filesystem/S3
│   ├── AuthConfig.cs             → Auth type, token expiry
│   ├── SecurityConfig.cs         → Max package size, JWT key, unpublish window
│   ├── TlsConfig.cs              → TLS cert/key paths
│   └── RateLimitingConfig.cs     → Per-category rate limits
├── Controllers/                  → REST API endpoints
│   ├── AuthController.cs         → Login, register, tokens, whoami
│   ├── PackagesController.cs     → Get, publish, unpublish, download
│   ├── SearchController.cs       → Search with pagination
│   └── AdminController.cs        → Stats, user/owner mgmt, audit log
├── Contracts/                    → DTO classes
│   ├── AuthContracts.cs          → Login/Register/Token request/response
│   ├── PackageContracts.cs       → Package/version detail, publish/unpublish
│   ├── SearchContracts.cs        → Search results, pagination
│   ├── AdminContracts.cs         → User/owner management, stats, audit
│   └── CommonContracts.cs        → ErrorResponse, SuccessResponse, HealthCheck
├── Database/                     → EF Core data layer
│   ├── RegistryDbContext.cs      → DbContext with 6 DbSets
│   ├── IRegistryDatabase.cs      → 40+ CRUD methods
│   ├── StashRegistryDatabase.cs  → EF Core implementation
│   └── Models/                   → Entity models (PackageRecord, VersionRecord, etc.)
├── Services/                     → Business logic
│   ├── PackageService.cs         → Publish/unpublish workflows
│   ├── AuditService.cs           → Audit logging
│   └── DeprecationService.cs     → (stub)
├── Storage/                      → Package file storage
│   ├── IPackageStorage.cs        → Store, Retrieve, Delete, Exists, GetSize
│   ├── FileSystemStorage.cs      → Local disk with path traversal protection
│   └── S3Storage.cs              → AWS S3 (stub)
├── Middleware/
│   └── RateLimitingMiddleware.cs → Per-category rate limiting
└── Endpoints/
    └── AuthHelper.cs             → Shared auth utilities
```

**Dependencies:** ASP.NET Core (.NET 10), EF Core, `Stash.Core` (project reference — not interpreter, only core types).

## Request Flow

```
Client (stash pkg CLI / HTTP)
    → RateLimitingMiddleware (IP/user-based throttling)
    → JWT Authentication (Bearer token + JTI revocation check)
    → Authorization Policies (RequirePublishScope, RequireAdmin)
    → Controllers (Auth, Packages, Search, Admin)
        → PackageService / AuditService / IAuthProvider
    → IRegistryDatabase (EF Core: SQLite or PostgreSQL)
    → IPackageStorage (Filesystem or S3)
```

## DI Services (Startup.cs)

| Service             | Lifetime  | Role                                       |
| ------------------- | --------- | ------------------------------------------ |
| `RegistryConfig`    | Singleton | Parsed `appsettings.json` configuration    |
| `RegistryDbContext` | Scoped    | EF Core DbContext (SQLite or PostgreSQL)   |
| `IRegistryDatabase` | Scoped    | Database abstraction (40+ CRUD methods)    |
| `IPackageStorage`   | Singleton | Tarball storage (filesystem or S3)         |
| `IAuthProvider`     | Singleton | Auth backend (local, LDAP stub, OIDC stub) |
| `JwtTokenService`   | Singleton | JWT creation and validation                |
| `PackageService`    | Scoped    | Publish/unpublish business logic           |
| `AuditService`      | Scoped    | Audit log recording                        |

## Controller Pattern

Controllers use constructor injection and return DTOs from `Contracts/`:

```csharp
[ApiController]
[Route("api/v1/packages")]
public class PackagesController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IPackageStorage _storage;
    private readonly PackageService _packageService;

    public PackagesController(IRegistryDatabase db, IPackageStorage storage, PackageService packageService)
    {
        _db = db;
        _storage = storage;
        _packageService = packageService;
    }

    [HttpGet("{name}")]
    public IActionResult GetPackage(string name) { ... }

    [HttpPut("{name}")]
    [Authorize(Policy = "RequirePublishScope")]
    public async Task<IActionResult> Publish(string name) { ... }
}
```

**Key rules:**

- Public read endpoints (GET packages, search, download, health) require **no authentication**
- Publish/unpublish require a token with `publish` or `admin` scope
- Admin endpoints require `admin` scope AND `admin` role
- Error responses use `ErrorResponse` from `CommonContracts.cs`
- Success responses use `SuccessResponse` or typed DTOs

## REST API Endpoints

| Method | Path                                         | Auth          | Controller         |
| ------ | -------------------------------------------- | ------------- | ------------------ |
| GET    | `/`                                          | None          | Health check       |
| POST   | `/api/v1/auth/login`                         | None          | AuthController     |
| POST   | `/api/v1/auth/register`                      | None          | AuthController     |
| GET    | `/api/v1/auth/whoami`                        | Bearer        | AuthController     |
| POST   | `/api/v1/auth/tokens`                        | Bearer        | AuthController     |
| DELETE | `/api/v1/auth/tokens/{id}`                   | Bearer        | AuthController     |
| GET    | `/api/v1/packages/{name}`                    | None          | PackagesController |
| GET    | `/api/v1/packages/{name}/{version}`          | None          | PackagesController |
| GET    | `/api/v1/packages/{name}/{version}/download` | None          | PackagesController |
| PUT    | `/api/v1/packages/{name}`                    | publish scope | PackagesController |
| DELETE | `/api/v1/packages/{name}/{version}`          | publish scope | PackagesController |
| GET    | `/api/v1/search?q=...`                       | None          | SearchController   |
| GET    | `/api/v1/admin/stats`                        | admin         | AdminController    |
| POST   | `/api/v1/admin/users`                        | admin         | AdminController    |
| DELETE | `/api/v1/admin/users/{username}`             | admin         | AdminController    |
| PUT    | `/api/v1/admin/packages/{name}/owners`       | admin         | AdminController    |
| GET    | `/api/v1/admin/audit-log`                    | admin         | AdminController    |

## Database Layer

### Entity Models (Database/Models/)

Six entities: `PackageRecord`, `VersionRecord`, `UserRecord`, `TokenRecord`, `OwnerEntry`, `AuditEntry`. Column names use snake_case in the database, configured in `RegistryDbContext.OnModelCreating`.

### IRegistryDatabase Interface

40+ methods covering CRUD for all entities. Key patterns:

- Methods return `null` when not found (not exceptions)
- Package name is the primary key (no auto-increment ID)
- Version primary key is composite `(package_name, version)`
- Foreign keys cascade on delete

### Adding New Database Methods

1. Add the method signature to `IRegistryDatabase.cs`
2. Implement in `StashRegistryDatabase.cs` using EF Core
3. If a new entity is needed, add to `Database/Models/`, register in `RegistryDbContext`

## Authentication

### JWT Token Structure

| Claim   | Description                             |
| ------- | --------------------------------------- |
| `sub`   | Username                                |
| `jti`   | Token ID (for revocation via DB lookup) |
| `scope` | `read`, `publish`, or `admin`           |
| `role`  | `user` or `admin`                       |
| `exp`   | Expiry timestamp                        |

### Auth Provider Interface

```csharp
public interface IAuthProvider
{
    bool Authenticate(string username, string password);
    void CreateUser(string username, string password);
    bool UserExists(string username);
}
```

Three implementations: `LocalAuthProvider` (active), `LdapAuthProvider` (stub), `OidcAuthProvider` (stub).

## Storage

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

- `FileSystemStorage`: active — stores tarballs at `{root}/{safeName}/{version}.tar.gz` with path traversal protection
- `S3Storage`: stub — throws `NotImplementedException`

## Security Requirements

- **Path traversal:** `FileSystemStorage` resolves canonical paths and verifies they start with the root directory
- **Input validation:** Usernames `[a-zA-Z0-9_-]{1,64}`, passwords min 8 chars
- **Integrity:** SHA-256 hash on all tarballs, verified on publish if client sends `X-Integrity` header
- **Rate limiting:** Per-category (Auth, Publish, Download, Search) with sliding-window buckets
- **JWT signing key:** Must be ≥32 chars for HMAC-SHA256; auto-generated if missing (with warning)

## Configuration

All configuration lives in `appsettings.json` with typed models in `Configuration/`. Key sections:

| Section        | Config Class         | Scope                                  |
| -------------- | -------------------- | -------------------------------------- |
| `Server`       | `ServerConfig`       | Host, port, TLS                        |
| `Database`     | `DatabaseConfig`     | SQLite path or PostgreSQL conn string  |
| `Storage`      | `StorageConfig`      | Filesystem path or S3 credentials      |
| `Auth`         | `AuthConfig`         | Provider type, registration, token TTL |
| `Security`     | `SecurityConfig`     | Package size, integrity, JWT key       |
| `RateLimiting` | `RateLimitingConfig` | Per-category throttle settings         |

## Tests

Tests live in `Stash.Tests/Registry/` and follow the project-wide `{Feature}_{Scenario}_{Expected}()` naming convention:

| Test File                   | Covers                                     |
| --------------------------- | ------------------------------------------ |
| `RegistryConfigTests.cs`    | Configuration parsing and defaults         |
| `SqliteDatabaseTests.cs`    | Database CRUD operations (SQLite)          |
| `LocalAuthProviderTests.cs` | Password auth, user creation               |
| `FileSystemStorageTests.cs` | Tarball storage, path traversal protection |
| `PackageServiceTests.cs`    | Publish/unpublish business logic           |
| `AuditServiceTests.cs`      | Audit log recording                        |
| `UserConfigTests.cs`        | CLI user config (`~/.stash/config.json`)   |

When adding new features, add corresponding tests in `Stash.Tests/Registry/`. Use `dotnet test --filter "FullyQualifiedName~Registry"` to run registry tests.

## Stubs (Not Yet Implemented)

| Component            | Status | Notes                                   |
| -------------------- | ------ | --------------------------------------- |
| `LdapAuthProvider`   | Stub   | Implements `IAuthProvider`, all no-ops  |
| `OidcAuthProvider`   | Stub   | Implements `IAuthProvider`, all no-ops  |
| `S3Storage`          | Stub   | Implements `IPackageStorage`, all throw |
| `DeprecationService` | Stub   | Not started                             |
| Password hashing     | PA-3   | Currently SHA-256, should be Argon2id   |
