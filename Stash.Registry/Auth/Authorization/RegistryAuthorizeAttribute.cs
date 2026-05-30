using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Declarative authorization attribute for registry controller actions.
/// </summary>
/// <remarks>
/// <para>
/// Attach this attribute to any controller action that must consult the registry PDP.
/// The attribute is composed with the existing <c>[Authorize]</c> attribute — it does
/// NOT replace it (authentication still relies on <c>[Authorize]</c>).
/// </para>
/// <para>
/// At runtime the attribute acts as an <see cref="IFilterFactory"/> that constructs a
/// per-request <see cref="RegistryAuthorizeFilter"/> inside the current DI scope via
/// <see cref="ActivatorUtilities.CreateInstance{T}"/>, avoiding the singleton-captures-
/// scoped-service trap inherent in constructor-injected attributes.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Authorize]
/// [RegistryAuthorize(RegistryAction.Whoami)]
/// [HttpGet("whoami")]
/// public IActionResult Whoami() { /* actual work — no PDP block here */ }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RegistryAuthorizeAttribute : Attribute, IFilterFactory
{
    /// <summary>The registry action this endpoint performs.</summary>
    public RegistryAction Action { get; }

    /// <summary>
    /// Initialises the attribute for the specified <paramref name="action"/>.
    /// </summary>
    /// <param name="action">
    /// The <see cref="RegistryAction"/> that the decorated endpoint performs.
    /// Used by <see cref="RegistryAuthorizeFilter"/> as the PDP input.
    /// </param>
    public RegistryAuthorizeAttribute(RegistryAction action)
    {
        Action = action;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see langword="false"/>: a new <see cref="RegistryAuthorizeFilter"/> is constructed
    /// for each request so scoped services (<see cref="IRegistryAuthorizer"/>,
    /// <see cref="Services.AuditService"/>) are always resolved from the correct per-request
    /// DI scope.
    /// </remarks>
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<RegistryAuthorizeFilter>(serviceProvider, Action);
}
