using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Service that handles the atomic scope-claim flow, including challenge generation
/// for <see cref="ScopeOwnershipPolicyKind.Verified"/> mode (501-stubbed this phase).
/// </summary>
/// <remarks>
/// All claim paths go through this service so the explicit-claim endpoint
/// (<c>POST /api/v1/scopes</c>) and the open-mode auto-claim triggered from
/// <c>CreatePackage</c> share one atomic insert-then-handle-unique-violation path.
/// </remarks>
public sealed class ScopeChallengeService
{
    private readonly IRegistryDatabase _db;
    private readonly RegistryConfig _config;

    /// <summary>
    /// Initialises the service with its required dependencies.
    /// </summary>
    public ScopeChallengeService(IRegistryDatabase db, RegistryConfig config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>
    /// Attempts to atomically claim a scope for a user-principal caller.
    /// Returns <c>true</c> and the resulting response on success, or <c>false</c>
    /// when a concurrent request won the uniqueness race (caller should return 409).
    /// </summary>
    /// <param name="scopeName">Bare scope name (pre-validated, lowercase).</param>
    /// <param name="ownerType">Owner type constant from <see cref="ScopeOwnerTypes"/>.</param>
    /// <param name="ownerId">Username for user-scopes, org ID for org-scopes.</param>
    /// <param name="ownerDisplayName">Display name (username or org name) for the response.</param>
    /// <returns>
    /// <c>(true, ScopeDetailResponse)</c> on success;
    /// <c>(false, null)</c> on unique-constraint collision.
    /// </returns>
    public async Task<(bool Succeeded, ScopeDetailResponse? Response)> TryClaimAsync(
        string scopeName,
        string ownerType,
        string ownerId,
        string ownerDisplayName)
    {
        bool isVerifiedMode = _config.Security.ScopeOwnershipPolicy == ScopeOwnershipPolicyKind.Verified;
        string state = isVerifiedMode ? ScopeStates.Pending : ScopeStates.Claimed;

        ScopeRecord newScope = ownerType == ScopeOwnerTypes.User
            ? new ScopeRecord
            {
                Name = scopeName,
                OwnerType = ScopeOwnerTypes.User,
                OwnerUsername = ownerId,
                OwnerOrgId = null,
                State = state
            }
            : new ScopeRecord
            {
                Name = scopeName,
                OwnerType = ScopeOwnerTypes.Org,
                OwnerOrgId = ownerId,
                OwnerUsername = null,
                State = state
            };

        bool inserted = await _db.TryCreateScopeAsync(newScope);
        if (!inserted)
            return (false, null);

        var response = new ScopeDetailResponse
        {
            Scope = scopeName,
            OwnerType = ownerType,
            Owner = ownerDisplayName,
            State = isVerifiedMode ? ScopeStates.Pending : null
        };

        if (isVerifiedMode)
        {
            response.Challenge = GenerateChallenge(scopeName);
        }

        return (true, response);
    }

    /// <summary>
    /// Generates a DNS-TXT verification challenge for a scope.
    /// The challenge is informational; the resolver is stubbed 501 this phase.
    /// </summary>
    private static ScopeChallengeBody GenerateChallenge(string scopeName)
    {
        // Generate a random 32-byte token encoded as base64url (URL-safe, no padding).
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return new ScopeChallengeBody
        {
            Method = "dns-txt",
            RecordName = $"_stash-challenge.{scopeName}",
            RecordValue = $"stash-verify={token}",
            ExpiresAt = DateTime.UtcNow.AddDays(7).ToString("o")
        };
    }
}
