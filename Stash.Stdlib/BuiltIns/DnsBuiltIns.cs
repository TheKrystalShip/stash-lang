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
    [StashFn(ReturnType = "ip")]
    private static StashValue Resolve(IInterpreterContext ctx, string hostname)
    {
        StashValue[] args = [StashValue.FromObj(hostname)];
        return NetSocketImpl.DnsResolve(ctx, args, "dns.resolve");
    }

    /// <summary>Resolves a hostname to all IP addresses via DNS.</summary>
    /// <param name="hostname">The hostname to resolve.</param>
    /// <returns>An array of resolved IP addresses.</returns>
    [StashFn(ReturnType = "array")]
    private static StashValue ResolveAll(IInterpreterContext ctx, string hostname)
    {
        StashValue[] args = [StashValue.FromObj(hostname)];
        return NetSocketImpl.DnsResolveAll(ctx, args, "dns.resolveAll");
    }

    /// <summary>Performs reverse DNS lookup for an IP address.</summary>
    /// <param name="ip">The IP address to lookup.</param>
    /// <returns>The hostname associated with the IP.</returns>
    // Raw = true: StashIpAddress is not in the typed parameter table (Phase A).
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue ReverseLookup(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.DnsReverseLookup(ctx, args, "dns.reverseLookup");

    /// <summary>Resolves MX records for a domain.</summary>
    /// <param name="domain">The domain to query.</param>
    /// <returns>An array of MxRecord structs with priority and exchange fields.</returns>
    [StashFn(ReturnType = "array")]
    private static StashValue ResolveMx(IInterpreterContext ctx, string domain)
    {
        StashValue[] args = [StashValue.FromObj(domain)];
        return NetSocketImpl.DnsResolveMx(ctx, args, "dns.resolveMx");
    }

    /// <summary>Resolves TXT records for a domain.</summary>
    /// <param name="domain">The domain to query.</param>
    /// <returns>An array of strings containing the TXT record values.</returns>
    [StashFn(ReturnType = "array")]
    private static StashValue ResolveTxt(IInterpreterContext ctx, string domain)
    {
        StashValue[] args = [StashValue.FromObj(domain)];
        return NetSocketImpl.DnsResolveTxt(ctx, args, "dns.resolveTxt");
    }
}
