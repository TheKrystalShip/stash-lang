namespace Stash.Registry.Configuration;

public sealed class ServerConfig
{
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8080;
    public string BasePath { get; set; } = "";
    public TlsConfig Tls { get; set; } = new();
}
