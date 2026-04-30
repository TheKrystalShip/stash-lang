namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>dns</c> namespace built-in functions for DNS resolution.
/// </summary>
public static class DnsBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("dns");
        ns.RequiresCapability(StashCapabilities.Network);

        ns.Struct("MxRecord", [
            new BuiltInField("priority", "int"),
            new BuiltInField("exchange", "string"),
        ]);

        // dns.resolve(hostname) — Resolves a hostname to its first IP address.
        ns.Function("resolve", [Param("hostname", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsResolve(ctx, args, "dns.resolve"),
            returnType: "ip",
            documentation: "Resolves a hostname to its first IP address via DNS.\n@param hostname The hostname to resolve.\n@return The first resolved IP address.");

        // dns.resolveAll(hostname) — Resolves a hostname to all IP addresses.
        ns.Function("resolveAll", [Param("hostname", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsResolveAll(ctx, args, "dns.resolveAll"),
            returnType: "array",
            documentation: "Resolves a hostname to all IP addresses via DNS.\n@param hostname The hostname to resolve.\n@return An array of resolved IP addresses.");

        // dns.reverseLookup(ip) — Reverse DNS lookup.
        ns.Function("reverseLookup", [Param("ip", "ip")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsReverseLookup(ctx, args, "dns.reverseLookup"),
            returnType: "string",
            documentation: "Performs reverse DNS lookup for an IP address.\n@param ip The IP address to lookup.\n@return The hostname associated with the IP.");

        // dns.resolveMx(domain) — Resolves MX records.
        ns.Function("resolveMx", [Param("domain", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsResolveMx(ctx, args, "dns.resolveMx"),
            returnType: "array",
            documentation: "Resolves MX records for a domain.\n@param domain The domain to query.\n@return An array of MxRecord structs with priority and exchange fields.");

        // dns.resolveTxt(domain) — Resolves TXT records.
        ns.Function("resolveTxt", [Param("domain", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsResolveTxt(ctx, args, "dns.resolveTxt"),
            returnType: "array",
            documentation: "Resolves TXT records for a domain.\n@param domain The domain to query.\n@return An array of strings containing the TXT record values.");

        return ns.Build();
    }
}
