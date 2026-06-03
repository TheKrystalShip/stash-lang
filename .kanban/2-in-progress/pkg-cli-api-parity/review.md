# pkg-cli-api-parity — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `44be3591..4fe44892` on branch `feature/pkg-cli-api-parity`
**Brief:** ../brief.md
**Generated:** 2026-06-03

---

## Summary

A clean feature. P1's `owners` drop is surgical (contract, controller, render, and tests all line up); P2's wire-layer additions consume the shared `Stash.Registry.Contracts` types without authoring a single CLI-local mirror DTO and ship 26 capturing-handler tests pinning verb/URL/snake_case body shape; P3 deletes `OwnerCommand` + the three legacy helpers + migrates `RegistryClientRemoveOwnerTests` to the self-service `RevokeRole` route in one atomic phase boundary; P4–P6 honour the brief's set-only / flat-info constraints and route writes through the self-service publish endpoints; P7's docs and `examples/package_roles.stash` faithfully document the new grammar. The PDP ceilings match the brief (`Stash.Registry/Auth/Authorization/RegistryAuthorizer.cs:115-124`), the controller routes match the new methods (`PackagesController.cs:393/420/452/486`, `ScopesController.cs:60/82`, `OrganizationsController.cs:49/107/156/208/226/273`), every consumed wire DTO is registered in `CliJsonContext`, and there are zero residual references to the deleted owner surface in production code. The solution-wide `dotnet build` is **0 Warning(s)** — the prompt's CS8019/CS8933 theme is no longer present (dismissed).

Two MEDIUM findings: a Verified-mode print instruction points to a CLI command that doesn't exist and a server endpoint that is 501; CLI failure messaging for the new commands silently drops the server's `ErrorResponse` and, in one place, misattributes the cause. One LOW for two minor polish items.

Counts: **CRITICAL=0, IMPORTANT/HIGH=0, MEDIUM=2, LOW=1.**

---

## F01 — [MEDIUM] `scope claim` Verified-mode points users at a non-existent CLI verb (and a 501 server route)

**Status:** open
**Files:** `Stash.Cli/PackageManager/Commands/ScopeCommand.cs:180`
**Phase:** P5
**Commit:** 74daa2f7

### Observation

When the registry runs in **Verified** ownership mode `ScopeCommand.Claim` prints a DNS-TXT challenge and then tells the user:

```csharp
// ScopeCommand.cs:180
Console.WriteLine("Once the record is in place, run 'stash pkg scope verify' to complete the claim.");
```

But `stash pkg scope verify` is **not implemented** — `ScopeCommand.ExecuteCore` (lines 78–94) only dispatches `claim` and `info`; there is no `verify` case, no `RegistryClient.VerifyScope` method, and `PackageCommands.cs` does not route to one. Worse, the brief's Open Questions explicitly note that the server's `POST /scopes/{scope}/verify` is a **501 stub** ("DNS-TXT verification not implemented") and is *correctly omitted* from this feature. So the printed instruction names a command that does not exist *and* would hit a 501 even if it did.

The integration test `PackageScopeCommandTests.Scope_Claim_VerifiedMode_PrintsDnsTxtChallenge` (lines 217–229) asserts the challenge labels appear but does not assert anything about the next-step instruction line, so the typo survives the test gate.

### Why this matters

The brief commits to "the CLI prints those instructions instead of reporting a completed claim" (Semantics §`scope claim`) — i.e. the user must be able to act on the printed guidance. Pointing them at a non-existent verb makes the Verified-mode happy path dead-end at the only step that requires user action; the user then types `stash pkg scope verify ...`, gets an "unknown command" error, and has no path forward in this CLI release. This is the single behavior the Verified branch ships and it does not connect to anything.

### Suggested fix

Replace the line with guidance that matches what this CLI actually offers, e.g.

```csharp
Console.WriteLine("Once the DNS TXT record propagates, ask the registry administrator to");
Console.WriteLine("verify the scope (server-side `POST /scopes/{scope}/verify` is a 501 stub");
Console.WriteLine("this release — tracked separately).");
```

…or some other phrasing that does not invent a `verify` subcommand. Extend `Scope_Claim_VerifiedMode_PrintsDnsTxtChallenge` with one negative assertion — `Assert.DoesNotContain("stash pkg scope verify", output)` — so a future re-introduction of the broken phrasing fails the gate. Optionally, file a backlog stub linking the absent `scope verify` subcommand to the 501 server route so the gap is tracked.

### Verify

```
dotnet test --filter "FullyQualifiedName~PackageScopeCommandTests"
```

---

## F02 — [MEDIUM] New CLI failure paths drop the server's `ErrorResponse` message; `scope claim` mis-attributes every failure to "already owned"

**Status:** open
**Files:** `Stash.Cli/PackageManager/Commands/ScopeCommand.cs:160-167`, `Stash.Cli/PackageManager/Commands/RoleCommand.cs:166-174`, `Stash.Cli/PackageManager/Commands/VisibilityCommand.cs:117-125`, `Stash.Cli/PackageManager/Commands/OrgCommand.cs:231-237`, `:332-343`, `:373-383`, `:413-419`, `:454-464`; `Stash.Cli/PackageManager/RegistryClient.cs:944-957` (`SetVisibility`), `:1007-1019` (`AssignRole`), `:1074-1087` (`ClaimScope`), `:1133-1146` (`CreateOrg`), `:1185-1196` (`AddOrgMember`), `:1201-1207` (`RemoveOrgMember`), `:1213-1228` (`CreateTeam`), `:1235-1246` (`AddTeamMember`); `Stash.Cli/PackageManager/Commands/PackageCommands.cs:151-155` (HttpRequestException leak)
**Phase:** cross-phase (P2/P4/P5/P6)
**Commit:** d46c883a, 1dcfffb9, 74daa2f7, 197e914c

### Observation

The brief's Semantics §"General error mapping" promises:

> `401 → "Not logged in. Run 'stash pkg login'."`, `403 → "Forbidden (<reason>)."`, `404 → "Not found: <resource>."`, `409 → "Conflict: <reason>."` — **always surfacing the server's `ErrorResponse` message where present**.

The new wire methods do not honor that contract — only `RevokeRole` surfaces the server message. Every other failure path collapses to either a bare boolean or a hand-written generic string:

1. **`ScopeCommand.Claim` mis-attributes every failure to "already owned"** (`ScopeCommand.cs:160-167`). `RegistryClient.ClaimScope` (`:1074-1087`) returns `null` on any non-2xx, dropping the server's body; the command then throws:

   ```csharp
   throw new InvalidOperationException(
       $"Failed to claim scope '{scopeName}'. " +
       "It may already be owned by another user or org. " +
       "Use 'stash pkg scope info <scope>' to check the current owner.");
   ```

   A 401 ("not logged in"), 403 ("publish ceiling required"), or a 5xx is reported to the user as an ownership conflict — wrong cause, wrong remediation. The test `Scope_Claim_AlreadyOwned_ByDifferentUser_ThrowsWithMessage` only asserts the scope name appears, so the mis-attribution survives.

2. **`AssignRole`, `SetVisibility`, `CreateOrg`, `AddOrgMember`, `RemoveOrgMember`, `CreateTeam`, `AddTeamMember`** all return bare `bool`/`null` from `IsSuccessStatusCode` and the command wrappers throw a hand-written generic message ("Failed to assign role.", "Failed to create organization 'X'. The name may already be taken or the token lacks sufficient permissions.", etc.). The server's `ErrorResponse.error` / `.message` — present on every error path — is read off the wire and discarded.

3. **`GetRoles`, `GetScope`, `GetOrg` leak raw `HttpRequestException`** for any non-404 error. They call `EnsureSuccessStatusCode()`, which produces `Response status code does not indicate success: 403 (Forbidden).`. `PackageCommands.Run`'s top-level catch (`PackageCommands.cs:151-155`) prints `ex.Message` verbatim — so a read-token user running `stash pkg role list @x/y` sees the raw .NET phrase rather than the brief's "Not logged in. Run 'stash pkg login'." / "Forbidden (...)."

### Why this matters

This is a parity gap against the brief's Semantics §General error mapping, not against an explicit `done_when` (P3's done_when does require surfacing the server message for **revoke**, and that is met). Severity is calibrated MEDIUM rather than HIGH on that basis. The user-visible effect is real though: the most common new failure modes (read-token holder hits an `assign`/`set`/`create`, second claimant on a scope, malformed org name) all surface as either a generic shrug or a raw .NET diagnostic, and the `ScopeCommand` case actively misleads the user about *why* the operation failed.

### Suggested fix

The cleanest single-place fix is at the wire layer: extract one `HandleNonSuccess(HttpResponseMessage, string action) → InvalidOperationException` helper that parses the response body as `ErrorResponse` (or falls back to the raw body), maps the HTTP status to the brief's prefix, and surfaces a message of the form

```
401 → "Not logged in. Run 'stash pkg login'."
403 → "Forbidden (<server message or "no reason">)."
404 → "Not found: <server message or resource>."
409 → "Conflict: <server message>."
other → "Error: HTTP <code> — <server message or status phrase>."
```

Then have each non-`Get*` method `throw` it (instead of returning `false`/`null`) on a non-success status; have each `Get*` method translate `EnsureSuccessStatusCode()` to the same helper (so 401/403 don't leak as raw `HttpRequestException`). At the command layer, delete the hand-written generic strings — the exception's `Message` is now the user-facing string. Update the `Claim`, `Assign`, etc. tests to assert the server's specific message is present (e.g. `Scope_Claim_AlreadyOwned_ByDifferentUser` should `Assert.Contains` the server's actual conflict phrase, not just the scope name).

Lower-effort alternative: at minimum fix the `ScopeCommand.Claim` misattribution — read the response body and include it in the thrown exception so a 401 doesn't masquerade as an ownership conflict.

### Verify

```
dotnet test --filter "FullyQualifiedName~PackageScopeCommandTests|FullyQualifiedName~PackageRoleCommandTests|FullyQualifiedName~PackageVisibilityCommandTests|FullyQualifiedName~PackageOrgCommandTests|FullyQualifiedName~RegistryClientParityTests"
```

---

## F03 — [LOW] Dead `if (ok)` branch in `RoleCommand.Revoke` + registered-but-unused `SetVisibilityResponse`

**Status:** open
**Files:** `Stash.Cli/PackageManager/Commands/RoleCommand.cs:209-213`, `Stash.Cli/PackageManager/CliJsonContext.cs:48`
**Phase:** P3, P2
**Commit:** a41ac1d8, d46c883a

### Observation

Two small polish items spotted while tracing the wire layer; neither materially affects behavior.

1. `RoleCommand.Revoke` (lines 209–213) treats `client.RevokeRole(...)` as if it could return `false`:

   ```csharp
   bool ok = client.RevokeRole(packageName, principalType, principalId);
   if (ok)
   {
       Console.WriteLine($"Revoked {principalType}/{principalId}'s role on {packageName}.");
   }
   ```

   But `RevokeRole` (`RegistryClient.cs:1039-1058`) only has two outcomes: `return true` on success or `throw InvalidOperationException` on failure. The `if (ok)` is therefore always true, and the implicit "else do nothing" branch is unreachable. Read as written it implies a third silent-failure outcome that doesn't exist.

2. `[JsonSerializable(typeof(SetVisibilityResponse))]` (`CliJsonContext.cs:48`) is registered but no consumer ever deserializes it — `RegistryClient.SetVisibility` (`:944-957`) only checks `IsSuccessStatusCode` and discards the body. The registration is dead.

### Why this matters

LOW: both are cosmetic and don't affect correctness today; flagged because they're both clean five-character deletes. If F02 is taken in the "wire-layer helper" form, `SetVisibility` will likely start consuming `SetVisibilityResponse` (the server returns the new tier, useful for the success message), so the registration may become live — note that case before deleting.

### Suggested fix

In `RoleCommand.Revoke`, drop the `if (ok)` and write the success line unconditionally (the throw on failure already prevents us from reaching it on the non-success path). Delete the `[JsonSerializable(typeof(SetVisibilityResponse))]` line **only if** F02 is not adopted in a form that consumes the response body.

### Verify

```
dotnet build Stash.Cli
dotnet test --filter "FullyQualifiedName~PackageRoleCommandTests|FullyQualifiedName~RegistryClientParityTests"
dotnet publish Stash.Cli -c Release -o /tmp/stash-cli-aot-f03
```
