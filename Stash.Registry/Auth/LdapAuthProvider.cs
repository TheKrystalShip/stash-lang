using System;
using System.Threading.Tasks;

namespace Stash.Registry.Auth;

public sealed class LdapAuthProvider : IAuthProvider
{
    private readonly string _server;
    private readonly int _port;
    private readonly string _baseDn;
    private readonly string? _userFilter;

    public LdapAuthProvider(string server, int port, string baseDn, string? userFilter)
    {
        _server = server;
        _port = port;
        _baseDn = baseDn;
        _userFilter = userFilter;
    }

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

    public Task CreateUserAsync(string username, string password)
    {
        throw new NotSupportedException(
            "User creation is managed by the LDAP directory, not the registry.");
    }

    public Task<bool> UserExistsAsync(string username)
    {
        throw new NotSupportedException(
            "LDAP user lookup is not yet implemented.");
    }
}
