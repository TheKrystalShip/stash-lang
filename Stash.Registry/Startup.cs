using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Bootstrap;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Middleware;
using Stash.Registry.Services;
using Stash.Registry.OpenApi;
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
        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                // Normalize [ApiController] + [FromBody] / [FromQuery] validation failures
                // (malformed JSON, illegal enum wire values, DataAnnotations constraint failures)
                // to the same ErrorResponse shape that business-logic 400s return.  Without this
                // override the framework emits ValidationProblemDetails (RFC 7807) — a different
                // shape that contradicts the published OpenAPI contract.
                // All field-level error messages are aggregated and joined with "; " so clients
                // receive the full failure picture in one round-trip (P2: multi-field aggregation).
                options.InvalidModelStateResponseFactory = BuildInvalidModelStateResponse;
            });

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
                            context.HttpContext.Items["TokenRevoked"] = true;
                            context.Fail("Token is missing jti claim.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<IRegistryDatabase>();
                        var token = await db.GetTokenByIdAsync(jti);
                        if (token == null)
                        {
                            // Mark the request as carrying a revoked token so the uniform gate
                            // (middleware between UseAuthentication and UseAuthorization) can
                            // reject it with 401 even on [PublicEndpoint] routes.
                            context.HttpContext.Items["TokenRevoked"] = true;
                            // Stash the principal's name for the audit entry — context.User
                            // is not yet set when OnTokenValidated fires.
                            string? revokedPrincipal = context.Principal?.FindFirstValue(ClaimTypes.Name)
                                ?? context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                            context.HttpContext.Items["TokenRevokedPrincipal"] = revokedPrincipal;
                            context.Fail("Token has been revoked.");
                            return;
                        }
                    }
                };
            });

        services.AddAuthorization();

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
                    // Append a busy-wait timeout so a second concurrent writer waits for the
                    // SQLite write lock instead of failing fast with SQLITE_BUSY (which the
                    // request pipeline would surface as an HTTP 500). Microsoft.Data.Sqlite
                    // retries on SQLITE_BUSY/SQLITE_LOCKED up to the command timeout; "Default
                    // Timeout" sets that. SQLite-only — Postgres handles concurrent writers
                    // natively and is unaffected.
                    options.UseSqlite($"Data Source={_config.Database.Path};Default Timeout=30");
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
        services.AddScoped<PackageRoleService>();
        services.AddScoped<AuditService>();
        services.AddScoped<DeprecationService>();

        // P1: PDP core — register as scoped so they share the per-request DbContext.
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddScoped<ScopeChallengeService>();
        services.AddScoped<IRegistryAuthorizer, RegistryAuthorizer>();

        // Shared principal factory — Singleton (pure function, no scoped state).
        services.AddSingleton<IRegistryAuthzPrincipalFactory, RegistryAuthzPrincipalFactory>();

        services.AddHttpContextAccessor();

        services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<OpenApiOperationIdTransformer>();
            options.AddDocumentTransformer<OpenApiDocumentMetadataTransformer>();
        });
    }

    /// <summary>
    /// Builds the <see cref="BadRequestObjectResult"/> for an invalid model state, aggregating
    /// all field-bound error messages with <c>"; "</c>. Extracted as a static method so
    /// <c>InvalidModelStateFactoryTests</c> can test the aggregation logic in isolation.
    /// </summary>
    /// <remarks>
    /// If no error messages are present (empty <c>ModelState</c>), falls back to the sentinel
    /// message <c>"Request body is invalid."</c> to ensure a well-formed response even when
    /// the binder fails before populating any field errors.
    /// </remarks>
    /// <param name="ctx">The action context provided by the ASP.NET Core <c>ModelStateInvalidFilter</c>.</param>
    /// <returns>A <see cref="BadRequestObjectResult"/> wrapping an <see cref="ErrorResponse"/>.</returns>
    internal static BadRequestObjectResult BuildInvalidModelStateResponse(ActionContext ctx)
    {
        var errors = ctx.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrEmpty(m))
            .ToList();

        string message = errors.Count > 0
            ? string.Join("; ", errors)
            : "Request body is invalid.";

        return new BadRequestObjectResult(new ErrorResponse
        {
            Error = "InvalidRequest",
            Message = message,
        });
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
    ///   <item><description><see cref="Microsoft.AspNetCore.Builder.UsePathBaseExtensions.UsePathBase"/> — applied only when <see cref="Configuration.ServerConfig.BasePath"/> is non-empty.</description></item>
    ///   <item><description><see cref="Middleware.RateLimitingMiddleware"/> — inserted only when <see cref="Configuration.RateLimitingConfig.Enabled"/> is <see langword="true"/>.</description></item>
    ///   <item><description>OpenAPI endpoint (<c>GET /openapi/v1.json</c>) — public in every environment (P1: <c>IsDevelopment()</c> gate removed).</description></item>
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

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            var bootstrapLogger = app.Services.GetRequiredService<ILogger<AdminBootstrapper>>();
            var bootstrapper = new AdminBootstrapper(db, _config, bootstrapLogger);
            bootstrapper.RunAsync().GetAwaiter().GetResult();

            if (!_config.Auth.RegistrationEnabled && string.IsNullOrEmpty(_config.Bootstrap.AdminPasswordEnv))
            {
                long adminCount = db.GetAdminCountAsync().GetAwaiter().GetResult();
                if (adminCount == 0)
                {
                    var startupLogger = app.Services.GetRequiredService<ILogger<Startup>>();
                    startupLogger.LogError(
                        "Registration is disabled and no Bootstrap.AdminPasswordEnv is configured. "
                        + "The registry has no admin and no path to create one. "
                        + "Set Registry:Bootstrap:AdminPasswordEnv and the named env var, then restart.");
                }
            }
        }

        // Read BasePath from the live IConfiguration so that overrides applied
        // after Startup construction (e.g. integration test factories) are honoured.
        var rawBasePath = !string.IsNullOrEmpty(_config.Server.BasePath)
            ? _config.Server.BasePath
            : app.Configuration["Registry:Server:BasePath"];
        var basePath = BasePathValidator.Normalize(rawBasePath);
        if (!string.IsNullOrEmpty(basePath))
        {
            app.UsePathBase(basePath);
        }

        if (_config.RateLimiting.Enabled)
        {
            app.UseMiddleware<RateLimitingMiddleware>(_config.RateLimiting);
        }

        // P1: OpenAPI endpoint is public in every environment (drop the IsDevelopment() gate).
        app.MapOpenApi();

        app.UseRouting();
        app.UseAuthentication();

        // Uniform revocation gate (D22): if OnTokenValidated set the "TokenRevoked" flag,
        // reject the request with 401 before it reaches any endpoint — including [PublicEndpoint]
        // routes. This ensures a presented-but-revoked token is always denied at the auth layer
        // (before the PDP), surfacing as TokenRevoked in the audit log.
        app.Use(async (context, next) =>
        {
            // P1: The OpenAPI document is public in every environment; bypass the JTI
            // revocation check entirely for /openapi/* paths so a caller presenting a stale
            // or revoked token can still fetch the API contract.
            if (context.Request.Path.StartsWithSegments("/openapi"))
            {
                await next();
                return;
            }

            if (context.Items.ContainsKey("TokenRevoked"))
            {
                // Audit the revoked-token presentation before terminating.
                // Use the principal name stashed during OnTokenValidated (context.User is not
                // yet populated when the JWT bearer event fires and calls context.Fail()).
                string? principalId = context.Items.TryGetValue("TokenRevokedPrincipal", out var p)
                    ? p as string
                    : context.User?.Identity?.Name;
                if (!string.IsNullOrEmpty(principalId))
                {
                    try
                    {
                        var auditService = context.RequestServices.GetRequiredService<AuditService>();
                        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "";
                        await auditService.LogAuthzDenyAsync(
                            "token.revoked",
                            principalId,
                            context.Request.Path.Value ?? "",
                            Auth.Authorization.AuthzDenyReason.TokenRevoked,
                            ip);
                    }
                    catch
                    {
                        // Best-effort audit — never let audit failure block the 401.
                    }
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"TokenRevoked\",\"message\":\"The presented token has been revoked.\"}");
                return;
            }
            await next();
        });

        app.UseAuthorization();

        // P1: Typed result so the health endpoint carries an operationId and a $ref schema
        // in the generated OpenAPI document. TypedResults.Ok<T> makes ApiExplorer emit the
        // component schema; .WithName gives it a stable operationId.
        app.MapGet("/", () => TypedResults.Ok(new HealthCheckResponse { Status = "ok", Version = "1.0.0" }))
            .WithName("Health_Check");

        app.MapControllers();
    }
}
