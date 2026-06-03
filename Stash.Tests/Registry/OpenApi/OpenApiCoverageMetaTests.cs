using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Stash.Tests.Registry.Authz;

namespace Stash.Tests.Registry.OpenApi;

/// <summary>
/// Coverage meta-test that reads the in-process-fetched <c>openapi.json</c> and asserts
/// every operation satisfies the P1 contract:
/// <list type="number">
///   <item><b>(a) operationId present</b> — every operation in the generated doc carries a non-null
///         operationId (provided by the <c>OpenApiOperationIdTransformer</c> for controller
///         actions and by <c>.WithName("Health_Check")</c> for the health endpoint).</item>
///   <item><b>(b) response $ref schema</b> — every declared response code under each operation
///         has a <c>content</c> map whose schema is a <c>$ref</c> to <c>components.schemas</c>.
///         This assertion is checked only for operations NOT in <see cref="NotYetMigratedOperations"/>
///         and NOT in <see cref="PermanentlyExemptOperations"/>.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Design rationale.</b> This test reads the GENERATED <c>openapi.json</c> in-process —
/// NOT source-level return types. A source-level proxy (e.g., "every action declares
/// a <c>Results&lt;&gt;</c> return") is exactly the failure shape the <em>AuthController
/// cautionary tale</em> warned about: a misfiring transformer or a <c>Results&lt;&gt;</c>
/// variant that ApiExplorer fails to read passes the source check and fails the user.
/// The load-bearing property is "every operation in the published doc has schemas"; only
/// a doc-level check captures it.
/// </para>
/// <para>
/// <b>Exemption categories.</b>
/// <list type="bullet">
///   <item><see cref="NotYetMigratedOperations"/> — controller actions not yet converted to
///         typed <c>Results&lt;&gt;</c> return types. This list shrinks to empty across P2/P3;
///         after P3 it must equal the empty set and can be removed.</item>
///   <item><see cref="PermanentlyExemptOperations"/> — permanently pinned exemptions.
///         Adding an entry here requires a deliberate test edit. Currently:
///         <c>Packages_DownloadVersion</c> (binary tarball stream, not a JSON DTO).</item>
/// </list>
/// </para>
/// <para>
/// <b>Enum schema assertion (P4).</b> The assertion "every enum domain appears as an OpenAPI
/// enum schema" is intentionally absent here — the seven bounded-domain enums do not exist
/// until P4. This class is extended in P4 to add that third assertion.
/// </para>
/// <para>
/// <b>Self-test (fail-path) fixture.</b> The scanner is a pure function over parsed JSON;
/// a hand-built stub operation (operationId present, schemaless 200) is fed to it in-class
/// without using a real controller. This avoids polluting the registry doc with fixture
/// controllers.
/// </para>
/// </remarks>
public sealed class OpenApiCoverageMetaTests
{
    // ─── Exemption lists ─────────────────────────────────────────────────────

    /// <summary>
    /// Controller actions not yet converted to typed <c>Results&lt;&gt;</c>. Every entry
    /// here is "work in progress" — this list shrinks to empty across P2 and P3.
    /// </summary>
    /// <remarks>
    /// Format: <c>{ControllerBaseName}_{ActionName}</c> matching the operationId convention.
    /// Adding an entry here is intentional tech-debt; removing an entry is the P2/P3 migration.
    /// After P3 this set should be empty and the assertion that checks it no longer needs exemptions.
    /// </remarks>
    private static readonly IReadOnlySet<string> NotYetMigratedOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        // Auth controller — migrated in P2 (all 8 actions removed from this list)
        // Packages controller — migrated in P2 (all 12 actions removed from this list)
        // Organizations controller — migrated in P3 (all 7 actions removed from this list)
        // Scopes controller — migrated in P3 (all 4 actions removed from this list)
        // Search controller — migrated in P3 (1 action removed from this list)
        // Admin controller — migrated in P3 (all 6 actions removed from this list)
    };

    /// <summary>
    /// Permanently pinned exemptions. Adding a new entry here requires a deliberate test edit,
    /// forcing reviewer attention — mirroring the <c>PinnedImperativeActions</c> pattern in
    /// <c>AuthzDispatchCoverageMetaTests</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> PermanentlyExemptOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        // PackagesController.DownloadVersion returns a binary tarball stream
        // (application/octet-stream + X-Integrity header), not a JSON DTO.
        // Typed Results<> over a body schema does not apply.
        "Packages_DownloadVersion",
    };

    // ─── Scanner (pure function over parsed JSON) ────────────────────────────

    /// <summary>
    /// A parsed OpenAPI operation, extracted from the fetched document for scanning.
    /// </summary>
    private sealed class ParsedOperation
    {
        public string OperationId { get; init; } = string.Empty;
        /// <summary>
        /// For each declared response code, whether the content map contains a $ref schema.
        /// Key = status code string (e.g. "200"), value = true when a $ref was found.
        /// If the response has no content (e.g. 204 No Content), it is not included.
        /// </summary>
        public Dictionary<string, bool> ResponseCodeHasRefSchema { get; init; } = new();
    }

    /// <summary>
    /// Extracts all operations from a parsed OpenAPI document as <see cref="ParsedOperation"/>
    /// instances. This is the scanner's input surface; the scanner logic itself never touches
    /// JSON directly.
    /// </summary>
    private static List<ParsedOperation> ExtractOperations(JsonDocument doc)
    {
        var result = new List<ParsedOperation>();
        var root = doc.RootElement;

        if (!root.TryGetProperty("paths", out var paths))
            return result;

        foreach (var pathProp in paths.EnumerateObject())
        {
            foreach (var methodProp in pathProp.Value.EnumerateObject())
            {
                var op = methodProp.Value;
                string operationId = op.TryGetProperty("operationId", out var opId)
                    ? (opId.GetString() ?? string.Empty)
                    : string.Empty;

                var responseCodes = new Dictionary<string, bool>();
                if (op.TryGetProperty("responses", out var responses))
                {
                    foreach (var respProp in responses.EnumerateObject())
                    {
                        var resp = respProp.Value;
                        if (!resp.TryGetProperty("content", out var content))
                            continue;

                        bool hasRef = false;
                        foreach (var ctProp in content.EnumerateObject())
                        {
                            if (ctProp.Value.TryGetProperty("schema", out var schema))
                            {
                                string schemaRaw = schema.GetRawText();
                                if (schemaRaw.Contains("\"$ref\""))
                                {
                                    hasRef = true;
                                    break;
                                }
                            }
                        }

                        responseCodes[respProp.Name] = hasRef;
                    }
                }

                result.Add(new ParsedOperation
                {
                    OperationId = operationId,
                    ResponseCodeHasRefSchema = responseCodes,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the list of violations detected by the coverage scanner on the supplied operations.
    /// </summary>
    private static List<string> FindViolations(
        IEnumerable<ParsedOperation> operations,
        IReadOnlySet<string> notYetMigrated,
        IReadOnlySet<string> permanentlyExempt)
    {
        var violations = new List<string>();

        foreach (var op in operations)
        {
            // (a) operationId must always be present — no exemption.
            if (string.IsNullOrEmpty(op.OperationId))
            {
                violations.Add("MISSING_OPERATIONID: (no operationId)");
                continue;
            }

            // (b) Response $ref schema — checked only for non-exempt operations.
            bool isExempt = notYetMigrated.Contains(op.OperationId)
                         || permanentlyExempt.Contains(op.OperationId);
            if (!isExempt)
            {
                foreach (var (code, hasRef) in op.ResponseCodeHasRefSchema)
                {
                    if (!hasRef)
                    {
                        violations.Add($"{op.OperationId}: response {code} has no $ref schema");
                    }
                }
            }
        }

        return violations;
    }

    // ─── Binding floor + fetch helper ────────────────────────────────────────

    private const int MinOperations = 30;

    private static async Task<JsonDocument> FetchOpenApiDocAsync()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client = ctx.Factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    // ─── Assertion 1: Binding floor (prevents vacuous pass) ──────────────────

    [Fact]
    public async Task OpenApiDoc_HasMoreThanMinOperations()
    {
        using var doc = await FetchOpenApiDocAsync();
        var operations = ExtractOperations(doc);

        Assert.True(
            operations.Count > MinOperations,
            $"The in-process-fetched openapi.json contains only {operations.Count} operation(s), " +
            $"which is at or below the binding floor of {MinOperations}. " +
            $"Either the endpoint is returning an error/empty doc (fetch would have returned HTTP 200 " +
            $"but the body is missing paths), or the floor needs updating. " +
            $"A vacuous pass (0 violations because 0 operations) is not acceptable.");
    }

    // ─── Assertion 2: Every operation has an operationId ─────────────────────

    [Fact]
    public async Task AllOperations_HaveOperationId()
    {
        using var doc = await FetchOpenApiDocAsync();
        var operations = ExtractOperations(doc);

        var missing = operations
            .Where(op => string.IsNullOrEmpty(op.OperationId))
            .Select(op => "(missing operationId)")
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} operation(s) are missing an operationId. " +
            $"The OpenApiOperationIdTransformer must fill every controller action, " +
            $"and minimal-API endpoints must use .WithName(). " +
            $"The operationId is not exemptible — it is the output of the Construct transformer.");
    }

    // ─── Assertion 3: NotYetMigrated set is exact (validates the hand-written list) ──

    /// <summary>
    /// Every operationId in <see cref="NotYetMigratedOperations"/> must appear in the
    /// live doc — a typo or rename in the list produces a phantom exemption that silently
    /// passes a non-existing operation. This assertion fails loudly on any such phantom.
    /// </summary>
    [Fact]
    public async Task NotYetMigratedList_ContainsNoPhantomEntries()
    {
        using var doc = await FetchOpenApiDocAsync();
        var operations = ExtractOperations(doc);
        var liveIds = new HashSet<string>(operations.Select(op => op.OperationId), StringComparer.Ordinal);

        var phantoms = NotYetMigratedOperations
            .Where(id => !liveIds.Contains(id))
            .OrderBy(id => id)
            .ToList();

        Assert.True(
            phantoms.Count == 0,
            $"The NotYetMigratedOperations list contains {phantoms.Count} operationId(s) " +
            $"that do NOT appear in the live openapi.json — phantom exemptions that could " +
            $"mask a real violation:\n{string.Join("\n", phantoms.Select(p => $"  {p}"))}\n" +
            $"Either update the list to match the actual operationIds, or remove the phantom entries.");
    }

    // ─── Assertion 4: PermanentlyExempt pin is exact ─────────────────────────

    /// <summary>
    /// The permanent-exemption set is pinned: adding or removing an entry requires a
    /// deliberate test edit (mirroring <c>AuthzDispatchCoverageMetaTests.PinnedImperativeActions</c>).
    /// </summary>
    [Fact]
    public async Task PermanentlyExemptList_ContainsNoPhantomEntries()
    {
        using var doc = await FetchOpenApiDocAsync();
        var operations = ExtractOperations(doc);
        var liveIds = new HashSet<string>(operations.Select(op => op.OperationId), StringComparer.Ordinal);

        var phantoms = PermanentlyExemptOperations
            .Where(id => !liveIds.Contains(id))
            .OrderBy(id => id)
            .ToList();

        Assert.True(
            phantoms.Count == 0,
            $"The PermanentlyExemptOperations list contains {phantoms.Count} operationId(s) " +
            $"that do NOT appear in the live openapi.json:\n" +
            $"{string.Join("\n", phantoms.Select(p => $"  {p}"))}\n" +
            $"Update the permanent pin list.");
    }

    // ─── Assertion 5: Production compliance (all exempted in P1, none in violation) ──

    [Fact]
    public async Task AllOperations_SatisfyCoverageRules()
    {
        using var doc = await FetchOpenApiDocAsync();
        var operations = ExtractOperations(doc);

        var violations = FindViolations(operations, NotYetMigratedOperations, PermanentlyExemptOperations);

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} coverage violation(s) found in the in-process-fetched openapi.json.\n" +
            $"Violations:\n{string.Join("\n", violations.Select(v => $"  {v}"))}\n\n" +
            $"To fix: convert the flagged controller action to a typed Results<...> return " +
            $"(P2/P3 migration), or add it to NotYetMigratedOperations as a documented work-item. " +
            $"operationId violations have NO exemption — the OpenApiOperationIdTransformer is the Construct.");
    }

    // ─── Assertion 6: Every bounded-domain enum appears as a named OpenAPI enum schema (P4) ──

    /// <summary>
    /// The seven bounded-domain enum types must appear as named schemas in <c>components.schemas</c>
    /// with their full lowercase value list. This assertion is added in P4 (the enum conversion phase)
    /// and ships GREEN at P4 close.
    /// </summary>
    [Fact]
    public async Task AllBoundedDomainEnums_AppearAsNamedEnumSchemas()
    {
        using var doc = await FetchOpenApiDocAsync();
        var root = doc.RootElement;

        // Each entry: (schemaName, expected lowercase wire values)
        var expectedSchemas = new (string Name, string[] Values)[]
        {
            ("PackageRoles", ["owner", "maintainer", "publisher", "reader"]),
            ("TokenScopes", ["read", "publish", "admin"]),
            ("Visibilities", ["public", "private", "internal"]),
            ("PrincipalTypes", ["user", "team", "org"]),
            ("ScopeOwnerTypes", ["user", "org", "system"]),
            ("OrgRoles", ["owner", "member"]),
            ("UserRoles", ["user", "admin"]),
        };

        var violations = new System.Collections.Generic.List<string>();

        if (!root.TryGetProperty("components", out var components) ||
            !components.TryGetProperty("schemas", out var schemas))
        {
            violations.Add("No components.schemas found in the OpenAPI document.");
            Assert.True(violations.Count == 0,
                $"Enum schema violations:\n{string.Join("\n", violations.Select(v => $"  {v}"))}");
            return;
        }

        foreach (var (name, expectedValues) in expectedSchemas)
        {
            if (!schemas.TryGetProperty(name, out var schema))
            {
                violations.Add($"{name}: schema not found in components.schemas.");
                continue;
            }

            // Check that the schema has an "enum" property (not just "type": "string")
            if (!schema.TryGetProperty("enum", out var enumProp))
            {
                // It may be wrapped under "oneOf" or "anyOf" by .NET 10 — check nested
                if (schema.TryGetProperty("oneOf", out var oneOf))
                {
                    bool foundEnum = false;
                    foreach (var variant in oneOf.EnumerateArray())
                    {
                        if (variant.TryGetProperty("enum", out enumProp)) { foundEnum = true; break; }
                    }
                    if (!foundEnum)
                    {
                        violations.Add($"{name}: schema found but has no 'enum' array (neither top-level nor in oneOf).");
                        continue;
                    }
                }
                else
                {
                    violations.Add($"{name}: schema found but has no 'enum' array.");
                    continue;
                }
            }

            // Extract enum values
            var actualValues = enumProp.EnumerateArray()
                .Select(v => v.GetString() ?? "")
                .ToArray();

            // Check all expected values are present
            foreach (var expected in expectedValues)
            {
                if (!actualValues.Contains(expected))
                    violations.Add($"{name}: missing expected enum value '{expected}'. Actual: [{string.Join(", ", actualValues)}]");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} bounded-domain enum schema violation(s) in the OpenAPI document.\n" +
            $"Violations:\n{string.Join("\n", violations.Select(v => $"  {v}"))}\n\n" +
            $"Each of the seven bounded-domain enums must appear as a named schema in " +
            $"components.schemas with a full 'enum' value list containing all lowercase wire strings. " +
            $"Ensure BoundedDomains.cs uses [JsonConverter(typeof(JsonStringEnumConverter<T>))] and " +
            $"[JsonStringEnumMemberName] on every member, and that the enum types are registered in " +
            $"the controllers' response DTOs so ApiExplorer picks them up.");
    }

    // ─── Fail-path self-test (proves the scanner has teeth) ──────────────────

    /// <summary>
    /// Proves the scanner correctly flags a synthetic operation that has an operationId
    /// but a 200 response with no $ref schema. This is the fail-path fixture, implemented
    /// as a pure-JSON stub (no real controller involved).
    /// </summary>
    [Fact]
    public void Scanner_FlagsOperation_WithSchemalessResponse()
    {
        // Build a synthetic openapi.json fragment with one operation that has an operationId
        // but a schemaless 200 response — exactly the violation the coverage test is guarding.
        const string stubJson = """
            {
              "paths": {
                "/stub": {
                  "get": {
                    "operationId": "Stub_UnmigratedAction",
                    "responses": {
                      "200": {
                        "description": "OK",
                        "content": {
                          "application/json": {
                            "schema": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        using var doc = JsonDocument.Parse(stubJson);
        var operations = ExtractOperations(doc);

        // Empty exemption sets — the stub op is not exempted.
        var emptyExemptions = new HashSet<string>(StringComparer.Ordinal);
        var violations = FindViolations(operations, emptyExemptions, emptyExemptions);

        Assert.True(
            violations.Count > 0,
            "The scanner should have flagged 'Stub_UnmigratedAction' (schemaless 200 response, not exempted), " +
            "but reported zero violations. The scanner has lost its teeth — fix it before relying on it.");

        Assert.Contains(violations, v => v.Contains("Stub_UnmigratedAction"));
    }

    /// <summary>
    /// Proves the scanner correctly flags an operation with a MISSING operationId (no exemption).
    /// </summary>
    [Fact]
    public void Scanner_FlagsOperation_WithMissingOperationId()
    {
        const string stubJson = """
            {
              "paths": {
                "/stub": {
                  "get": {
                    "responses": {
                      "200": {
                        "description": "OK",
                        "content": {
                          "application/json": {
                            "schema": { "$ref": "#/components/schemas/SomeResponse" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        using var doc = JsonDocument.Parse(stubJson);
        var operations = ExtractOperations(doc);

        var emptyExemptions = new HashSet<string>(StringComparer.Ordinal);
        var violations = FindViolations(operations, emptyExemptions, emptyExemptions);

        Assert.True(
            violations.Count > 0,
            "The scanner should have flagged a missing operationId, but reported zero violations.");
        Assert.Contains(violations, v => v.Contains("MISSING_OPERATIONID"));
    }

    /// <summary>
    /// Proves the scanner does NOT flag a properly-formed operation (has operationId + $ref schema).
    /// </summary>
    [Fact]
    public void Scanner_DoesNotFlag_WellFormedOperation()
    {
        const string stubJson = """
            {
              "paths": {
                "/health": {
                  "get": {
                    "operationId": "Health_Check",
                    "responses": {
                      "200": {
                        "description": "OK",
                        "content": {
                          "application/json": {
                            "schema": { "$ref": "#/components/schemas/HealthCheckResponse" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        using var doc = JsonDocument.Parse(stubJson);
        var operations = ExtractOperations(doc);

        var emptyExemptions = new HashSet<string>(StringComparer.Ordinal);
        var violations = FindViolations(operations, emptyExemptions, emptyExemptions);

        Assert.True(
            violations.Count == 0,
            $"The scanner should NOT flag a well-formed operation, but found violations:\n" +
            $"{string.Join("\n", violations.Select(v => $"  {v}"))}");
    }
}
