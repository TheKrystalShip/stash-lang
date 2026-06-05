using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Stash.Registry.Services.Metrics;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for administrative operations.
/// </summary>
/// <remarks>
/// <para>
/// All endpoints in this controller require a JWT with the <c>admin</c> role
/// and an admin token ceiling, enforced per-endpoint by
/// <see cref="IRegistryAuthorizer"/> with the admin-ceiling-first check. The
/// class-level <c>[Authorize]</c> only requires authentication; the PDP makes
/// the authorization decision.
/// </para>
/// </remarks>
[Authorize]
[ApiController]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IAuthProvider _authProvider;
    private readonly AuditService _auditService;
    private readonly PackageRoleService _roleService;
    private readonly RegistryConfig _config;
    private readonly IDownloadMetricsStore _metricsStore;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    public AdminController(
        IRegistryDatabase db,
        IAuthProvider authProvider,
        AuditService auditService,
        PackageRoleService roleService,
        RegistryConfig config,
        IDownloadMetricsStore metricsStore)
    {
        _db = db;
        _authProvider = authProvider;
        _auditService = auditService;
        _roleService = roleService;
        _config = config;
        _metricsStore = metricsStore;
    }

    /// <summary>
    /// Returns high-level registry statistics including storage bytes, download totals,
    /// and recent publish/unpublish/deprecation activity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Download totals follow the D8 read-model contract: closed hourly rollups are
    /// authoritative; the current open bucket is computed from raw <c>download_events</c>
    /// and added.
    /// </para>
    /// <para>
    /// Activity counts are derived from audit-log entries with <c>decision = "allow"</c>
    /// over a rolling 24-hour window.  Publish counts include both <c>package.create</c>
    /// (first publish) and <c>package.publish</c> (subsequent versions).  Deprecation
    /// counts sum <c>package.deprecate</c> and <c>version.deprecate</c>.
    /// </para>
    /// </remarks>
    [RegistryAuthorize(RegistryAction.ReadAdminStats)]
    [HttpGet("stats")]
    public async Task<Ok<StatsResponse>> GetStats()
    {
        DateTime now     = DateTime.UtcNow;
        DateTime since24h = now - TimeSpan.FromHours(24);

        int  users        = (await _db.ListUsersAsync()).Count;
        int  packages     = await _db.CountAllPackagesAsync();
        int  versions     = await _db.CountAllVersionsAsync();
        long storageBytes = await _db.GetTotalStorageBytesAsync();

        var (dlTotal, dlLast24h) =
            await _metricsStore.GetRegistryDownloadTotalsAsync(now);

        int publishes = await _db.CountAuditEntriesByActionSinceAsync(
            new[] { AuditActions.PackageCreate, AuditActions.PackagePublish }, since24h);

        int unpublishes = await _db.CountAuditEntriesByActionSinceAsync(
            new[] { AuditActions.PackageUnpublish }, since24h);

        int deprecations = await _db.CountAuditEntriesByActionSinceAsync(
            new[] { AuditActions.PackageDeprecate, AuditActions.VersionDeprecate }, since24h);

        return TypedResults.Ok(new StatsResponse
        {
            Users        = users,
            Packages     = packages,
            Versions     = versions,
            StorageBytes = storageBytes,
            Downloads    = new AdminDownloadsSummary { Total = dlTotal, Last24h = dlLast24h },
            Activity     = new AdminActivitySummary
            {
                PublishesLast24h     = publishes,
                UnpublishesLast24h   = unpublishes,
                DeprecationsLast24h  = deprecations,
            },
        });
    }

    /// <summary>
    /// Creates a new user account as an administrator.
    /// </summary>
    [RegistryAuthorize(RegistryAction.ManageUser)]
    [HttpPost("users")]
    public async Task<Results<Created<CreateUserResponse>, BadRequest<ErrorResponse>, Conflict<ErrorResponse>>> CreateUser([FromBody] CreateUserRequest request)
    {
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        string newUsername = request.Username!.Trim();
        string password = request.Password!;
        UserRoles newRole = request.Role ?? UserRoles.User;

        try
        {
            await _authProvider.CreateUserAsync(newUsername, password);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(new ErrorResponse { Error = ex.Message });
        }

        if (newRole == UserRoles.Admin)
            await _db.UpdateUserRoleAsync(newUsername, UserRoles.Admin.ToWire());

        await _auditService.LogUserCreateAsync(newUsername, ip);

        return TypedResults.Created((string?)null, new CreateUserResponse { Username = newUsername, Role = newRole });
    }

    /// <summary>
    /// Deletes a user account and its associated data.
    /// </summary>
    [RegistryAuthorize(RegistryAction.ManageUser)]
    [HttpDelete("users/{username}")]
    public async Task<Results<Ok<SuccessResponse>, NotFound<ErrorResponse>>> DeleteUser(string username)
    {
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        string decodedUsername = Uri.UnescapeDataString(username);
        UserRecord? user = await _db.GetUserAsync(decodedUsername);
        if (user == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"User '{decodedUsername}' not found." });

        await _db.DeleteUserAsync(decodedUsername);

        string actingUser = User.Identity!.Name!;
        await _auditService.LogUserDisableAsync(actingUser, decodedUsername, ip);

        return TypedResults.Ok(new SuccessResponse());
    }

    /// <summary>
    /// Assigns a role to a principal on a package (admin override).
    /// </summary>
    [RegistryAuthorize(RegistryAction.AdminAssignPackageRole)]
    [HttpPut("packages/{scope}/{name}/roles")]
    public async Task<Results<Ok<SuccessResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>>> AdminAssignRole(string scope, string name, [FromBody] AssignRoleRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;
        string username = User.Identity!.Name!;

        if (!await _db.PackageExistsAsync(packageName))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Validation is handled by JsonStringEnumConverter — invalid values return 400 before reaching here.
        await _db.AssignPackageRoleAsync(packageName, request.PrincipalType.ToWire(), request.PrincipalId, request.Role.ToWire());
        await _auditService.LogRoleMutationAllowAsync(AuditActions.RoleAssign, username, packageName, request.PrincipalId, ip);

        return TypedResults.Ok(new SuccessResponse());
    }

    /// <summary>
    /// Revokes the role of a principal on a package (admin override).
    /// </summary>
    /// <remarks>
    /// Returns 204 on success, 404 for missing package or missing role,
    /// 409 if the revoke would drop the owner count to zero.
    /// Unlike the owner-gated endpoint this uses <c>AdminRevokePackageRole</c> action
    /// so the PDP applies the admin short-circuit.
    /// </remarks>
    [RegistryAuthorize(RegistryAction.AdminRevokePackageRole)]
    [HttpDelete("packages/{scope}/{name}/roles")]
    public async Task<Results<NoContent, NotFound<ErrorResponse>, Conflict<ErrorResponse>, BadRequest<ErrorResponse>>> AdminRevokeRole(string scope, string name, [FromBody] RevokeRoleRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        string username = User.Identity!.Name!;

        try
        {
            await _roleService.RevokeRoleAsync(packageName, request.PrincipalType.ToWire(), request.PrincipalId);
            await _auditService.LogRoleMutationAllowAsync(AuditActions.RoleRevoke, username, packageName, request.PrincipalId, ip);
            return TypedResults.NoContent();
        }
        catch (RoleNotFoundException)
        {
            return TypedResults.NotFound(new ErrorResponse { Error = $"Not found: Principal '{request.PrincipalType.ToWire()}:{request.PrincipalId}' holds no role on '{packageName}'." });
        }
        catch (LastOwnerException ex)
        {
            return TypedResults.Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Returns a paginated list of audit log entries.
    /// The response uses the shared <see cref="PagedResponse{T}"/> envelope with collection key <c>"items"</c>.
    /// </summary>
    [RegistryAuthorize(RegistryAction.ReadAuditLog)]
    [HttpGet("audit-log")]
    public async Task<Ok<PagedResponse<AuditEntryResponse>>> GetAuditLog([FromQuery] AuditLogQuery query)
    {
        var result = await _auditService.GetAuditLogAsync(
            query.page, query.pageSize,
            query.package, query.action,
            query.user, query.target, query.version,
            query.ip, query.from, query.to);
        int totalPages = (int)Math.Ceiling(result.TotalCount / (double)query.pageSize);

        return TypedResults.Ok(new PagedResponse<AuditEntryResponse>
        {
            Items = result.Items.Select(e => new AuditEntryResponse
            {
                Action = e.Action,
                Package = e.Package,
                Version = e.Version,
                User = e.User,
                Target = e.Target,
                Ip = e.Ip,
                Timestamp = e.Timestamp,
                Decision = e.Decision,
                DenyReason = e.DenyReason
            }).ToList(),
            TotalCount = result.TotalCount,
            Page = query.page,
            PageSize = query.pageSize,
            TotalPages = totalPages
        });
    }

    // ── Audit export ─────────────────────────────────────────────────────────────

    // RFC-4180 CSV column order (stable A4 shape; previousHash/entryHash added in A6).
    private static readonly string[] CsvColumns =
    [
        "action", "package", "version", "user", "target", "ip", "timestamp", "decision", "denyReason"
    ];

    /// <summary>
    /// Streams the full filtered audit log in either JSONL (<c>application/x-ndjson</c>) or
    /// CSV (<c>text/csv</c>) format.  Honours all filter parameters from <see cref="AuditExportQuery"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Export is not paginated — it streams the full filtered set.  The <c>?format</c> parameter
    /// is required; an absent or unknown value returns <c>400 InvalidRequest</c>.
    /// </para>
    /// <para>
    /// The IP column carries the already-transformed stored value (export never reveals a raw IP
    /// that was stored hashed, because <see cref="AuditService"/> applied the transform at write time).
    /// </para>
    /// </remarks>
    [RegistryAuthorize(RegistryAction.ReadAuditLog)]
    [HttpGet("audit-log/export")]
    public async Task ExportAuditLog([FromQuery] AuditExportQuery query)
    {
        // [ApiController]'s ModelStateInvalidFilter handles the 400 for invalid/missing format
        // before reaching this body — we only run when ModelState is valid.
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        AuditExportFormat format = query.format!.Value;

        if (format == AuditExportFormat.Jsonl)
        {
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/x-ndjson";
            await Response.StartAsync();

            var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
            await foreach (var entry in _auditService.StreamAuditLogAsync(
                query.package, query.action,
                query.user, query.target, query.version,
                query.ip, query.from, query.to))
            {
                var dto = MapToResponse(entry);
                string line = JsonSerializer.Serialize(dto, jsonOptions);
                await Response.WriteAsync(line + "\n");
            }
        }
        else // AuditExportFormat.Csv
        {
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/csv";
            await Response.StartAsync();

            // Header row
            await Response.WriteAsync(string.Join(",", CsvColumns) + "\r\n");

            await foreach (var entry in _auditService.StreamAuditLogAsync(
                query.package, query.action,
                query.user, query.target, query.version,
                query.ip, query.from, query.to))
            {
                var dto = MapToResponse(entry);
                string row = ToCsvRow(dto);
                await Response.WriteAsync(row + "\r\n");
            }
        }
    }

    /// <summary>Maps an <see cref="AuditEntry"/> entity to its wire DTO, reusing the same
    /// projection used by the <see cref="GetAuditLog"/> list endpoint.</summary>
    private static AuditEntryResponse MapToResponse(AuditEntry e) => new()
    {
        Action     = e.Action,
        Package    = e.Package,
        Version    = e.Version,
        User       = e.User,
        Target     = e.Target,
        Ip         = e.Ip,
        Timestamp  = e.Timestamp,
        Decision   = e.Decision,
        DenyReason = e.DenyReason,
    };

    /// <summary>
    /// Serializes an <see cref="AuditEntryResponse"/> to a single RFC-4180 CSV data row.
    /// Fields are double-quoted when they contain a comma, double-quote, or line ending;
    /// embedded double-quotes are escaped by doubling them.
    /// </summary>
    private static string ToCsvRow(AuditEntryResponse dto)
    {
        return string.Join(",", new[]
        {
            CsvField(dto.Action),
            CsvField(dto.Package),
            CsvField(dto.Version),
            CsvField(dto.User),
            CsvField(dto.Target),
            CsvField(dto.Ip),
            CsvField(dto.Timestamp.ToString("o", CultureInfo.InvariantCulture)),
            CsvField(dto.Decision),
            CsvField(dto.DenyReason),
        });
    }

    /// <summary>
    /// Returns the RFC-4180 representation of a single field value.
    /// <c>null</c> → empty string (no quotes).  Values containing a comma, double-quote,
    /// CR, or LF are wrapped in double-quotes with internal quotes doubled.
    /// </summary>
    private static string CsvField(string? value)
    {
        if (value == null)
            return "";
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Returns the top packages by download count over a configurable rolling window.
    /// Packages are ordered descending by download count for the requested window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the same read-model contract as the per-package metrics endpoints: closed
    /// hourly rollups are authoritative; the current open bucket is computed from raw
    /// <c>download_events</c> and added to avoid double-counting.
    /// </para>
    /// <para>
    /// Requires an <c>admin</c>-ceiling JWT with an admin token ceiling and admin role.
    /// The class-level <c>[Authorize]</c> handles authentication; this action carries
    /// <c>[RegistryAuthorize(RegistryAction.ReadAdminStats)]</c> for PDP dispatch.
    /// </para>
    /// </remarks>
    [RegistryAuthorize(RegistryAction.ReadAdminStats)]
    [HttpGet("metrics/downloads")]
    public async Task<Ok<PagedResponse<TopPackageDownloadsEntry>>> GetDownloadsMetrics([FromQuery] TopPackagesQuery query)
    {
        DateTime now = DateTime.UtcNow;
        var (entries, totalCount) = await _metricsStore.GetTopPackagesAsync(
            query.windowDays, query.page, query.pageSize, now);

        int totalPages = (int)Math.Ceiling(totalCount / (double)query.pageSize);

        return TypedResults.Ok(new PagedResponse<TopPackageDownloadsEntry>
        {
            Items      = entries,
            TotalCount = totalCount,
            Page       = query.page,
            PageSize   = query.pageSize,
            TotalPages = totalPages,
        });
    }
}
