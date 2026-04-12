using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Stash.Registry.Configuration;

namespace Stash.Registry;

public class Program
{
    private const string Version = "1.0.0";

    public static async Task Main(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg is "--version" or "-v")
            {
                Console.WriteLine($"stash-registry {Version}");
                return;
            }

            if (arg is "--help" or "-h")
            {
                Console.WriteLine($"""
                    stash-registry v{Version} — Package registry server for Stash

                    Usage: stash-registry [options]

                    Options:
                      -h, --help       Show this help message
                      -v, --version    Show version information

                    Configuration:
                      The registry reads configuration from appsettings.json (or the path
                      specified by ASPNETCORE_CONFIGURATION environment variable).

                      Key settings (under "Registry" section):
                        Server.Host          Bind address (default: 0.0.0.0)
                        Server.Port          Listen port (default: 8080)
                        Server.BasePath      URL base path (default: /)
                        Database.Type        Database type: sqlite, postgresql
                        Storage.Type         Package storage: filesystem, s3
                        Auth.Type            Authentication: local, ldap, oidc
                        Server.Tls.Enabled   Enable HTTPS with TLS certificates

                      See appsettings.json for all available options.
                    """);
                return;
            }
        }

        var remainingArgs = Array.FindAll(args, a => a is not "--help" and not "-h" and not "--version" and not "-v");
        var builder = WebApplication.CreateBuilder(remainingArgs);

        var config = builder.Configuration.GetSection("Registry").Get<RegistryConfig>() ?? new RegistryConfig();

        var startup = new Startup(config);
        startup.ConfigureServices(builder.Services);

        builder.WebHost.ConfigureKestrel(options =>
        {
            if (config.Server.Tls.Enabled && config.Server.Tls.Cert != null && config.Server.Tls.Key != null)
            {
                options.ListenAnyIP(config.Server.Port, listenOptions =>
                {
                    listenOptions.UseHttps(X509Certificate2.CreateFromPemFile(config.Server.Tls.Cert, config.Server.Tls.Key));
                });
            }
            else
            {
                options.ListenAnyIP(config.Server.Port);
            }
            options.Limits.MaxRequestBodySize = config.Security.MaxPackageSizeBytes;
        });

        var app = builder.Build();

        startup.Configure(app);

        Console.WriteLine($"Stash Registry v{Version}");
        Console.WriteLine($"Listening on http://{config.Server.Host}:{config.Server.Port}{config.Server.BasePath}");
        Console.WriteLine($"Database: {config.Database.Type} ({(config.Database.Type == "sqlite" ? config.Database.Path : "configured")})");
        Console.WriteLine($"Storage: {config.Storage.Type} ({(config.Storage.Type == "filesystem" ? config.Storage.Path : config.Storage.Bucket)})");
        Console.WriteLine($"Auth: {config.Auth.Type}");

        await app.RunAsync();
    }
}
