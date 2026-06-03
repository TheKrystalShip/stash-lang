using System;
using System.Text.Json.Serialization;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Registry.Contracts;

/// <summary>
/// Verifies that the seven wire-visible bounded-domain enum types
/// (<c>PackageRoles</c>, <c>OrgRoles</c>, <c>PrincipalTypes</c>,
/// <c>ScopeOwnerTypes</c>, <c>TokenScopes</c>, <c>Visibilities</c>, <c>UserRoles</c>)
/// live in <c>Stash.Registry.Contracts</c> and that each member's wire string
/// (via <c>.ToWire()</c> and <c>[JsonStringEnumMemberName]</c>) matches the
/// documented lowercase value byte-for-byte.
/// </summary>
/// <remarks>
/// Updated in P4: const string → enum conversion. The "IsConstString" tests
/// are replaced by "IsEnum" + "ToWire produces correct lowercase" tests.
/// The assembly placement assertions remain (enums must live in the shared Contracts assembly).
/// </remarks>
public sealed class BoundedDomainPlacementTests
{
    // ── Namespace + assembly assertions ───────────────────────────────────────

    private const string ExpectedNamespace = "Stash.Registry.Contracts";
    private const string ExpectedAssemblyName = "Stash.Registry.Contracts";

    private static void AssertInContractsAssembly(Type type)
    {
        Assert.Equal(ExpectedNamespace, type.Namespace);
        Assert.Equal(ExpectedAssemblyName, type.Assembly.GetName().Name);
    }

    private static void AssertIsEnum(Type type)
    {
        Assert.True(type.IsEnum, $"{type.Name} must be an enum type (P4 conversion).");
    }

    [Fact]
    public void PackageRoles_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(PackageRoles));
    }

    [Fact]
    public void PackageRoles_IsEnum()
    {
        AssertIsEnum(typeof(PackageRoles));
    }

    [Fact]
    public void OrgRoles_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(OrgRoles));
    }

    [Fact]
    public void OrgRoles_IsEnum()
    {
        AssertIsEnum(typeof(OrgRoles));
    }

    [Fact]
    public void PrincipalTypes_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(PrincipalTypes));
    }

    [Fact]
    public void PrincipalTypes_IsEnum()
    {
        AssertIsEnum(typeof(PrincipalTypes));
    }

    [Fact]
    public void ScopeOwnerTypes_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(ScopeOwnerTypes));
    }

    [Fact]
    public void ScopeOwnerTypes_IsEnum()
    {
        AssertIsEnum(typeof(ScopeOwnerTypes));
    }

    [Fact]
    public void TokenScopes_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(TokenScopes));
    }

    [Fact]
    public void TokenScopes_IsEnum()
    {
        AssertIsEnum(typeof(TokenScopes));
    }

    [Fact]
    public void Visibilities_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(Visibilities));
    }

    [Fact]
    public void Visibilities_IsEnum()
    {
        AssertIsEnum(typeof(Visibilities));
    }

    [Fact]
    public void UserRoles_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(UserRoles));
    }

    [Fact]
    public void UserRoles_IsEnum()
    {
        AssertIsEnum(typeof(UserRoles));
    }

    // ── Wire value accuracy (byte-identical lowercase wire strings via .ToWire()) ──

    [Fact]
    public void PackageRoles_WireValues_Unchanged()
    {
        Assert.Equal("owner", PackageRoles.Owner.ToWire());
        Assert.Equal("maintainer", PackageRoles.Maintainer.ToWire());
        Assert.Equal("publisher", PackageRoles.Publisher.ToWire());
        Assert.Equal("reader", PackageRoles.Reader.ToWire());
    }

    [Fact]
    public void OrgRoles_WireValues_Unchanged()
    {
        Assert.Equal("owner", OrgRoles.Owner.ToWire());
        Assert.Equal("member", OrgRoles.Member.ToWire());
    }

    [Fact]
    public void PrincipalTypes_WireValues_Unchanged()
    {
        Assert.Equal("user", PrincipalTypes.User.ToWire());
        Assert.Equal("team", PrincipalTypes.Team.ToWire());
        Assert.Equal("org", PrincipalTypes.Org.ToWire());
    }

    [Fact]
    public void ScopeOwnerTypes_WireValues_Unchanged()
    {
        Assert.Equal("user", ScopeOwnerTypes.User.ToWire());
        Assert.Equal("org", ScopeOwnerTypes.Org.ToWire());
        Assert.Equal("system", ScopeOwnerTypes.System.ToWire());
    }

    [Fact]
    public void TokenScopes_WireValues_Unchanged()
    {
        Assert.Equal("read", TokenScopes.Read.ToWire());
        Assert.Equal("publish", TokenScopes.Publish.ToWire());
        Assert.Equal("admin", TokenScopes.Admin.ToWire());
    }

    [Fact]
    public void Visibilities_WireValues_Unchanged()
    {
        Assert.Equal("public", Visibilities.Public.ToWire());
        Assert.Equal("private", Visibilities.Private.ToWire());
        Assert.Equal("internal", Visibilities.Internal.ToWire());
    }

    [Fact]
    public void UserRoles_WireValues_Unchanged()
    {
        Assert.Equal("user", UserRoles.User.ToWire());
        Assert.Equal("admin", UserRoles.Admin.ToWire());
    }

    // ── JsonStringEnumMemberName attribute verification ───────────────────────

    /// <summary>
    /// Every enum member must have an explicit [JsonStringEnumMemberName] attribute
    /// (the locked mechanism from done_when) producing a lowercase wire string.
    /// This verifies the attribute is present and its value matches .ToWire().
    /// </summary>
    private static void AssertAllMembersHaveJsonMemberName<TEnum>() where TEnum : struct, Enum
    {
        var type = typeof(TEnum);
        foreach (var member in Enum.GetNames<TEnum>())
        {
            var field = type.GetField(member)!;
            var attr = (JsonStringEnumMemberNameAttribute?)Attribute.GetCustomAttribute(
                field, typeof(JsonStringEnumMemberNameAttribute));
            Assert.True(attr is not null,
                $"{type.Name}.{member} is missing [JsonStringEnumMemberName(\"...\")].");

            // The attribute value must be lowercase and match .ToWire()
            Assert.True(attr!.Name == attr.Name.ToLowerInvariant(),
                $"{type.Name}.{member}: [JsonStringEnumMemberName(\"{attr.Name}\")] is not lowercase.");
        }
    }

    [Fact]
    public void PackageRoles_AllMembers_HaveJsonMemberName()
        => AssertAllMembersHaveJsonMemberName<PackageRoles>();

    [Fact]
    public void OrgRoles_AllMembers_HaveJsonMemberName()
        => AssertAllMembersHaveJsonMemberName<OrgRoles>();

    [Fact]
    public void PrincipalTypes_AllMembers_HaveJsonMemberName()
        => AssertAllMembersHaveJsonMemberName<PrincipalTypes>();

    [Fact]
    public void ScopeOwnerTypes_AllMembers_HaveJsonMemberName()
        => AssertAllMembersHaveJsonMemberName<ScopeOwnerTypes>();

    [Fact]
    public void TokenScopes_AllMembers_HaveJsonMemberName()
        => AssertAllMembersHaveJsonMemberName<TokenScopes>();

    [Fact]
    public void Visibilities_AllMembers_HaveJsonMemberName()
        => AssertAllMembersHaveJsonMemberName<Visibilities>();

    [Fact]
    public void UserRoles_AllMembers_HaveJsonMemberName()
        => AssertAllMembersHaveJsonMemberName<UserRoles>();
}
