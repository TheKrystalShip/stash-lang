using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Stash.Registry.OpenApi;

// ── Custom typed result helpers for status codes that lack a body-carrying TypedResults variant ──
//
// TypedResults.Unauthorized(), TypedResults.Forbid(), and TypedResults.StatusCode(501) carry NO
// body — unlike BadRequest<T> / NotFound<T> which do.  To preserve the ErrorResponse (or other)
// wire body on these status codes while still advertising the schema to ApiExplorer (so the
// coverage meta-test sees a $ref for these response codes), we define public IResult +
// IEndpointMetadataProvider implementations here.  They must be public (not private or internal)
// because a type used in a public action's generic Results<…> return triggers CS0050 otherwise.

/// <summary>
/// Returns HTTP 401 with a JSON-serialised body, advertising the body type to ApiExplorer.
/// </summary>
public sealed class JsonUnauthorized<T> : IResult, IEndpointMetadataProvider
{
    private readonly T _body;
    public JsonUnauthorized(T body) => _body = body;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        httpContext.Response.ContentType = "application/json";
        return httpContext.Response.WriteAsJsonAsync(_body);
    }

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(T), ["application/json"]));
    }
}

/// <summary>
/// Returns HTTP 403 with a JSON-serialised body, advertising the body type to ApiExplorer.
/// </summary>
public sealed class JsonForbidden<T> : IResult, IEndpointMetadataProvider
{
    private readonly T _body;
    public JsonForbidden(T body) => _body = body;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        httpContext.Response.ContentType = "application/json";
        return httpContext.Response.WriteAsJsonAsync(_body);
    }

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(T), ["application/json"]));
    }
}

/// <summary>
/// Returns HTTP 501 with a JSON-serialised body, advertising the body type to ApiExplorer.
/// </summary>
public sealed class JsonNotImplemented<T> : IResult, IEndpointMetadataProvider
{
    private readonly T _body;
    public JsonNotImplemented(T body) => _body = body;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status501NotImplemented;
        httpContext.Response.ContentType = "application/json";
        return httpContext.Response.WriteAsJsonAsync(_body);
    }

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status501NotImplemented, typeof(T), ["application/json"]));
    }
}
