using System;
using System.Reflection;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Asserts that the CLI and the registry both resolve
/// <c>Stash.Registry.Contracts</c> types from the same assembly —
/// the "one definition, two consumers" invariant.
/// </summary>
/// <remarks>
/// If the CLI ever re-declares a type that already exists in the shared
/// <c>Stash.Registry.Contracts</c> project, the two assemblies would contain
/// two distinct type definitions with the same name, and a breaking field
/// change on the shared type would not fail the CLI build. This test ensures
/// that does not happen: the <c>PackageRoleResponse</c> type visible to
/// <c>Stash.Tests</c> (which references both <c>Stash.Registry</c> and,
/// transitively, <c>Stash.Cli</c>) resolves to exactly one assembly —
/// <c>Stash.Registry.Contracts</c>.
/// </remarks>
public sealed class CliContractsConsumptionTests
{
    /// <summary>
    /// Verifies that <c>PackageRoleResponse</c> is defined in
    /// <c>Stash.Registry.Contracts</c> and is the same type that both the
    /// registry tests and the CLI consume. There must be exactly one definition
    /// visible from the test assembly; no CLI-local shadow copy may exist.
    /// </summary>
    [Fact]
    public void PackageRoleResponse_ResolvedFromSharedContractsAssembly()
    {
        var type = typeof(PackageRoleResponse);

        // The type must live in the shared contracts assembly.
        Assert.Equal("Stash.Registry.Contracts", type.Assembly.GetName().Name);

        // The full type name must match exactly.
        Assert.Equal("Stash.Registry.Contracts.PackageRoleResponse", type.FullName);

        // The namespace must be the shared contracts namespace.
        Assert.Equal("Stash.Registry.Contracts", type.Namespace);
    }

    /// <summary>
    /// Verifies that multiple wire-bounded DTO types used by the CLI are all resolved
    /// from <c>Stash.Registry.Contracts</c>, not from a CLI-local shadow declaration.
    /// </summary>
    [Fact]
    public void SharedDtoTypes_AllResolvedFromContractsAssembly()
    {
        string expectedAssembly = "Stash.Registry.Contracts";

        var typesToCheck = new[]
        {
            typeof(AssignRoleRequest),
            typeof(RevokeRoleRequest),
            typeof(TokenCreateRequest),
            typeof(TokenCreateResponse),
            typeof(TokenListResponse),
            typeof(TokenListItem),
            typeof(RefreshTokenRequest),
            typeof(DeprecatePackageRequest),
            typeof(DeprecateVersionRequest),
            typeof(SearchResponse),
            typeof(PackageSummaryResponse),
            typeof(LoginRequest),
            typeof(PackageRoleResponse),
            typeof(PackageRolesListResponse),
        };

        foreach (var type in typesToCheck)
        {
            Assert.True(
                type.Assembly.GetName().Name == expectedAssembly,
                $"Type '{type.FullName}' is resolved from assembly " +
                $"'{type.Assembly.GetName().Name}', but expected '{expectedAssembly}'. " +
                "The CLI must not declare a local shadow of this shared DTO.");
        }
    }
}
