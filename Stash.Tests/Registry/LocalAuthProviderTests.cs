using System;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Auth;
using Stash.Registry.Database;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry;

public sealed class LocalAuthProviderTests : IDisposable
{
    private readonly RegistryDbContext _context;
    private readonly StashRegistryDatabase _db;
    private readonly LocalAuthProvider _auth;

    public LocalAuthProviderTests()
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new RegistryDbContext(options);
        _context.Database.OpenConnection();
        _db = new StashRegistryDatabase(_context);
        _db.Initialize();
        _auth = new LocalAuthProvider(_db);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task CreateUser_Authenticate_Succeeds()
    {
        await _auth.CreateUserAsync("alice", "secret123");

        bool result = await _auth.AuthenticateAsync("alice", "secret123");

        Assert.True(result);
    }

    [Fact]
    public async Task Authenticate_WrongPassword_Fails()
    {
        await _auth.CreateUserAsync("bob", "correct-password");

        bool result = await _auth.AuthenticateAsync("bob", "wrong-password");

        Assert.False(result);
    }

    [Fact]
    public async Task Authenticate_NonExistentUser_Fails()
    {
        bool result = await _auth.AuthenticateAsync("nobody", "password");

        Assert.False(result);
    }

    [Fact]
    public async Task CreateUser_DuplicateUser_Throws()
    {
        await _auth.CreateUserAsync("charlie", "pass1");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _auth.CreateUserAsync("charlie", "pass2"));
    }

    [Fact]
    public async Task UserExists_ExistingUser_ReturnsTrue()
    {
        await _auth.CreateUserAsync("dave", "pass");

        Assert.True(await _auth.UserExistsAsync("dave"));
    }

    [Fact]
    public async Task UserExists_NonExistent_ReturnsFalse()
    {
        Assert.False(await _auth.UserExistsAsync("ghost"));
    }
}
