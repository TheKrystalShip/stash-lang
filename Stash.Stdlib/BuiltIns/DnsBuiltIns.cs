namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>dns</c> namespace built-in functions for DNS resolution.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class DnsBuiltIns
{
    /// <summary>An MX record returned by dns.resolveMx.</summary>
    [StashStruct]
    public sealed record MxRecord(long Priority, string Exchange);

    /// <summary>Resolves a hostname to its first IP address via DNS.</summary>
    /// <param name="hostname">The hostname to resolve.</param>
    /// <returns>The first resolved IP address.</returns>
    [StashFn(Raw = true, ReturnType = "ip")]
    private static StashValue Resolve(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.DnsResolve(ctx, args, "dns.resolve");

    /// <summary>Resolves a hostname to all IP addresses via DNS.</summary>
    /// <param name="hostname">The hostname to resolve.</param>
    /// <returns>An array of resolved IP addresses.</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue ResolveAll(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.DnsResolveAll(ctx, args, "dns.resolveAll");

    /// <summary>Performs reverse DNS lookup for an IP address.</summary>
    /// <param name="ip">The IP address to lookup.</param>
    /// <returns>The hostname associated with the IP.</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue ReverseLookup(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.DnsReverseLookup(ctx, args, "dns.reverseLookup");

    /// <summary>Resolves MX records for a domain.</summary>
    /// <param name="domain">The domain to query.</param>
    /// <returns>An array of MxRecord structs with priority and exchange fields.</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue ResolveMx(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.DnsResolveMx(ctx, args, "dns.resolveMx");

    /// <summary>Resolves TXT records for a domain.</summary>
    /// <param name="domain">The domain to query.</param>
    /// <returns>An array of strings containing the TXT record values.</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue ResolveTxt(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.DnsResolveTxt(ctx, args, "dns.resolveTxt");
}
