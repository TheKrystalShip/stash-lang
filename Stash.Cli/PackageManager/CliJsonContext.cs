using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stash.Registry.Contracts;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for all JSON types used by
/// the Stash CLI package-manager subsystem.
/// </summary>
/// <remarks>
/// <para>
/// Registering types here enables AOT-safe (trimming- and NativeAOT-compatible)
/// JSON serialisation without runtime reflection. The context is consumed by
/// <see cref="RegistryClient"/> and <see cref="UserConfig"/> wherever
/// <see cref="JsonSerializer"/> is called.
/// </para>
/// <para>
/// Generation options applied to all registered types:
/// <list type="bullet">
///   <item><description>Property name matching is case-insensitive during deserialisation.</description></item>
///   <item><description>Properties with <c>null</c> values are omitted when serialising.</description></item>
///   <item><description>Output JSON is indented for human readability.</description></item>
/// </list>
/// </para>
/// </remarks>
[JsonSerializable(typeof(UserConfig))]
[JsonSerializable(typeof(RegistryEntry))]
[JsonSerializable(typeof(Dictionary<string, RegistryEntry>))]
// Shared Stash.Registry.Contracts types — wire DTOs consumed by the CLI
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(AssignRoleRequest))]
[JsonSerializable(typeof(RevokeRoleRequest))]
[JsonSerializable(typeof(TokenCreateRequest))]
[JsonSerializable(typeof(TokenCreateResponse))]
[JsonSerializable(typeof(TokenListResponse))]
[JsonSerializable(typeof(TokenListItem))]
[JsonSerializable(typeof(List<TokenListItem>))]
[JsonSerializable(typeof(RefreshTokenRequest))]
[JsonSerializable(typeof(DeprecatePackageRequest))]
[JsonSerializable(typeof(DeprecateVersionRequest))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(PackageSummaryResponse))]
[JsonSerializable(typeof(List<PackageSummaryResponse>))]
// Package role DTOs (P2 parity additions)
[JsonSerializable(typeof(PackageRoleResponse))]
[JsonSerializable(typeof(PackageRolesListResponse))]
[JsonSerializable(typeof(List<PackageRoleResponse>))]
[JsonSerializable(typeof(SetVisibilityRequest))]
// Scope DTOs
[JsonSerializable(typeof(ClaimScopeRequest))]
[JsonSerializable(typeof(ScopeDetailResponse))]
[JsonSerializable(typeof(ScopeChallengeBody))]
// Organization and team DTOs
[JsonSerializable(typeof(CreateOrgRequest))]
[JsonSerializable(typeof(CreateOrgResponse))]
[JsonSerializable(typeof(OrgDetailResponse))]
[JsonSerializable(typeof(AddOrgMemberRequest))]
[JsonSerializable(typeof(CreateTeamRequest))]
[JsonSerializable(typeof(CreateTeamResponse))]
[JsonSerializable(typeof(AddTeamMemberRequest))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
internal partial class CliJsonContext : JsonSerializerContext { }

/// <summary>
/// Holds the parsed response from a successful login, including both
/// access and refresh tokens with their metadata.
/// </summary>
/// <remarks>
/// This is a CLI-only projection; it is not a wire DTO. The wire response is
/// <see cref="Stash.Registry.Contracts.LoginResponse"/>.
/// </remarks>
public sealed class LoginResult
{
    /// <summary>The short-lived access token.</summary>
    public string Token { get; set; } = "";

    /// <summary>UTC expiry of the access token.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>The long-lived refresh token.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>UTC expiry of the refresh token.</summary>
    public DateTime? RefreshTokenExpiresAt { get; set; }

    /// <summary>The machine fingerprint used during login.</summary>
    public string? MachineId { get; set; }
}
