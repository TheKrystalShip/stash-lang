namespace Stash.Registry.Configuration;

public sealed class RegistryConfig
{
    public ServerConfig Server { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public RateLimitingConfig RateLimiting { get; set; } = new();
    public BootstrapConfig Bootstrap { get; set; } = new();
    public CorsConfig Cors { get; set; } = new();

    /// <summary>
    /// Operator-configurable download-metrics settings (IP handling mode, raw
    /// retention, rollup interval).  Bound from <c>Registry:Metrics</c>.
    /// </summary>
    public MetricsConfig Metrics { get; set; } = new();
}
