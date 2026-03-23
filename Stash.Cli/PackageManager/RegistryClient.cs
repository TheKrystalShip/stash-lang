using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stash.Common;

namespace Stash.Cli.PackageManager;

/// <summary>
/// HTTP client for the Stash package registry REST API, implementing
/// <see cref="IPackageSource"/> for use with <see cref="DependencyResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// All HTTP calls are performed synchronously using <c>GetAwaiter().GetResult()</c>
/// to remain compatible with synchronous CLI entry points.
/// </para>
/// <para>
/// When a bearer <paramref name="token"/> is supplied to the constructor it is
/// attached to every request via the <c>Authorization: Bearer</c> header.
/// </para>
/// <para>
/// API surface (relative to <c>baseUrl</c>):
/// <list type="bullet">
///   <item><description><c>GET  /packages/{name}</c> — package metadata with version list</description></item>
///   <item><description><c>GET  /packages/{name}/{version}</c> — single-version metadata</description></item>
///   <item><description><c>GET  /packages/{name}/{version}/download</c> — tarball download URL</description></item>
///   <item><description><c>PUT  /packages/{name}</c> — publish a new version (tarball body)</description></item>
///   <item><description><c>DELETE /packages/{name}/{version}</c> — unpublish a version</description></item>
///   <item><description><c>GET  /search?q=…</c> — full-text package search</description></item>
///   <item><description><c>POST /auth/login</c> — obtain a bearer token</description></item>
///   <item><description><c>POST /auth/register</c> — create a new account</description></item>
///   <item><description><c>GET  /auth/whoami</c> — return the authenticated username</description></item>
///   <item><description><c>PUT  /admin/packages/{name}/owners</c> — manage package ownership</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class RegistryClient : IPackageSource
{
    /// <summary>The base URL of the registry, with any trailing slash removed.</summary>
    private readonly string _baseUrl;

    /// <summary>Optional bearer token sent with every request when non-<c>null</c>.</summary>
    private readonly string? _token;

    /// <summary>Shared <see cref="HttpClient"/> instance used for all registry requests.</summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initialises a new <see cref="RegistryClient"/> targeting the given registry URL.
    /// </summary>
    /// <param name="baseUrl">
    /// The root URL of the registry API (e.g. <c>https://registry.stash-lang.dev</c>).
    /// Trailing slashes are trimmed automatically.
    /// </param>
    /// <param name="token">
    /// An optional bearer authentication token. When provided it is sent with every
    /// HTTP request as <c>Authorization: Bearer &lt;token&gt;</c>.
    /// </param>
    public RegistryClient(string baseUrl, string? token = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _token = token;
        _http = new HttpClient();
        if (_token != null)
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
        }
    }

    // IPackageSource implementation

    /// <summary>
    /// Queries the registry for all published versions of the specified package.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /packages/{name}</c> and parses the <c>versions</c> object keys
    /// as <see cref="SemVer"/> values.
    /// </remarks>
    /// <param name="packageName">The name of the package to query.</param>
    /// <returns>
    /// A list of parsed <see cref="SemVer"/> versions, or an empty list when the
    /// package does not exist on the registry (HTTP 404).
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown for non-404 HTTP error responses.
    /// </exception>
    public List<SemVer> GetAvailableVersions(string packageName)
    {
        string url = $"{_baseUrl}/packages/{EncodePackageName(packageName)}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new List<SemVer>();
        }

        response.EnsureSuccessStatusCode();
        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);

        var versions = new List<SemVer>();
        if (doc.RootElement.TryGetProperty("versions", out var versionsObj))
        {
            foreach (var prop in versionsObj.EnumerateObject())
            {
                var sv = SemVer.Parse(prop.Name);
                if (sv != null)
                {
                    versions.Add(sv);
                }
            }
        }
        return versions;
    }

    /// <summary>
    /// Fetches metadata for a specific version of a package and returns it as a
    /// <see cref="PackageManifest"/>.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /packages/{name}/{version}</c> and maps the JSON response fields
    /// (<c>description</c>, <c>license</c>, <c>repository</c>, <c>dependencies</c>,
    /// <c>stash</c>) to the corresponding <see cref="PackageManifest"/> properties.
    /// </remarks>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The exact version to retrieve metadata for.</param>
    /// <returns>
    /// A populated <see cref="PackageManifest"/>, or <c>null</c> when the version is
    /// not found (HTTP 404).
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown for non-404 HTTP error responses.
    /// </exception>
    public PackageManifest? GetManifest(string packageName, SemVer version)
    {
        string url = $"{_baseUrl}/packages/{EncodePackageName(packageName)}/{version}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Build a manifest from the version metadata
        var manifest = new PackageManifest
        {
            Name = packageName,
            Version = version.ToString()
        };

        // Get package-level fields from the parent if available
        if (root.TryGetProperty("description", out var desc))
        {
            manifest.Description = desc.GetString();
        }

        if (root.TryGetProperty("license", out var lic))
        {
            manifest.License = lic.GetString();
        }

        if (root.TryGetProperty("repository", out var repo))
        {
            manifest.Repository = repo.GetString();
        }

        // Dependencies from version
        if (root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object)
        {
            manifest.Dependencies = new Dictionary<string, string>();
            foreach (var prop in deps.EnumerateObject())
            {
                manifest.Dependencies[prop.Name] = prop.Value.GetString() ?? "";
            }
        }

        // Stash version constraint
        if (root.TryGetProperty("stash", out var stash))
        {
            manifest.Stash = stash.GetString();
        }

        return manifest;
    }

    /// <summary>
    /// Constructs the tarball download URL for a specific package version without
    /// making a network request.
    /// </summary>
    /// <remarks>
    /// Returns a URL of the form <c>{baseUrl}/packages/{name}/{version}/download</c>.
    /// </remarks>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version whose download URL is needed.</param>
    /// <returns>The absolute download URL string.</returns>
    public string GetResolvedUrl(string packageName, SemVer version)
    {
        return $"{_baseUrl}/packages/{EncodePackageName(packageName)}/{version}/download";
    }

    /// <summary>
    /// Retrieves the integrity hash for a specific package version from the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /packages/{name}/{version}</c> and reads the <c>integrity</c>
    /// field from the response JSON.
    /// </remarks>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version whose integrity hash is requested.</param>
    /// <returns>
    /// The integrity hash string (e.g. <c>sha512-…</c>), or <c>null</c> when the
    /// version does not exist or the field is absent.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown for non-404 HTTP error responses.
    /// </exception>
    public string? GetIntegrity(string packageName, SemVer version)
    {
        string url = $"{_baseUrl}/packages/{EncodePackageName(packageName)}/{version}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("integrity", out var integrity))
        {
            return integrity.GetString();
        }

        return null;
    }

    // Registry-specific operations

    /// <summary>
    /// Authenticates with the registry and returns a bearer token on success.
    /// </summary>
    /// <remarks>
    /// Issues <c>POST /auth/login</c> with a JSON body containing
    /// <c>username</c> and <c>password</c>, then reads the <c>token</c> field from
    /// the response.
    /// </remarks>
    /// <param name="username">The account username.</param>
    /// <param name="password">The account password.</param>
    /// <returns>
    /// The bearer token string, or <c>null</c> when authentication fails
    /// (non-success HTTP status).
    /// </returns>
    public string? Login(string username, string password)
    {
        var body = JsonSerializer.Serialize(new LoginRequest { Username = username, Password = password }, CliJsonContext.Default.LoginRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync($"{_baseUrl}/auth/login", content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("token", out var token))
        {
            return token.GetString();
        }

        return null;
    }

    /// <summary>
    /// Registers a new account on the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>POST /auth/register</c> with a JSON body containing
    /// <c>username</c> and <c>password</c>.
    /// </remarks>
    /// <param name="username">The desired username for the new account.</param>
    /// <param name="password">The desired password for the new account.</param>
    /// <returns><c>true</c> when registration succeeds; <c>false</c> otherwise.</returns>
    public bool Register(string username, string password)
    {
        var body = JsonSerializer.Serialize(new LoginRequest { Username = username, Password = password }, CliJsonContext.Default.LoginRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync($"{_baseUrl}/auth/register", content).GetAwaiter().GetResult();
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Returns the username associated with the current bearer token.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /auth/whoami</c> and reads the <c>username</c> field from the
    /// response JSON.
    /// </remarks>
    /// <returns>
    /// The authenticated username, or <c>null</c> when the request fails (e.g. token
    /// is expired or missing).
    /// </returns>
    public string? Whoami()
    {
        var response = _http.GetAsync($"{_baseUrl}/auth/whoami").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("username", out var username))
        {
            return username.GetString();
        }

        return null;
    }

    /// <summary>
    /// Publishes a package to the registry by uploading a gzip-compressed tarball.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The package name is extracted from the <c>stash.json</c> file embedded within
    /// the tarball, then used to construct the target URL
    /// <c>PUT /packages/{name}</c>.
    /// </para>
    /// <para>
    /// When <paramref name="integrity"/> is provided it is sent as the
    /// <c>X-Integrity</c> request header so the registry can verify the upload.
    /// </para>
    /// </remarks>
    /// <param name="tarball">A readable stream containing the gzip-compressed package tarball.</param>
    /// <param name="integrity">
    /// An optional integrity hash string (e.g. <c>sha512-…</c>) to attach to the upload.
    /// </param>
    /// <returns><c>true</c> when the server accepts the upload.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the package name cannot be extracted from the tarball, or when the
    /// server returns a non-success response.
    /// </exception>
    public bool Publish(Stream tarball, string? integrity = null)
    {
        // We need to get the package name from the tarball to construct the URL.
        // Read the tarball into memory, extract name from stash.json
        using var ms = new MemoryStream();
        tarball.CopyTo(ms);
        byte[] tarballBytes = ms.ToArray();

        // Extract package name from tarball
        string? name = ExtractPackageName(tarballBytes);
        if (name == null)
        {
            throw new InvalidOperationException("Cannot determine package name from tarball.");
        }

        var content = new ByteArrayContent(tarballBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        if (integrity != null)
        {
            content.Headers.Add("X-Integrity", integrity);
        }

        var response = _http.PutAsync($"{_baseUrl}/packages/{EncodePackageName(name)}", content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Publish failed ({response.StatusCode}): {error}");
        }
        return true;
    }

    /// <summary>
    /// Removes a specific version of a package from the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>DELETE /packages/{name}/{version}</c>.
    /// </remarks>
    /// <param name="packageName">The name of the package to unpublish.</param>
    /// <param name="version">The version string to remove.</param>
    /// <returns><c>true</c> when the server confirms deletion.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// </exception>
    public bool Unpublish(string packageName, string version)
    {
        var response = _http.DeleteAsync($"{_baseUrl}/packages/{EncodePackageName(packageName)}/{version}").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Unpublish failed ({response.StatusCode}): {error}");
        }
        return true;
    }

    /// <summary>
    /// Searches the registry for packages matching the given query string.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /search?q={query}&amp;page={page}&amp;pageSize={pageSize}</c> and
    /// deserialises the JSON response into a <see cref="SearchResults"/> object using
    /// <see cref="CliJsonContext"/>.
    /// </remarks>
    /// <param name="query">The search query string.</param>
    /// <param name="page">The 1-based page number to retrieve (default <c>1</c>).</param>
    /// <param name="pageSize">The number of results per page (default <c>20</c>).</param>
    /// <returns>
    /// A <see cref="SearchResults"/> object with matching packages, or <c>null</c> on
    /// failure.
    /// </returns>
    public SearchResults? Search(string query, int page = 1, int pageSize = 20)
    {
        string url = $"{_baseUrl}/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.SearchResults);
    }

    /// <summary>
    /// Returns the raw JSON metadata string for a package from the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /packages/{name}</c> and returns the response body as-is.
    /// </remarks>
    /// <param name="packageName">The name of the package to retrieve info for.</param>
    /// <returns>
    /// The raw JSON string, or <c>null</c> when the package is not found (HTTP 404).
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown for non-404 HTTP error responses.
    /// </exception>
    public string? GetPackageInfo(string packageName)
    {
        string url = $"{_baseUrl}/packages/{EncodePackageName(packageName)}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Retrieves the list of owners for the specified package.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /packages/{name}</c> and reads the <c>owners</c> array from the
    /// response JSON.
    /// </remarks>
    /// <param name="packageName">The name of the package whose owners to retrieve.</param>
    /// <returns>
    /// A list of owner username strings, or <c>null</c> when the package is not
    /// found (HTTP 404).
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown for non-404 HTTP error responses.
    /// </exception>
    public List<string>? GetOwners(string packageName)
    {
        string url = $"{_baseUrl}/packages/{EncodePackageName(packageName)}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);

        var owners = new List<string>();
        if (doc.RootElement.TryGetProperty("owners", out var ownersArr))
        {
            foreach (var o in ownersArr.EnumerateArray())
            {
                string? s = o.GetString();
                if (s != null)
                {
                    owners.Add(s);
                }
            }
        }
        return owners;
    }

    /// <summary>
    /// Grants ownership of a package to an additional user.
    /// </summary>
    /// <remarks>
    /// Issues <c>PUT /admin/packages/{name}/owners</c> with an
    /// <see cref="OwnerUpdateRequest"/> body that adds <paramref name="username"/>.
    /// </remarks>
    /// <param name="packageName">The name of the package to update ownership for.</param>
    /// <param name="username">The username to add as an owner.</param>
    /// <returns><c>true</c> when the server accepts the change; <c>false</c> otherwise.</returns>
    public bool AddOwner(string packageName, string username)
    {
        var body = JsonSerializer.Serialize(new OwnerUpdateRequest { Add = [username], Remove = [] }, CliJsonContext.Default.OwnerUpdateRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PutAsync($"{_baseUrl}/admin/packages/{EncodePackageName(packageName)}/owners", content).GetAwaiter().GetResult();
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Revokes ownership of a package from a user.
    /// </summary>
    /// <remarks>
    /// Issues <c>PUT /admin/packages/{name}/owners</c> with an
    /// <see cref="OwnerUpdateRequest"/> body that removes <paramref name="username"/>.
    /// </remarks>
    /// <param name="packageName">The name of the package to update ownership for.</param>
    /// <param name="username">The username to remove from the package's owner list.</param>
    /// <returns><c>true</c> when the server accepts the change; <c>false</c> otherwise.</returns>
    public bool RemoveOwner(string packageName, string username)
    {
        var body = JsonSerializer.Serialize(new OwnerUpdateRequest { Add = [], Remove = [username] }, CliJsonContext.Default.OwnerUpdateRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PutAsync($"{_baseUrl}/admin/packages/{EncodePackageName(packageName)}/owners", content).GetAwaiter().GetResult();
        return response.IsSuccessStatusCode;
    }

    // Encode scoped package names: @scope/name → @scope%2Fname
    /// <summary>
    /// URL-encodes a package name for use in registry API path segments.
    /// </summary>
    /// <remarks>
    /// Scoped packages (e.g. <c>@scope/name</c>) have the <c>/</c> between scope and
    /// name percent-encoded as <c>%2F</c> so the entire scoped name fits in one path
    /// segment. All other names are encoded with <see cref="Uri.EscapeDataString"/>.
    /// </remarks>
    /// <param name="name">The raw package name to encode.</param>
    /// <returns>The URL-safe encoded name suitable for use in a URL path segment.</returns>
    private static string EncodePackageName(string name)
    {
        if (name.StartsWith("@") && name.Contains('/'))
        {
            int slashIdx = name.IndexOf('/');
            return name[..slashIdx] + "%2F" + name[(slashIdx + 1)..];
        }
        return Uri.EscapeDataString(name);
    }

    /// <summary>
    /// Reads the <c>stash.json</c> manifest embedded in a tarball byte array and
    /// returns the package name.
    /// </summary>
    /// <param name="tarballBytes">The raw bytes of the gzip-compressed tarball.</param>
    /// <returns>
    /// The <c>name</c> field from the embedded <c>stash.json</c>, or <c>null</c> when
    /// the manifest is absent or cannot be parsed.
    /// </returns>
    private static string? ExtractPackageName(byte[] tarballBytes)
    {
        try
        {
            using var ms = new MemoryStream(tarballBytes);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var tar = new TarReader(gz);

            while (tar.GetNextEntry() is TarEntry entry)
            {
                string entryName = entry.Name.Replace('\\', '/');
                if (entryName == "stash.json" || entryName.EndsWith("/stash.json", StringComparison.Ordinal))
                {
                    if (entry.DataStream != null)
                    {
                        using var reader = new StreamReader(entry.DataStream);
                        string json = reader.ReadToEnd();
                        var manifest = JsonSerializer.Deserialize(json, PackageManifestJsonContext.Default.PackageManifest);
                        return manifest?.Name;
                    }
                }
            }
        }
        catch
        {
            // If we can't extract, return null
        }
        return null;
    }
}

/// <summary>
/// Represents a paginated set of package search results returned by the registry
/// search endpoint.
/// </summary>
/// <remarks>
/// Deserialised from <c>GET /search?q=…</c> responses using <see cref="CliJsonContext"/>.
/// </remarks>
public sealed class SearchResults
{
    /// <summary>The list of packages matching the search query on the current page.</summary>
    [JsonPropertyName("packages")]
    public List<SearchResultPackage> Packages { get; set; } = new();

    /// <summary>The total number of packages matching the query across all pages.</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    /// <summary>The 1-based index of the current page.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>The total number of pages available for the query.</summary>
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}

/// <summary>
/// Represents a single package entry within a <see cref="SearchResults"/> response.
/// </summary>
public sealed class SearchResultPackage
{
    /// <summary>The name of the package.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>A short description of the package, or <c>null</c> if not provided.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The latest published version string, or <c>null</c> if unavailable.</summary>
    [JsonPropertyName("latest")]
    public string? Latest { get; set; }
}
