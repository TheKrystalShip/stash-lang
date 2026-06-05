# Registry Website Phase 3 — Review

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

**Scope reviewed:** commits `d226bf46..f24707e2` on branch `feature/registry-website-phase3`
**Brief:** ../brief.md
**Generated:** 2026-06-05

---

## F01 — [MAJOR] C1 chokepoint scan has a sink-coverage hole — extension-method HTTP calls bypass the guard

**Status:** fixed
**Fixed in:** a46c6a22
**Files:** `Stash.Tests/Registry/Web/AuthClientChokepointMetaTests.cs:57-65`, `Stash.Registry.Web/Auth/LoginService.cs:97`, `Stash.Registry.Web/Auth/LoginService.cs:146`
**Phase:** A2
**Commit:** 36a20bc4

### Observation

`AuthClientChokepointMetaTests.SinkMethods` lists only the six HTTP-method names defined directly on `HttpClient`:

```csharp
private static readonly HashSet<string> SinkMethods = new(StringComparer.Ordinal)
{
    "SendAsync", "GetAsync", "PostAsync", "PutAsync", "PatchAsync", "DeleteAsync",
};
```

It does **not** include the `System.Net.Http.Json` / `HttpClient` extension methods that are idiomatic in modern ASP.NET Core: `PostAsJsonAsync`, `PutAsJsonAsync`, `GetFromJsonAsync`, `GetStringAsync`, `GetByteArrayAsync`, `GetStreamAsync`, `PatchAsJsonAsync`, or the sync `Send` method.

The clincher is already in the production code under review: `LoginService.cs:97` and `:146` both invoke `client.PostAsJsonAsync(...)`. The scan literally cannot see these call sites. They are reported as zero violations and they do **not** increment `allowedCallSitesFound` either (so they don't even contribute to the binding floor that's supposed to fail vacuous passes).

The brief's C1 row states the scan "scans `Stash.Registry.Web/` for `HttpClient.SendAsync` / `GetAsync` / `PostAsync` / `DeleteAsync` call sites" and the A2 `done_when` says it "finds every `HttpClient` invocation". Both overstate what the test does.

### Why this matters

The Construct layer (`IAuthenticatedRegistryClient`'s typed wrapper + DI-throws-without-session) still holds — so this is not currently exploitable. But the Detect layer is the brief's load-bearing omission guard, and it has a quiet bypass: a future page model that injects `IHttpClientFactory` and writes

```csharp
var http = _httpClientFactory.CreateClient();
var data = await http.GetFromJsonAsync<Foo>("/api/v1/...");
```

would issue an un-threaded registry call and pass the chokepoint scan green. That is precisely the "forgotten-controller / silent omission" class the brief's Motivation section calls out as the omission this feature is built to prevent. The Construct over Detect over Instruct doctrine demands both layers actually bite at the abstraction the doctrine is meant to defend.

### Suggested fix

Two complementary changes — both small:

1. **Extend `SinkMethods`** to cover the extension-method surface that real call sites use:
   `PostAsJsonAsync`, `PutAsJsonAsync`, `PatchAsJsonAsync`, `GetFromJsonAsync`, `GetStringAsync`, `GetByteArrayAsync`, `GetStreamAsync`, and `Send` (sync). The scan is purely syntactic (method-name only), so adding names is one-line per name. The existing `KnownNonHttpClientCalls` denylist absorbs any false positives.
2. **Add fail-path fixture entries** in `ChokepointFailPathFixture_HttpClient` that use `PostAsJsonAsync` and `GetFromJsonAsync` from a rogue type, and add a self-test (`Scanner_RogueExtensionMethodCallSite_FlagsViolation`) so the new sinks have provable teeth — mirroring the existing `Scanner_RogueCallSite_FlagsViolation` pattern.

After the sink-set change, `LoginService.PostAsJsonAsync` call sites should start incrementing `allowedCallSitesFound`. Consider strengthening the binding floor to `>= 2` so a future allowlist edit that accidentally drops one of `LoginService` / `HttpAuthenticatedRegistryClient` fails loudly.

### Verify

```
dotnet test --filter "FullyQualifiedName~AuthClientChokepointMetaTests"
```

After the change all production sites must still be allowed, and the new rogue-extension-method fixtures must trip the violation list. Also confirm `LoginService.cs:97` and `:146` are counted in `allowedCallSitesFound`.

---

## F02 — [MAJOR] Dashboard does not honor the C4 401 → clear-session → /login?expired=1 contract

**Status:** fixed
**Fixed in:** 5494d9b7
**Files:** `Stash.Registry.Web/Areas/Maintainer/Pages/Dashboard.cshtml.cs:62-70`
**Phase:** A2
**Commit:** 36a20bc4

### Observation

Both `Manage.cshtml.cs` (A3) and `Settings/Tokens.cshtml.cs` (A4) implement `ClearSessionAndRedirectToLoginExpiredAsync` and catch `RegistryClientException` with a `when (ex.StatusCode == HttpStatusCode.Unauthorized)` clause that clears the server-side session, deletes the cookie, and redirects to `/login?expired=1`. The `Dashboard.cshtml.cs` page model — the authenticated landing page — does not:

```csharp
try
{
    Packages = await _authClient.SearchOwnedAsync(query, cancellationToken)
        .ConfigureAwait(false);
}
catch (RegistryClientException ex)
{
    RegistryError = ex.Message ?? "The registry is temporarily unreachable.";
}
```

There is no 401 special case. A registry 401 (publish token revoked out-of-band) renders the dashboard with the raw exception message `RegistryError = "Registry returned 401 Unauthorized."` instead of clearing the session and redirecting to `/login?expired=1`. The session cookie remains live, so every subsequent page-load repeats the same broken state until the cookie's `Expires` time arrives.

Secondary nit (same catch block): the dashboard uses `ex.Message` while the other pages use `ex.ErrorMessage`. `ex.Message` is the composed `"Registry returned 500 InternalServerError. Error: foo. <text>"` form, including the status code and error code; `ex.ErrorMessage` is the human message alone, which is what the other pages display in their banners. Fixing F02 should fold this in (use `ex.ErrorMessage`).

The brief's **Semantics → Authn failure** section is explicit: "An authed call that receives 401 from the registry (e.g. session token revoked out-of-band) clears the cookie + session and redirects to `/login?expired=1`." This is also called out under Acceptance Criteria. The A2 `done_when` for the dashboard does not call it out, but the cross-cutting C4 invariant ("Error mapping") applies to *every* authenticated page; A3 and A4 honor it, A2 does not.

### Why this matters

C4 is one of the seven cross-cutting concerns at the spine of this feature, and the dashboard is the post-login landing page that authenticated users hit first — so this is the most-likely path for a user with a revoked session to actually observe. Behaviorally the user sees an authenticated-looking page with a confusing red error banner instead of the clean re-login flow the rest of the area implements. There's no integration test asserting the 401 path on the dashboard, which is how this slipped (compare `ManagePageTests` and `TokenSettingsPageTests` — both have explicit 401-handling tests).

### Suggested fix

Inject `ISessionStore` into `DashboardModel` (matching `ManageModel`'s constructor), add the same `ClearSessionAndRedirectToLoginExpiredAsync` private helper, and split the catch into:

```csharp
catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
{
    return await ClearSessionAndRedirectToLoginExpiredAsync(cancellationToken);
}
catch (RegistryClientException ex)
{
    RegistryError = ex.ErrorMessage ?? "The registry is temporarily unreachable.";
}
```

Change `OnGetAsync`'s return type from `Task` to `Task<IActionResult>` accordingly. Add a `DashboardPageTests` case asserting that a 401 from the registry clears the session, deletes the cookie, and 302s to `/login?expired=1` — mirroring the existing `ManagePageTests` 401 case.

Consider extracting `ClearSessionAndRedirectToLoginExpiredAsync` into a small shared helper (e.g. a `MaintainerPageModel` base class or a `SessionExpiryFlow` service) so future authed pages can't omit it. That's optional — three duplications is not by itself a refactoring obligation — but it would Construct-prevent F02 from recurring.

### Verify

```
dotnet test --filter "FullyQualifiedName~DashboardPageTests"
```

The new test must fail before the fix and pass after; the existing `ManagePageTests` / `TokenSettingsPageTests` 401 tests must continue to pass.

---

## F03 — [MINOR] Maintainer-area Authorize convention relies on default auth scheme — deviates from `done_when` text

**Status:** open
**Files:** `Stash.Registry.Web/Areas/Maintainer/MaintainerAreaConventions.cs:41-50`
**Phase:** A2
**Commit:** 36a20bc4

### Observation

A2's `done_when` line specifies:

> `MaintainerAreaConventions` applies `[Authorize(AuthenticationSchemes = "BffCookie")]` to every page under `Areas/Maintainer/Pages/` (via `PageConventionCollection.AuthorizeAreaFolder("Maintainer", "/")`-style convention).

The shipped code calls the 2-arg overload `options.Conventions.AuthorizeAreaFolder(AreaName, "/")`, which adds an `[Authorize]` with **no** `AuthenticationSchemes` constraint — it resolves to the *default* authentication scheme. `Program.cs:88` registers `BffCookie` as the default via `AddAuthentication(SessionCookie.AuthScheme)`, so this currently behaves identically: anonymous → 302 to `/login?returnUrl=…`, which the integration tests confirm.

The implementer flagged this in their notes as a latent fragility: there is no overload of `AuthorizeAreaFolder` that takes a scheme directly, so honoring the `done_when` text literally would require building a named policy via `AddAuthorization(o => o.AddPolicy("BffCookieAuthed", p => { p.AuthenticationSchemes.Add(SessionCookie.AuthScheme); p.RequireAuthenticatedUser(); }))` and passing the policy name to a 3-arg `AuthorizeAreaFolder(area, folder, policyName)` overload.

### Why this matters

If a future feature ever registers a second authentication scheme — e.g. an `ApiKey` scheme for a future machine-to-machine surface, or a `Bearer` scheme to share login with a SPA — and changes the default scheme (or removes the default-scheme designation from `BffCookie`), the Maintainer area's protection silently shifts to whatever is default. The brief explicitly says "make token-threading a Construct chokepoint: an authenticated read or write that forgets the session token must be unrepresentable" — relying on a default-scheme convention weakens this for the auth gate itself. The blast radius is bounded today (single scheme), so MINOR, not MAJOR.

This is also a brief-parity issue: the `done_when` text and the shipped code disagree on the mechanism, even though they agree on the current behavior.

### Suggested fix

Register a named policy that pins the scheme explicitly, then pass it to the 3-arg `AuthorizeAreaFolder`:

```csharp
// In Program.cs:
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(MaintainerAreaConventions.BffCookieAuthedPolicy, policy =>
    {
        policy.AuthenticationSchemes.Add(SessionCookie.AuthScheme);
        policy.RequireAuthenticatedUser();
    });
});

// In MaintainerAreaConventions.Apply:
options.Conventions.AuthorizeAreaFolder(AreaName, "/", BffCookieAuthedPolicy);
```

Add a `BffCookieAuthedPolicy` const on `MaintainerAreaConventions` so the policy name is named (no inline literal). The existing `MaintainerAreaAuthorizationTests` should pass unchanged.

### Verify

```
dotnet test --filter "FullyQualifiedName~MaintainerAreaAuthorizationTests"
```

Anonymous → 302 to `/login` must still hold; authed → 200 must still hold.

---

## F04 — [MINOR] CookieSessionTokenAccessor does sync-over-async on every authed request

**Status:** open
**Files:** `Stash.Registry.Web/Auth/CookieSessionTokenAccessor.cs:36-46`
**Phase:** A1
**Commit:** 6c0035ff

### Observation

`CookieSessionTokenAccessor.TryGetSession` resolves the session synchronously by calling `.GetAwaiter().GetResult()` on `ResolveSessionAsync()`:

```csharp
public bool TryGetSession(out BffSession? session)
{
    if (!_resolved)
    {
        // Synchronously resolve: we need the result now for DI factory decisions.
        // The store is in-memory (Task.CompletedTask), so .GetAwaiter().GetResult() is safe.
        _cachedSession = ResolveSessionAsync().GetAwaiter().GetResult();
        _resolved = true;
    }
    ...
}
```

The comment is accurate today: `InMemorySessionStore.GetAsync` returns `Task.FromResult(…)`, so there is no continuation to deadlock on. But the brief sells `ISessionStore` as the explicit swap point for a distributed store: "Multi-instance hosting needs a distributed store (Redis / SQL Server / Postgres) — left as a TODO in A1's docstring; the `ISessionStore` seam exists so this is a configuration swap, not a redesign." A Redis-backed `GetAsync` returns a `Task` that *does* require continuation back to the request synchronization context, and that's the canonical ASP.NET Core sync-over-async deadlock vector.

`TryGetSession` is called from the `HttpAuthenticatedRegistryClient` constructor — i.e. every authed page that resolves the client. With a distributed store this becomes a thread-pool starvation / deadlock risk on every authed request.

### Why this matters

The "swap to Redis without a redesign" claim is load-bearing for the operational note in the README. The current implementation silently breaks that promise — the swap requires an additional API redesign on `ISessionTokenAccessor` to surface an async-only accessor (or to fold the lookup into something that doesn't need to be synchronous). That's not currently catastrophic (in-memory is fine), but it is exactly the kind of latent assumption that bites at v2.

### Suggested fix

Two options:

- **(A) Resolve the session in middleware before the DI factory runs.** `SessionCookieAuthenticationHandler.HandleAuthenticateAsync` already does the lookup naturally-async; store the resolved `BffSession` in `HttpContext.Items` (a small named-key constant), and have `CookieSessionTokenAccessor.TryGetSession` read it synchronously from `Items`. The accessor becomes a pure synchronous read of an already-resolved value. This is the cleanest fix and aligns with the brief's "the auth handler 302s anonymous requests before the page model is constructed" story — by then the lookup has already happened.
- **(B) Make the DI factory itself async.** Change `HttpAuthenticatedRegistryClient`'s factory to use `IAsyncServiceProvider` / build the client lazily on first method call. More intrusive; not recommended.

Recommend (A).

### Verify

```
dotnet test --filter "FullyQualifiedName~SessionCookieAuthenticationHandlerTests|FullyQualifiedName~DashboardPageTests|FullyQualifiedName~ManagePageTests|FullyQualifiedName~TokenSettingsPageTests"
```

After the change, no `.GetAwaiter().GetResult()` remains in the accessor; all integration tests still pass.

---

## F05 — [MINOR] `asp-area="Maintainer"` and `area = "Maintainer"` literals duplicate the single source of truth

**Status:** open
**Files:** `Stash.Registry.Web/Areas/Maintainer/Pages/Settings/Tokens.cshtml.cs:206`, `Stash.Registry.Web/Areas/Maintainer/Views/MaintainerDeprecateForm.cshtml:38`, `:54`, `Stash.Registry.Web/Areas/Maintainer/Views/MaintainerVersionsTable.cshtml:63`, `:77`, `Stash.Registry.Web/Areas/Maintainer/Views/MaintainerVisibilityForm.cshtml:24`, `Stash.Registry.Web/Areas/Maintainer/Views/TokenCreateForm.cshtml:17`, `Stash.Registry.Web/Areas/Maintainer/Views/TokenList.cshtml:62`
**Phase:** A3, A4
**Commit:** 277c685b, f79972bf

### Observation

`MaintainerAreaConventions.AreaName = "Maintainer"` is the declared single source of truth for the area name, but the literal string `"Maintainer"` is duplicated across eight call sites in views and one in C# code:

- `Tokens.cshtml.cs:206` — `new { area = "Maintainer" }` (C# code).
- Seven `asp-area="Maintainer"` attributes in partial views.

The brief's standing project rule ("Bounded values — claim names, roles, scopes, **area names**, policy names — must come from a named const/enum") explicitly lists area names as bounded. Razor tag-helper attribute values DO support `@expression` interpolation, so all of these are mechanically replaceable by `@MaintainerAreaConventions.AreaName` (after a `@using` for the namespace).

### Why this matters

Low blast radius today — the area is unlikely to be renamed. But this is exactly the "literal copied in eight places" defect the C6 Decision-Log row argues against. Mechanical fixability + named home already existing makes this cheap to resolve and worth resolving so future code keeps the convention.

### Suggested fix

In the views, add `@using Stash.Registry.Web.Areas.Maintainer` to `_ViewImports.cshtml` and replace each `asp-area="Maintainer"` with `asp-area="@MaintainerAreaConventions.AreaName"`. In `Tokens.cshtml.cs:206`, replace `new { area = "Maintainer" }` with `new { area = MaintainerAreaConventions.AreaName }`.

### Verify

```
grep -RnE 'area\s*=\s*"Maintainer"|asp-area\s*=\s*"Maintainer"' Stash.Registry.Web/
# → should return zero non-comment hits.
dotnet test --filter "FullyQualifiedName~ManagePageTests|FullyQualifiedName~TokenSettingsPageTests"
```

The integration tests continue to drive `/manage/...` and `/settings/tokens` routes, so a broken area reference would surface as a routing 404.

---

## F06 — [MINOR] LoginService.IsLocalUrl is a weak hand-rolled validator — deviates from brief's `Url.IsLocalUrl` commitment

**Status:** open
**Files:** `Stash.Registry.Web/Auth/LoginService.cs:231-238`
**Phase:** A1
**Commit:** 6c0035ff

### Observation

The brief's Open Questions section commits: "Only same-origin relative paths are honored to prevent open-redirect. … the implementer enforces with `Url.IsLocalUrl(returnUrl)`." The shipped implementation is hand-rolled:

```csharp
private static bool IsLocalUrl(string? url)
{
    if (string.IsNullOrEmpty(url))
        return false;
    // Must start with / but NOT with // (protocol-relative) and not be an absolute URL.
    return url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal);
}
```

This is weaker than the framework `IUrlHelper.IsLocalUrl` it's supposed to replicate. For example `"/\\evil.com"` and `"/%2fevil.com"` both pass the hand-rolled check (start with `/`, do not start with `//`), but the framework helper rejects them.

The good news is that `LoginModel.OnPostAsync` returns `LocalRedirect(result.RedirectUrl!)`, and `LocalRedirect` itself calls the framework `IsLocalUrl` and **throws** on a non-local URL. So a malicious `returnUrl` that sneaks past the hand-rolled check produces a 500 (`InvalidOperationException`) rather than an open redirect. Not a security exposure, but a brief-parity gap and a UX deviation (500 instead of clean redirect to `/dashboard`).

### Why this matters

The brief commits to a specific mechanism for a security-flavored decision; the implementation diverges. Use of the framework helper would also be self-documenting and prevent the maintenance gotcha of "the hand-rolled validator now subtly disagrees with `LocalRedirect`".

### Suggested fix

In `LoginService`, inject `IUrlHelperFactory` (or thread the page's `IUrlHelper` in via the existing `HttpContext`-route — `LoginModel.OnPostAsync` already has `Url` available and could pass the validated URL down). Simplest variant: delete `IsLocalUrl` from `LoginService` and have `LoginModel.OnPostAsync` do the validation with its `Url.IsLocalUrl(ReturnUrl)` before calling `_loginService.LoginAsync`, passing `null` if not local. `LoginService` then trusts the caller and always falls back to `/dashboard` when its parameter is `null`.

### Verify

```
dotnet test --filter "FullyQualifiedName~LoginPageTests"
```

Add a test case `Login_WithBackslashReturnUrl_RedirectsToDashboard` proving the validator rejects `/\\evil.com` and the user is sent to `/dashboard` (not 500).
