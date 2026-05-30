using System;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Request body for the <c>POST /api/v1/scopes</c> endpoint.
/// </summary>
public sealed class ClaimScopeRequest
{
    /// <summary>The bare scope name to claim (without the leading <c>@</c>).</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>The owner type: <c>user</c> or <c>org</c>.</summary>
    [JsonPropertyName("owner_type")]
    public string? OwnerType { get; set; }

    /// <summary>The owner identifier — a username for <c>user</c> scopes, an org name for <c>org</c> scopes.</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>
    /// Optional verification method for Verified policy mode. Currently only <c>dns-txt</c> is supported.
    /// Ignored under Open/Claim policy.
    /// </summary>
    [JsonPropertyName("verification_method")]
    public string? VerificationMethod { get; set; }
}

/// <summary>
/// Response body returned by <c>GET /api/v1/scopes/{scope}</c> and <c>POST /api/v1/scopes</c>.
/// </summary>
public sealed class ScopeDetailResponse
{
    /// <summary>The bare scope name (without the leading <c>@</c>).</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; set; }

    /// <summary>The type of owner: <c>system</c>, <c>user</c>, or <c>org</c>.</summary>
    [JsonPropertyName("owner_type")]
    public required string OwnerType { get; set; }

    /// <summary>The owner identifier (username, org name, or <c>null</c> for system scopes).</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>
    /// Scope lifecycle state. Present only when <c>ScopeOwnershipPolicy=Verified</c>
    /// and the scope was just claimed (pending verification).
    /// Values: <c>claimed</c> (normal) or <c>pending</c> (awaiting verification).
    /// </summary>
    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; set; }

    /// <summary>
    /// Verification challenge details — present only in Verified mode immediately after claim.
    /// </summary>
    [JsonPropertyName("challenge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScopeChallengeBody? Challenge { get; set; }
}

/// <summary>
/// Challenge body returned in Verified mode immediately after <c>POST /api/v1/scopes</c>.
/// </summary>
public sealed class ScopeChallengeBody
{
    /// <summary>The verification method. Currently <c>dns-txt</c>.</summary>
    [JsonPropertyName("method")]
    public required string Method { get; set; }

    /// <summary>The DNS TXT record name (e.g. <c>_stash-challenge.acme</c>).</summary>
    [JsonPropertyName("record_name")]
    public required string RecordName { get; set; }

    /// <summary>The expected DNS TXT record value (e.g. <c>stash-verify=TOKEN</c>).</summary>
    [JsonPropertyName("record_value")]
    public required string RecordValue { get; set; }

    /// <summary>ISO-8601 expiry timestamp for the challenge token.</summary>
    [JsonPropertyName("expires_at")]
    public required string ExpiresAt { get; set; }
}

/// <summary>
/// Response body for <c>POST /api/v1/scopes/{scope}/verify</c> (501 stub this phase).
/// </summary>
public sealed class ScopeVerifyResponse
{
    /// <summary>The scope that was submitted for verification.</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; set; }

    /// <summary>The verification method that was attempted.</summary>
    [JsonPropertyName("method")]
    public required string Method { get; set; }

    /// <summary>A human-readable message explaining the 501 status.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }
}
