using System;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry;

public sealed class DeprecationServiceTests
{
    private static StashRegistryDatabase CreateTestDb()
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var context = new RegistryDbContext(options);
        context.Database.OpenConnection();
        var db = new StashRegistryDatabase(context);
        db.Initialize();
        return db;
    }

    private static PackageRecord MakePackage(string name, string latest = "1.0.0") => new()
    {
        Name = name,
        Description = $"Description for {name}",
        License = "MIT",
        Latest = latest,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static VersionRecord MakeVersion(string packageName, string version, string publisher = "testuser") => new()
    {
        PackageName = packageName,
        Version = version,
        Integrity = "sha256-abc123",
        PublishedAt = DateTime.UtcNow,
        PublishedBy = publisher
    };

    // ── Package Deprecation ─────────────────────────────────────────────

    [Fact]
    public async Task DeprecatePackage_ValidPackage_SetsDeprecated()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("my-pkg"));

        await service.DeprecatePackageAsync("my-pkg", "Use new-pkg instead", null, "admin");

        var pkg = await db.GetPackageAsync("my-pkg");
        Assert.NotNull(pkg);
        Assert.True(pkg.Deprecated);
        Assert.Equal("Use new-pkg instead", pkg.DeprecationMessage);
        Assert.Equal("admin", pkg.DeprecatedBy);
    }

    [Fact]
    public async Task DeprecatePackage_WithAlternative_SetsAlternative()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("old-pkg"));

        await service.DeprecatePackageAsync("old-pkg", "Replaced", "new-pkg", "admin");

        var pkg = await db.GetPackageAsync("old-pkg");
        Assert.NotNull(pkg);
        Assert.True(pkg.Deprecated);
        Assert.Equal("new-pkg", pkg.DeprecationAlternative);
    }

    [Fact]
    public async Task DeprecatePackage_NonExistent_ThrowsInvalidOperation()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeprecatePackageAsync("ghost-pkg", "No longer maintained", null, "admin"));
    }

    [Fact]
    public async Task UndeprecatePackage_DeprecatedPackage_ClearsDeprecation()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("my-pkg"));
        await service.DeprecatePackageAsync("my-pkg", "Outdated", "better-pkg", "admin");

        await service.UndeprecatePackageAsync("my-pkg");

        var pkg = await db.GetPackageAsync("my-pkg");
        Assert.NotNull(pkg);
        Assert.False(pkg.Deprecated);
        Assert.Null(pkg.DeprecationMessage);
        Assert.Null(pkg.DeprecationAlternative);
        Assert.Null(pkg.DeprecatedBy);
    }

    [Fact]
    public async Task UndeprecatePackage_NonExistent_ThrowsInvalidOperation()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UndeprecatePackageAsync("ghost-pkg"));
    }

    // ── Version Deprecation ─────────────────────────────────────────────

    [Fact]
    public async Task DeprecateVersion_ValidVersion_SetsDeprecated()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("my-pkg"));
        await db.AddVersionAsync("my-pkg", MakeVersion("my-pkg", "1.0.0"));

        await service.DeprecateVersionAsync("my-pkg", "1.0.0", "Critical bug in this version", "admin");

        var ver = await db.GetPackageVersionAsync("my-pkg", "1.0.0");
        Assert.NotNull(ver);
        Assert.True(ver.Deprecated);
        Assert.Equal("Critical bug in this version", ver.DeprecationMessage);
        Assert.Equal("admin", ver.DeprecatedBy);
    }

    [Fact]
    public async Task DeprecateVersion_NonExistent_ThrowsInvalidOperation()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("my-pkg"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeprecateVersionAsync("my-pkg", "9.9.9", "Missing version", "admin"));
    }

    [Fact]
    public async Task UndeprecateVersion_DeprecatedVersion_ClearsDeprecation()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("my-pkg"));
        await db.AddVersionAsync("my-pkg", MakeVersion("my-pkg", "1.0.0"));
        await service.DeprecateVersionAsync("my-pkg", "1.0.0", "Bad release", "admin");

        await service.UndeprecateVersionAsync("my-pkg", "1.0.0");

        var ver = await db.GetPackageVersionAsync("my-pkg", "1.0.0");
        Assert.NotNull(ver);
        Assert.False(ver.Deprecated);
        Assert.Null(ver.DeprecationMessage);
        Assert.Null(ver.DeprecatedBy);
    }

    [Fact]
    public async Task UndeprecateVersion_NonExistent_ThrowsInvalidOperation()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("my-pkg"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UndeprecateVersionAsync("my-pkg", "9.9.9"));
    }

    // ── Combined Scenarios ──────────────────────────────────────────────

    [Fact]
    public async Task DeprecatePackage_DoesNotAffectVersions()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("my-pkg"));
        await db.AddVersionAsync("my-pkg", MakeVersion("my-pkg", "1.0.0"));

        await service.DeprecatePackageAsync("my-pkg", "Deprecated package", null, "admin");

        var ver = await db.GetPackageVersionAsync("my-pkg", "1.0.0");
        Assert.NotNull(ver);
        Assert.False(ver.Deprecated);
        Assert.Null(ver.DeprecationMessage);
    }

    [Fact]
    public async Task DeprecateVersion_DoesNotAffectPackage()
    {
        var db = CreateTestDb();
        var service = new DeprecationService(db);
        await db.CreatePackageAsync(MakePackage("my-pkg"));
        await db.AddVersionAsync("my-pkg", MakeVersion("my-pkg", "1.0.0"));

        await service.DeprecateVersionAsync("my-pkg", "1.0.0", "Bad release", "admin");

        var pkg = await db.GetPackageAsync("my-pkg");
        Assert.NotNull(pkg);
        Assert.False(pkg.Deprecated);
        Assert.Null(pkg.DeprecationMessage);
    }

    [Fact]
    public async Task DeprecatePackage_AlreadyDeprecated_OverwritesFields()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("redepr-pkg"));
        var service = new DeprecationService(db);

        await service.DeprecatePackageAsync("redepr-pkg", "Old message", "old-alt", "alice");
        await service.DeprecatePackageAsync("redepr-pkg", "New message", "new-alt", "bob");

        PackageRecord? pkg = await db.GetPackageAsync("redepr-pkg");
        Assert.NotNull(pkg);
        Assert.True(pkg.Deprecated);
        Assert.Equal("New message", pkg.DeprecationMessage);
        Assert.Equal("new-alt", pkg.DeprecationAlternative);
        Assert.Equal("bob", pkg.DeprecatedBy);
    }

    [Fact]
    public async Task UndeprecatePackage_NotDeprecated_NoOp()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("noop-pkg"));
        var service = new DeprecationService(db);

        await service.UndeprecatePackageAsync("noop-pkg");

        PackageRecord? pkg = await db.GetPackageAsync("noop-pkg");
        Assert.NotNull(pkg);
        Assert.False(pkg.Deprecated);
        Assert.Null(pkg.DeprecationMessage);
    }

    [Fact]
    public async Task DeprecateVersion_AlreadyDeprecated_OverwritesFields()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("revdepr-pkg"));
        await db.AddVersionAsync("revdepr-pkg", MakeVersion("revdepr-pkg", "1.0.0"));
        var service = new DeprecationService(db);

        await service.DeprecateVersionAsync("revdepr-pkg", "1.0.0", "Old msg", "alice");
        await service.DeprecateVersionAsync("revdepr-pkg", "1.0.0", "New msg", "bob");

        VersionRecord? ver = await db.GetPackageVersionAsync("revdepr-pkg", "1.0.0");
        Assert.NotNull(ver);
        Assert.True(ver.Deprecated);
        Assert.Equal("New msg", ver.DeprecationMessage);
        Assert.Equal("bob", ver.DeprecatedBy);
    }
}
