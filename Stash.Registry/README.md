# Stash Package Registry

A self-hosted package registry for the [Stash scripting language](../README.md). It powers all `stash pkg` CLI commands — publishing, installing, searching, and managing packages across your team or publicly.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- (Optional) PostgreSQL 14+ — if you prefer PostgreSQL over the default SQLite
- (Optional) A TLS certificate — for HTTPS without a reverse proxy

## Quick Start

Get a registry running in under 2 minutes:

```bash
# Clone and build
git clone <repo>
cd stash-lang
dotnet build Stash.Registry/

# Run with default config (SQLite, port 8080, no TLS)
dotnet run --project Stash.Registry/

# Health check
curl http://localhost:8080/
# Returns: {"status":"ok","version":"1.0.0"}
```

The server creates the SQLite database and package storage directory automatically on first start. No setup scripts required.

## First-Time Setup

### 1. Register an account

```bash
curl -X POST http://localhost:8080/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "your-secure-password"}'
```

### 2. Login to get a token

```bash
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "your-secure-password"}'
# Returns: {"token": "eyJ...", "expiresAt": "..."}
```

### 3. Grant admin access

The first registered user is a regular user. To promote to admin, update the database directly:

```bash
sqlite3 data/registry.db "UPDATE users SET role='admin' WHERE username='admin'"
```

If you already have an admin account, use the admin API instead:

```bash
# As an existing admin, create a new admin user directly
curl -X POST http://localhost:8080/api/v1/admin/users \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  -d '{"username": "newadmin", "password": "secure-password", "role": "admin"}'
```

### 4. Use with the Stash CLI

```bash
# Login from the Stash CLI
stash pkg login --registry http://localhost:8080

# Publish a package
cd my-package/
stash pkg publish --registry http://localhost:8080

# Install a package
stash pkg install my-package --registry http://localhost:8080
```

## Configuration

The registry reads from `appsettings.json` in the current directory. To specify a different path, pass it as the first argument:

```bash
dotnet run --project Stash.Registry/ -- /path/to/custom-config.json
```

### Full Default Configuration

```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 8080,
    "BasePath": "/api/v1",
    "Tls": {
      "Enabled": false,
      "Cert": "",
      "Key": ""
    }
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
    "UnpublishWindow": "72h"
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

### Server Configuration

| Property    | Type   | Default   | Description             |
| ----------- | ------ | --------- | ----------------------- |
| Host        | string | `0.0.0.0` | Bind address            |
| Port        | int    | `8080`    | Listen port             |
| BasePath    | string | `/api/v1` | API path prefix         |
| Tls.Enabled | bool   | `false`   | Enable HTTPS            |
| Tls.Cert    | string | `""`      | Path to PEM certificate |
| Tls.Key     | string | `""`      | Path to PEM private key |

### Database Configuration

| Property         | Type   | Default            | Description                                                       |
| ---------------- | ------ | ------------------ | ----------------------------------------------------------------- |
| Type             | string | `sqlite`           | `sqlite` or `postgresql`                                          |
| Path             | string | `data/registry.db` | SQLite file path (auto-created with parent directories)           |
| ConnectionString | string | `""`               | PostgreSQL connection string (required when Type is `postgresql`) |

### Storage Configuration

| Property  | Type   | Default         | Description                                   |
| --------- | ------ | --------------- | --------------------------------------------- |
| Type      | string | `filesystem`    | `filesystem` or `s3` (s3 not yet implemented) |
| Path      | string | `data/packages` | Local storage directory                       |
| Bucket    | string | `""`            | S3 bucket name                                |
| Region    | string | `""`            | S3 region                                     |
| Endpoint  | string | `""`            | S3-compatible endpoint URL                    |
| AccessKey | string | `""`            | S3 access key                                 |
| SecretKey | string | `""`            | S3 secret key                                 |

### Auth Configuration

| Property            | Type   | Default | Description                              |
| ------------------- | ------ | ------- | ---------------------------------------- |
| Type                | string | `local` | `local`, `ldap` (stub), or `oidc` (stub) |
| RegistrationEnabled | bool   | `true`  | Allow self-registration                  |
| TokenExpiry         | string | `90d`   | Token lifetime (`90d`, `24h`, `30m`)     |

### Security Configuration

| Property          | Type   | Default  | Description                                                                                          |
| ----------------- | ------ | -------- | ---------------------------------------------------------------------------------------------------- |
| MaxPackageSize    | string | `10MB`   | Maximum tarball size (supports `KB`, `MB`, `GB`)                                                     |
| RequiredIntegrity | string | `sha256` | Hash algorithm for integrity checks                                                                  |
| UnpublishWindow   | string | `72h`    | Time window after publishing during which a version can be unpublished                               |
| JwtSigningKey     | string | `null`   | HMAC-SHA256 signing key (min 32 chars). Auto-generated if not set — tokens will not survive restarts |

## TLS / HTTPS Setup

To enable HTTPS directly on the registry (without a reverse proxy):

```json
{
  "Server": {
    "Port": 443,
    "Tls": {
      "Enabled": true,
      "Cert": "/path/to/cert.pem",
      "Key": "/path/to/key.pem"
    }
  }
}
```

For local testing, generate a self-signed certificate:

```bash
openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -days 365 -nodes \
  -subj "/CN=registry.example.com"
```

For production, use a certificate from Let's Encrypt or your CA, or terminate TLS at a reverse proxy (see [Production Deployment](#production-deployment)).

## PostgreSQL Setup

Switch from SQLite to PostgreSQL by setting the database type and connection string:

```json
{
  "Database": {
    "Type": "postgresql",
    "ConnectionString": "Host=localhost;Port=5432;Database=stash_registry;Username=stash;Password=secret"
  }
}
```

Tables are created automatically on first start via EF Core `EnsureCreated()`. No migration scripts needed.

## Production Deployment

### 1. Set a JWT signing key

Without a signing key, a random key is generated at startup and tokens are invalidated every restart:

```json
{
  "Security": {
    "JwtSigningKey": "your-secret-key-at-least-32-characters-long"
  }
}
```

### 2. Disable open registration

For a private registry, prevent anyone from creating accounts:

```json
{
  "Auth": {
    "RegistrationEnabled": false
  }
}
```

Create user accounts through the admin API instead.

### 3. Enable TLS or use a reverse proxy

**nginx reverse proxy example:**

```nginx
server {
    listen 443 ssl;
    server_name registry.example.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        client_max_body_size 10M;
    }
}
```

### 4. Use PostgreSQL

For production workloads with many concurrent users, PostgreSQL is recommended over SQLite. See [PostgreSQL Setup](#postgresql-setup).

### 5. Run as a systemd service

```ini
[Unit]
Description=Stash Package Registry
After=network.target

[Service]
Type=simple
User=stash
WorkingDirectory=/opt/stash-registry
ExecStart=/opt/stash-registry/StashRegistry /opt/stash-registry/appsettings.json
Restart=always
RestartSec=5
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable stash-registry
sudo systemctl start stash-registry
```

### 6. Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Stash.Registry/ -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["./StashRegistry", "appsettings.json"]
```

```bash
docker build -t stash-registry .
docker run -d \
  -p 8080:8080 \
  -v $(pwd)/appsettings.json:/app/appsettings.json \
  -v $(pwd)/data:/app/data \
  stash-registry
```

## API Quick Reference

| Method | Endpoint                                     | Auth    | Description         |
| ------ | -------------------------------------------- | ------- | ------------------- |
| GET    | `/`                                          | —       | Health check        |
| POST   | `/api/v1/auth/login`                         | —       | Login, get JWT      |
| POST   | `/api/v1/auth/register`                      | —       | Register account    |
| GET    | `/api/v1/auth/whoami`                        | Bearer  | Current user info   |
| POST   | `/api/v1/auth/tokens`                        | Bearer  | Create scoped token |
| DELETE | `/api/v1/auth/tokens/{id}`                   | Bearer  | Revoke token        |
| GET    | `/api/v1/packages/{name}`                    | —       | Package metadata    |
| GET    | `/api/v1/packages/{name}/{version}`          | —       | Version details     |
| GET    | `/api/v1/packages/{name}/{version}/download` | —       | Download tarball    |
| PUT    | `/api/v1/packages/{name}`                    | Publish | Publish version     |
| DELETE | `/api/v1/packages/{name}/{version}`          | Publish | Unpublish version   |
| PATCH  | `/api/v1/packages/{name}/deprecate`          | Publish | Deprecate package   |
| DELETE | `/api/v1/packages/{name}/deprecate`          | Publish | Undeprecate package |
| PATCH  | `/api/v1/packages/{name}/{version}/deprecate`| Publish | Deprecate version   |
| DELETE | `/api/v1/packages/{name}/{version}/deprecate`| Publish | Undeprecate version |
| GET    | `/api/v1/search?q=query`                     | —       | Search packages     |
| GET    | `/api/v1/admin/stats`                        | Admin   | Registry stats      |
| POST   | `/api/v1/admin/users`                        | Admin   | Create user         |
| DELETE | `/api/v1/admin/users/{username}`             | Admin   | Delete user         |
| PUT    | `/api/v1/admin/packages/{name}/owners`       | Admin   | Manage owners       |
| GET    | `/api/v1/admin/audit-log`                    | Admin   | Audit log           |

### OpenAPI

In development mode, the OpenAPI spec is served at `/openapi/v1.json`:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project Stash.Registry/
curl http://localhost:8080/openapi/v1.json
```

## Troubleshooting

**`WARNING: No JWT signing key configured`**
Set `Security.JwtSigningKey` in `appsettings.json`. Without it, tokens become invalid on every restart.

**Database not created**
The server auto-creates the SQLite database file and all parent directories on startup. If it fails, check that the process has write permission to the working directory.

**Connection refused on startup**
Check that the port (default 8080) is not already in use (`ss -tlnp | grep 8080`) and that `Server.Host` is set to bind the right interface.

**Package upload rejected (413 or size error)**
Increase `Security.MaxPackageSize` in `appsettings.json`. Also ensure your reverse proxy's `client_max_body_size` matches.

**Rate limited (HTTP 429)**
Adjust the `RateLimiting` config for the specific category (`Auth`, `Publish`, `Download`, `Search`) or wait for the window to expire.

**Can't unpublish a version**
The unpublish window (default `72h`) has expired. Extend `Security.UnpublishWindow` before the deadline, or use the admin API to force-remove a version.

**Tokens stop working after restart**
`Security.JwtSigningKey` is not set. A new random key is generated each start, invalidating all previous tokens.

## Package Deprecation

Packages and individual versions can be marked as deprecated. Deprecated packages remain fully installable — deprecation serves as a soft warning to consumers.

### Deprecate a package

```bash
curl -X PATCH http://localhost:8080/api/v1/packages/my-package/deprecate \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"message": "This package is no longer maintained.", "alternative": "my-new-package"}'
```

### Deprecate a specific version

```bash
curl -X PATCH http://localhost:8080/api/v1/packages/my-package/1.0.0/deprecate \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"message": "Use 2.0.0 instead — this version has a known bug."}'
```

### Remove deprecation

```bash
# Undeprecate a package
curl -X DELETE http://localhost:8080/api/v1/packages/my-package/deprecate \
  -H "Authorization: Bearer <token>"

# Undeprecate a specific version
curl -X DELETE http://localhost:8080/api/v1/packages/my-package/1.0.0/deprecate \
  -H "Authorization: Bearer <token>"
```

Deprecation requires a token with `publish` scope and package ownership (or admin role). Search results include a `deprecated` flag so consumers can filter or warn on deprecated packages.

## Further Documentation

- [Full Registry Documentation](../docs/Registry%20—%20Package%20Registry.md)
- [Language Specification](../docs/Stash%20—%20Language%20Specification.md)
- [Standard Library Reference](../docs/Stash%20—%20Standard%20Library%20Reference.md)
