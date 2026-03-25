using System;
using System.Threading.Tasks;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Services;

public sealed class AuditService
{
    private readonly IRegistryDatabase _db;

    public AuditService(IRegistryDatabase db)
    {
        _db = db;
    }

    public async Task LogPublishAsync(string package, string version, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "publish",
            Package = package,
            Version = version,
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogUnpublishAsync(string package, string version, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "unpublish",
            Package = package,
            Version = version,
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogOwnerAddAsync(string package, string user, string target, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "owner.add",
            Package = package,
            User = user,
            Target = target,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogOwnerRemoveAsync(string package, string user, string target, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "owner.remove",
            Package = package,
            User = user,
            Target = target,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogUserCreateAsync(string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "user.create",
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogUserDisableAsync(string user, string target, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "user.disable",
            User = user,
            Target = target,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogTokenCreateAsync(string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "token.create",
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogTokenRevokeAsync(string user, string tokenId, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "token.revoke",
            User = user,
            Target = tokenId,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogPackageDeprecateAsync(string package, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "package.deprecate",
            Package = package,
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogPackageUndeprecateAsync(string package, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "package.undeprecate",
            Package = package,
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogVersionDeprecateAsync(string package, string version, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "version.deprecate",
            Package = package,
            Version = version,
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogVersionUndeprecateAsync(string package, string version, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "version.undeprecate",
            Package = package,
            Version = version,
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task<SearchResult<AuditEntry>> GetAuditLogAsync(int page, int pageSize, string? packageName = null, string? action = null)
    {
        return await _db.GetAuditLogAsync(page, pageSize, packageName, action);
    }
}
