using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Services;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// MVC authorization filter that performs the registry's full PDP dispatch for every
/// endpoint decorated with <see cref="RegistryAuthorizeAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// Execution order per request:
/// <list type="number">
///   <item><description>Resolve scoped services (<see cref="IRegistryAuthorizer"/>,
///   <see cref="AuditService"/>, <see cref="IRegistryAuthzPrincipalFactory"/>) from
///   <c>HttpContext.RequestServices</c> — not from the constructor — to stay within the
///   per-request DI scope and avoid capturing a <c>RegistryDbContext</c> across
///   requests.</description></item>
///   <item><description>Build the typed <see cref="Principal"/> via the shared
///   factory.</description></item>
///   <item><description>Resolve the <see cref="ResourceRef"/> from route values using
///   <see cref="RegistryActionResourceResolver"/>.</description></item>
///   <item><description>Call the PDP via
///   <see cref="IRegistryAuthorizer.AuthorizeAsync"/>.</description></item>
///   <item><description>On allow: return (let the action run).</description></item>
///   <item><description>On deny: audit (authenticated denials only), then set
///   <c>context.Result</c> via <see cref="AuthzDenyResponse.For"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Lifetime note:</b> this class is constructed per-request by
/// <see cref="RegistryAuthorizeAttribute"/> via <c>ActivatorUtilities.CreateInstance</c>
/// inside the per-request DI scope.  It must NOT be reused across requests
/// (<see cref="RegistryAuthorizeAttribute.IsReusable"/> returns <see langword="false"/>).
/// </para>
/// </remarks>
public sealed class RegistryAuthorizeFilter : IAsyncAuthorizationFilter
{
    private readonly RegistryAction _action;

    /// <summary>
    /// Initialises the filter for the specified <paramref name="action"/>.
    /// </summary>
    /// <param name="action">
    /// The registry action that this filter will authorize.
    /// Supplied by <see cref="RegistryAuthorizeAttribute.CreateInstance"/> via
    /// <c>ActivatorUtilities.CreateInstance</c>.
    /// </param>
    public RegistryAuthorizeFilter(RegistryAction action)
    {
        _action = action;
    }

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 1. Resolve scoped services per-request to avoid cross-request DbContext capture.
        var services    = context.HttpContext.RequestServices;
        var authorizer  = services.GetRequiredService<IRegistryAuthorizer>();
        var auditSvc    = services.GetRequiredService<AuditService>();
        var factory     = services.GetRequiredService<IRegistryAuthzPrincipalFactory>();

        // 2. Build the typed principal from the current HTTP user.
        var principal = factory.Build(context.HttpContext.User);

        // 3. Resolve the resource from route values (pure — no DB, no body).
        //    context.RouteData.Values is the authoritative route dictionary in MVC filter
        //    pipeline (set before authorization filters run).
        var routeValues = context.RouteData.Values;
        ResourceRef resource = RegistryActionResourceResolver.Resolve(_action, routeValues, context.HttpContext);

        // 4. Call the PDP.
        AuthzDecision decision = await authorizer.AuthorizeAsync(principal, _action, resource);

        // 5. Allow → let the action execute.
        if (decision.Allowed)
            return;

        // 6. Deny.
        //    a. Audit authenticated denials (anonymous skipped — no username to log).
        if (principal is UserPrincipal up)
        {
            string? ip = context.HttpContext.Connection.RemoteIpAddress?.ToString();
            string resourceId = ResourceIdForAudit(resource);
            await auditSvc.LogAuthzDenyAsync(_action.ToString(), up.Username, resourceId, decision.Reason, ip);
        }

        //    b. Build the 404 not-found message for package resources.
        //       Version-route denials restore the pre-refactor version-scoped message;
        //       package-level route denials use the package-scoped message.
        string? notFoundMessage = null;
        if (resource is PackageResource pkg &&
            (decision.Reason == AuthzDenyReason.VisibilityHidden ||
             decision.Reason == AuthzDenyReason.PackageNotFound))
        {
            notFoundMessage = pkg.Version != null
                ? $"Version '{pkg.Version}' of package '{pkg.FullName}' not found."
                : $"Package '{pkg.FullName}' not found.";
        }

        //    c. Set the unified deny result.
        context.Result = AuthzDenyResponse.For(decision, notFoundMessage);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a human-readable resource identifier for audit log entries.
    /// Mirrors the resource-id strings used in today's per-controller PDP blocks.
    /// </summary>
    private static string ResourceIdForAudit(ResourceRef resource) => resource switch
    {
        PackageResource pkg   => pkg.FullName,
        ScopeResource   scope => scope.Scope,
        OrgResource     org   => org.OrgName,
        TokenResource   tok   => tok.TokenId,
        _                     => string.Empty
    };
}
