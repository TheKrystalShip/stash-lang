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
using Stash.Registry.Contracts;

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
///   <item><description><c>GET  /packages/{scope}/{name}</c> — package metadata with version list</description></item>
///   <item><description><c>GET  /packages/{scope}/{name}/{version}</c> — single-version metadata</description></item>
///   <item><description><c>GET  /packages/{scope}/{name}/{version}/download</c> — tarball download URL</description></item>
///   <item><description><c>PUT  /packages/{scope}/{name}</c> — publish a new version (tarball body)</description></item>
///   <item><description><c>DELETE /packages/{scope}/{name}/{version}</c> — unpublish a version</description></item>
///   <item><description><c>GET  /search?q=…</c> — full-text package search</description></item>
///   <item><description><c>POST /auth/login</c> — obtain a bearer token</description></item>
///   <item><description><c>POST /auth/register</c> — create a new account</description></item>
///   <item><description><c>GET  /auth/whoami</c> — return the authenticated username</description></item>
///   <item><description><c>PUT  /admin/packages/{scope}/{name}/roles</c> — manage package roles</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class RegistryClient : IPackageSource, IVersionLookup
{
    /// <summary>The base URL of the registry, with any trailing slash removed.</summary>
    private readonly string _baseUrl;

    /// <summary>Current access token, updated on refresh.</summary>
    private string? _token;

    /// <summary>Current refresh token, updated on refresh.</summary>
    private string? _refreshToken;

    /// <summary>UTC expiry time of the current access token.</summary>
    private DateTime? _tokenExpiresAt;

    /// <summary>Machine fingerprint for refresh token binding.</summary>
    private readonly string? _machineId;

    /// <summary>The canonical registry URL used for config updates.</summary>
    private readonly string? _registryUrl;

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
    /// <param name="refreshToken">An optional refresh token for automatic token renewal.</param>
    /// <param name="tokenExpiresAt">The UTC expiry time of the access token.</param>
    /// <param name="machineId">The machine fingerprint for refresh token binding.</param>
    /// <param name="registryUrl">The canonical registry URL for persisting refreshed tokens.</param>
    public RegistryClient(string baseUrl, string? token = null, string? refreshToken = null,
        DateTime? tokenExpiresAt = null, string? machineId = null, string? registryUrl = null)
        : this(baseUrl, new HttpClient(), token, refreshToken, tokenExpiresAt, machineId, registryUrl)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="RegistryClient"/> using a provided <see cref="HttpClient"/>
    /// instance.  Intended for unit testing with a fake message handler.
    /// </summary>
    internal RegistryClient(string baseUrl, HttpClient http, string? token = null,
        string? refreshToken = null, DateTime? tokenExpiresAt = null,
        string? machineId = null, string? registryUrl = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _token = token;
        _refreshToken = refreshToken;
        _tokenExpiresAt = tokenExpiresAt;
        _machineId = machineId;
        _registryUrl = registryUrl;
        _http = http;
        if (_token != null)
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
        }
    }

    /// <summary>
    /// Checks if the access token is expired or near expiry and attempts an automatic
    /// refresh using the stored refresh token. Updates the stored configuration on success.
    /// </summary>
    private void EnsureTokenFresh()
    {
        if (_token == null || _refreshToken == null || _machineId == null || _tokenExpiresAt == null)
        {
            return;
        }

        // Refresh if token expires within 5 minutes
        if (_tokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(5))
        {
            return;
        }

        try
        {
            string body = JsonSerializer.Serialize(new RefreshTokenRequest
            {
                RefreshToken = _refreshToken,
                AccessToken = _token,
                MachineId = _machineId
            }, CliJsonContext.Default.RefreshTokenRequest);
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = _http.PostAsync($"{_baseUrl}/auth/tokens/refresh", content).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Console.Error.WriteLine("warning: token refresh failed (refresh token expired). Run 'stash pkg login' to re-authenticate.");
                }
                else if ((int)response.StatusCode >= 500)
                {
                    Console.Error.WriteLine($"warning: could not refresh token (registry returned {(int)response.StatusCode}). Continuing with existing token.");
                }
                return;
            }

            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? newAccessToken = null;
            string? newRefreshToken = null;
            DateTime? newExpiresAt = null;
            DateTime? newRefreshTokenExpiresAt = null;

            if (root.TryGetProperty("accessToken", out var at))
            {
                newAccessToken = at.GetString();
            }
            if (root.TryGetProperty("refreshToken", out var rt))
            {
                newRefreshToken = rt.GetString();
            }
            if (root.TryGetProperty("expiresAt", out var exp))
            {
                newExpiresAt = exp.GetDateTime();
            }
            if (root.TryGetProperty("refreshTokenExpiresAt", out var rtExp))
            {
                newRefreshTokenExpiresAt = rtExp.GetDateTime();
            }

            if (newAccessToken != null && newRefreshToken != null)
            {
                _token = newAccessToken;
                _refreshToken = newRefreshToken;
                _tokenExpiresAt = newExpiresAt;

                // Update the Authorization header
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _token);

                // Persist the new tokens to disk
                if (_registryUrl != null)
                {
                    var config = UserConfig.Load();
                    config.SetToken(_registryUrl, _token, _tokenExpiresAt, _refreshToken, newRefreshTokenExpiresAt, _machineId);
                }
            }
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine("warning: could not refresh token (registry unreachable). Continuing with existing token.");
        }
        catch
        {
            // Refresh failed — the original token will be used
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
        string url = $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}";
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
    /// Fetches both the published versions list and the registry's <c>latest</c> pointer
    /// for a package in a single request.
    /// </summary>
    /// <returns>
    /// Tuple of (versions, latest). <c>latest</c> is the registry's recommended version
    /// (parsed from the response's <c>latest</c> field), or <c>null</c> when missing or
    /// unparseable. Versions list is empty when the package is not found (HTTP 404).
    /// </returns>
    /// <exception cref="HttpRequestException">Thrown for non-404 HTTP error responses.</exception>
    public (List<SemVer> Versions, SemVer? Latest) GetVersionsAndLatest(string packageName)
    {
        string url = $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (new List<SemVer>(), null);
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

        SemVer? latest = null;
        if (doc.RootElement.TryGetProperty("latest", out var latestProp) && latestProp.ValueKind == JsonValueKind.String)
        {
            latest = SemVer.Parse(latestProp.GetString()!);
        }

        return (versions, latest);
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
        string url = $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/{version}";
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
        return $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/{version}/download";
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
        string url = $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/{version}";
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

    /// <summary>
    /// Retrieves the deprecation status for a specific package version from the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /packages/{name}/{version}</c> and reads the <c>deprecated</c> and
    /// <c>deprecationMessage</c> fields from the response JSON.
    /// </remarks>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version whose deprecation status is requested.</param>
    /// <returns>
    /// A tuple of (<c>Deprecated</c>, <c>Message</c>). Returns <c>(false, null)</c> when
    /// the version is not deprecated, is not found (HTTP 404), or the field is absent.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown for non-404 HTTP error responses.
    /// </exception>
    public (bool Deprecated, string? Message) GetDeprecation(string packageName, SemVer version)
    {
        string url = $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/{version}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (false, null);
        }

        response.EnsureSuccessStatusCode();
        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        bool deprecated = root.TryGetProperty("deprecated", out var d) && d.ValueKind == JsonValueKind.True;
        string? message = null;
        if (root.TryGetProperty("deprecationMessage", out var m) && m.ValueKind == JsonValueKind.String)
        {
            message = m.GetString();
        }

        return (deprecated, message);
    }

    // Registry-specific operations

    /// <summary>
    /// Authenticates with the registry and returns the login response on success.
    /// </summary>
    /// <remarks>
    /// Issues <c>POST /auth/login</c> with the machine fingerprint header so
    /// the server can bind a refresh token to this machine.
    /// </remarks>
    public LoginResult? Login(string username, string password)
    {
        string machineId = MachineFingerprint.Generate();
        var body = JsonSerializer.Serialize(new LoginRequest { Username = username, Password = password }, CliJsonContext.Default.LoginRequest);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/auth/login")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Machine-Id", machineId);
        var response = _http.SendAsync(request).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? token = null;
        DateTime? expiresAt = null;
        string? refreshToken = null;
        DateTime? refreshTokenExpiresAt = null;

        if (root.TryGetProperty("accessToken", out var t))
        {
            token = t.GetString();
        }
        if (root.TryGetProperty("expiresAt", out var exp))
        {
            expiresAt = exp.GetDateTime();
        }
        if (root.TryGetProperty("refreshToken", out var rt))
        {
            refreshToken = rt.GetString();
        }
        if (root.TryGetProperty("refreshTokenExpiresAt", out var rtExp))
        {
            refreshTokenExpiresAt = rtExp.GetDateTime();
        }

        if (token == null)
        {
            return null;
        }

        return new LoginResult
        {
            Token = token,
            ExpiresAt = expiresAt,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = refreshTokenExpiresAt,
            MachineId = machineId
        };
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
        EnsureTokenFresh();
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
    /// Holds the fields returned by <c>GET /auth/whoami</c>.
    /// </summary>
    /// <remarks>
    /// The <c>email</c> field has been intentionally omitted: <c>WhoamiResponse</c>
    /// carries no <c>email</c>, <c>UserRecord</c> has no <c>email</c> column, and no
    /// auth provider populates one — the field was always <c>null</c>. See Decision Log.
    /// </remarks>
    public sealed record WhoamiInfo(string? Username, string? Role);

    /// <summary>
    /// Returns detailed information about the authenticated user.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Whoami"/>, this method throws on any failure so callers can
    /// surface a specific error message to the user.
    /// </remarks>
    /// <returns>A <see cref="WhoamiInfo"/> record with the user's details.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown on network failure, 401, other non-success status, or a missing
    /// <c>username</c> field in the response.
    /// </exception>
    public WhoamiInfo WhoamiDetailed()
    {
        EnsureTokenFresh();
        HttpResponseMessage response;
        try
        {
            response = _http.GetAsync($"{_baseUrl}/auth/whoami").GetAwaiter().GetResult();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to reach registry: {ex.Message}", ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException($"Not logged in. Run 'stash pkg login {_baseUrl}'.");
        }

        if (!response.IsSuccessStatusCode)
        {
            string reason = response.ReasonPhrase ?? string.Empty;
            throw new InvalidOperationException($"Registry returned {(int)response.StatusCode}: {reason}");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("username", out var usernameProp))
        {
            throw new InvalidOperationException("Registry whoami response missing 'username' field.");
        }

        string? username = usernameProp.GetString();
        string? role = root.TryGetProperty("role", out var roleProp) ? roleProp.GetString() : null;

        return new WhoamiInfo(username, role);
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
        EnsureTokenFresh();
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

        var response = _http.PutAsync($"{_baseUrl}/packages/{ScopedPackagePath(name)}", content).GetAwaiter().GetResult();
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
        EnsureTokenFresh();
        var response = _http.DeleteAsync($"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/{version}").GetAwaiter().GetResult();
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
    /// deserialises the JSON response into a <see cref="SearchResponse"/> object using
    /// <see cref="CliJsonContext"/>.
    /// </remarks>
    /// <param name="query">The search query string.</param>
    /// <param name="page">The 1-based page number to retrieve (default <c>1</c>).</param>
    /// <param name="pageSize">The number of results per page (default <c>20</c>).</param>
    /// <returns>
    /// A <see cref="SearchResponse"/> object with matching packages, or <c>null</c> on
    /// failure.
    /// </returns>
    public SearchResponse? Search(string query, int page = 1, int pageSize = 20)
    {
        string url = $"{_baseUrl}/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.SearchResponse);
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
        string url = $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates a new API token on the registry.
    /// </summary>
    public TokenCreateResponse? CreateToken(string? scope = null, string? description = null, string? expiresIn = null)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(new TokenCreateRequest
        {
            Ceiling = scope,
            Description = description,
            ExpiresIn = expiresIn
        }, CliJsonContext.Default.TokenCreateRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync($"{_baseUrl}/auth/tokens", content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Token creation failed ({response.StatusCode}): {error}");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.TokenCreateResponse);
    }

    /// <summary>
    /// Lists all API tokens for the authenticated user.
    /// </summary>
    public TokenListResponse? ListTokens()
    {
        EnsureTokenFresh();
        var response = _http.GetAsync($"{_baseUrl}/auth/tokens").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Token listing failed ({response.StatusCode}): {error}");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.TokenListResponse);
    }

    /// <summary>
    /// Revokes an API token by its ID.
    /// </summary>
    public bool RevokeToken(string tokenId)
    {
        EnsureTokenFresh();
        var response = _http.DeleteAsync($"{_baseUrl}/auth/tokens/{Uri.EscapeDataString(tokenId)}").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Token revocation failed ({response.StatusCode}): {error}");
        }
        return true;
    }

    /// <summary>
    /// Marks an entire package as deprecated on the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>PATCH /packages/{name}/deprecate</c> with a JSON body containing the
    /// deprecation message and an optional alternative package name.
    /// </remarks>
    /// <param name="packageName">The name of the package to deprecate.</param>
    /// <param name="message">A human-readable deprecation message shown to consumers.</param>
    /// <param name="alternative">
    /// An optional replacement package name to recommend to consumers.
    /// </param>
    /// <returns><c>true</c> when the server confirms the deprecation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// </exception>
    public bool DeprecatePackage(string packageName, string message, string? alternative)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new DeprecatePackageRequest { Message = message, Alternative = alternative },
            CliJsonContext.Default.DeprecatePackageRequest);
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/deprecate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var response = _http.SendAsync(request).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Deprecate package failed ({response.StatusCode}): {error}");
        }
        return true;
    }

    /// <summary>
    /// Removes the deprecation flag from an entire package on the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>DELETE /packages/{name}/deprecate</c>.
    /// </remarks>
    /// <param name="packageName">The name of the package to undeprecate.</param>
    /// <returns><c>true</c> when the server confirms the removal.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// </exception>
    public bool UndeprecatePackage(string packageName)
    {
        EnsureTokenFresh();
        var response = _http.DeleteAsync($"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/deprecate").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Undeprecate package failed ({response.StatusCode}): {error}");
        }
        return true;
    }

    /// <summary>
    /// Marks a specific version of a package as deprecated on the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>PATCH /packages/{name}/{version}/deprecate</c> with a JSON body
    /// containing the deprecation message.
    /// </remarks>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string to deprecate.</param>
    /// <param name="message">A human-readable deprecation message shown to consumers.</param>
    /// <returns><c>true</c> when the server confirms the deprecation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// </exception>
    public bool DeprecateVersion(string packageName, string version, string message)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new DeprecateVersionRequest { Message = message },
            CliJsonContext.Default.DeprecateVersionRequest);
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/{version}/deprecate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var response = _http.SendAsync(request).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Deprecate version failed ({response.StatusCode}): {error}");
        }
        return true;
    }

    /// <summary>
    /// Removes the deprecation flag from a specific version of a package on the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>DELETE /packages/{name}/{version}/deprecate</c>.
    /// </remarks>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string to undeprecate.</param>
    /// <returns><c>true</c> when the server confirms the removal.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// </exception>
    public bool UndeprecateVersion(string packageName, string version)
    {
        EnsureTokenFresh();
        var response = _http.DeleteAsync($"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/{version}/deprecate").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Undeprecate version failed ({response.StatusCode}): {error}");
        }
        return true;
    }

    // ── P2 parity additions ────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the visibility of a package on the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>PATCH /packages/{scope}/{name}/visibility</c> with a
    /// <see cref="SetVisibilityRequest"/> body. Requires a publish-ceiling token and the
    /// appropriate resource-side role.
    /// </remarks>
    /// <param name="packageName">The fully-qualified scoped package name (e.g. <c>@alice/widget</c>).</param>
    /// <param name="visibility">The new visibility value: <c>public</c>, <c>private</c>, or <c>internal</c>.</param>
    /// <returns><c>true</c> when the server accepts the change.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// The server's <c>ErrorResponse</c> message is surfaced with the brief's status prefix.
    /// </exception>
    public bool SetVisibility(string packageName, string visibility)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new SetVisibilityRequest { Visibility = visibility },
            CliJsonContext.Default.SetVisibilityRequest);
        var request = new HttpRequestMessage(new HttpMethod("PATCH"),
            $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/visibility")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var response = _http.SendAsync(request).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"set visibility of '{packageName}'");
        }
        return true;
    }

    /// <summary>
    /// Returns the role list for a package.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /packages/{scope}/{name}/roles</c>. Requires a publish-ceiling token.
    /// </remarks>
    /// <param name="packageName">The fully-qualified scoped package name.</param>
    /// <returns>
    /// A <see cref="PackageRolesListResponse"/> on success, or <c>null</c> when the
    /// package is not found (HTTP 404).
    /// </returns>
    public PackageRolesListResponse? GetRoles(string packageName)
    {
        EnsureTokenFresh();
        string url = $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/roles";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"get roles for '{packageName}'");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.PackageRolesListResponse);
    }

    /// <summary>
    /// Assigns a role to a principal on a package using the self-service publish route.
    /// </summary>
    /// <remarks>
    /// Issues <c>PUT /packages/{scope}/{name}/roles</c> with an
    /// <see cref="AssignRoleRequest"/> body. Requires a publish-ceiling token.
    /// </remarks>
    /// <param name="packageName">The fully-qualified scoped package name.</param>
    /// <param name="principalType">The principal type: <c>user</c>, <c>team</c>, or <c>org</c>.</param>
    /// <param name="principalId">The principal identifier (username, team name, or org name).</param>
    /// <param name="role">The role to assign: <c>owner</c>, <c>maintainer</c>, <c>publisher</c>, or <c>reader</c>.</param>
    /// <returns><c>true</c> when the server accepts the assignment.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// The server's <c>ErrorResponse</c> message is surfaced with the brief's status prefix.
    /// </exception>
    public bool AssignRole(string packageName, string principalType, string principalId, string role)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new AssignRoleRequest { PrincipalType = principalType, PrincipalId = principalId, Role = role },
            CliJsonContext.Default.AssignRoleRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PutAsync(
            $"{_baseUrl}/packages/{ScopedPackagePath(packageName)}/roles", content)
            .GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"assign role on '{packageName}'");
        }
        return true;
    }

    /// <summary>
    /// Revokes the role of a principal on a package using the self-service publish route.
    /// </summary>
    /// <remarks>
    /// Issues <c>DELETE /packages/{scope}/{name}/roles</c> with a
    /// <see cref="RevokeRoleRequest"/> body. Requires a publish-ceiling token.
    /// The server enforces the last-owner invariant: a revoke that would leave the
    /// package with zero owners is refused with <c>409 Conflict</c>; a principal holding
    /// no role yields <c>404</c>. Both are surfaced as exceptions.
    /// </remarks>
    /// <param name="packageName">The fully-qualified scoped package name.</param>
    /// <param name="principalType">The principal type: <c>user</c>, <c>team</c>, or <c>org</c>.</param>
    /// <param name="principalId">The principal identifier.</param>
    /// <returns><c>true</c> when the server confirms revocation (<c>204 No Content</c>).</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response — e.g. <c>404</c> (the principal
    /// holds no role) or <c>409</c> (cannot remove the last owner).
    /// The server's error message is included so the caller can surface the reason.
    /// </exception>
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
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        string error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        throw new InvalidOperationException($"Revoke role failed ({response.StatusCode}): {error}");
    }

    /// <summary>
    /// Claims a scope on the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>POST /scopes</c> with a <see cref="ClaimScopeRequest"/> body.
    /// In Verified mode the response carries <c>state=pending</c> and a DNS-TXT challenge.
    /// </remarks>
    /// <param name="scope">The bare scope name to claim (without the leading <c>@</c>).</param>
    /// <param name="ownerType">The owner type: <c>user</c> or <c>org</c>.</param>
    /// <param name="owner">The owner identifier (username or org name).</param>
    /// <returns>
    /// A <see cref="ScopeDetailResponse"/> on success (may carry a pending challenge).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// The server's <c>ErrorResponse</c> message is surfaced with the brief's status prefix.
    /// </exception>
    public ScopeDetailResponse ClaimScope(string scope, string ownerType, string owner)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new ClaimScopeRequest { Scope = scope, OwnerType = ownerType, Owner = owner },
            CliJsonContext.Default.ClaimScopeRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync($"{_baseUrl}/scopes", content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"claim scope '{scope}'");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.ScopeDetailResponse)!;
    }

    /// <summary>
    /// Retrieves scope details from the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /scopes/{scope}</c>. This is an anonymous read — no token required.
    /// </remarks>
    /// <param name="scope">The bare scope name (without the leading <c>@</c>).</param>
    /// <returns>
    /// A <see cref="ScopeDetailResponse"/> on success, or <c>null</c> when the scope is
    /// not found (HTTP 404).
    /// </returns>
    public ScopeDetailResponse? GetScope(string scope)
    {
        string url = $"{_baseUrl}/scopes/{Uri.EscapeDataString(scope)}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"get scope '{scope}'");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.ScopeDetailResponse);
    }

    /// <summary>
    /// Creates a new organization on the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>POST /orgs</c> with a <see cref="CreateOrgRequest"/> body.
    /// Requires a publish-ceiling token.
    /// </remarks>
    /// <param name="name">The unique lower-case organization name.</param>
    /// <param name="displayName">An optional human-readable display name.</param>
    /// <returns>
    /// A <see cref="CreateOrgResponse"/> on success.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// The server's <c>ErrorResponse</c> message is surfaced with the brief's status prefix.
    /// </exception>
    public CreateOrgResponse CreateOrg(string name, string? displayName = null)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new CreateOrgRequest { Name = name, DisplayName = displayName },
            CliJsonContext.Default.CreateOrgRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync($"{_baseUrl}/orgs", content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"create organization '{name}'");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.CreateOrgResponse)!;
    }

    /// <summary>
    /// Retrieves organization details from the registry.
    /// </summary>
    /// <remarks>
    /// Issues <c>GET /orgs/{org}</c>. This is an anonymous read — no token required.
    /// Returns flat metadata only; member/team lists are not available (no server read path).
    /// </remarks>
    /// <param name="org">The organization name.</param>
    /// <returns>
    /// An <see cref="OrgDetailResponse"/> on success, or <c>null</c> when the org is
    /// not found (HTTP 404).
    /// </returns>
    public OrgDetailResponse? GetOrg(string org)
    {
        string url = $"{_baseUrl}/orgs/{Uri.EscapeDataString(org)}";
        var response = _http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"get organization '{org}'");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.OrgDetailResponse);
    }

    /// <summary>
    /// Adds a member to an organization.
    /// </summary>
    /// <remarks>
    /// Issues <c>POST /orgs/{org}/members</c> with an <see cref="AddOrgMemberRequest"/> body.
    /// Requires a publish-ceiling token.
    /// </remarks>
    /// <param name="org">The organization name.</param>
    /// <param name="username">The username to add.</param>
    /// <param name="orgRole">The org role to assign: <c>owner</c> or <c>member</c>. Defaults to <c>member</c>.</param>
    /// <returns><c>true</c> when the server accepts the change.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// The server's <c>ErrorResponse</c> message is surfaced with the brief's status prefix.
    /// </exception>
    public bool AddOrgMember(string org, string username, string? orgRole = null)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new AddOrgMemberRequest { Username = username, OrgRole = orgRole },
            CliJsonContext.Default.AddOrgMemberRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync(
            $"{_baseUrl}/orgs/{Uri.EscapeDataString(org)}/members", content)
            .GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"add member '{username}' to organization '{org}'");
        }
        return true;
    }

    /// <summary>
    /// Removes a member from an organization.
    /// </summary>
    /// <remarks>
    /// Issues <c>DELETE /orgs/{org}/members/{username}</c>. Requires a publish-ceiling token.
    /// </remarks>
    /// <param name="org">The organization name.</param>
    /// <param name="username">The username to remove.</param>
    /// <returns><c>true</c> when the server confirms the removal.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// The server's <c>ErrorResponse</c> message is surfaced with the brief's status prefix.
    /// </exception>
    public bool RemoveOrgMember(string org, string username)
    {
        EnsureTokenFresh();
        var response = _http.DeleteAsync(
            $"{_baseUrl}/orgs/{Uri.EscapeDataString(org)}/members/{Uri.EscapeDataString(username)}")
            .GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"remove member '{username}' from organization '{org}'");
        }
        return true;
    }

    /// <summary>
    /// Creates a new team within an organization.
    /// </summary>
    /// <remarks>
    /// Issues <c>POST /orgs/{org}/teams</c> with a <see cref="CreateTeamRequest"/> body.
    /// Requires a publish-ceiling token.
    /// </remarks>
    /// <param name="org">The organization name.</param>
    /// <param name="team">The team name (unique within the organization).</param>
    /// <returns>
    /// A <see cref="CreateTeamResponse"/> on success.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// The server's <c>ErrorResponse</c> message is surfaced with the brief's status prefix.
    /// </exception>
    public CreateTeamResponse CreateTeam(string org, string team)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new CreateTeamRequest { Name = team },
            CliJsonContext.Default.CreateTeamRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync(
            $"{_baseUrl}/orgs/{Uri.EscapeDataString(org)}/teams", content)
            .GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"create team '{team}' in organization '{org}'");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.CreateTeamResponse)!;
    }

    /// <summary>
    /// Adds a member to a team within an organization.
    /// </summary>
    /// <remarks>
    /// Issues <c>POST /orgs/{org}/teams/{team}/members</c> with an
    /// <see cref="AddTeamMemberRequest"/> body. Requires a publish-ceiling token.
    /// </remarks>
    /// <param name="org">The organization name.</param>
    /// <param name="team">The team name.</param>
    /// <param name="username">The username to add to the team.</param>
    /// <returns><c>true</c> when the server accepts the addition.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a non-success response.
    /// The server's <c>ErrorResponse</c> message is surfaced with the brief's status prefix.
    /// </exception>
    public bool AddTeamMember(string org, string team, string username)
    {
        EnsureTokenFresh();
        string body = JsonSerializer.Serialize(
            new AddTeamMemberRequest { Username = username },
            CliJsonContext.Default.AddTeamMemberRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync(
            $"{_baseUrl}/orgs/{Uri.EscapeDataString(org)}/teams/{Uri.EscapeDataString(team)}/members",
            content)
            .GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw HandleNonSuccess(response, $"add '{username}' to team '{team}' in organization '{org}'");
        }
        return true;
    }

    // ── private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the response body, maps the HTTP status to the brief's error-prefix convention,
    /// and returns a ready-to-throw <see cref="InvalidOperationException"/> with the server's
    /// <see cref="ErrorResponse"/> message (or the raw body as fallback).
    /// </summary>
    /// <remarks>
    /// Status mapping (per Semantics §"General error mapping"):
    /// <list type="bullet">
    ///   <item><c>401</c> → "Not logged in. Run 'stash pkg login'."</item>
    ///   <item><c>403</c> → "Forbidden (&lt;server message or 'no reason'&gt;)."</item>
    ///   <item><c>404</c> → "Not found: &lt;server message or action&gt;."</item>
    ///   <item><c>409</c> → "Conflict: &lt;server message&gt;."</item>
    ///   <item>other → "Error: HTTP &lt;code&gt; — &lt;server message or status phrase&gt;."</item>
    /// </list>
    /// The server's <c>message</c> field is preferred; <c>error</c> is used as fallback.
    /// </remarks>
    /// <param name="resp">The non-success HTTP response.</param>
    /// <param name="action">A short action description used as fallback in the "Not found" prefix.</param>
    /// <returns>An <see cref="InvalidOperationException"/> ready to be thrown.</returns>
    private static InvalidOperationException HandleNonSuccess(HttpResponseMessage resp, string action)
    {
        string rawBody = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        // Try to parse the structured ErrorResponse; fall back to the raw body.
        string serverMessage = rawBody;
        try
        {
            var err = JsonSerializer.Deserialize(rawBody, CliJsonContext.Default.ErrorResponse);
            if (err != null)
            {
                serverMessage = err.Message ?? err.Error;
            }
        }
        catch (JsonException)
        {
            // Raw body is already in serverMessage.
        }

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

        return new InvalidOperationException(message);
    }

    /// <summary>
    /// Builds the two-segment URL path for a scoped package name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Splits <paramref name="name"/> (e.g. <c>@alice/widget</c>) into its bare scope
    /// and local name components using <see cref="PackageManifest.SplitScopedName"/>,
    /// then percent-encodes each segment independently and joins them with <c>/</c>.
    /// </para>
    /// <para>
    /// Example: <c>@alice/widget</c> → <c>alice/widget</c> for use in a URL path like
    /// <c>/packages/alice/widget</c>.
    /// </para>
    /// </remarks>
    /// <param name="name">A valid scoped package name of the form <c>@scope/name</c>.</param>
    /// <returns>The two-segment path string (e.g. <c>alice/widget</c>).</returns>
    private static string ScopedPackagePath(string name)
    {
        var (scope, localName) = PackageManifest.SplitScopedName(name);
        return $"{Uri.EscapeDataString(scope)}/{Uri.EscapeDataString(localName)}";
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

