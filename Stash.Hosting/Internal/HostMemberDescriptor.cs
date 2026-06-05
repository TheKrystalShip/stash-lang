namespace Stash.Hosting.Internal;

using System;
using Stash.Runtime;

/// <summary>
/// Describes one registered member (property, method, or async method) on a host type.
/// P1: skeleton only — field is populated in P2/P3. Member dispatch is not yet wired.
/// </summary>
internal sealed record HostMemberDescriptor(
    HostMemberKind Kind,
    Func<object, StashValue>? Getter,
    Action<object, StashValue>? Setter,
    Delegate? Invoke);

/// <summary>Discriminator for the kind of host member.</summary>
internal enum HostMemberKind
{
    Property,
    Method,
    AsyncMethod,
}
