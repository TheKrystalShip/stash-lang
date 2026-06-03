# pkg-cli-api-parity — Review (Pass 2)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.
>
> **Finding `**Status:**` lifecycle** (the promotion gate enforces this — see `promote-gate.stash`):
> - `open` — not yet addressed. **Blocks `/done`.**
> - `fixed` — resolved in code; carries a `**Fixed in:** <sha>` line. Set by `/resolve`.
> - `accepted` — a deliberate, human-recorded decision to ship without fixing. Set ONLY by a human
>   via `/accept <feature> <Fxx> <reason>`. Requires an `**Accepted because:** <reason>` line, and a
>   backlog stub for any deferred work. **CRITICAL findings can NEVER be `accepted`** — they must be
>   fixed or the run stops. The autopilot never self-accepts.
> - Any other value (typos, `wontfix`, …) is rejected by the gate — it fails closed.

**Scope reviewed:** commits `44be3591..c3f10160` on branch `feature/pkg-cli-api-parity` (pass 2 focuses on the F01/F02/F03 resolution commits `9ea494f8` and `0fb55794`)
**Brief:** ../brief.md
**Generated:** 2026-06-03

---

## Summary

Pass-1 findings have all landed and three of them check out cleanly: F01's misleading `stash pkg scope verify` instruction is replaced with honest "501 stub, tracked separately" language and `Scope_Claim_VerifiedMode_PrintsDnsTxtChallenge` now carries `Assert.DoesNotContain("stash pkg scope verify", output)` so a regression fails the gate (`PackageScopeCommandTests.cs:235`); F03's dead `if (ok)` in `RoleCommand.Revoke` and the orphan `[JsonSerializable(typeof(SetVisibilityResponse))]` registration are both gone; signature changes from nullable to non-nullable on `ClaimScope`/`CreateOrg`/`CreateTeam` are consistent across callers (`ScopeCommand.Claim`/`OrgCommand.Create`/`OrgCommand.TeamAdd` no longer null-check the result — the throw paths cover that contract). The new `HandleNonSuccess` helper is throw-safe across the relevant edge cases (empty body, non-JSON body, missing-`required` member all caught by `catch (JsonException)`; `string.IsNullOrWhiteSpace` fallback to `ReasonPhrase`), `ErrorResponse` is registered in `CliJsonContext` for AOT, and every wire DTO consumed by the new helper-routed methods is registered. `GetRoles`/`GetScope`/`GetOrg` correctly preserve the legitimate `404 → null` signal while routing other non-success through the helper.

Pass 2 surfaces two issues with the F02 resolution itself.

The F02 fix added `HandleNonSuccess` and routed eight wire methods through it (`SetVisibility`, `AssignRole`, `ClaimScope`, `CreateOrg`, `AddOrgMember`, `RemoveOrgMember`, `CreateTeam`, `AddTeamMember`, plus the `Get*` non-404 paths) — but `RevokeRole` was left out. It still throws `"Revoke role failed ({StatusCode}): {raw_json_body}"` instead of the brief's prescribed `404 → "Not found: <…>."` / `409 → "Cannot revoke: <…>."` / `401 → "Not logged in. Run 'stash pkg login'."` / `403 → "Forbidden (<…>)."` mapping. The brief's Semantics §`role revoke` (lines 191–195 of `brief.md`) names this mapping explicitly as the D18 surface; the F02 commit message describes the helper as "all failure paths now route through" — which is empirically not the case for `RevokeRole`. The existing revoke tests assert on substrings that already appear inside the StatusCode enum name and raw body, so the deviation survives the gate. Flagging as MEDIUM (F01).

The new helper itself takes an `action` parameter that every call site passes (e.g. `"set visibility of '{packageName}'"`, `"claim scope '{scope}'"`) and the helper silently discards — the doc comment on `RegistryClient.cs:1351` claims it's "used as fallback in the 'Not found' prefix" but the switch (lines 1377–1384) only ever references `serverMessage`, never `action`. Dead parameter + lying doc, LOW (F02).

Note: I relied on the baseline test summary (pass=13066/skip=6/fail=0) and the `dotnet build` warning-clean status reported by pass 1; I did not re-run a full build/AOT-publish post-fix. The F02 fix adds one `[JsonSerializable]` entry for an existing shared `ErrorResponse` type and F03 removes one entry — no new reflection, no new types — so the risk is near-zero, but it is unverified here.

Counts: **CRITICAL=0, IMPORTANT/HIGH=0, MEDIUM=1, LOW=1.**

---

## F01 — [MEDIUM] `RevokeRole` skipped by the F02 helper convergence — brief's revoke error mapping not implemented

**Status:** open
**Files:** `Stash.Cli/PackageManager/RegistryClient.cs:1059-1078`; `Stash.Tests/Cli/PackageRoleCommandTests.cs:157,183`; `Stash.Tests/Cli/RegistryClientParityTests.cs:192-219`
**Phase:** cross-phase (P2/P3 / F02 follow-up)
**Commit:** `9ea494f8` (the F02 fix that converged the other methods but missed `RevokeRole`)

### Observation

The F02 resolution introduced a wire-layer `HandleNonSuccess` helper (`RegistryClient.cs:1353-1387`) and routed eight new methods through it so the brief's status-prefix mapping (`401 → "Not logged in. Run 'stash pkg login'."` / `403 → "Forbidden (…)."` / `404 → "Not found: …"` / `409 → "Conflict: …"`) is uniformly applied. `RevokeRole` was not converted:

```csharp
// RegistryClient.cs:1070-1077
var response = _http.SendAsync(request).GetAwaiter().GetResult();
if (response.IsSuccessStatusCode)
{
    return true;
}

string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
throw new InvalidOperationException($"Revoke role failed ({response.StatusCode}): {error}");
```

That string is what a user sees end-to-end (it propagates through `PackageCommands.Run`'s `catch (InvalidOperationException)`). For the four failure modes the brief Semantics §`role revoke` (`brief.md:191-195`) names explicitly:

| Status | Brief prescribes | Today's actual output |
| --- | --- | --- |
| `401` | `Not logged in. Run 'stash pkg login'.` | `Revoke role failed (Unauthorized): {…raw body…}` |
| `403` | `Forbidden (<reason>).` | `Revoke role failed (Forbidden): {…raw body…}` |
| `404` | `Not found: <principal> holds no role on <pkg>.` | `Revoke role failed (NotFound): {"error":"RoleNotFoundException","message":"…"}` |
| `409` | `Cannot revoke: that would leave <pkg> with no owner.` | `Revoke role failed (Conflict): {"error":"LastOwnerException","message":"cannot remove the last owner of a package"}` |

The 401 case is the most user-visible regression-shape: an unauthenticated `role assign` now produces the actionable `"Not logged in. Run 'stash pkg login'."` (per the helper), while an unauthenticated `role revoke` on the same package produces `"Revoke role failed (Unauthorized): …"` — no actionable guidance. For 404 and 409 the server message is present but wrapped in the raw JSON envelope (`{"error":"…","message":"…"}`) instead of being unwrapped through `err.Message ?? err.Error` the way the helper does for every other failure path.

The masking is at the test layer:

- `Role_Revoke_LastOwner_ThrowsWithServerMessage` (`PackageRoleCommandTests.cs:157`) asserts `Contains("cannot remove the last owner of a package")` — passes off the raw `message` substring inside the JSON envelope, not the brief's `"Cannot revoke:"` prefix.
- `Role_Revoke_NoSuchRole_ThrowsNotFound` (`PackageRoleCommandTests.cs:183`) asserts `Contains("NotFound")` — passes off the `HttpStatusCode` enum name in the wrapper, not the brief's `"Not found:"` prefix.
- `RevokeRole_Server404_ThrowsWithServerMessage` / `RevokeRole_Server409LastOwner_ThrowsWithServerMessage` (`RegistryClientParityTests.cs:192-219`) assert `Contains("NotFound")`/`Contains("Conflict")` for the same reason.

So the brief's revoke mapping is unverified by the gate, and the F02 fix's stated invariant ("all failure paths now route through" `HandleNonSuccess` — per commit message `9ea494f8`) is empirically false for `RevokeRole`.

### Why this matters

This is a brief-parity gap, not a security or correctness defect — the user does still see the server's reason inside the wrapped envelope. Calibrated MEDIUM rather than LOW because: (a) the 401 case actively loses the actionable `"Run 'stash pkg login'."` instruction that the analogous failure on `role assign` already produces (so two adjacent CLI verbs report the same root cause inconsistently); (b) the brief's Semantics §`role revoke` calls out the four-status mapping by name, with the explicit `D18` cross-reference, as one of the feature's defining behaviors; and (c) the inconsistency now stands out — every other failure surface in the new parity surface follows the helper convention except this one. Pass 1's F02 adjudicated message-dropping ("only `RevokeRole` surfaces the server message" — true, it doesn't drop) but did not evaluate `RevokeRole` against the brief's prefix mapping, so this is a dimension pass 1 did not check rather than an overturn of a pass-1 decision.

### Suggested fix

Route `RevokeRole` through `HandleNonSuccess` like the other failure paths:

```csharp
// RegistryClient.cs:1059
public bool RevokeRole(string packageName, string principalType, string principalId)
{
    EnsureTokenFresh();
    string body = JsonSerializer.Serialize(
        new RevokeRoleRequest { PrincipalType = principalType, PrincipalId = principalId },
        CliJsonContext.Default.RevokeRoleRequest);
    var request = new HttpRequestMessage(HttpMethod.Delete,
        $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/roles")
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
    var response = _http.SendAsync(request).GetAwaiter().GetResult();
    if (!response.IsSuccessStatusCode)
    {
        throw HandleNonSuccess(response, $"revoke role on '{packageName}'");
    }
    return true;
}
```

Then tighten the test assertions so the brief's prefix mapping is actually enforced:

- `Role_Revoke_LastOwner_ThrowsWithServerMessage`: `Assert.Contains("Conflict:", ex.Message)` and `Assert.Contains("cannot remove the last owner", ex.Message)` (substring of the server `message` field, NOT the raw `"LastOwnerException"` error code).
- `Role_Revoke_NoSuchRole_ThrowsNotFound`: `Assert.Contains("Not found:", ex.Message)` and `Assert.Contains("holds no role", ex.Message)`.
- `RegistryClientParityTests.RevokeRole_Server404_ThrowsWithServerMessage`: same `Contains("Not found:")` / `Contains("principal holds no role on this package")` change.
- `RegistryClientParityTests.RevokeRole_Server409LastOwner_ThrowsWithServerMessage`: same `Contains("Conflict:")` change.

Without the test tightening, a future regression that reverts the helper routing on `RevokeRole` will not fail the gate (the StatusCode enum name and raw body substrings both appear in either form of the message).

### Verify

```
dotnet test --filter "FullyQualifiedName~PackageRoleCommandTests|FullyQualifiedName~RegistryClientParityTests"
```

---

## F02 — [LOW] `HandleNonSuccess` `action` parameter is dead — passed at every call site, never used, docstring lies

**Status:** open
**Files:** `Stash.Cli/PackageManager/RegistryClient.cs:1351,1353,1377-1384` (helper); `RegistryClient.cs:972,1000,1035,1107,1136,1169,1199,1233,1259,1292,1327` (call sites)
**Phase:** P2 / F02 follow-up
**Commit:** `9ea494f8`

### Observation

`HandleNonSuccess(HttpResponseMessage resp, string action)` (`RegistryClient.cs:1353`) is invoked by 11 call sites that each pass a contextual phrase — `"set visibility of '{packageName}'"`, `"assign role on '{packageName}'"`, `"claim scope '{scope}'"`, `"create organization '{name}'"`, `"add member '{username}' to organization '{org}'"`, etc. The helper's switch (lines 1377–1384) and the empty-body fallback (lines 1372–1375) only reference `serverMessage`:

```csharp
if (string.IsNullOrWhiteSpace(serverMessage))
{
    serverMessage = resp.ReasonPhrase ?? resp.StatusCode.ToString();
}

string message = (int)resp.StatusCode switch
{
    401 => "Not logged in. Run 'stash pkg login'.",
    403 => $"Forbidden ({serverMessage}).",
    404 => $"Not found: {serverMessage}",
    409 => $"Conflict: {serverMessage}",
    _   => $"Error: HTTP {(int)resp.StatusCode} — {serverMessage}"
};
```

`action` is never read. The XML doc on line 1351 claims it is "used as fallback in the 'Not found' prefix" — which is false; the actual fallback is `ReasonPhrase ?? StatusCode.ToString()`. The doc comment on lines 1342–1346 also says `404 → "Not found: <server message or action>."` — same misrepresentation.

Concrete consequence: a 404 with an empty response body on, say, `RemoveOrgMember("acme", "nobody")` produces `"Not found: Not Found"` (the `ReasonPhrase` echoing the prefix) instead of `"Not found: remove member 'nobody' from organization 'acme'"` that the docstring promises. This is not a runtime defect — empty 404 bodies are uncommon and the user still sees something — but it is the doc-vs-code drift the helper was meant to avoid.

### Why this matters

LOW because the helper's "happy paths" (server returns an `ErrorResponse` body with a non-empty `Message` or `Error`) work correctly and that is the path every test exercises; the dead-parameter case only surfaces on a server that returns an empty body on a non-success status, which the registry does not currently do. Flagged because (a) dead parameters that lie in their XML doc are a maintenance trap — a future engineer reading the doc will write tests that fail, or rely on context that does not propagate; and (b) this is a freshly-introduced helper, so it costs nothing to make it match its docstring now versus removing the param after it has fanned out further.

### Suggested fix

Pick one of two cheap edits.

Option A — actually use the parameter (matches the docstring's promise):

```csharp
// RegistryClient.cs:1372-1384
if (string.IsNullOrWhiteSpace(serverMessage))
{
    serverMessage = !string.IsNullOrWhiteSpace(action)
        ? action
        : resp.ReasonPhrase ?? resp.StatusCode.ToString();
}
// (switch unchanged)
```

Option B — drop the parameter and the docstring claim, since the eleven call sites already pass it but get no benefit:

```csharp
private static InvalidOperationException HandleNonSuccess(HttpResponseMessage resp)
{
    // …same body, no action…
}
```

…and at every call site replace `throw HandleNonSuccess(response, "…")` with `throw HandleNonSuccess(response)`. Then remove the `<param name="action">` and `<list>` `or action` clauses from the docstring.

Option A is the smaller diff and lines up with what the doc already says; Option B is more honest about what the helper actually does today. Either is fine; not both.

### Verify

```
dotnet build Stash.Cli
dotnet test --filter "FullyQualifiedName~RegistryClientParityTests"
```
