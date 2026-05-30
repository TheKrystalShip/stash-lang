using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Contracts;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Shared helper that converts an <see cref="AuthzDecision"/> denial into the canonical
/// HTTP response: the correct status code plus an <see cref="ErrorResponse"/> body.
/// </summary>
/// <remarks>
/// <para>
/// Status-code mapping (superset of <c>PackagesController.DenyReasonToStatus</c>):
/// <list type="bullet">
///   <item><description><see cref="AuthzDenyReason.VisibilityHidden"/> → <c>404</c></description></item>
///   <item><description><see cref="AuthzDenyReason.PackageNotFound"/> → <c>404</c></description></item>
///   <item><description><see cref="AuthzDenyReason.NotAuthenticated"/> → <c>401</c></description></item>
///   <item><description>All others → <c>403</c></description></item>
/// </list>
/// </para>
/// <para>
/// The optional <paramref name="notFoundMessage"/> overrides the default body for 404
/// responses on package endpoints (e.g. <c>"Package '@scope/name' not found."</c>).
/// When omitted the body defaults to <c>decision.Reason.ToString()</c>.
/// </para>
/// </remarks>
public static class AuthzDenyResponse
{
    /// <summary>
    /// Builds an <see cref="ObjectResult"/> that encodes the denial in the registry's
    /// canonical wire format.
    /// </summary>
    /// <param name="decision">The deny decision from the PDP. Must have <c>Allowed == false</c>.</param>
    /// <param name="notFoundMessage">
    /// When supplied, used as the <c>Error</c> field in the 404 body for
    /// <see cref="AuthzDenyReason.VisibilityHidden"/> and
    /// <see cref="AuthzDenyReason.PackageNotFound"/> denials.
    /// </param>
    /// <returns>An <see cref="ObjectResult"/> ready to be assigned to <c>context.Result</c>.</returns>
    public static ObjectResult For(AuthzDecision decision, string? notFoundMessage = null)
    {
        int status = decision.Reason switch
        {
            AuthzDenyReason.VisibilityHidden => 404,
            AuthzDenyReason.PackageNotFound  => 404,
            AuthzDenyReason.NotAuthenticated => 401,
            _                                => 403
        };

        bool isPackageNotFound = status == 404 && notFoundMessage != null;

        string errorText = isPackageNotFound
            ? notFoundMessage!
            : decision.Reason.ToString();

        // Package not-found 404 bodies set Error only — no Message — to match the
        // pre-refactor wire shape (baseline: new ErrorResponse { Error = "…" }).
        // All other deny paths retain Message = Detail for uniform deny-audit.
        var body = new ErrorResponse
        {
            Error   = errorText,
            Message = isPackageNotFound ? null : decision.Detail
        };

        return new ObjectResult(body) { StatusCode = status };
    }
}
