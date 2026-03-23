using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for all JSON types used by
/// the Stash CLI package-manager subsystem.
/// </summary>
/// <remarks>
/// <para>
/// Registering types here enables AOT-safe (trimming- and NativeAOT-compatible)
/// JSON serialisation without runtime reflection. The context is consumed by
/// <see cref="RegistryClient"/> and <see cref="UserConfig"/> wherever
/// <see cref="JsonSerializer"/> is called.
/// </para>
/// <para>
/// Generation options applied to all registered types:
/// <list type="bullet">
///   <item><description>Property name matching is case-insensitive during deserialisation.</description></item>
///   <item><description>Properties with <c>null</c> values are omitted when serialising.</description></item>
///   <item><description>Output JSON is indented for human readability.</description></item>
/// </list>
/// </para>
/// </remarks>
[JsonSerializable(typeof(UserConfig))]
[JsonSerializable(typeof(RegistryEntry))]
[JsonSerializable(typeof(Dictionary<string, RegistryEntry>))]
[JsonSerializable(typeof(SearchResults))]
[JsonSerializable(typeof(SearchResultPackage))]
[JsonSerializable(typeof(List<SearchResultPackage>))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(OwnerUpdateRequest))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
internal partial class CliJsonContext : JsonSerializerContext { }

/// <summary>
/// Request body sent to <c>POST /auth/login</c> and <c>POST /auth/register</c>
/// endpoints.
/// </summary>
internal sealed class LoginRequest
{
    /// <summary>The account username.</summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    /// <summary>The account password.</summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

/// <summary>
/// Request body sent to <c>PUT /admin/packages/{name}/owners</c> to add or remove
/// package owners in a single operation.
/// </summary>
internal sealed class OwnerUpdateRequest
{
    /// <summary>Usernames to grant ownership to.</summary>
    [JsonPropertyName("add")]
    public string[] Add { get; set; } = [];

    /// <summary>Usernames to revoke ownership from.</summary>
    [JsonPropertyName("remove")]
    public string[] Remove { get; set; } = [];
}
