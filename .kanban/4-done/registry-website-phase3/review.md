# Registry Website Phase 3 — Review

> Produced by `/feature-review` (pass 2 — re-review after F01–F06 resolution).
> One finding per H2 section.
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

**Scope reviewed:** commits `d226bf46..047904bc` on branch `feature/registry-website-phase3`
**Brief:** ../brief.md
**Generated:** 2026-06-05 (review pass 2)

---

## Re-review verdict — clean

**Zero findings.** All six pass-1 findings (F01 MAJOR, F02 MAJOR, F03/F04/F05/F06 MINOR) are
correctly fixed and regression-free. The full unfiltered `dotnet test` baseline is green —
13583 passed / 0 failed / 6 source-quarantined skips, +3 vs pass 1 (the three regression
tests the resolvers added).

### Fix-by-fix confirmation (regression surface checked per finding)

**F01 (`a46c6a22`) — chokepoint scan extension-method coverage.** `SinkMethods`
broadened with the `System.Net.Http.Json` surface (`PostAsJsonAsync`, `PutAsJsonAsync`,
`PatchAsJsonAsync`, `GetFromJsonAsync`, `GetStringAsync`, `GetByteArrayAsync`,
`GetStreamAsync`) plus sync `Send`; a separate `ExtensionMethodSinks` subset feeds a new
binding-floor (`extensionMethodSinkCallSitesFound >= 1`) backed by `LoginService.PostAsJsonAsync`
at `:97` and `:146`; new `RogueExtensionMethodSnippet` fixture + `Scanner_RogueExtensionMethodCallSite_FlagsViolation`
self-test proves the new sinks have teeth (asserts `>= 2` violations for the rogue fixture).
The broadened sink set could in principle paper over a non-`HttpClient` site of a same-named
method, but `KnownNonHttpClientCalls` denylist only suppresses two `ISessionStore.GetAsync`
sites (`CookieSessionTokenAccessor`, `SessionCookieAuthenticationHandler`) plus the in-memory
store internals — auditable, append-only, and not silently grown to mask anything. The
green meta-test under the broadened sink set confirms zero false-positive violations.

**F02 (`5494d9b7`) — dashboard honors C4 401 contract.** `DashboardModel` now injects
`ISessionStore`, returns `Task<IActionResult>`, and splits the catch into
`when (ex.StatusCode == HttpStatusCode.Unauthorized)` → `ClearSessionAndRedirectToLoginExpiredAsync`
vs the non-401 `ex.ErrorMessage` banner — matching `ManageModel` and `TokensModel`. The success
path still returns `Page()` (no accidental swallow). The new
`Dashboard_AuthenticatedGet_Registry401_ClearsSessionAndRedirectsToLoginExpired` test asserts
all three contract outcomes: 302 to `/login?expired=1`, session cookie deleted via `Set-Cookie`
(`expires=` or `max-age=0`), and the entry removed from `ISessionStore`.

**F03 (`a0791f80`) — Authorize convention pins scheme via named policy.** `AddAuthorization`
is registered exactly once in `Program.cs` and creates a single named policy
`MaintainerAreaConventions.BffCookieAuthedPolicy` (`= "BffCookieAuthed"`) that explicitly
calls `policy.AuthenticationSchemes.Add(SessionCookie.AuthScheme)` and
`policy.RequireAuthenticatedUser()`. `AuthorizeAreaFolder(AreaName, "/", BffCookieAuthedPolicy)`
is the only auth-folder convention. `MaintainerAreaAuthorizationTests` still passes (anonymous
→ 302 to `/login`, authenticated → 200), so the gate is stable on the new policy path. The
policy name is a named const — no inlined literal.

**F04 (`0db626c3`) — `CookieSessionTokenAccessor` no longer sync-over-async.**
`SessionCookieAuthenticationHandler.HandleAuthenticateAsync` now stashes the resolved
`BffSession` in `HttpContext.Items[SessionCookie.SessionItemsKey]` (named const,
`= "Stash.Registry.Web.Auth.BffSession"`). `TryGetSession` reads `Items` synchronously
— no `.GetAwaiter().GetResult()` and no `ISessionStore` call on the request thread.
Ordering is correct: `UseAuthentication` runs the handler before page-model construction,
and `BffCookie` is the default scheme so the auto-authenticate fires on every request.
The async `GetPublishTokenAsync` (truly async via `ResolveSessionAsync`) is unused in
production — the constructor of `HttpAuthenticatedRegistryClient` uses `TryGetSession`
only. Distributed-store (Redis/SQL) substitution is now safe.

**F05 (`a0791f80`) — `"Maintainer"` literals retired.** New `Areas/Maintainer/Views/_ViewImports.cshtml`
adds `@using Stash.Registry.Web.Areas.Maintainer`; all 7 `asp-area` attributes in partials
now use `@MaintainerAreaConventions.AreaName`; `Tokens.cshtml.cs:206` uses
`new { area = MaintainerAreaConventions.AreaName }`. Verified clean:
`grep -RnE 'asp-area\s*=\s*"Maintainer"|area\s*=\s*"Maintainer"' Stash.Registry.Web/`
returns zero hits. The `Pages/_ViewImports.cshtml` cascade is unaffected (it has its own
namespace and tag-helpers, untouched). The integration tests
(`ManagePageTests`, `TokenSettingsPageTests`) exercise the partials end-to-end and remain green.

**F06 (`0db626c3`) — `LoginService.IsLocalUrl` retired in favor of framework `Url.IsLocalUrl`.**
The hand-rolled validator is deleted from `LoginService`; `LoginModel.OnPostAsync` validates
`ReturnUrl` with `Url.IsLocalUrl(ReturnUrl)` and passes `null` if non-local. `LoginService`
falls back to `/dashboard` when its `returnUrl` argument is null. **Caller coverage verified:**
`LoginService.LoginAsync` has exactly one caller (`Pages/Login.cshtml.cs:69`), so no second
caller bypasses validation. Defense-in-depth: `LoginModel` ends in `LocalRedirect(result.RedirectUrl!)`,
which re-checks `IsLocalUrl` and throws on non-local, closing the open-redirect at two layers.
The new `Login_WithBackslashReturnUrl_RedirectsToDashboard` test proves `/\evil.com` produces
a clean 302 to `/dashboard`, not a 500.

### Cross-cutting checks (all hold)

- **Zero registry-side changes:** `git diff d226bf46..047904bc -- 'Stash.Registry/**' 'Stash.Registry.Contracts/**'`
  returns empty — the BFF composes only existing endpoints (brief commitment honored).
- **Phase-2 byte-unchanged invariant:** `PhaseTwoFilesByteUnchangedMetaTests` passes against the
  pinned file set; none of the four resolution commits touches a Phase-2 file.
- **No-magic-strings regression:** every new bounded value introduced by the fixes has a named
  home — `BffCookieAuthedPolicy` (`MaintainerAreaConventions`), `SessionItemsKey`
  (`SessionCookie`); F05 retired the `"Maintainer"` literals.
- **Chokepoint guards intact:** `AuthClientChokepointMetaTests` (with the broadened sink set and
  the new extension-method binding floor) is green;
  `SessionTokenLeakMetaTests`, `AntiForgeryConstructMetaTests`, `WebProjectIsolationMetaTests`,
  `FieldDisciplineMetaTests`, `MaintainerAreaAuthorizationTests` all green.
- **Brief parity:** every acceptance criterion (login flow, dashboard visibility-aware listing,
  manage page contracts, token CRUD, 401 expiry redirect, anti-forgery, structural meta-tests,
  zero registry-side change) maps to live implementation + a passing test.

### Note (not a finding) — anticipated triplication of session-expiry plumbing

The F02 resolver added `ExpiredQueryKey`/`ExpiredQueryValue` constants and the
`ClearSessionAndRedirectToLoginExpiredAsync` helper to `DashboardModel`, bringing the count
of identical copies from 2 → 3 (Manage A3, Tokens A4, Dashboard A2/F02). Pass 1 explicitly
foresaw this and judged it non-obligatory: *"Consider extracting `ClearSessionAndRedirectToLoginExpiredAsync`
into a small shared helper… That's optional — three duplications is not by itself a
refactoring obligation."* This is **not a regression or a new defect** introduced by the
fixes — it is the anticipated consequence the prior review chose not to mandate. Recorded
here for the historical record; not filed as a finding (filing would block `/done` and
silently re-litigate the prior call). If a future authenticated page is added, that is
the right moment to extract a `MaintainerPageModel` base or a `SessionExpiryFlow` service.
