# First-User Admin Bootstrap — Replace the `sqlite3 UPDATE` Hack

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Medium
> **Discovery context:** Stash package registry audit — finding **A11**.

## Background

The Stash registry has a `role` column on the `users` table with values `user` and `admin`. Several endpoints (deprecate, unpublish, package-owner management on packages the caller doesn't own) gate on `role == admin`. Fresh registries have **zero admin users**. The README's instructions for self-hosters are:

> Run `sqlite3 registry.db "UPDATE users SET role='admin' WHERE username='<you>';"` after registering your first account.

This is bad on multiple axes:

- Requires `sqlite3` on the host (not always installed).
- Bakes the database engine into the operations docs (the registry can be configured to use other DBs in the future; the `UPDATE` would need to change).
- Encourages self-hosters to develop a habit of poking the DB directly — exactly the wrong instinct.
- Race: between "registry boots" and "I run UPDATE", anyone who registers and creates a package becomes that package's owner with admin-blocked operations partially available.
- Can't be automated by a Docker entrypoint or a CI deploy script without shelling into the container.

## Scope

**Files:**
- `Stash.Registry/Program.cs` / `Startup.cs` — add startup-time bootstrap logic.
- `Stash.Registry/Configuration/...` — bind a new `Bootstrap` config section.
- `Stash.Registry/Services/UserService.cs` — add a "promote to admin" method (idempotent).
- `Stash.Registry/README.md` — replace the `sqlite3` instructions with the new mechanism.
- `Stash.Registry/Endpoints/AuthEndpoints.cs` — apply the "first registration becomes admin" policy if that option is selected.

## Decision

Three options were considered. **Recommended: combine option 1 (env var seed) and option 3 (first-registration policy) — env var is the operational mechanism; first-registration is the fallback for users who don't set it.** This matches Gitea/Forgejo's bootstrap pattern, which is the closest comparable self-hosted service.

### Option 1 — Env var seed (CHOSEN as primary)
Config:
```json
"Bootstrap": {
  "AdminUsername": "admin",
  "AdminEmail": "admin@example.com",
  "AdminPasswordEnv": "STASH_REGISTRY_ADMIN_PASSWORD"
}
```
On startup, if no admin user exists in the DB AND the env var named in `AdminPasswordEnv` is set, create the user with that password and `role = admin`. Idempotent: if the user already exists with admin role, do nothing. If `AdminUsername` exists but is not admin, log a warning and do not auto-promote (avoids surprise privilege escalation across restarts).

**Pros:** Automatable, clear audit trail in logs, no DB poking, works in Docker.
**Cons:** Requires planning before first boot.

### Option 3 — First-registration becomes admin (CHOSEN as fallback)
On the registration endpoint, if there are zero users in the DB, the new user is created with `role = admin`. Subsequent registrations are normal users.

**Pros:** Zero config; just register and you're admin.
**Cons:** Must be enabled deliberately (off by default if `RegistrationEnabled: false` per spec 13). Race-y if multiple users hit `/register` simultaneously on first boot — guard with a transaction.

### Option 2 — `--bootstrap-admin` CLI flag (REJECTED)
A CLI flag that creates an admin user on startup. Less ergonomic than env vars in container deployments; env-var is strictly better.

## Scope (Concrete)

1. Add `Bootstrap` section to `appsettings.json` with safe defaults (all empty/null).
2. On startup, after EF migrations run:
   - If `Bootstrap.AdminPasswordEnv` is set and the named env var exists and is non-empty, and no user with `AdminUsername` exists, create the admin. Log: `Bootstrap admin '<username>' created from env var.`
   - If the user already exists with admin role, log debug: `Bootstrap admin '<username>' already present, skipping.`
   - If exists without admin role, log warning: `User '<username>' already exists but is not admin; bootstrap will not auto-promote.`
3. In the registration endpoint, wrap the user-count check + insert in a transaction:
   - If `users` count == 0 at the start of the transaction, insert with `role = admin`. Otherwise insert with `role = user`.
4. Update the README:
   - Document the env-var seed as the recommended self-host bootstrap.
   - Document the first-registration fallback.
   - Delete the `sqlite3 UPDATE` instructions.

## Acceptance Criteria

- [ ] Fresh DB + env-var-seed config + env var set → admin user exists after first boot.
- [ ] Fresh DB without env-var seed → first registered user becomes admin; second registered user is normal.
- [ ] Existing DB with admin user + env-var seed → no change, no error, no duplicate.
- [ ] Race condition: two simultaneous registrations against an empty DB result in exactly one admin (transaction enforces).
- [ ] README no longer contains the `sqlite3 UPDATE` instructions.
- [ ] xUnit integration tests cover all three scenarios above.

## Risk / Notes

- The env-var-seed password must never be logged. Confirm logging frameworks don't inadvertently capture env values.
- Document that if `RegistrationEnabled: false` is set (per spec 13) AND no env-var seed is configured, the registry has no admin and no path to create one — must clearly fail-fast at startup with a remediation message.
- The first-registration policy interacts with `RegistrationEnabled`. If registration is disabled, the policy never fires. Document the interaction.

## Out of Scope

- Multi-admin bootstrap.
- Role-based permissions beyond admin/user.
- Web-UI based admin management.
