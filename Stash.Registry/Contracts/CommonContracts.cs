using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

public sealed class ErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; set; }
}

public sealed class SuccessResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;
}

public sealed class HealthCheckResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }
}
