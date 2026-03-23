using System;
using System.Threading.Tasks;

namespace Stash.Registry.Auth;

/// <summary>
/// Stub <see cref="IAuthProvider"/> implementation for LDAP / Active Directory authentication.
/// </summary>
/// <remarks>
/// <para>
/// <b>⚠ Not yet implemented.</b> All methods throw <see cref="NotSupportedException"/>.
/// To use the registry, set <c>Registry:Auth:Type</c> to <c>"local"</c> in
/// <c>appsettings.json</c> and use <see cref="LocalAuthProvider"/> instead.
/// </para>
/// <para>
/// This provider is selected when <c>Registry:Auth:Type</c> is <c>"ldap"</c> and is
/// instantiated by <see cref="Startup.ConfigureServices"/> with the server address, port,
/// base DN, and optional user-filter expression sourced from
/// <see cref="Configuration.AuthConfig"/>.
/// </para>
/// <para>
/// <b>Implementation blocker:</b> LDAP bind operations require
/// <c>System.DirectoryServices.Protocols</c>, which is not compatible with ahead-of-time
/// (AOT) compilation. Full support will be added when the registry is built in a non-AOT
/// configuration, or authentication is delegated to a sidecar service.
/// </para>
/// </remarks>
public sealed class LdapAuthProvider : IAuthProvider
{
    /// <summary>The hostname or IP address of the LDAP/AD server.</summary>
    private readonly string _server;

    /// <summary>The TCP port on which the LDAP server is listening (typically 389 or 636 for LDAPS).</summary>
    private readonly int _port;

    /// <summary>The base Distinguished Name used as the search root for user lookups.</summary>
    private readonly string _baseDn;

    /// <summary>
    /// An optional LDAP filter expression used to locate the user entry, e.g.
    /// <c>"(&amp;(objectClass=person)(sAMAccountName={0}))"</c>. When <see langword="null"/>
    /// a default filter will be applied by the implementation.
    /// </summary>
    private readonly string? _userFilter;

    /// <summary>
    /// Initialises a new <see cref="LdapAuthProvider"/> with the supplied LDAP connection details.
    /// </summary>
    /// <param name="server">The hostname or IP address of the LDAP server.</param>
    /// <param name="port">The LDAP server port (e.g. 389 for plain LDAP, 636 for LDAPS).</param>
    /// <param name="baseDn">The base DN for user search operations.</param>
    /// <param name="userFilter">An optional LDAP filter to locate user entries; may be <see langword="null"/>.</param>
    public LdapAuthProvider(string server, int port, string baseDn, string? userFilter)
    {
        _server = server;
        _port = port;
        _baseDn = baseDn;
        _userFilter = userFilter;
    }

    /// <summary>
    /// Not implemented. Authenticating via LDAP bind is not yet supported.
    /// </summary>
    /// <param name="username">The username that would be bound against the LDAP directory.</param>
    /// <param name="password">The password that would be used for the LDAP bind operation.</param>
    /// <returns>This method never returns a value.</returns>
    /// <exception cref="NotSupportedException">
    /// Always thrown. Configure <c>Registry:Auth:Type</c> as <c>"local"</c> until this
    /// provider is implemented.
    /// </exception>
    public Task<bool> AuthenticateAsync(string username, string password)
    {
        // LDAP authentication requires System.DirectoryServices.Protocols
        // which is not AOT-compatible. This will be implemented when the
        // registry is built without AOT, or via a separate auth service.
        throw new NotSupportedException(
            $"LDAP authentication is not yet implemented. " +
            $"Configure Auth:Type as 'local' in appsettings.json. " +
            $"(Server: {_server}:{_port}, BaseDN: {_baseDn})");
    }

    /// <summary>
    /// Not implemented. User creation is managed by the external LDAP/AD directory,
    /// not by the Stash Registry.
    /// </summary>
    /// <param name="username">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <returns>This method never returns a value.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task CreateUserAsync(string username, string password)
    {
        throw new NotSupportedException(
            "User creation is managed by the LDAP directory, not the registry.");
    }

    /// <summary>
    /// Not implemented. LDAP user existence checks are not yet supported.
    /// </summary>
    /// <param name="username">Ignored.</param>
    /// <returns>This method never returns a value.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task<bool> UserExistsAsync(string username)
    {
        throw new NotSupportedException(
            "LDAP user lookup is not yet implemented.");
    }
}
