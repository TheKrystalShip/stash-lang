using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Registry.Auth;
using Stash.Registry.Bootstrap;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;

namespace Stash.Tests.Registry;

public sealed class AdminBootstrapTests : IDisposable
{
    private readonly RegistryDbContext _context;
    private readonly StashRegistryDatabase _db;

    public AdminBootstrapTests()
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new RegistryDbContext(options);
        _context.Database.OpenConnection();
        _db = new StashRegistryDatabase(_context);
        _db.Initialize();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private static AdminBootstrapper MakeBootstrapper(IRegistryDatabase db, RegistryConfig config) =>
        new AdminBootstrapper(db, config, NullLogger<AdminBootstrapper>.Instance);

    [Fact]
    public async Task Bootstrap_Disabled_NoAdminPasswordEnv_DoesNotCreateUser()
    {
        var config = new RegistryConfig(); // Bootstrap.AdminPasswordEnv = ""

        await MakeBootstrapper(_db, config).RunAsync();

        long count = await _db.GetAdminCountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Bootstrap_EnvVarSet_ButEmpty_DoesNotCreateUser()
    {
        string envName = $"STASH_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envName, "");
        try
        {
            var config = new RegistryConfig
            {
                Bootstrap = new BootstrapConfig { AdminPasswordEnv = envName, AdminUsername = "admin" }
            };

            await MakeBootstrapper(_db, config).RunAsync();

            long count = await _db.GetAdminCountAsync();
            Assert.Equal(0, count);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public async Task Bootstrap_EnvVarSet_WithPassword_CreatesAdminUser()
    {
        string envName = $"STASH_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envName, "supersecret123");
        try
        {
            var config = new RegistryConfig
            {
                Bootstrap = new BootstrapConfig { AdminPasswordEnv = envName, AdminUsername = "bootstrapadmin" }
            };

            await MakeBootstrapper(_db, config).RunAsync();

            var user = await _db.GetUserAsync("bootstrapadmin");
            Assert.NotNull(user);
            Assert.Equal(UserRoles.Admin, user.Role);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public async Task Bootstrap_AdminAlreadyExists_DoesNotDuplicate()
    {
        string envName = $"STASH_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envName, "supersecret123");
        try
        {
            string hash = LocalAuthProvider.HashPasswordInternal("supersecret123");
            await _db.CreateUserAsync("myadmin", hash, "admin");

            var config = new RegistryConfig
            {
                Bootstrap = new BootstrapConfig { AdminPasswordEnv = envName, AdminUsername = "myadmin" }
            };

            await MakeBootstrapper(_db, config).RunAsync();

            long count = await _db.GetAdminCountAsync();
            Assert.Equal(1, count); // still just one admin
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public async Task Bootstrap_UserExistsAsNonAdmin_DoesNotPromote()
    {
        string envName = $"STASH_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envName, "supersecret123");
        try
        {
            string hash = LocalAuthProvider.HashPasswordInternal("otherpass");
            await _db.CreateUserAsync("regularuser", hash, "user");

            var config = new RegistryConfig
            {
                Bootstrap = new BootstrapConfig { AdminPasswordEnv = envName, AdminUsername = "regularuser" }
            };

            await MakeBootstrapper(_db, config).RunAsync();

            var user = await _db.GetUserAsync("regularuser");
            Assert.NotNull(user);
            Assert.Equal(UserRoles.User, user.Role); // NOT promoted
            Assert.Equal(0, await _db.GetAdminCountAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }
}
