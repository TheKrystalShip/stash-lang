using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Web.Areas.Maintainer;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Configuration;
using Stash.Registry.Web.Rendering;
using Stash.Registry.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
var registryClientConfig = builder.Configuration
    .GetSection("Registry")
    .Get<RegistryClientConfig>() ?? new RegistryClientConfig();

builder.Services.AddSingleton(registryClientConfig);

// ── HTTP clients ──────────────────────────────────────────────────────────────

// Phase 2 — anonymous browse client (unchanged).
builder.Services.AddHttpClient(HttpRegistryClient.HttpClientName, client =>
{
    client.BaseAddress = new Uri(registryClientConfig.BaseUrl);
});

// Phase 3 — anonymous login client (no Authorization header).
builder.Services.AddHttpClient(LoginHttpClients.AuthLogin, client =>
{
    client.BaseAddress = new Uri(registryClientConfig.BaseUrl);
});

// Phase 3 — anonymous mint client; the read JWT is attached per-call inside LoginService.
builder.Services.AddHttpClient(LoginHttpClients.AuthMint, client =>
{
    client.BaseAddress = new Uri(registryClientConfig.BaseUrl);
});

// Phase 3 — revoke client; the publish JWT is attached per-call inside LogoutService.
builder.Services.AddHttpClient(LogoutHttpClients.AuthRevoke, client =>
{
    client.BaseAddress = new Uri(registryClientConfig.BaseUrl);
});

// Phase 3 A2 — authenticated registry client (per-request scoped).
// Base address is the same registry URL; Authorization is set per-request inside
// HttpAuthenticatedRegistryClient.CreateRequest (C1 chokepoint — no opt-out).
builder.Services.AddHttpClient(AuthenticatedRegistryHttpClients.AuthenticatedRegistry, client =>
{
    client.BaseAddress = new Uri(registryClientConfig.BaseUrl);
});

// ── Registry clients ──────────────────────────────────────────────────────────

// Phase 2 — anonymous registry client (unchanged).
builder.Services.AddScoped<IRegistryClient, HttpRegistryClient>();

// Phase 3 A2 — authenticated registry client (Scoped: one per HTTP request).
// The factory throws NoActiveSessionException if no session is in scope (C1 fail-closed backstop).
// On anonymous requests, the [Authorize] page convention 302s to /login BEFORE the page model
// is constructed — this factory throw is defense-in-depth, not the primary auth gate.
builder.Services.AddScoped<IAuthenticatedRegistryClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var sessionTokenAccessor = sp.GetRequiredService<ISessionTokenAccessor>();
    return new HttpAuthenticatedRegistryClient(httpClientFactory, sessionTokenAccessor);
});

// ── README renderer (singleton — Markdig pipeline + HtmlSanitizer are thread-safe) ──────
builder.Services.AddSingleton<IReadmeRenderer, ReadmeRenderer>();

// ── BFF session store ─────────────────────────────────────────────────────────
// Singleton: InMemorySessionStore uses ConcurrentDictionary — safe to share across requests.
// TODO: replace with a Redis- or Postgres-backed store for multi-instance deployments.
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();

// ── BFF session token accessor (per-request) ──────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISessionTokenAccessor, CookieSessionTokenAccessor>();

// ── BFF login / logout services ───────────────────────────────────────────────
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<LogoutService>();

// ── DataProtection ────────────────────────────────────────────────────────────
builder.Services.AddDataProtection();

// ── Authentication: BffCookie scheme ─────────────────────────────────────────
builder.Services
    .AddAuthentication(SessionCookie.AuthScheme)
    .AddScheme<SessionCookieAuthenticationOptions, SessionCookieAuthenticationHandler>(
        SessionCookie.AuthScheme,
        configureOptions: null);

// ── Authorization ─────────────────────────────────────────────────────────────
// Register a named policy that pins the BFF cookie scheme explicitly so the
// Maintainer area's auth gate is stable regardless of the default scheme.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(MaintainerAreaConventions.BffCookieAuthedPolicy, policy =>
    {
        policy.AuthenticationSchemes.Add(SessionCookie.AuthScheme);
        policy.RequireAuthenticatedUser();
    });
});

// ── Razor Pages + global anti-forgery ────────────────────────────────────────
// AutoValidateAntiforgeryTokenAttribute is registered globally so every POST,
// PUT, PATCH, DELETE that does not carry a valid anti-forgery token fails 400
// before any page handler runs. CSRF lands here (A1) with the first POST (/login)
// — deferring to A3 would leave /login itself unprotected.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());

    // Phase 3 A2 — apply Authorize convention to all Maintainer area pages.
    // Anonymous requests are 302'd to /login?returnUrl=… BEFORE the page model is constructed.
    MaintainerAreaConventions.Apply(options);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

// Authentication + authorization MUST be added BEFORE MapRazorPages.
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();

// Expose Program to WebApplicationFactory<Program> in integration tests.
public partial class Program { }
