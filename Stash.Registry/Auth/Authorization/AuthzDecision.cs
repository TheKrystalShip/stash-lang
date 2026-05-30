namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// The immutable result of a PDP authorization evaluation.
/// </summary>
/// <param name="Allowed">Whether the action is permitted.</param>
/// <param name="Reason">The denial reason when <paramref name="Allowed"/> is <c>false</c>; <see cref="AuthzDenyReason.None"/> otherwise.</param>
/// <param name="Detail">Optional human-readable context (e.g. a policy message).</param>
public readonly record struct AuthzDecision(bool Allowed, AuthzDenyReason Reason, string? Detail)
{
    /// <summary>Creates an ALLOW decision.</summary>
    public static AuthzDecision Allow() => new(true, AuthzDenyReason.None, null);

    /// <summary>Creates a DENY decision with the given reason and optional detail.</summary>
    public static AuthzDecision Deny(AuthzDenyReason reason, string? detail = null) =>
        new(false, reason, detail);
}
