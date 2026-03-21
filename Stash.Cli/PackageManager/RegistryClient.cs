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

public sealed class RegistryClient : IPackageSource
{
    private readonly string _baseUrl;
    private readonly string? _token;
    private readonly HttpClient _http;

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

    public string GetResolvedUrl(string packageName, SemVer version)
    {
        return $"{_baseUrl}/packages/{EncodePackageName(packageName)}/{version}/download";
    }

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

    public bool Register(string username, string password)
    {
        var body = JsonSerializer.Serialize(new LoginRequest { Username = username, Password = password }, CliJsonContext.Default.LoginRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PostAsync($"{_baseUrl}/auth/register", content).GetAwaiter().GetResult();
        return response.IsSuccessStatusCode;
    }

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

    public bool AddOwner(string packageName, string username)
    {
        var body = JsonSerializer.Serialize(new OwnerUpdateRequest { Add = [username], Remove = [] }, CliJsonContext.Default.OwnerUpdateRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PutAsync($"{_baseUrl}/admin/packages/{EncodePackageName(packageName)}/owners", content).GetAwaiter().GetResult();
        return response.IsSuccessStatusCode;
    }

    public bool RemoveOwner(string packageName, string username)
    {
        var body = JsonSerializer.Serialize(new OwnerUpdateRequest { Add = [], Remove = [username] }, CliJsonContext.Default.OwnerUpdateRequest);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = _http.PutAsync($"{_baseUrl}/admin/packages/{EncodePackageName(packageName)}/owners", content).GetAwaiter().GetResult();
        return response.IsSuccessStatusCode;
    }

    // Encode scoped package names: @scope/name → @scope%2Fname
    private static string EncodePackageName(string name)
    {
        if (name.StartsWith("@") && name.Contains('/'))
        {
            int slashIdx = name.IndexOf('/');
            return name[..slashIdx] + "%2F" + name[(slashIdx + 1)..];
        }
        return Uri.EscapeDataString(name);
    }

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

public sealed class SearchResults
{
    [JsonPropertyName("packages")]
    public List<SearchResultPackage> Packages { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}

public sealed class SearchResultPackage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("latest")]
    public string? Latest { get; set; }
}
