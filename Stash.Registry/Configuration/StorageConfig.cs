namespace Stash.Registry.Configuration;

public sealed class StorageConfig
{
    public string Type { get; set; } = "filesystem";
    public string Path { get; set; } = "data/packages";
    public string? Bucket { get; set; }
    public string? Region { get; set; }
    public string? Endpoint { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}
