using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;
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
        var b = _config.Bootstrap;
        if (string.IsNullOrEmpty(b.AdminPasswordEnv))
        {
            return; // disabled
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
            if (existing.Role == "admin")
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
    }
}
