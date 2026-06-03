using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Registry.OpenApi;

/// <summary>
/// Snapshot test for the published <c>openapi.json</c> document.
/// Asserts the generated document matches the baselined snapshot byte-for-byte,
/// enabling drift detection: any change to the API surface shows up as a snapshot
/// diff in code review.
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot normalization.</b> The <c>servers[*].url</c> field is populated
/// dynamically per request (scheme + host + BasePath, honoring forwarded headers —
/// locked in the P1 Decision Log). Comparing it raw would couple the snapshot to the
/// test host/port and make it flaky. Before comparison, every <c>servers[].url</c>
/// is replaced with the fixed placeholder <c>https://registry.invalid</c>.
/// No other fields require normalization (all other parts of the document are
/// deterministic).
/// </para>
/// <para>
/// <b>Re-baselining.</b> Run with <c>STASH_SNAPSHOT_REGEN=1</c> to overwrite the
/// snapshot file on disk:
/// <code>
/// STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~OpenApiSnapshotTests
/// </code>
/// The regen run intentionally fails (via <c>Assert.Fail</c>) so the new snapshot
/// shows up as a working-tree diff for review. Re-run without the flag to verify.
/// </para>
/// <para>
/// <b>Snapshot location.</b>
/// <c>Stash.Tests/Registry/OpenApi/Snapshots/openapi-v1.json</c> — a committed test
/// asset. Registered as an embedded resource in <c>Stash.Tests.csproj</c>.
/// </para>
/// </remarks>
public sealed class OpenApiSnapshotTests
{
    /// <summary>The placeholder URL substituted for every request-derived <c>servers[].url</c>.</summary>
    private const string ServerUrlPlaceholder = "https://registry.invalid";

    private const string SnapshotResourceName = "Stash.Tests.Registry.OpenApi.Snapshots.openapi-v1.json";

    // ── Snapshot test ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the in-process <c>openapi.json</c>, normalizes the dynamic
    /// <c>servers[].url</c> to <c>https://registry.invalid</c>, and asserts
    /// the result matches the committed snapshot byte-for-byte.
    /// </summary>
    [Fact]
    public async Task OpenApiDoc_MatchesBaselineSnapshot()
    {
        string normalized = await FetchAndNormalizeAsync();

        if (Environment.GetEnvironmentVariable("STASH_SNAPSHOT_REGEN") == "1")
        {
            string fixturePath = ResolveSnapshotPath();
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            File.WriteAllText(fixturePath, normalized, Encoding.UTF8);
            Assert.Fail(
                $"OpenAPI snapshot regenerated at:\n  {fixturePath}\n\n" +
                $"Re-run without STASH_SNAPSHOT_REGEN=1 to verify the new snapshot.");
        }

        string? expected = ReadEmbeddedSnapshot();
        if (expected == null)
        {
            Assert.Fail(
                $"OpenAPI snapshot fixture is missing.\n\n" +
                $"Run the following to baseline it:\n" +
                $"  STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~OpenApiSnapshotTests\n\n" +
                $"Actual normalized document:\n{normalized}");
        }

        // Normalize line endings — embedded resources on Windows may carry CRLF.
        expected = expected!.Replace("\r\n", "\n");

        if (!string.Equals(expected, normalized, StringComparison.Ordinal))
        {
            Assert.Fail(
                $"OpenAPI snapshot diverged.\n\n" +
                $"Re-baseline with:\n" +
                $"  STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~OpenApiSnapshotTests\n\n" +
                $"--- expected ({expected.Length} bytes)\n{expected[..Math.Min(500, expected.Length)]}\n...\n\n" +
                $"--- actual ({normalized.Length} bytes)\n{normalized[..Math.Min(500, normalized.Length)]}\n...");
        }
    }

    // ── Normalization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches <c>GET /openapi/v1.json</c> in-process and returns a deterministic
    /// normalized JSON string suitable for snapshot comparison.
    /// </summary>
    private static async Task<string> FetchAndNormalizeAsync()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        // Parse into a mutable JSON tree for normalization.
        JsonNode? root = JsonNode.Parse(body);
        Assert.NotNull(root);

        // Normalize servers[*].url → ServerUrlPlaceholder.
        // The servers array is dynamic (request-derived) and would otherwise
        // couple the snapshot to the test host/port.
        if (root!["servers"] is JsonArray servers)
        {
            foreach (var server in servers)
            {
                if (server is JsonObject serverObj && serverObj.ContainsKey("url"))
                {
                    serverObj["url"] = ServerUrlPlaceholder;
                }
            }
        }

        // Re-serialize with a fixed canonical format (indented, sorted is impractical
        // for large documents — we keep insertion order but use WriteIndented for
        // human-readable diffs).
        var writeOpts = new JsonSerializerOptions { WriteIndented = true };
        string normalized = root!.ToJsonString(writeOpts);

        // Ensure exactly one trailing newline for clean git diffs.
        return normalized.TrimEnd() + "\n";
    }

    // ── Snapshot I/O ──────────────────────────────────────────────────────────

    private static string? ReadEmbeddedSnapshot()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(SnapshotResourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ResolveSnapshotPath([CallerFilePath] string callerPath = "")
    {
        string dir = Path.GetDirectoryName(callerPath)!;
        return Path.Combine(dir, "Snapshots", "openapi-v1.json");
    }

    // ── Normalization unit test ───────────────────────────────────────────────

    /// <summary>
    /// Proves that the server-URL normalization replaces the dynamic URL with the
    /// placeholder. This is a pure-function test — no HTTP involved.
    /// </summary>
    [Fact]
    public void NormalizeServers_ReplacesUrlWithPlaceholder()
    {
        const string input = """
            {
              "openapi": "3.0.1",
              "servers": [
                { "url": "http://localhost:5000" }
              ],
              "paths": {}
            }
            """;

        JsonNode root = JsonNode.Parse(input)!;

        if (root["servers"] is JsonArray servers)
        {
            foreach (var server in servers)
            {
                if (server is JsonObject serverObj && serverObj.ContainsKey("url"))
                    serverObj["url"] = ServerUrlPlaceholder;
            }
        }

        var normalized = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        Assert.Contains(ServerUrlPlaceholder, normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("5000", normalized, StringComparison.Ordinal);
    }
}
