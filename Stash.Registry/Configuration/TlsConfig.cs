namespace Stash.Registry.Configuration;

public sealed class TlsConfig
{
    public bool Enabled { get; set; }
    public string? Cert { get; set; }
    public string? Key { get; set; }
}
