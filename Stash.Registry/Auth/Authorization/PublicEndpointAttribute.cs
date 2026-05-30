using System;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Marks a controller action as intentionally public (no authentication required).
/// </summary>
/// <remarks>
/// <para>
/// Every action on every registry controller must carry either <c>[Authorize(...)]</c>
/// or <c>[PublicEndpoint("&lt;justification&gt;")]</c>. The
/// <see cref="AuthzCoverageMetaTests"/> meta-test enumerates all controller actions
/// and fails the build when any action lacks an explicit classification.
/// </para>
/// <para>
/// This attribute does not influence the ASP.NET Core authorization pipeline directly —
/// it is a declaration of intent. The meta-test reads it via reflection; the framework
/// still requires <c>[AllowAnonymous]</c> (or equivalent middleware) for unauthenticated
/// access. Replacing <c>[AllowAnonymous]</c> with this attribute is intentional: all
/// <c>[AllowAnonymous]</c> usages are removed from controllers in favour of the
/// auditable <c>[PublicEndpoint]</c> declaration, and the host-level
/// <c>AllowAnonymous</c> middleware handles the unauthenticated path.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PublicEndpointAttribute : Attribute
{
    /// <summary>
    /// Gets the human-readable justification for why this endpoint is public.
    /// </summary>
    public string Justification { get; }

    /// <summary>
    /// Initialises the attribute with a mandatory justification string.
    /// </summary>
    /// <param name="justification">
    /// A concise explanation of why this endpoint requires no authentication
    /// (e.g. <c>"login does not require a prior session"</c>).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="justification"/> is <see langword="null"/>.
    /// </exception>
    public PublicEndpointAttribute(string justification)
    {
        ArgumentNullException.ThrowIfNull(justification);
        Justification = justification;
    }
}
