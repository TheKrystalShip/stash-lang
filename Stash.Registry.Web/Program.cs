using Stash.Registry.Web.Configuration;
using Stash.Registry.Web.Rendering;
using Stash.Registry.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
var registryClientConfig = builder.Configuration
    .GetSection("Registry")
    .Get<RegistryClientConfig>() ?? new RegistryClientConfig();

builder.Services.AddSingleton(registryClientConfig);

// ── HTTP client ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient(HttpRegistryClient.HttpClientName, client =>
{
    client.BaseAddress = new Uri(registryClientConfig.BaseUrl);
});

builder.Services.AddScoped<IRegistryClient, HttpRegistryClient>();

// ── README renderer (singleton — Markdig pipeline + HtmlSanitizer are thread-safe) ──────
builder.Services.AddSingleton<IReadmeRenderer, ReadmeRenderer>();

// ── Razor Pages ───────────────────────────────────────────────────────────────
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();

// Expose Program to WebApplicationFactory<Program> in integration tests.
public partial class Program { }
