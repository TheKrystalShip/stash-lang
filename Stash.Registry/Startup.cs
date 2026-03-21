using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Middleware;
using Stash.Registry.Services;
using Stash.Registry.Storage;

namespace Stash.Registry;

public sealed class Startup
{
    private readonly RegistryConfig _config;

    public Startup(RegistryConfig config)
    {
        _config = config;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();

        var jwtService = new JwtTokenService(_config);
        services.AddSingleton(jwtService);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = jwtService.GetValidationParameters();
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        string? jti = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
                        if (string.IsNullOrEmpty(jti))
                        {
                            context.Fail("Token is missing jti claim.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<IRegistryDatabase>();
                        var token = await db.GetTokenByIdAsync(jti);
                        if (token == null)
                        {
                            context.Fail("Token has been revoked.");
                            return;
                        }
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("RequirePublishScope", policy =>
                policy.RequireClaim("token_scope", "publish", "admin"))
            .AddPolicy("RequireAdminScope", policy =>
                policy.RequireClaim("token_scope", "admin"))
            .AddPolicy("RequireAdmin", policy =>
            {
                policy.RequireClaim("token_scope", "admin");
                policy.RequireRole("admin");
            });

        services.AddSingleton(_config);

        services.AddDbContext<RegistryDbContext>(options =>
        {
            switch (_config.Database.Type.ToLowerInvariant())
            {
                case "postgresql":
                case "postgres":
                    options.UseNpgsql(_config.Database.ConnectionString
                        ?? throw new InvalidOperationException("PostgreSQL connection string is required."));
                    break;
                default:
                    options.UseSqlite($"Data Source={_config.Database.Path}");
                    break;
            }
        });

        services.AddScoped<IRegistryDatabase, StashRegistryDatabase>();

        services.AddSingleton<IPackageStorage>(sp =>
        {
            return _config.Storage.Type.ToLowerInvariant() switch
            {
                "s3" => new S3Storage(
                    _config.Storage.Bucket ?? "stash-registry",
                    _config.Storage.Region ?? "us-east-1",
                    _config.Storage.Endpoint,
                    _config.Storage.AccessKey ?? "",
                    _config.Storage.SecretKey ?? ""),
                _ => new FileSystemStorage(_config.Storage.Path)
            };
        });

        services.AddScoped<IAuthProvider>(sp =>
        {
            return _config.Auth.Type.ToLowerInvariant() switch
            {
                "ldap" => new LdapAuthProvider(
                    _config.Auth.LdapServer ?? "localhost",
                    _config.Auth.LdapPort,
                    _config.Auth.LdapBaseDn ?? "",
                    _config.Auth.LdapUserFilter),
                "oidc" => new OidcAuthProvider(
                    _config.Auth.OidcAuthority ?? "",
                    _config.Auth.OidcClientId ?? ""),
                _ => new LocalAuthProvider(sp.GetRequiredService<IRegistryDatabase>())
            };
        });

        services.AddScoped<PackageService>();
        services.AddScoped<AuditService>();

        services.AddOpenApi();
    }

    public void Configure(WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            db.Initialize();
        }

        if (_config.RateLimiting.Enabled)
        {
            app.UseMiddleware<RateLimitingMiddleware>(_config.RateLimiting);
        }

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/", () => Results.Json(new HealthCheckResponse { Status = "ok", Version = "1.0.0" }));

        app.MapControllers();
    }
}
