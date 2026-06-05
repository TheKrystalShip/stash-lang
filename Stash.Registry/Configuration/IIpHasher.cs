using System.Net;

namespace Stash.Registry.Configuration;

/// <summary>
/// Transforms a remote IP address according to the configured
/// <see cref="IpHandlingMode"/> before persisting it in telemetry events.
/// </summary>
/// <remarks>
/// <para>
/// This is the single authoritative transform for every code path that records a
/// caller's IP address.  Inject <see cref="IIpHasher"/> by constructor and call
/// <see cref="Apply"/> — never read <c>HttpContext.Connection.RemoteIpAddress</c>
/// directly outside of <see cref="IpHasher"/>.
/// </para>
/// <para>
/// Registered in DI as a <b>singleton</b>: the HMAC key is loaded once at startup
/// and shared across all requests.
/// </para>
/// </remarks>
public interface IIpHasher
{
    /// <summary>
    /// Applies the configured IP-handling transform to <paramref name="address"/> and
    /// returns the result as a string, or <c>null</c> when the mode is
    /// <see cref="IpHandlingMode.Off"/> or <paramref name="address"/> is <c>null</c>.
    /// </summary>
    /// <param name="address">
    /// The caller's remote IP address, or <c>null</c> if unavailable.
    /// </param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><term><see cref="IpHandlingMode.Raw"/></term><description>The verbatim address string.</description></item>
    ///   <item><term><see cref="IpHandlingMode.Truncated"/></term><description>The /24 prefix for IPv4 or /64 prefix for IPv6.</description></item>
    ///   <item><term><see cref="IpHandlingMode.Hashed"/></term><description>A 32-character lowercase hex HMAC-SHA256 of the raw address string.</description></item>
    ///   <item><term><see cref="IpHandlingMode.Off"/></term><description><c>null</c>.</description></item>
    /// </list>
    /// Returns <c>null</c> when <paramref name="address"/> is <c>null</c> regardless of mode.
    /// </returns>
    string? Apply(IPAddress? address);
}
