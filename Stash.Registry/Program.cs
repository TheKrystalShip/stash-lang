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
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

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

        Console.WriteLine($"Stash Registry v1.0.0");
        Console.WriteLine($"Listening on http://{config.Server.Host}:{config.Server.Port}{config.Server.BasePath}");
        Console.WriteLine($"Database: {config.Database.Type} ({(config.Database.Type == "sqlite" ? config.Database.Path : "configured")})");
        Console.WriteLine($"Storage: {config.Storage.Type} ({(config.Storage.Type == "filesystem" ? config.Storage.Path : config.Storage.Bucket)})");
        Console.WriteLine($"Auth: {config.Auth.Type}");

        await app.RunAsync();
    }
}
