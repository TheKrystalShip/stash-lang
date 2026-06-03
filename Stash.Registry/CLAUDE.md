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
│   ├── LocalAuthProvider.cs      → Built-in password auth (Argon2id)
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
│   ├── PackagesController.cs     → Get, publish, unpublish, download, roles, visibility
│   ├── OrganizationsController.cs → Org CRUD, members, teams
│   ├── ScopesController.cs       → Scope resolution and claims
│   ├── SearchController.cs       → Search with pagination
│   └── AdminController.cs        → Stats, user mgmt, package role override, audit log
│   (Contracts/ removed — all wire DTOs live in Stash.Registry.Contracts/)
├── Database/                     → EF Core data layer
│   ├── RegistryDbContext.cs      → DbContext with 12 DbSets
│   ├── IRegistryDatabase.cs      → 40+ CRUD methods
│   ├── StashRegistryDatabase.cs  → EF Core implementation
│   └── Models/                   → Entity models (PackageRecord, VersionRecord, OrganizationRecord, etc.)
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

Stash.Registry.Contracts/         → Shared wire-contract assembly (dependency-free; sibling project at repo root)
├── AuthContracts.cs              → Login/Register/Token request/response
├── PackageContracts.cs           → Package/version detail, publish/unpublish, roles, visibility
├── OrganizationContracts.cs      → Org/team/member request/response
├── ScopeContracts.cs             → Scope detail/claim request/response
├── SearchContracts.cs            → Search results, pagination
├── AdminContracts.cs             → User management, stats, audit
├── CommonContracts.cs            → ErrorResponse, SuccessResponse, HealthCheck
└── BoundedDomains.cs             → Wire-visible bounded-domain enum types (PackageRoles,
                                    TokenScopes, Visibilities, PrincipalTypes,
                                    ScopeOwnerTypes, OrgRoles, UserRoles)
```

**Dependencies:** ASP.NET Core (.NET 10), EF Core, `Stash.Core` (project reference — not interpreter, only core types).

## Request Flow

```
Client (stash pkg CLI / HTTP)
    → RateLimitingMiddleware (IP/user-based throttling)
    → UseAuthentication() — JWT Bearer validation; sets HttpContext.User
    → JTI revocation middleware — rejects revoked tokens with 401 before
      any endpoint (runs between UseAuthentication and UseAuthorization)
    → UseAuthorization() — [Authorize] authentication gate
    → RegistryAuthorizeFilter (IAsyncAuthorizationFilter, per-request)
        — principal-build → resource-resolve → PDP call → unified deny
        — runs for every [RegistryAuthorize(RegistryAction.X)] endpoint
        — one [ImperativeAuthz] endpoint (ScopesController.ClaimScope) bypasses
          this filter and calls the PDP inline (see Controller Pattern below)
    → Controllers (Auth, Packages, Orgs, Scopes, Search, Admin)
        → PackageService / AuditService / IAuthProvider
    → IRegistryDatabase (EF Core: SQLite or PostgreSQL)
    → IPackageStorage (Filesystem or S3)
```

## DI Services (Startup.cs)

| Service                          | Lifetime  | Role                                                             |
| -------------------------------- | --------- | ---------------------------------------------------------------- |
| `RegistryConfig`                 | Singleton | Parsed `appsettings.json` configuration                          |
| `RegistryDbContext`              | Scoped    | EF Core DbContext (SQLite or PostgreSQL)                         |
| `IRegistryDatabase`              | Scoped    | Database abstraction (40+ CRUD methods)                          |
| `IPackageStorage`                | Singleton | Tarball storage (filesystem or S3)                               |
| `IAuthProvider`                  | Singleton | Auth backend (local, LDAP stub, OIDC stub)                       |
| `JwtTokenService`                | Singleton | JWT creation and validation                                      |
| `PackageService`                 | Scoped    | Publish/unpublish business logic                                  |
| `AuditService`                   | Scoped    | Audit log recording                                              |
| `IRegistryAuthorizer`            | Scoped    | PDP — two-axis authz (ceiling × resource role)                   |
| `IRegistryAuthzPrincipalFactory` | Singleton | Builds typed `Principal` from `ClaimsPrincipal` (shared factory) |

## Controller Pattern

Controllers use constructor injection and return DTOs from `Stash.Registry.Contracts` (the shared project):

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

    // Public endpoint — no auth required; [PublicEndpoint] satisfies AuthzCoverageMetaTests
    [HttpGet("{scope}/{name}")]
    [PublicEndpoint("package metadata is publicly readable")]
    public IActionResult GetPackage(string scope, string name) { ... }

    // Authenticated + declarative PDP dispatch — [RegistryAuthorize] is the canonical pattern
    [Authorize]
    [RegistryAuthorize(RegistryAction.UnpublishVersion)]
    [HttpDelete("{scope}/{name}/{version}")]
    public async Task<IActionResult> UnpublishVersion(string scope, string name, string version)
    {
        // RegistryAuthorizeFilter has already run: principal built, PDP called, deny handled.
        // The action body contains only the actual work — no PDP block, no deny response.
        ...
    }
}
```

**Declarative authorization (`[RegistryAuthorize]`) — the canonical pattern for authenticated endpoints:**

`[RegistryAuthorize(RegistryAction.X)]` is the standard declaration for any endpoint that must consult the PDP. It is always composed with `[Authorize]` — it does NOT replace it:

- `[Authorize]` handles authentication (JWT validation via `UseAuthorization()`).
- `[RegistryAuthorize(RegistryAction.X)]` triggers `RegistryAuthorizeFilter`, which builds the typed `Principal`, resolves the `ResourceRef` from route values, calls `IRegistryAuthorizer.AuthorizeAsync`, and renders the unified deny response (`context.Result`) before the action body runs.
- The action body is pure work — no PDP call, no deny block.

~33 controller actions carry this pattern.

**Audited exemptions (`[ImperativeAuthz]`) — for endpoints with pre/post-PDP dependencies:**

One endpoint cannot express its authorization as a simple resource-action pair and carries `[ImperativeAuthz("reason")]` instead. It calls the PDP inline. The attribute documents why folding is blocked and satisfies `AuthzDispatchCoverageMetaTests`:

```csharp
[Authorize]
[ImperativeAuthz("scope/owner/ownerType fields come from the JSON request body (not route values), so the shared filter's pure-route resolver cannot build a ScopeResource before the PDP call; the bespoke 409 status mapping (ScopeReserved/ScopeNotOwned → 409 instead of 403) also requires inline coordination. Folding requires the body-resolver refactor tracked in .kanban/0-backlog/registry/Body-resolver authz filter.md")]
[HttpPost]
public async Task<IActionResult> ClaimScope() { ... }
```

The irreducible `[ImperativeAuthz]` endpoint after `registry-authz-pdp-completion`:

| Endpoint | Reason | Deferred work |
| -------- | ------ | ------------- |
| `ScopesController.ClaimScope` | scope/owner/ownerType fields come from the JSON body (not route values); bespoke 409 status mapping for ScopeReserved/ScopeNotOwned | Body-resolver refactor documented in `.kanban/0-backlog/registry/Body-resolver authz filter.md` |

The three formerly-imperative endpoints now folded into the shared filter (via `registry-authz-pdp-completion`):

- `PackagesController.PublishPackage` — folded; PDP delegates internally to `CreatePackage` or `PublishVersion` based on a DB existence check
- `OrganizationsController.DeleteOrg` — folded; wrong-action bug (`AddOrgMember`) fixed; proper `DeleteOrg` PDP handler added
- `ScopesController.DeleteScope` — folded; filter's `ResourceIdForAudit(ScopeResource)` emits `@` + scope uniformly

**Key rules:**

- Public read endpoints (GET packages, search, download, health) carry `[PublicEndpoint("reason")]` instead of `[Authorize]`; the JTI revocation middleware still rejects revoked tokens even on public endpoints
- `AuthzCoverageMetaTests` requires every action to carry `[Authorize]` or `[PublicEndpoint]` (authentication classification); `AuthzDispatchCoverageMetaTests` additionally requires every non-`[PublicEndpoint]` action to carry `[RegistryAuthorize]` or `[ImperativeAuthz]` (PDP dispatch classification). Class-level `[Authorize]` alone does NOT satisfy the dispatch requirement.
- Unauthorized callers on private/internal packages receive `404 Not Found` (not `403`) to avoid leaking package existence — this mapping (`VisibilityHidden`/`PackageNotFound` → 404) lives in `AuthzDenyResponse` and applies uniformly to all endpoints via the filter
- Publish/unpublish require a token with `publish` or `admin` coarse ceiling; the PDP checks the ceiling first, then the package role
- Admin endpoints require `admin` ceiling AND `admin` role; the admin short-circuit resolves the resource-side dimension to effective `owner` but does NOT bypass the ceiling check
- Authorization is a **two-step PDP** (`IRegistryAuthorizer` in `Auth/Authorization/`): (1) token ceiling check, (2) resource-side check (package role / scope ownership / org membership / visibility). Both must pass. The shared `RegistryAuthorizeFilter` performs this dispatch for the ~36 clean endpoints; one `[ImperativeAuthz]` endpoint (`ScopesController.ClaimScope`) still calls the PDP inline.
- **No unbounded magic strings for auth domains.** Every bounded value comes from a named definition — never inlined. The closed sets are split across two homes by a deliberate **wire-visible / server-internal** boundary:
  - **Wire-visible sets** (appear in request/response bodies; shared with `Stash.Cli` and future UIs) live in `Stash.Registry.Contracts/BoundedDomains.cs` as C# `enum` types: `PackageRoles`, `TokenScopes`, `Visibilities`, `PrincipalTypes`, `ScopeOwnerTypes`, `OrgRoles`, `UserRoles`. Each enum carries `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` and explicit `[JsonStringEnumMemberName("wire_value")]` attributes on every member to pin the lowercase wire strings byte-for-byte. Single source of truth for registry + CLI; dependency-free so any consumer can reference it.
  - **Server-internal sets** (JWT machinery, policy names, server-only identifiers) stay in `Auth/RegistryAuthConstants.cs`: `RegistryClaims`, `ReservedScopes`, `TokenCeilingConverter`. These must never move to the shared project.
  - Wire strings are parsed into enums at the boundary (`IRegistryAuthzPrincipalFactory` → `TokenCeilingConverter`). `NoMagicAuthStringsMetaTests` fails the build if a bare string literal reaches an auth sink (`IsInRole`/`FindFirstValue`/`FindFirst`/`HasClaim`/`RequireClaim`/`RequireRole`)
- Bounded enum values stored as EF columns use explicit `.HasConversion(v => v.ToWire(), v => v.ToXxx())` value converters (via `BoundedDomainWire` extension methods) plus `.HasDefaultValueSql("'wire_value'")` raw SQL literals. The SQL literal must match the CLR-default enum member's wire string — for `OrgRoles` this means `Member = 0` (the CLR default) is ordered first in the enum declaration so that EF's omit-default-from-INSERT behavior stores the right value. C# field initializers (e.g. `PackageRecord.Visibility = Visibilities.Public`) use the enum value directly; no `const string` is needed.
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
| GET    | `/api/v1/auth/tokens`                        | Bearer        | AuthController     |
| DELETE | `/api/v1/auth/tokens/{id}`                   | Bearer        | AuthController     |
| GET    | `/api/v1/packages/{scope}/{name}`                      | None (public) | PackagesController       |
| GET    | `/api/v1/packages/{scope}/{name}/{version}`            | None (public) | PackagesController       |
| GET    | `/api/v1/packages/{scope}/{name}/{version}/download`   | None (public) | PackagesController       |
| PUT    | `/api/v1/packages/{scope}/{name}`                      | publish scope | PackagesController       |
| DELETE | `/api/v1/packages/{scope}/{name}/{version}`            | publish scope | PackagesController       |
| PATCH  | `/api/v1/packages/{scope}/{name}/deprecate`            | publish scope | PackagesController       |
| DELETE | `/api/v1/packages/{scope}/{name}/deprecate`            | publish scope | PackagesController       |
| PATCH  | `/api/v1/packages/{scope}/{name}/{version}/deprecate`  | publish scope | PackagesController       |
| DELETE | `/api/v1/packages/{scope}/{name}/{version}/deprecate`  | publish scope | PackagesController       |
| GET    | `/api/v1/packages/{scope}/{name}/roles`                | admin         | PackagesController       |
| PUT    | `/api/v1/packages/{scope}/{name}/roles`                | publish scope | PackagesController       |
| DELETE | `/api/v1/packages/{scope}/{name}/roles`                | publish scope | PackagesController       |
| PATCH  | `/api/v1/packages/{scope}/{name}/visibility`           | publish scope | PackagesController       |
| GET    | `/api/v1/search?q=...`                                 | None          | SearchController         |
| POST   | `/api/v1/orgs`                                         | publish scope | OrganizationsController  |
| GET    | `/api/v1/orgs/{org}`                                   | None          | OrganizationsController  |
| POST   | `/api/v1/orgs/{org}/members`                           | publish scope | OrganizationsController  |
| DELETE | `/api/v1/orgs/{org}/members/{username}`                | publish scope | OrganizationsController  |
| POST   | `/api/v1/orgs/{org}/teams`                             | publish scope | OrganizationsController  |
| POST   | `/api/v1/orgs/{org}/teams/{team}/members`              | publish scope | OrganizationsController  |
| GET    | `/api/v1/scopes/{scope}`                               | None          | ScopesController         |
| POST   | `/api/v1/scopes`                                       | publish scope | ScopesController         |
| GET    | `/api/v1/admin/stats`                                  | admin         | AdminController          |
| POST   | `/api/v1/admin/users`                                  | admin         | AdminController          |
| DELETE | `/api/v1/admin/users/{username}`                       | admin         | AdminController          |
| PUT    | `/api/v1/admin/packages/{scope}/{name}/roles`          | admin         | AdminController          |
| DELETE | `/api/v1/admin/packages/{scope}/{name}/roles`          | admin         | AdminController          |
| GET    | `/api/v1/admin/audit-log`                              | admin         | AdminController          |

## Database Layer

### Entity Models (Database/Models/)

Eleven entities: `PackageRecord`, `VersionRecord`, `UserRecord`, `TokenRecord`, `RefreshTokenRecord`, `AuditEntry`, `OrganizationRecord`, `OrgMemberEntry`, `TeamRecord`, `TeamMemberEntry`, `ScopeRecord`, `PackageRoleEntry`. The old `OwnerEntry` table and model have been replaced by `PackageRoleEntry` (D3 clean break). Column names use snake_case in the database, configured in `RegistryDbContext.OnModelCreating`.

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

| Claim         | Description                             |
| ------------- | --------------------------------------- |
| `sub`         | Username                                |
| `jti`         | Token ID (for revocation via DB lookup) |
| `token_scope` | `read`, `publish`, or `admin`           |
| `role`        | `user` or `admin`                       |
| `exp`         | Expiry timestamp                        |

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
- **Input validation:** Usernames `^[a-z][a-z0-9-]{0,38}$` (scope grammar: max 39 chars, leading lowercase letter, hyphens/digits only — no uppercase, no underscore), passwords min 8 chars
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

| Test File                              | Covers                                                |
| -------------------------------------- | ----------------------------------------------------- |
| `RegistryConfigTests.cs`               | Configuration parsing and defaults                    |
| `SqliteDatabaseTests.cs`               | Database CRUD operations (SQLite)                     |
| `LocalAuthProviderTests.cs`            | Password auth, user creation                          |
| `FileSystemStorageTests.cs`            | Tarball storage, path traversal protection            |
| `PackageServiceTests.cs`               | Publish/unpublish business logic                      |
| `AuditServiceTests.cs`                 | Audit log recording                                   |
| `UserConfigTests.cs`                   | CLI user config (`~/.stash/config.json`)              |
| `RegistryRoutesTests.cs`               | Scoped route shapes for all PackagesController routes |

### Authorization meta-tests (Stash.Tests/Registry/Authz/)

Several meta-tests enforce structural invariants that must stay green when adding or modifying controller actions:

| Test Class                        | What it guarantees                                                                                                                                 |
| --------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AuthzCoverageMetaTests`          | Every action on a production controller carries `[Authorize]` or `[PublicEndpoint]`. Class-level `[Authorize]` counts. Fail-path fixture included. |
| `AuthzDispatchCoverageMetaTests`  | Every non-`[PublicEndpoint]` action carries `[RegistryAuthorize]` OR `[ImperativeAuthz]`. Class-level `[Authorize]` alone does NOT satisfy this — the action itself must be classified. Includes a fail-path fixture (proving the scan has teeth) and an imperative-pin assertion (the set of `[ImperativeAuthz]` endpoints must equal `{ScopesController.ClaimScope}` — the irreducible end-state after `registry-authz-pdp-completion`). Adding or removing an `[ImperativeAuthz]` marker requires updating the pin assertion. |
| `NoMagicAuthStringsMetaTests`     | No bare string literal reaches an auth sink (`IsInRole`/`FindFirst`/etc.). Sink-targeted scan; includes a self-test and floor guard. New auth sinks must be added to the scanner. |
| `RegistryAuthzMatrixTests`        | Every (action × principal) row in `AuthzMatrixData` produces the correct HTTP status and `ErrorResponse` body — the primary behavior-preservation gate. |
| `RegistryAuthzAuditMutationTests` | Authenticated denials on key endpoints (including `CreateOrg`, `AddOrgMember`, `VerifyScope`) write exactly one audit entry. Guards the uniform-audit behavior introduced by the shared filter. |

When adding new features, add corresponding tests in `Stash.Tests/Registry/`. Use `dotnet test --filter "FullyQualifiedName~Registry"` to run registry tests.

## Stubs (Not Yet Implemented)

| Component            | Status | Notes                                   |
| -------------------- | ------ | --------------------------------------- |
| `LdapAuthProvider`   | Stub   | Implements `IAuthProvider`, all no-ops  |
| `OidcAuthProvider`   | Stub   | Implements `IAuthProvider`, all no-ops  |
| `S3Storage`          | Stub   | Implements `IPackageStorage`, all throw |
| `DeprecationService` | Stub   | Not started                             |
