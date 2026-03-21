using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.Cli.PackageManager;

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

internal sealed class LoginRequest
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

internal sealed class OwnerUpdateRequest
{
    [JsonPropertyName("add")]
    public string[] Add { get; set; } = [];

    [JsonPropertyName("remove")]
    public string[] Remove { get; set; } = [];
}
