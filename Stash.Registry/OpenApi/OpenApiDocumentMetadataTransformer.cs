using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Stash.Registry.OpenApi;

/// <summary>
/// An <see cref="IOpenApiDocumentTransformer"/> that populates document-level metadata
/// (<c>info</c>, <c>servers</c>) at request-serve time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dynamic <c>servers</c>.</b> The <c>servers</c> array is populated from the incoming HTTP
/// request at serve time, honouring <c>X-Forwarded-Proto</c> and <c>X-Forwarded-Host</c> so a
/// reverse-proxied deployment advertises its public URL, not Kestrel's internal one. When
/// <see cref="IHttpContextAccessor.HttpContext"/> is null (e.g., during the
/// <c>dotnet build</c> design-time document generation), the <c>servers</c> array is left
/// empty so the build does not throw.
/// </para>
/// </remarks>
public sealed class OpenApiDocumentMetadataTransformer : IOpenApiDocumentTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // ── Document info ────────────────────────────────────────────────────────
        document.Info = new OpenApiInfo
        {
            Title = "Stash Package Registry",
            Description = "REST API for the Stash language's package registry — packages, scopes, organizations, search, auth.",
            Version = "1.0.0",
            License = new OpenApiLicense
            {
                Name = "MIT",
            },
            Contact = new OpenApiContact
            {
                Name = "Stash project",
                Url = new System.Uri("https://github.com/cmoraru/stash-lang"),
            },
        };

        // ── Dynamic servers (request-derived, null-safe for design-time build) ──
        var httpContextAccessor = context.ApplicationServices.GetService<IHttpContextAccessor>();
        var httpContext = httpContextAccessor?.HttpContext;

        if (httpContext is not null)
        {
            var request = httpContext.Request;

            // Honour X-Forwarded-Proto / X-Forwarded-Host for reverse-proxy deployments.
            var scheme = request.Headers.TryGetValue("X-Forwarded-Proto", out var fwdProto) && !string.IsNullOrEmpty(fwdProto)
                ? (string)fwdProto!
                : request.Scheme;

            var host = request.Headers.TryGetValue("X-Forwarded-Host", out var fwdHost) && !string.IsNullOrEmpty(fwdHost)
                ? (string)fwdHost!
                : request.Host.ToString();

            var basePath = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;

            var serverUrl = $"{scheme}://{host}{basePath}";

            document.Servers = new List<OpenApiServer>
            {
                new OpenApiServer { Url = serverUrl },
            };
        }

        return Task.CompletedTask;
    }
}
