using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Middleware;
using Stash.Registry.Services;
using Stash.Registry.Storage;

namespace Stash.Registry;

/// <summary>
/// Configures the Stash registry server's dependency-injection container and HTTP
/// middleware pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This class is instantiated from <c>Program.cs</c> with a fully-populated
/// <see cref="Configuration.RegistryConfig"/> loaded from the host's configuration
/// sources.  It exposes two conventional ASP.NET Core entry points:
/// <see cref="ConfigureServices"/> and <see cref="Configure"/>.
/// </para>
/// <para>
/// Authentication is JWT Bearer-based.  Every validated token is additionally
/// checked against the database to support revocation.  Storage and authentication
/// back-ends are pluggable via <see cref="Configuration.RegistryConfig.Storage"/>
/// and <see cref="Configuration.RegistryConfig.Auth"/> respectively.
/// </para>
/// </remarks>
public sealed class Startup
{
    /// <summary>
    /// The registry configuration loaded from the host environment, used to
    /// wire up database, storage, authentication, and rate-limiting services.
    /// </summary>
    private readonly RegistryConfig _config;

    /// <summary>
    /// Initialises a new <see cref="Startup"/> instance with the supplied registry
    /// configuration.
    /// </summary>
    /// <param name="config">
    /// The <see cref="RegistryConfig"/> instance that drives all service and
    /// middleware configuration choices.
    /// </param>
    public Startup(RegistryConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Registers all application services with the dependency-injection container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers (among others): MVC controllers, JWT Bearer authentication with
    /// token-revocation checking, authorisation policies, the database context
    /// (SQLite or PostgreSQL based on <see cref="Configuration.DatabaseConfig.Type"/>),
    /// the package storage back-end (file-system or S3), the authentication provider
    /// (local, LDAP, or OIDC), <see cref="Services.PackageService"/>,
    /// <see cref="Services.AuditService"/>, and the OpenAPI document generator.
    /// </para>
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to register services into.</param>
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
        services.AddScoped<DeprecationService>();

        services.AddOpenApi();
    }

    /// <summary>
    /// Configures the HTTP middleware pipeline and performs one-time database initialisation.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication"/> to configure.</param>
    /// <remarks>
    /// <para>
    /// Pipeline (in order):
    /// <list type="number">
    ///   <item><description>Database initialisation — calls <see cref="Database.IRegistryDatabase.Initialize"/> in a transient scope.</description></item>
    ///   <item><description><see cref="Middleware.RateLimitingMiddleware"/> — inserted only when <see cref="Configuration.RateLimitingConfig.Enabled"/> is <see langword="true"/>.</description></item>
    ///   <item><description>OpenAPI endpoint (<c>GET /openapi/v1.json</c>) — mapped only in the <c>Development</c> environment.</description></item>
    ///   <item><description>Routing, Authentication (JWT Bearer), Authorization.</description></item>
    ///   <item><description>Health-check endpoint at <c>GET /</c> returning a <see cref="Contracts.HealthCheckResponse"/> JSON payload.</description></item>
    ///   <item><description>MVC controller endpoints.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public void Configure(WebApplication app)
    {
        var jwtService = app.Services.GetRequiredService<JwtTokenService>();
        if (jwtService.IsKeyAutoGenerated)
        {
            var logger = app.Services.GetRequiredService<ILogger<Startup>>();
            logger.LogWarning(
                "No JWT signing key configured. Using auto-generated key (tokens will not survive restarts). "
                + "Set 'Registry:Security:JwtSigningKey' in appsettings.json for production.");
        }

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
