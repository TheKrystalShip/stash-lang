using System;
using System.Reflection;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Registry.Contracts;

/// <summary>
/// Verifies that the seven wire-visible bounded-domain constant classes
/// (<c>PackageRoles</c>, <c>OrgRoles</c>, <c>PrincipalTypes</c>,
/// <c>ScopeOwnerTypes</c>, <c>TokenScopes</c>, <c>Visibilities</c>, <c>UserRoles</c>)
/// live in <c>Stash.Registry.Contracts</c> (not in <c>Stash.Registry.Auth</c>),
/// and that each representative member is a <c>const string</c>
/// (compile-time constant) rather than a <c>static readonly</c> field —
/// the invariant required by EF column defaults and C# field initializers.
/// </summary>
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

    [Fact]
    public void PackageRoles_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(PackageRoles));
    }

    [Fact]
    public void OrgRoles_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(OrgRoles));
    }

    [Fact]
    public void PrincipalTypes_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(PrincipalTypes));
    }

    [Fact]
    public void ScopeOwnerTypes_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(ScopeOwnerTypes));
    }

    [Fact]
    public void TokenScopes_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(TokenScopes));
    }

    [Fact]
    public void Visibilities_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(Visibilities));
    }

    // ── const string (not static readonly) assertions ─────────────────────────

    /// <summary>
    /// Asserts that the named field on <paramref name="ownerType"/>
    /// is a compile-time constant (<c>IsLiteral == true &amp;&amp; IsInitOnly == false</c>).
    /// This is the EF-default invariant: <c>HasDefaultValue(Visibilities.Public)</c>
    /// and field initializers like <c>Visibility = Visibilities.Public</c> both require
    /// the value to be a C# <c>const</c>, which is inlined at the call site across assemblies.
    /// </summary>
    private static void AssertIsConstString(Type ownerType, string fieldName)
    {
        FieldInfo? field = ownerType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.True(field is not null,
            $"{ownerType.Name}.{fieldName} field not found.");
        Assert.True(field!.IsLiteral,
            $"{ownerType.Name}.{fieldName} must be a const (IsLiteral=true), not static readonly.");
        Assert.False(field.IsInitOnly,
            $"{ownerType.Name}.{fieldName} must be a const (IsInitOnly=false).");
        Assert.Equal(typeof(string), field.FieldType);
    }

    [Fact]
    public void PackageRoles_Owner_IsConstString()
    {
        AssertIsConstString(typeof(PackageRoles), nameof(PackageRoles.Owner));
    }

    [Fact]
    public void PackageRoles_Maintainer_IsConstString()
    {
        AssertIsConstString(typeof(PackageRoles), nameof(PackageRoles.Maintainer));
    }

    [Fact]
    public void PackageRoles_Publisher_IsConstString()
    {
        AssertIsConstString(typeof(PackageRoles), nameof(PackageRoles.Publisher));
    }

    [Fact]
    public void PackageRoles_Reader_IsConstString()
    {
        AssertIsConstString(typeof(PackageRoles), nameof(PackageRoles.Reader));
    }

    [Fact]
    public void OrgRoles_Owner_IsConstString()
    {
        AssertIsConstString(typeof(OrgRoles), nameof(OrgRoles.Owner));
    }

    [Fact]
    public void OrgRoles_Member_IsConstString()
    {
        AssertIsConstString(typeof(OrgRoles), nameof(OrgRoles.Member));
    }

    [Fact]
    public void PrincipalTypes_User_IsConstString()
    {
        AssertIsConstString(typeof(PrincipalTypes), nameof(PrincipalTypes.User));
    }

    [Fact]
    public void PrincipalTypes_Team_IsConstString()
    {
        AssertIsConstString(typeof(PrincipalTypes), nameof(PrincipalTypes.Team));
    }

    [Fact]
    public void PrincipalTypes_Org_IsConstString()
    {
        AssertIsConstString(typeof(PrincipalTypes), nameof(PrincipalTypes.Org));
    }

    [Fact]
    public void ScopeOwnerTypes_User_IsConstString()
    {
        AssertIsConstString(typeof(ScopeOwnerTypes), nameof(ScopeOwnerTypes.User));
    }

    [Fact]
    public void ScopeOwnerTypes_Org_IsConstString()
    {
        AssertIsConstString(typeof(ScopeOwnerTypes), nameof(ScopeOwnerTypes.Org));
    }

    [Fact]
    public void ScopeOwnerTypes_System_IsConstString()
    {
        AssertIsConstString(typeof(ScopeOwnerTypes), nameof(ScopeOwnerTypes.System));
    }

    [Fact]
    public void TokenScopes_Read_IsConstString()
    {
        AssertIsConstString(typeof(TokenScopes), nameof(TokenScopes.Read));
    }

    [Fact]
    public void TokenScopes_Publish_IsConstString()
    {
        AssertIsConstString(typeof(TokenScopes), nameof(TokenScopes.Publish));
    }

    [Fact]
    public void TokenScopes_Admin_IsConstString()
    {
        AssertIsConstString(typeof(TokenScopes), nameof(TokenScopes.Admin));
    }

    [Fact]
    public void Visibilities_Public_IsConstString()
    {
        AssertIsConstString(typeof(Visibilities), nameof(Visibilities.Public));
    }

    [Fact]
    public void Visibilities_Private_IsConstString()
    {
        AssertIsConstString(typeof(Visibilities), nameof(Visibilities.Private));
    }

    [Fact]
    public void Visibilities_Internal_IsConstString()
    {
        AssertIsConstString(typeof(Visibilities), nameof(Visibilities.Internal));
    }

    // ── Wire value accuracy (spot-check the byte-identical move) ─────────────

    [Fact]
    public void PackageRoles_WireValues_Unchanged()
    {
        Assert.Equal("owner", PackageRoles.Owner);
        Assert.Equal("maintainer", PackageRoles.Maintainer);
        Assert.Equal("publisher", PackageRoles.Publisher);
        Assert.Equal("reader", PackageRoles.Reader);
    }

    [Fact]
    public void OrgRoles_WireValues_Unchanged()
    {
        Assert.Equal("owner", OrgRoles.Owner);
        Assert.Equal("member", OrgRoles.Member);
    }

    [Fact]
    public void PrincipalTypes_WireValues_Unchanged()
    {
        Assert.Equal("user", PrincipalTypes.User);
        Assert.Equal("team", PrincipalTypes.Team);
        Assert.Equal("org", PrincipalTypes.Org);
    }

    [Fact]
    public void ScopeOwnerTypes_WireValues_Unchanged()
    {
        Assert.Equal("user", ScopeOwnerTypes.User);
        Assert.Equal("org", ScopeOwnerTypes.Org);
        Assert.Equal("system", ScopeOwnerTypes.System);
    }

    [Fact]
    public void TokenScopes_WireValues_Unchanged()
    {
        Assert.Equal("read", TokenScopes.Read);
        Assert.Equal("publish", TokenScopes.Publish);
        Assert.Equal("admin", TokenScopes.Admin);
    }

    [Fact]
    public void Visibilities_WireValues_Unchanged()
    {
        Assert.Equal("public", Visibilities.Public);
        Assert.Equal("private", Visibilities.Private);
        Assert.Equal("internal", Visibilities.Internal);
    }

    [Fact]
    public void UserRoles_LivesInContractsAssembly()
    {
        AssertInContractsAssembly(typeof(UserRoles));
    }

    [Fact]
    public void UserRoles_User_IsConstString()
    {
        AssertIsConstString(typeof(UserRoles), nameof(UserRoles.User));
    }

    [Fact]
    public void UserRoles_Admin_IsConstString()
    {
        AssertIsConstString(typeof(UserRoles), nameof(UserRoles.Admin));
    }

    [Fact]
    public void UserRoles_WireValues_Unchanged()
    {
        Assert.Equal("user", UserRoles.User);
        Assert.Equal("admin", UserRoles.Admin);
    }
}
