# Registry — `DefaultConfig_HasExpectedDefaults` Test Fails

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Low
> **Discovery context:** Found while reviewing `.kanban/3-review/11-install-atomicity.md`. Failure is pre-existing and unrelated to that spec.

## Symptom

`Stash.Tests.Registry.RegistryConfigTests.DefaultConfig_HasExpectedDefaults` fails with:

```
Assert.True() Failure
Expected: True
Actual:   False
   at Stash.Tests.Registry.RegistryConfigTests.DefaultConfig_HasExpectedDefaults() in Stash.Tests/Registry/RegistryConfigTests.cs:line 73
```

Line 73 is `Assert.True(config.Auth.RegistrationEnabled);`. A freshly-constructed `RegistryConfig` has `Auth.RegistrationEnabled == false`, but the test expects `true`.

Reproduces both in isolation (`dotnet test --filter "FullyQualifiedName~DefaultConfig_HasExpectedDefaults"`) and in the full suite — not a parallelism flake.

## Root Cause Hypothesis

Either:
1. The `RegistryConfig.AuthSection.RegistrationEnabled` default was changed to `false` (perhaps as a security-hardening default) without updating this test, or
2. The test was added asserting the desired default but the implementation never set it.

Check `Stash.Registry/.../RegistryConfig.cs` (or wherever `AuthSection` lives) — what is the actual default value, and what should it be?

## Affected Files

- `Stash.Tests/Registry/RegistryConfigTests.cs` (line 73)
- The `RegistryConfig` / `AuthSection` definition in `Stash.Registry/`

## Fix

Decide which side is correct:
- If the default should be `true` (open registration by default), set the field initializer accordingly.
- If the default should be `false` (closed registration by default — safer), update the test assertion and any documentation that claims the opposite.

Confirm against `docs/Registry — Package Registry.md` for the documented default.
