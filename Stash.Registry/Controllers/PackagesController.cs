using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Stash.Common;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Http;
using Stash.Registry.Services;
using Stash.Registry.Services.Metrics;
using Stash.Registry.Storage;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for package operations (get, publish, unpublish, download, roles, visibility).
/// </summary>
/// <remarks>
/// <para>
/// All authorization decisions are delegated to <see cref="IRegistryAuthorizer"/> (the PDP).
/// No hard-coded role or permission strings remain in this controller (P3 migration).
/// </para>
/// <para>
/// For private packages, the PDP returns <see cref="AuthzDenyReason.VisibilityHidden"/>;
/// the controller maps this to <c>404</c> (not <c>403</c>) to avoid leaking the
/// package's existence to unauthorized callers.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/packages")]
public class PackagesController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IPackageStorage _storage;
    private readonly PackageService _packageService;
    private readonly PackageRoleService _roleService;
    private readonly AuditService _auditService;
    private readonly DeprecationService _deprecationService;
    private readonly IRegistryAuthorizer _authorizer;
    private readonly IRegistryAuthzPrincipalFactory _principalFactory;
    private readonly RegistryConfig _config;
    private readonly IDownloadEventQueue _downloadEventQueue;
    private readonly IIpHasher _ipHasher;
    private readonly IDownloadMetricsStore _metricsStore;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    public PackagesController(
        IRegistryDatabase db,
        IPackageStorage storage,
        PackageService packageService,
        PackageRoleService roleService,
        AuditService auditService,
        DeprecationService deprecationService,
        IRegistryAuthorizer authorizer,
        IRegistryAuthzPrincipalFactory principalFactory,
        RegistryConfig config,
        IDownloadEventQueue downloadEventQueue,
        IIpHasher ipHasher,
        IDownloadMetricsStore metricsStore)
    {
        _db = db;
        _storage = storage;
        _packageService = packageService;
        _roleService = roleService;
        _auditService = auditService;
        _deprecationService = deprecationService;
        _authorizer = authorizer;
        _principalFactory = principalFactory;
        _config = config;
        _downloadEventQueue = downloadEventQueue;
        _ipHasher = ipHasher;
        _metricsStore = metricsStore;
    }

    // ── Read endpoints ────────────────────────────────────────────────────────

    /// <summary>
    /// Gets package metadata including all published versions.
    /// </summary>
    [PublicEndpoint("package metadata is public for public packages; visibility enforced by IRegistryAuthorizer")]
    [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]
    [HttpGet("{scope}/{name}")]
    public async Task<Results<Ok<PackageDetailResponse>, NotFound<ErrorResponse>>> GetPackage(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        List<string> allVersions = await _db.GetAllVersionsAsync(packageName);
        var versionsDict = new Dictionary<string, VersionDetailResponse>();
        foreach (string v in allVersions)
        {
            VersionRecord? vr = await _db.GetPackageVersionAsync(packageName, v);
            if (vr != null)
                versionsDict[v] = BuildVersionResponse(vr);
        }

        List<string>? keywords = package.Keywords != null
            ? JsonSerializer.Deserialize<List<string>>(package.Keywords)
            : null;

        var response = new PackageDetailResponse
        {
            Name = package.Name,
            Description = package.Description,
            License = package.License,
            Repository = package.Repository,
            Keywords = keywords ?? new List<string>(),
            Readme = package.Readme,
            Versions = versionsDict,
            Latest = package.Latest,
            CreatedAt = package.CreatedAt.ToString("o"),
            UpdatedAt = package.UpdatedAt.ToString("o"),
            Deprecated = package.Deprecated,
            DeprecationMessage = package.DeprecationMessage,
            DeprecationAlternative = package.DeprecationAlternative
        };

        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Gets metadata for a specific version of a package.
    /// </summary>
    [PublicEndpoint("version metadata is public for public packages; visibility gated by IRegistryAuthorizer")]
    [RegistryAuthorize(RegistryAction.ReadPackageVersion)]
    [HttpGet("{scope}/{name}/{version}")]
    public async Task<Results<Ok<VersionDetailResponse>, NotFound<ErrorResponse>>> GetVersion(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        VersionRecord? vr = await _db.GetPackageVersionAsync(packageName, version);
        if (vr == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        return TypedResults.Ok(BuildVersionResponse(vr));
    }

    /// <summary>
    /// Returns a paginated list of all published versions for a package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Visibility is enforced by the same PDP chokepoint as <see cref="GetPackage"/>:
    /// <c>[RegistryAuthorize(RegistryAction.ReadPackageMetadata)]</c> runs
    /// <c>AuthorizePackageReadAsync</c> before the action body executes. Anonymous callers
    /// on private or internal packages receive <c>404 Not Found</c> — never 403 — to avoid
    /// leaking the package's existence.
    /// </para>
    /// <para>
    /// Responses include <c>ETag</c>, <c>Last-Modified</c>, and <c>Cache-Control</c> headers
    /// and honor <c>If-None-Match</c> / <c>If-Modified-Since</c> conditional requests with
    /// <c>304 Not Modified</c> (RFC 7232 §4.1).
    /// </para>
    /// </remarks>
    /// <param name="scope">The package scope (e.g. <c>org</c> from <c>@org/name</c>).</param>
    /// <param name="name">The unscoped package name (e.g. <c>name</c> from <c>@org/name</c>).</param>
    /// <param name="query">Pagination parameters: <c>page</c> (default 1) and <c>pageSize</c> (default 20, max 100).</param>
    /// <returns>
    /// <c>200</c> with a <see cref="PagedResponse{T}"/> of <see cref="VersionDetailResponse"/> items,
    /// or <c>304 Not Modified</c> when the caller's conditional headers match the current state,
    /// or <c>404 Not Found</c> if the package does not exist or is not visible to the caller.
    /// </returns>
    [PublicEndpoint("version listing is public for public packages; visibility enforced by IRegistryAuthorizer")]
    [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]
    [HttpGet("{scope}/{name}/versions")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<Results<Ok<PagedResponse<VersionDetailResponse>>, NotFound<ErrorResponse>, StatusCodeHttpResult>> GetVersions(
        string scope, string name, [FromQuery] VersionsQuery query)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        SearchResult<VersionRecord> result = await _db.GetPackageVersionsAsync(packageName, query.page, query.pageSize);

        // Evaluate conditional request (ETag / Last-Modified). 304 fires after the PDP allow.
        if (ConditionalResponse.SetHeadersAndCheckNotModified(HttpContext, package.UpdatedAt, result.TotalCount))
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);

        int totalPages = (int)Math.Ceiling(result.TotalCount / (double)query.pageSize);
        var items = result.Items.Select(BuildVersionResponse).ToList();

        return TypedResults.Ok(new PagedResponse<VersionDetailResponse>
        {
            Items = items,
            TotalCount = result.TotalCount,
            Page = query.page,
            PageSize = query.pageSize,
            TotalPages = totalPages
        });
    }

    /// <summary>
    /// Returns the README for a package as a typed JSON response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns a <see cref="ReadmeResponse"/> JSON DTO — not a raw <c>text/markdown</c> body.
    /// A raw body would require a new OpenAPI schema exemption; the JSON wrapper preserves
    /// the "zero new exemptions" invariant in <c>OpenApiCoverageMetaTests</c>.
    /// </para>
    /// <para>
    /// Visibility is enforced by the same PDP chokepoint as <see cref="GetPackage"/>:
    /// <c>[RegistryAuthorize(RegistryAction.ReadPackageMetadata)]</c> runs
    /// <c>AuthorizePackageReadAsync</c> before the action body executes.  Anonymous callers
    /// on private or internal packages receive <c>404 Not Found</c> — never 403.
    /// </para>
    /// <para>
    /// When the package record has no README (null or empty <c>PackageRecord.Readme</c>),
    /// the endpoint returns <c>404 Not Found</c>.
    /// </para>
    /// <para>
    /// Responses include <c>ETag</c>, <c>Last-Modified</c>, and <c>Cache-Control</c> headers
    /// and honor <c>If-None-Match</c> / <c>If-Modified-Since</c> conditional requests with
    /// <c>304 Not Modified</c> (RFC 7232 §4.1).
    /// </para>
    /// </remarks>
    /// <param name="scope">The package scope (e.g. <c>org</c> from <c>@org/name</c>).</param>
    /// <param name="name">The unscoped package name (e.g. <c>name</c> from <c>@org/name</c>).</param>
    /// <returns>
    /// <c>200</c> with a <see cref="ReadmeResponse"/>,
    /// or <c>304 Not Modified</c> when the caller's conditional headers match the current state,
    /// or <c>404 Not Found</c> if the package does not exist, is not visible to the caller,
    /// or has no README.
    /// </returns>
    [PublicEndpoint("readme is public for public packages; visibility enforced by IRegistryAuthorizer")]
    [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]
    [HttpGet("{scope}/{name}/readme")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<Results<Ok<ReadmeResponse>, NotFound<ErrorResponse>, StatusCodeHttpResult>> GetReadme(
        string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        // A package with no README returns 404 — the resource (the readme) does not exist.
        if (string.IsNullOrEmpty(package.Readme))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' has no README." });

        string content = package.Readme;
        int byteSize = System.Text.Encoding.UTF8.GetByteCount(content);

        // Evaluate conditional request (ETag / Last-Modified). 304 fires after the PDP allow.
        if (ConditionalResponse.SetHeadersAndCheckNotModified(HttpContext, package.UpdatedAt, byteSize))
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);

        return TypedResults.Ok(new ReadmeResponse
        {
            Content = content,
            ContentType = ReadmeContentTypes.Markdown,
            ByteSize = byteSize,
            ExtractedFromVersion = package.Latest
        });
    }

    /// <summary>
    /// Returns download metrics for a package across all versions and four rolling windows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Visibility is enforced by the same PDP chokepoint as <see cref="GetPackage"/>:
    /// <c>[RegistryAuthorize(RegistryAction.ReadPackageMetadata)]</c> runs
    /// <c>AuthorizePackageReadAsync</c> before the action body executes.  Anonymous callers
    /// on private or internal packages receive <c>404 Not Found</c> — never 403 — to avoid
    /// leaking the package's existence.  The body of the 404 from the filter does NOT
    /// include the package name.
    /// </para>
    /// <para>
    /// The read model follows the D8 brief decision: closed hourly rollups are authoritative;
    /// the current open bucket (current hour) is computed from raw <c>download_events</c>
    /// and added to the closed-bucket sum to avoid double-counting.
    /// </para>
    /// </remarks>
    [PublicEndpoint("package metrics are public for public packages; visibility enforced by IRegistryAuthorizer")]
    [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]
    [HttpGet("{scope}/{name}/metrics")]
    public async Task<Results<Ok<PackageMetricsResponse>, NotFound<ErrorResponse>>> GetPackageMetrics(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        // Retrieve all versions to know which versions exist (for perVersion ordering).
        List<string> allVersions = await _db.GetAllVersionsAsync(packageName);

        DateTime now = DateTime.UtcNow;
        Dictionary<string, DownloadWindowCounts> perVersionData =
            await _metricsStore.GetPackageDownloadsAsync(packageName, now);

        // Aggregate total across all versions.
        var aggregate = new DownloadWindowCounts();
        foreach (var counts in perVersionData.Values)
        {
            aggregate.Total   += counts.Total;
            aggregate.Last24h += counts.Last24h;
            aggregate.Last7d  += counts.Last7d;
            aggregate.Last30d += counts.Last30d;
        }

        // Build per-version list — include ALL known versions (even zero-download ones).
        var perVersionList = allVersions
            .Select(v =>
            {
                if (!perVersionData.TryGetValue(v, out var c))
                    c = new DownloadWindowCounts();
                return new VersionDownloadCounts
                {
                    Version  = v,
                    Total    = c.Total,
                    Last24h  = c.Last24h,
                    Last7d   = c.Last7d,
                    Last30d  = c.Last30d,
                };
            })
            .OrderByDescending(v => v.Total)
            .ToList();

        return TypedResults.Ok(new PackageMetricsResponse
        {
            Package     = packageName,
            Downloads   = aggregate,
            PerVersion  = perVersionList,
            GeneratedAt = now,
        });
    }

    /// <summary>
    /// Returns download metrics for a specific package version across four rolling windows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Visibility is enforced by the same PDP chokepoint as <see cref="GetVersion"/>:
    /// <c>[RegistryAuthorize(RegistryAction.ReadPackageVersion)]</c> runs the PDP before
    /// the action body executes.  Anonymous callers on private or internal packages receive
    /// <c>404 Not Found</c> — never 403.
    /// </para>
    /// <para>
    /// The read model follows the D8 brief: closed hourly rollups are authoritative;
    /// the current open bucket is computed from raw events and added.
    /// </para>
    /// </remarks>
    [PublicEndpoint("version metrics are public for public packages; visibility enforced by IRegistryAuthorizer")]
    [RegistryAuthorize(RegistryAction.ReadPackageVersion)]
    [HttpGet("{scope}/{name}/{version}/metrics")]
    public async Task<Results<Ok<VersionMetricsResponse>, NotFound<ErrorResponse>>> GetVersionMetrics(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        VersionRecord? vr = await _db.GetPackageVersionAsync(packageName, version);
        if (vr == null)
            return TypedResults.NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        DateTime now = DateTime.UtcNow;
        Dictionary<string, DownloadWindowCounts> allVersionData =
            await _metricsStore.GetPackageDownloadsAsync(packageName, now);

        allVersionData.TryGetValue(version, out var counts);
        counts ??= new DownloadWindowCounts();

        return TypedResults.Ok(new VersionMetricsResponse
        {
            Package     = packageName,
            Version     = version,
            Downloads   = counts,
            GeneratedAt = now,
        });
    }

    /// <summary>
    /// Downloads the tarball for a specific package version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns a binary tarball stream (application/octet-stream + X-Integrity header).
    /// Permanently exempt from the typed Results refactor — binary streams do not have
    /// a JSON DTO schema; see <c>OpenApiCoverageMetaTests.PermanentlyExemptOperations</c>.
    /// </para>
    /// <para>
    /// A single download event is enqueued via <see cref="IDownloadEventQueue"/> using
    /// <c>Response.OnCompleted</c> — exactly when the response stream finishes writing
    /// to the client with HTTP 200. A 404 (missing version), a
    /// <c>VisibilityHidden</c> 404 (anonymous on a private package), or a mid-stream
    /// client disconnect each result in ZERO enqueued events.
    /// </para>
    /// <para>
    /// The IP is read from <c>HttpContext.Connection.RemoteIpAddress</c> here (this
    /// file is in the <c>NoMagicRemoteIpAccessMetaTests</c> permanent-exempt list for
    /// its audit reads) and immediately transformed via
    /// <see cref="IIpHasher.Apply"/>. The transformed string — never the raw IP — is
    /// placed in the <see cref="DownloadEvent"/> and persisted by the background service.
    /// </para>
    /// </remarks>
    [PublicEndpoint("tarball download is public for public packages; visibility gated by IRegistryAuthorizer")]
    [RegistryAuthorize(RegistryAction.DownloadPackageVersion)]
    [HttpGet("{scope}/{name}/{version}/download")]
    public async Task<IActionResult> DownloadVersion(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        VersionRecord? versionRecord = await _db.GetPackageVersionAsync(packageName, version);
        if (versionRecord == null)
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        Stream? stream = await _storage.RetrieveAsync(packageName, version);
        if (stream == null)
            return NotFound(new ErrorResponse { Error = "Package tarball not found in storage." });

        if (!string.IsNullOrEmpty(versionRecord.Integrity))
            Response.Headers["X-Integrity"] = versionRecord.Integrity;

        // Capture all fields needed for the download event BEFORE the OnCompleted hook
        // fires — HttpContext is in teardown at that point and must not be accessed.
        // The IP is transformed here (PackagesController.cs is in the permanent-exempt
        // list for its audit-log reads; the download-metrics read is proven compliant by
        // DownloadCaptureSemanticsTests asserting ip is populated via IIpHasher.Apply).
        string capturedPackageName = packageName;
        string capturedVersion = version;
        string? capturedIp = _ipHasher.Apply(HttpContext.Connection.RemoteIpAddress);
        string? capturedUserAgent = Request.Headers.UserAgent.Count > 0
            ? Request.Headers.UserAgent.ToString()
            : null;
        long capturedBytesServed = versionRecord.StorageBytes;
        string? capturedUser = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;

        // Register the capture callback.  OnCompleted fires after the response stream
        // has been fully written to the client with the committed status code.
        // We check RequestAborted to exclude mid-stream disconnects (the stream may
        // still reach OnCompleted with partial bytes if the OS write buffer absorbed the
        // remaining data before the disconnect was detected, so we also rely on the
        // CancellationToken to exclude those cases).
        var capturedContext = HttpContext;
        var capturedQueue = _downloadEventQueue;
        Response.OnCompleted(() =>
        {
            if (capturedContext.Response.StatusCode == StatusCodes.Status200OK &&
                !capturedContext.RequestAborted.IsCancellationRequested)
            {
                capturedQueue.Enqueue(new DownloadEvent
                {
                    PackageName = capturedPackageName,
                    Version = capturedVersion,
                    Ts = DateTime.UtcNow,
                    Ip = capturedIp,
                    UserAgent = capturedUserAgent,
                    BytesServed = capturedBytesServed,
                    RequesterUser = capturedUser,
                });
            }
            return Task.CompletedTask;
        });

        return File(stream, "application/gzip", $"{packageName.Replace('/', '-').TrimStart('@', '-')}-{version}.tgz");
    }

    // ── Write endpoints ───────────────────────────────────────────────────────

    /// <summary>
    /// Publishes a new package or a new version of an existing package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <see cref="RegistryAction.CreatePackage"/> when the package does not yet exist
    /// and <see cref="RegistryAction.PublishVersion"/> when it does.  Both decisions are
    /// made by the PDP before the tarball is parsed.
    /// </para>
    /// <para>
    /// The optional <c>X-Package-Version</c> header allows callers to assert the
    /// manifest version they intend to publish.  A disagreement with the manifest's
    /// <c>version</c> field returns 400 ManifestRouteMismatch (Q5 closure).
    /// </para>
    /// </remarks>
    [Authorize]
    [RegistryAuthorize(RegistryAction.PublishPackage)]
    [HttpPut("{scope}/{name}")]
    public async Task<Results<Created<PublishResponse>, BadRequest<ErrorResponse>, Conflict<VersionConflictResponse>>> PublishPackage(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        var principal = _principalFactory.Build(User);
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        string? clientIntegrity = Request.Headers["X-Integrity"];
        string? expectedVersion = Request.Headers["X-Package-Version"].Count > 0
            ? Request.Headers["X-Package-Version"].ToString()
            : null;
        string username = ((UserPrincipal)principal).Username;

        try
        {
            var (vr, isNewPackage) = await _packageService.PublishAsync(
                resource, Request.Body, principal, clientIntegrity, expectedVersion);

            // Audit: emit one allow entry per successful mutation
            string auditAction = isNewPackage ? AuditActions.PackageCreate : AuditActions.PackagePublish;
            await _auditService.LogMutationAllowAsync(auditAction, username, packageName, ip);

            return TypedResults.Created((string?)null, new PublishResponse
            {
                Package = vr.PackageName,
                Version = vr.Version,
                Integrity = vr.Integrity
            });
        }
        catch (ManifestRouteMismatchException ex)
        {
            return TypedResults.BadRequest(new ErrorResponse
            {
                Error = "ManifestRouteMismatch",
                Message = ex.Message
            });
        }
        catch (VersionConflictException ex)
        {
            return TypedResults.Conflict(new VersionConflictResponse { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes a specific version of a package from the registry.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.UnpublishVersion)]
    [HttpDelete("{scope}/{name}/{version}")]
    public async Task<Results<Ok<UnpublishResponse>, BadRequest<ErrorResponse>>> UnpublishVersion(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        string username = User.Identity!.Name!;
        try
        {
            await _packageService.UnpublishAsync(packageName, version, username);
            await _auditService.LogMutationAllowAsync(AuditActions.PackageUnpublish, username, packageName, ip);
            return TypedResults.Ok(new UnpublishResponse { Package = packageName, Version = version });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    // ── Deprecation endpoints ─────────────────────────────────────────────────────

    /// <summary>
    /// Marks an entire package as deprecated.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.DeprecatePackage)]
    [HttpPatch("{scope}/{name}/deprecate")]
    public async Task<Results<Ok<DeprecationResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> DeprecatePackage(string scope, string name, [FromBody] DeprecatePackageRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _db.PackageExistsAsync(packageName))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        string username = User.Identity!.Name!;
        try
        {
            await _deprecationService.DeprecatePackageAsync(packageName, request.Message, request.Alternative, username);
            await _auditService.LogMutationAllowAsync(AuditActions.PackageDeprecate, username, packageName, ip);
            return TypedResults.Ok(new DeprecationResponse { Package = packageName, Deprecated = true });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes the deprecation status from a package.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.DeprecatePackage)]
    [HttpDelete("{scope}/{name}/deprecate")]
    public async Task<Results<Ok<DeprecationResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> UndeprecatePackage(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _db.PackageExistsAsync(packageName))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        string username = User.Identity!.Name!;
        try
        {
            await _deprecationService.UndeprecatePackageAsync(packageName);
            await _auditService.LogMutationAllowAsync(AuditActions.PackageUndeprecate, username, packageName, ip);
            return TypedResults.Ok(new DeprecationResponse { Package = packageName, Deprecated = false });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Marks a specific version of a package as deprecated.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.DeprecateVersion)]
    [HttpPatch("{scope}/{name}/{version}/deprecate")]
    public async Task<Results<Ok<DeprecationResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> DeprecateVersion(string scope, string name, string version, [FromBody] DeprecateVersionRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _db.VersionExistsAsync(packageName, version))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        string username = User.Identity!.Name!;
        try
        {
            await _deprecationService.DeprecateVersionAsync(packageName, version, request.Message, username);
            await _auditService.LogMutationAllowAsync(AuditActions.VersionDeprecate, username, packageName, ip);
            return TypedResults.Ok(new DeprecationResponse { Package = packageName, Version = version, Deprecated = true });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes the deprecation status from a specific version.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.DeprecateVersion)]
    [HttpDelete("{scope}/{name}/{version}/deprecate")]
    public async Task<Results<Ok<DeprecationResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> UndeprecateVersion(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _db.VersionExistsAsync(packageName, version))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        string username = User.Identity!.Name!;
        try
        {
            await _deprecationService.UndeprecateVersionAsync(packageName, version);
            await _auditService.LogMutationAllowAsync(AuditActions.VersionUndeprecate, username, packageName, ip);
            return TypedResults.Ok(new DeprecationResponse { Package = packageName, Version = version, Deprecated = false });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    // ── Role endpoints ────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all role entries for a package.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.ListPackageRoles)]
    [HttpGet("{scope}/{name}/roles")]
    public async Task<Results<Ok<PackageRolesListResponse>, NotFound<ErrorResponse>>> GetRoles(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        List<PackageRoleEntry> roles = await _db.GetPackageRolesAsync(packageName);
        return TypedResults.Ok(new PackageRolesListResponse
        {
            Package = packageName,
            Roles = roles.Select(r => new PackageRoleResponse
            {
                PrincipalType = r.PrincipalType,
                PrincipalId = r.PrincipalId,
                Role = r.Role
            }).ToList()
        });
    }

    /// <summary>
    /// Assigns a role to a principal on a package.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.AssignPackageRole)]
    [HttpPut("{scope}/{name}/roles")]
    public async Task<Results<Ok<SuccessResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> AssignRole(string scope, string name, [FromBody] AssignRoleRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Validation is now handled by the deserializer (JsonStringEnumConverter) — invalid values return 400 before reaching here.
        // The request.PrincipalType and request.Role are already the correct enum values if deserialization succeeded.
        string username = User.Identity!.Name!;
        await _db.AssignPackageRoleAsync(packageName, request.PrincipalType.ToWire(), request.PrincipalId, request.Role.ToWire());
        await _auditService.LogRoleMutationAllowAsync("role.assign", username, packageName, request.PrincipalId, ip);
        return TypedResults.Ok(new SuccessResponse());
    }

    /// <summary>
    /// Revokes the role of a principal on a package (owner self-service).
    /// </summary>
    /// <remarks>
    /// Returns 204 on success, 404 for missing package or missing role assignment,
    /// 409 if the revoke would drop the owner count to zero.
    /// </remarks>
    [Authorize]
    [RegistryAuthorize(RegistryAction.RevokePackageRole)]
    [HttpDelete("{scope}/{name}/roles")]
    public async Task<Results<NoContent, NotFound<ErrorResponse>, Conflict<ErrorResponse>, BadRequest<ErrorResponse>>> RevokeRole(string scope, string name, [FromBody] RevokeRoleRequest request)
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
            await _auditService.LogRoleMutationAllowAsync("role.revoke", username, packageName, request.PrincipalId, ip);
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

    // ── Visibility endpoint ───────────────────────────────────────────────────

    /// <summary>
    /// Changes the visibility of a package.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.ChangePackageVisibility)]
    [HttpPatch("{scope}/{name}/visibility")]
    public async Task<Results<Ok<SetVisibilityResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> SetVisibility(string scope, string name, [FromBody] SetVisibilityRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return TypedResults.NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Validation is handled by JsonStringEnumConverter — invalid values return 400 before reaching here.
        string username = User.Identity!.Name!;
        await _db.SetPackageVisibilityAsync(packageName, request.Visibility.ToWire());
        await _auditService.LogMutationAllowAsync("package.visibility_change", username, packageName, ip);
        return TypedResults.Ok(new SetVisibilityResponse { Package = packageName, Visibility = request.Visibility });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static VersionDetailResponse BuildVersionResponse(VersionRecord vr)
    {
        Dictionary<string, object>? deps = null;
        if (vr.Dependencies != null)
            deps = JsonSerializer.Deserialize<Dictionary<string, object>>(vr.Dependencies);

        return new VersionDetailResponse
        {
            Version = vr.Version,
            StashVersion = vr.StashVersion,
            Dependencies = deps ?? new Dictionary<string, object>(),
            Integrity = vr.Integrity,
            PublishedAt = vr.PublishedAt.ToString("o"),
            PublishedBy = vr.PublishedBy,
            Deprecated = vr.Deprecated,
            DeprecationMessage = vr.DeprecationMessage
        };
    }
}
