using System.Net;

namespace Stash.Registry.Web.Services;

/// <summary>
/// Thrown by <see cref="HttpRegistryClient"/> when the registry returns a non-success,
/// non-404 HTTP status code (e.g. 5xx server errors or unexpected 4xx responses).
/// </summary>
/// <remarks>
/// Page models catch this exception and map it to the appropriate HTTP error response:
/// <list type="bullet">
///   <item>5xx → website 502 "registry unreachable" page.</item>
///   <item>400 → website 400 with the registry's validation message bubbled up.</item>
/// </list>
/// 404 is never surfaced as a <see cref="RegistryClientException"/>; instead
/// <see cref="HttpRegistryClient"/> returns <see langword="null"/> for nullable methods.
/// </remarks>
public sealed class RegistryClientException : Exception
{
    /// <summary>
    /// The HTTP status code returned by the registry.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The machine-readable error code from the registry's <c>ErrorResponse.error</c> field,
    /// or <see langword="null"/> if the response body could not be parsed.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// The human-readable message from the registry's <c>ErrorResponse.message</c> field,
    /// or <see langword="null"/> if absent or unparseable.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Initializes a new <see cref="RegistryClientException"/> with a status code and optional registry error fields.
    /// </summary>
    public RegistryClientException(
        HttpStatusCode statusCode,
        string? errorCode = null,
        string? errorMessage = null)
        : base(BuildMessage(statusCode, errorCode, errorMessage))
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    private static string BuildMessage(HttpStatusCode statusCode, string? errorCode, string? errorMessage)
    {
        var parts = new List<string> { $"Registry returned {(int)statusCode} {statusCode}." };
        if (errorCode is not null) parts.Add($"Error: {errorCode}.");
        if (errorMessage is not null) parts.Add(errorMessage);
        return string.Join(" ", parts);
    }
}
