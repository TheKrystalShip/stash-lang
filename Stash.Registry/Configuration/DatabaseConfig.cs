namespace Stash.Registry.Configuration;

public sealed class DatabaseConfig
{
    public string Type { get; set; } = "sqlite";
    public string Path { get; set; } = "data/registry.db";
    public string? ConnectionString { get; set; }
}
