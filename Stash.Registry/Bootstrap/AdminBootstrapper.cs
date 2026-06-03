using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;

namespace Stash.Registry.Bootstrap;

public sealed class AdminBootstrapper
{
    private readonly IRegistryDatabase _db;
    private readonly RegistryConfig _config;
    private readonly ILogger<AdminBootstrapper> _logger;

    public AdminBootstrapper(IRegistryDatabase db, RegistryConfig config, ILogger<AdminBootstrapper> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        // Always seed system scopes — idempotent, independent of admin-password config.
        await _db.SeedSystemScopesAsync();
        _logger.LogDebug("System scopes (@stash, @admin) verified/seeded.");

        var b = _config.Bootstrap;
        if (string.IsNullOrEmpty(b.AdminPasswordEnv))
        {
            return; // admin bootstrap disabled
        }

        string? password = Environment.GetEnvironmentVariable(b.AdminPasswordEnv);
        if (string.IsNullOrEmpty(password))
        {
            _logger.LogDebug("Bootstrap env var '{Env}' is empty or unset; skipping admin seed.", b.AdminPasswordEnv);
            return;
        }

        string username = string.IsNullOrEmpty(b.AdminUsername) ? "admin" : b.AdminUsername;
        var existing = await _db.GetUserAsync(username);
        if (existing is not null)
        {
            if (existing.Role == UserRoles.Admin)
            {
                _logger.LogDebug("Bootstrap admin '{User}' already present, skipping.", username);
            }
            else
            {
                _logger.LogWarning(
                    "User '{User}' already exists but is not admin; bootstrap will not auto-promote.",
                    username);
            }
            return;
        }

        string hash = LocalAuthProvider.HashPasswordInternal(password);
        await _db.CreateUserAsync(username, hash, "admin");
        _logger.LogInformation(
            "Bootstrap admin '{User}' created from env var '{Env}'.",
            username,
            b.AdminPasswordEnv);
        // NEVER log the password.

        // Auto-provision @<username> personal scope for the bootstrap admin user.
        // Skip if the scope name already exists (e.g. 'admin' is already a system scope).
        bool scopeExists = await _db.ScopeExistsAsync(username);
        if (!scopeExists)
        {
            await _db.CreateScopeAsync(new Database.Models.ScopeRecord
            {
                Name = username,
                OwnerType = ScopeOwnerTypes.User,
                OwnerUsername = username,
                OwnerOrgId = null
            });
            _logger.LogDebug("Personal scope '@{User}' provisioned for bootstrap admin.", username);
        }
        else
        {
            _logger.LogDebug("Scope '@{User}' already exists; skipping personal scope provisioning for bootstrap admin.", username);
        }
    }
}
