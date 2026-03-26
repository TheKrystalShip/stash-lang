# CI/CD Integration Guide — Stash Package Registry

This guide walks you through setting up automated package publishing from a CI/CD pipeline (GitHub Actions, GitLab CI, Jenkins, etc.) to a Stash package registry.

**Prerequisites:**

- A deployed Stash registry (this guide uses `https://registry.example.com:8080/api/v1`)
- A registered user account on the registry
- The Stash CLI installed in your CI environment

---

## Step 1: Create a CI/CD Token

CI/CD pipelines should use a dedicated, scoped API token — not your interactive login credentials. Create one from your local machine using the Stash CLI:

```bash
stash pkg login --registry https://registry.example.com:8080/api/v1
stash pkg token create --scope publish --description "CI/CD deploy token" --expires-in 90d
```

Output:

```
Token created.
  Token ID:   550e8400-e29b-41d4-a716-446655440000
  Scope:      publish
  Expires:    2026-06-24T12:00:00Z
  Token:      eyJhbGciOiJIUzI1NiIs...CI_TOKEN_VALUE

Save this token now — it will not be shown again.
```

> **Important:** Copy the token value now. It is only shown once and cannot be retrieved later. The `Token ID` is used if you ever need to revoke it.

### Available scopes

| Scope     | Use case                                                                  |
| --------- | ------------------------------------------------------------------------- |
| `read`    | Download and search packages only (read-only pipelines)                   |
| `publish` | Publish, unpublish, and manage owned packages **(recommended for CI/CD)** |
| `admin`   | Full registry access (requires admin role — avoid in CI/CD)               |

---

## Step 2: Store the Token as a CI/CD Secret

Add the token as a secret environment variable in your CI/CD platform. **Never commit tokens to version control.**

| Platform        | Where to store                                  | Variable name (suggested) |
| --------------- | ----------------------------------------------- | ------------------------- |
| GitHub Actions  | Repository → Settings → Secrets → Actions       | `STASH_TOKEN`             |
| GitLab CI       | Project → Settings → CI/CD → Variables (masked) | `STASH_TOKEN`             |
| Jenkins         | Credentials → Secret text                       | `STASH_TOKEN`             |
| Azure Pipelines | Pipeline → Variables (secret)                   | `STASH_TOKEN`             |

---

## Step 3: Publish from CI/CD

Set the `STASH_TOKEN` and `STASH_REGISTRY_URL` environment variables and run `stash pkg publish` — no config file needed:

```bash
export STASH_TOKEN="<your-ci-token>"
export STASH_REGISTRY_URL="https://registry.example.com:8080/api/v1"
stash pkg publish
```

The CLI reads these environment variables automatically. `STASH_TOKEN` takes priority over any token stored in `~/.stash/config.json`.

### Alternative: `--token` flag

You can also pass the token directly via the CLI flag:

```bash
stash pkg publish \
  --registry https://registry.example.com:8080/api/v1 \
  --token "$STASH_TOKEN"
```

---

## Full Pipeline Examples

### GitHub Actions

```yaml
name: Publish Package
on:
  push:
    tags:
      - "v*"

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install Stash
        run: |
          curl -fsSL https://stash-lang.dev/install.sh | bash
          echo "$HOME/.stash/bin" >> $GITHUB_PATH

      - name: Publish to registry
        env:
          STASH_TOKEN: ${{ secrets.STASH_TOKEN }}
          STASH_REGISTRY_URL: https://registry.example.com:8080/api/v1
        run: stash pkg publish
```

### GitLab CI

```yaml
publish:
  stage: deploy
  image: ubuntu:latest
  only:
    - tags
  variables:
    STASH_REGISTRY_URL: https://registry.example.com:8080/api/v1
  script:
    - curl -fsSL https://stash-lang.dev/install.sh | bash
    - export PATH="$HOME/.stash/bin:$PATH"
    - stash pkg publish
  # Set STASH_TOKEN as a masked variable in Project → Settings → CI/CD → Variables
```

### Jenkins (Declarative Pipeline)

```groovy
pipeline {
    agent any
    stages {
        stage('Publish') {
            when { buildingTag() }
            environment {
                STASH_TOKEN = credentials('stash-registry-token')
                STASH_REGISTRY_URL = 'https://registry.example.com:8080/api/v1'
            }
            steps {
                sh '''
                    curl -fsSL https://stash-lang.dev/install.sh | bash
                    export PATH="$HOME/.stash/bin:$PATH"
                    stash pkg publish
                '''
            }
        }
    }
}
```

---

## Token Management

### List your tokens

```bash
stash pkg token list --registry https://registry.example.com:8080/api/v1
```

### Verify your token works

```bash
curl -s https://registry.example.com:8080/api/v1/auth/whoami \
  -H "Authorization: Bearer <your-token>" | jq
```

Expected response:

```json
{
  "username": "alice",
  "role": "user"
}
```

### Revoke a compromised token

If a token is leaked, revoke it immediately using the Token ID:

```bash
stash pkg token revoke 550e8400-e29b-41d4-a716-446655440000 \
  --registry https://registry.example.com:8080/api/v1
```

Revocation takes effect immediately — the revoked token is rejected on the next request.

### Rotate tokens before expiry

Tokens created with `--expires-in 90d` expire after 90 days. To rotate:

```bash
# Create a replacement token
stash pkg token create --scope publish --description "CI/CD deploy token (rotated)" \
  --registry https://registry.example.com:8080/api/v1

# Update the CI/CD secret with the new token value

# Revoke the old token
stash pkg token revoke <old-token-id> --registry https://registry.example.com:8080/api/v1
```

---

## Package Setup Checklist

Before your first publish, make sure your project has a valid `stash.json`:

```bash
# Initialize a new package (interactive prompts)
stash pkg init

# Or with defaults
stash pkg init --yes
```

Minimal `stash.json` required for publishing:

```json
{
  "name": "my-package",
  "version": "1.0.0"
}
```

**Naming rules:** lowercase, starts with a letter, alphanumeric and hyphens only, max 64 characters.

**Version format:** [Semantic versioning](https://semver.org/) — `MAJOR.MINOR.PATCH` (e.g., `1.0.0`, `2.3.1`).

> Packages with `"private": true` in `stash.json` cannot be published.

---

## Troubleshooting

| Problem                 | Cause                           | Fix                                                                                   |
| ----------------------- | ------------------------------- | ------------------------------------------------------------------------------------- |
| `401 Unauthorized`      | Token is expired or revoked     | Create a new token with `stash pkg token create` and update the CI/CD secret          |
| `403 Forbidden`         | Token scope is insufficient     | Create a token with `publish` scope instead of `read`                                 |
| `409 Conflict`          | Version already exists          | Bump the version in `stash.json` before publishing                                    |
| `400 Bad Request`       | Missing or invalid `stash.json` | Run `stash pkg init` and ensure `name` + `version` are set                            |
| `429 Too Many Requests` | Rate limit exceeded             | Wait for the `Retry-After` duration and retry; consider staggering parallel publishes |
| Token not found         | `STASH_TOKEN` not set or empty  | Ensure the environment variable is exported in your pipeline step                     |

---

## Security Best Practices

1. **Use `publish` scope** — never give CI/CD tokens `admin` scope unless absolutely necessary.
2. **One token per pipeline** — create separate tokens for each project or environment. If one leaks, revoke it without disrupting other pipelines.
3. **Mask secrets in logs** — ensure your CI platform masks the `STASH_TOKEN` value in build output.
4. **Rotate regularly** — create new tokens and revoke old ones before expiry. Automate this if possible.
5. **Revoke immediately on compromise** — use `stash pkg token revoke <id>` as shown above. Revocation is instant.
6. **Limit network access** — if your registry supports IP allowlisting or VPN, restrict CI/CD token usage to known runner IPs.

---

## Advanced: Config File Approach

For advanced scenarios where environment variables are not available, the CLI reads credentials from `~/.stash/config.json`. Create this file in your pipeline before running publish commands:

```bash
mkdir -p ~/.stash
cat > ~/.stash/config.json << EOF
{
  "defaultRegistry": "https://registry.example.com:8080/api/v1",
  "registries": {
    "https://registry.example.com:8080/api/v1": {
      "token": "${STASH_REGISTRY_TOKEN}"
    }
  }
}
EOF
chmod 600 ~/.stash/config.json
stash pkg publish
```

> **Note:** The env var approach (`STASH_TOKEN` + `STASH_REGISTRY_URL`) is simpler and preferred.
> Use the config file approach only when necessary (e.g., managing multiple registries in a single pipeline).
