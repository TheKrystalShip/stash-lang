namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>net</c> namespace built-in functions for network operations and subnet utilities.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class NetBuiltIns
{
    // ── Structs ──────────────────────────────────────────────────────────────

    /// <summary>Subnet details returned by net.subnetInfo.</summary>
    [StashStruct(Name = "SubnetInfo")]
    public sealed record SubnetInfoResult(
        [property: StashField(Type = "ip")] StashIpAddress? Network,
        [property: StashField(Type = "ip")] StashIpAddress? Broadcast,
        [property: StashField(Type = "ip")] StashIpAddress? Mask,
        [property: StashField(Type = "ip")] StashIpAddress? Wildcard,
        long HostCount,
        [property: StashField(Type = "ip")] StashIpAddress? FirstHost,
        [property: StashField(Type = "ip")] StashIpAddress? LastHost);

    /// <summary>Result of a ping operation.</summary>
    [StashStruct]
    public sealed record PingResult(bool Alive, double Latency, long Ttl);

    /// <summary>Information about a network interface.</summary>
    [StashStruct]
    public sealed record InterfaceInfo(
        string Name,
        [property: StashField(Type = "ip")] StashIpAddress? Ip,
        [property: StashField(Type = "ip")] StashIpAddress? Ipv6,
        string Mac,
        [property: StashField(Type = "ip")] StashIpAddress? Gateway,
        [property: StashField(Type = "ip")] StashIpAddress? Subnet,
        string Status,
        string Type,
        bool Up);

    /// <summary>A TCP connection handle.</summary>
    [StashStruct]
    public sealed record TcpConnection(string Host, long Port, long LocalPort);

    /// <summary>A received UDP datagram.</summary>
    [StashStruct]
    public sealed record UdpMessage(string Data, string Host, long Port);

    /// <summary>An MX record returned by net.resolveMx.</summary>
    [StashStruct]
    public sealed record MxRecord(long Priority, string Exchange);

    /// <summary>A WebSocket connection handle.</summary>
    [StashStruct]
    public sealed record WsConnection(string Url, string Protocol);

    /// <summary>A received WebSocket message.</summary>
    [StashStruct]
    public sealed record WsMessage(string Data, string Type, bool Close);

    /// <summary>Options for net.tcpConnect / net.tcpConnectAsync.</summary>
    [StashStruct]
    public sealed record TcpConnectOptions(long TimeoutMs, bool Tls, bool NoDelay, bool KeepAlive, bool TlsVerify, string TlsSni);

    /// <summary>Options for net.tcpRecv / net.tcpRecvAsync.</summary>
    [StashStruct]
    public sealed record TcpRecvOptions(long MaxBytes, long TimeoutMs);

    /// <summary>A TCP server handle returned by net.tcpListenAsync.</summary>
    [StashStruct]
    public sealed record TcpServer(long Port, bool Active);

    // ── Enums ──────────────────────────────────────────────────────────────────

    /// <summary>The state of a WebSocket connection.</summary>
    [StashEnum]
    public enum WsConnectionState { Connecting, Open, Closing, Closed }

    /// <summary>The state of a TCP connection.</summary>
    [StashEnum]
    public enum TcpConnectionState { Open, Closed }

    // ── Network utility functions ──────────────────────────────────────────────────────────


    /// <summary>Returns subnet details for a CIDR IP address.</summary>
    /// <param name="ip">The CIDR IP address.</param>
    // Raw = true: StashIpAddress is not in the typed parameter table (Phase A).
    [StashFn(Raw = true, ReturnType = "SubnetInfo")]
    private static StashValue SubnetInfo(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var ip = SvArgs.IpAddress(args, 0, "net.subnetInfo");
        if (ip.PrefixLength is null)
            throw new RuntimeError("'net.subnetInfo' requires a CIDR IP address (e.g., @192.168.1.0/24).", errorType: StashErrorTypes.TypeError);

        var (maskBytes, networkBytes, broadcastBytes, wildcardBytes) = ComputeSubnetComponents(ip);
        long hostCount = ComputeHostCount(ip);

        int prefix = ip.PrefixLength.Value;
        int hostBits = (ip.Version == 4 ? 32 : 128) - prefix;

        byte[] firstHostBytes = (byte[])networkBytes.Clone();
        byte[] lastHostBytes = (byte[])broadcastBytes.Clone();
        if (ip.Version == 4 && hostBits > 1)
        {
            firstHostBytes[^1] += 1;
            lastHostBytes[^1] -= 1;
        }

        var mask = new StashIpAddress(maskBytes, null);
        var wildcard = new StashIpAddress(wildcardBytes, null);
        var network = new StashIpAddress(networkBytes, prefix);
        var broadcast = new StashIpAddress(broadcastBytes, null);
        var firstHost = new StashIpAddress(firstHostBytes, null);
        var lastHost = new StashIpAddress(lastHostBytes, null);

        return StashValue.FromObj(new StashInstance("SubnetInfo", new Dictionary<string, StashValue>
        {
            ["network"] = StashValue.FromObj(network),
            ["broadcast"] = StashValue.FromObj(broadcast),
            ["mask"] = StashValue.FromObj(mask),
            ["wildcard"] = StashValue.FromObj(wildcard),
            ["hostCount"] = StashValue.FromInt(hostCount),
            ["firstHost"] = StashValue.FromObj(firstHost),
            ["lastHost"] = StashValue.FromObj(lastHost),
        }));
    }


    /// <summary>Returns the subnet mask for a CIDR IP address.</summary>
    /// <param name="ip">The CIDR IP address.</param>
    // Raw = true: StashIpAddress is not in the typed parameter table (Phase A).
    [StashFn(Raw = true, ReturnType = "ip")]
    private static StashValue Mask(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var ip = SvArgs.IpAddress(args, 0, "net.mask");
        var (maskBytes, _, _, _) = ComputeSubnetComponents(ip, "net.mask");
        return StashValue.FromObj(new StashIpAddress(maskBytes, null));
    }


    /// <summary>Returns the network address for a CIDR IP address.</summary>
    /// <param name="ip">The CIDR IP address.</param>
    // Raw = true: StashIpAddress is not in the typed parameter table (Phase A).
    [StashFn(Raw = true, ReturnType = "ip")]
    private static StashValue Network(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var ip = SvArgs.IpAddress(args, 0, "net.network");
        var (_, networkBytes, _, _) = ComputeSubnetComponents(ip, "net.network");
        return StashValue.FromObj(new StashIpAddress(networkBytes, ip.PrefixLength!.Value));
    }


    /// <summary>Returns the broadcast address for a CIDR IP address.</summary>
    /// <param name="ip">The CIDR IP address.</param>
    // Raw = true: StashIpAddress is not in the typed parameter table (Phase A).
    [StashFn(Raw = true, ReturnType = "ip")]
    private static StashValue Broadcast(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var ip = SvArgs.IpAddress(args, 0, "net.broadcast");
        var (_, _, broadcastBytes, _) = ComputeSubnetComponents(ip, "net.broadcast");
        return StashValue.FromObj(new StashIpAddress(broadcastBytes, null));
    }


    /// <summary>Returns the number of usable host addresses in a CIDR subnet.</summary>
    /// <param name="ip">The CIDR IP address.</param>
    // Raw = true: StashIpAddress is not in the typed parameter table (Phase A).
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue HostCount(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var ip = SvArgs.IpAddress(args, 0, "net.hostCount");
        ComputeSubnetComponents(ip, "net.hostCount"); // validates PrefixLength
        return StashValue.FromInt(ComputeHostCount(ip));
    }


    /// <summary>Deprecated. Use dns.resolve.</summary>
    /// <param name="hostname">The hostname to resolve.</param>
    [StashFn(ReturnType = "ip")]
    [StashDeprecated("dns.resolve")]
    private static StashValue Resolve(IInterpreterContext ctx, string hostname)
    {
        StashValue[] args = [StashValue.FromObj(hostname)];
        return NetSocketImpl.DnsResolve(ctx, args, "net.resolve");
    }


    /// <summary>Deprecated. Use dns.resolveAll.</summary>
    /// <param name="hostname">The hostname to resolve.</param>
    [StashFn(ReturnType = "array")]
    [StashDeprecated("dns.resolveAll")]
    private static StashValue ResolveAll(IInterpreterContext ctx, string hostname)
    {
        StashValue[] args = [StashValue.FromObj(hostname)];
        return NetSocketImpl.DnsResolveAll(ctx, args, "net.resolveAll");
    }


    /// <summary>Deprecated. Use dns.reverseLookup.</summary>
    // Raw = true: StashIpAddress is not in the typed parameter table (Phase A).
    [StashFn(Raw = true, ReturnType = "string")]
    [StashDeprecated("dns.reverseLookup")]
    private static StashValue ReverseLookup(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.DnsReverseLookup(ctx, args, "net.reverseLookup");


    /// <summary>Sends an ICMP ping to a host and returns the result.</summary>
    /// <param name="host">The IP address to ping.</param>
    // Raw = true: StashIpAddress is not in the typed parameter table (Phase A).
    [StashFn(Raw = true, ReturnType = "PingResult")]
    private static StashValue Ping(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var host = SvArgs.IpAddress(args, 0, "net.ping");
        using var pinger = new Ping();
        try
        {
            PingReply reply = pinger.Send(host.Address, 5000);
            return StashValue.FromObj(new StashInstance("PingResult", new Dictionary<string, StashValue>
            {
                ["alive"] = StashValue.FromBool(reply.Status == IPStatus.Success),
                ["latency"] = StashValue.FromFloat((double)reply.RoundtripTime),
                ["ttl"] = StashValue.FromInt(reply.Status == IPStatus.Success ? (long)(reply.Options?.Ttl ?? 0) : 0L),
            }));
        }
        catch (PingException)
        {
            return StashValue.FromObj(new StashInstance("PingResult", new Dictionary<string, StashValue>
            {
                ["alive"] = StashValue.False,
                ["latency"] = StashValue.FromFloat(0.0),
                ["ttl"] = StashValue.Zero,
            }));
        }
    }


    /// <summary>Checks if a TCP port is open on a host.</summary>
    /// <param name="host">The IP address or hostname string to check.</param>
    /// <param name="port">The port number (1-65535).</param>
    /// <param name="timeout">Optional timeout in milliseconds (default 3000).</param>
    // Raw = true: first argument is polymorphic (StashIpAddress or string), not expressible as a single typed param.
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue IsPortOpen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 2 || args.Length > 3)
            throw new RuntimeError("net.isPortOpen: expected 2 or 3 arguments.");
        object? hostArg = args[0].ToObject();
        var port = SvArgs.Long(args, 1, "net.isPortOpen");
        if (port < 1 || port > 65535)
            throw new RuntimeError("Port must be between 1 and 65535.", errorType: StashErrorTypes.ValueError);
        int timeout = args.Length > 2 ? (int)SvArgs.Long(args, 2, "net.isPortOpen") : 3000;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
            cts.CancelAfter(timeout);
            using var client = new TcpClient();
            if (hostArg is StashIpAddress ip)
                client.ConnectAsync(ip.Address, (int)port, cts.Token).GetAwaiter().GetResult();
            else if (hostArg is string hostname)
                client.ConnectAsync(hostname, (int)port, cts.Token).GetAwaiter().GetResult();
            else
                throw new RuntimeError("First argument to 'net.isPortOpen' must be an IP address or hostname string.", errorType: StashErrorTypes.TypeError);
            return StashValue.FromBool(client.Connected);
        }
        catch (RuntimeError) { throw; }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return StashValue.False;
        }
    }


    /// <summary>Returns information about all network interfaces.</summary>
    [StashFn]
    private static List<StashValue> Interfaces()
        => BuildInterfaceList(NetworkInterface.GetAllNetworkInterfaces());


    /// <summary>Returns information about a specific network interface.</summary>
    /// <param name="name">The interface name (e.g., "eth0", "wlan0").</param>
    [StashFn(ReturnType = "InterfaceInfo", Name = "interface")]
    private static StashValue Interface(string name)
    {
        var match = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(ni => ni.Name == name);
        if (match is null)
            throw new RuntimeError($"Network interface '{name}' not found.", errorType: StashErrorTypes.IOError);
        return BuildInterfaceList([match])[0];
    }

    // ── Deprecated DNS aliases ─────────────────────────────────────────────────────────


    // ── Deprecated TCP aliases ─────────────────────────────────────────────────────────

    /// <summary>Deprecated. Use tcp.connect.</summary>
    // Raw = true: requires StashInstance (TcpConnection) polymorphism, not in typed table.
    [StashFn(Raw = true, ReturnType = "TcpConnection")]
    [StashDeprecated("tcp.connect")]
    private static StashValue TcpConnect(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpConnect(ctx, args, "net.tcpConnect");

    /// <summary>Deprecated. Use tcp.send.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection), not in typed table.
    [StashFn(Raw = true, ReturnType = "int")]
    [StashDeprecated("tcp.send")]
    private static StashValue TcpSend(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpSend(ctx, args, "net.tcpSend");

    /// <summary>Deprecated. Use tcp.recv.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection), not in typed table.
    [StashFn(Raw = true, ReturnType = "string")]
    [StashDeprecated("tcp.recv")]
    private static StashValue TcpRecv(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpRecv(ctx, args, "net.tcpRecv");

    /// <summary>Deprecated. Use tcp.close.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection), not in typed table.
    [StashFn(Raw = true, ReturnType = "null")]
    [StashDeprecated("tcp.close")]
    private static StashValue TcpClose(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpClose(ctx, args, "net.tcpClose");

    /// <summary>Deprecated. Use tcp.listen.</summary>
    // Raw = true: requires IStashCallable handler arg with StashInstance result — mixed types.
    [StashFn(Raw = true, ReturnType = "null")]
    [StashDeprecated("tcp.listen")]
    private static StashValue TcpListen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpListen(ctx, args, "net.tcpListen");


    // ── Deprecated UDP aliases ─────────────────────────────────────────────────────────

    /// <summary>Deprecated. Use udp.send.</summary>
    /// <param name="host">The destination hostname or IP address.</param>
    /// <param name="port">The destination port (1-65535).</param>
    /// <param name="data">The string data to send.</param>
    [StashFn]
    [StashDeprecated("udp.send")]
    private static long UdpSend(IInterpreterContext ctx, string host, long port, string data)
    {
        StashValue[] args = [StashValue.FromObj(host), StashValue.FromInt(port), StashValue.FromObj(data)];
        return NetSocketImpl.UdpSend(ctx, args, "net.udpSend").AsInt;
    }

    /// <summary>Deprecated. Use udp.recv.</summary>
    /// <param name="port">The port to listen on (1-65535).</param>
    /// <param name="timeout">Optional timeout in milliseconds (default 5000).</param>
    [StashFn(ReturnType = "UdpMessage")]
    [StashDeprecated("udp.recv")]
    private static StashValue UdpRecv(IInterpreterContext ctx, long port, long timeout = 5000)
    {
        StashValue[] args = [StashValue.FromInt(port), StashValue.FromInt(timeout)];
        return NetSocketImpl.UdpRecv(ctx, args, "net.udpRecv");
    }


    /// <summary>Deprecated. Use dns.resolveMx.</summary>
    /// <param name="domain">The domain to query.</param>
    [StashFn(ReturnType = "array")]
    [StashDeprecated("dns.resolveMx")]
    private static StashValue ResolveMx(IInterpreterContext ctx, string domain)
    {
        StashValue[] args = [StashValue.FromObj(domain)];
        return NetSocketImpl.DnsResolveMx(ctx, args, "net.resolveMx");
    }

    /// <summary>Deprecated. Use dns.resolveTxt.</summary>
    /// <param name="domain">The domain to query.</param>
    [StashFn(ReturnType = "array")]
    [StashDeprecated("dns.resolveTxt")]
    private static StashValue ResolveTxt(IInterpreterContext ctx, string domain)
    {
        StashValue[] args = [StashValue.FromObj(domain)];
        return NetSocketImpl.DnsResolveTxt(ctx, args, "net.resolveTxt");
    }


    // ── Deprecated WebSocket aliases ─────────────────────────────────────────────────────────

    /// <summary>Deprecated. Use ws.connect.</summary>
    // Raw = true: returns StashFuture — async returns are out of scope for Phase A.
    [StashFn(Raw = true, ReturnType = "WsConnection")]
    [StashDeprecated("ws.connect")]
    private static StashValue WsConnect(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsConnect(ctx, args, "net.wsConnect");

    /// <summary>Deprecated. Use ws.send.</summary>
    // Raw = true: first arg is StashInstance (WsConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "int")]
    [StashDeprecated("ws.send")]
    private static StashValue WsSend(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsSend(ctx, args, "net.wsSend");

    /// <summary>Deprecated. Use ws.sendBinary.</summary>
    // Raw = true: first arg is StashInstance (WsConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "int")]
    [StashDeprecated("ws.sendBinary")]
    private static StashValue WsSendBinary(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsSendBinary(ctx, args, "net.wsSendBinary");

    /// <summary>Deprecated. Use ws.recv.</summary>
    // Raw = true: first arg is StashInstance (WsConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "WsMessage")]
    [StashDeprecated("ws.recv")]
    private static StashValue WsRecv(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsRecv(ctx, args, "net.wsRecv");

    /// <summary>Deprecated. Use ws.close.</summary>
    // Raw = true: first arg is StashInstance (WsConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "null")]
    [StashDeprecated("ws.close")]
    private static StashValue WsClose(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsClose(ctx, args, "net.wsClose");

    /// <summary>Deprecated. Use ws.state.</summary>
    // Raw = true: first arg is StashInstance (WsConnection), not in typed table.
    [StashFn(Raw = true, ReturnType = "WsConnectionState")]
    [StashDeprecated("ws.state")]
    private static StashValue WsState(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsState(ctx, args, "net.wsState");

    /// <summary>Deprecated. Use ws.isOpen.</summary>
    // Raw = true: first arg is StashInstance (WsConnection), not in typed table.
    [StashFn(Raw = true, ReturnType = "bool")]
    [StashDeprecated("ws.isOpen")]
    private static StashValue WsIsOpen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsIsOpen(ctx, args, "net.wsIsOpen");


    /// <summary>Deprecated. Use tcp.connectAsync.</summary>
    // Raw = true: returns StashFuture — async returns are out of scope for Phase A.
    [StashFn(Raw = true, ReturnType = "TcpConnection")]
    [StashDeprecated("tcp.connectAsync")]
    private static StashValue TcpConnectAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpConnectAsync(ctx, args, "net.tcpConnectAsync");

    /// <summary>Deprecated. Use tcp.sendAsync.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "int")]
    [StashDeprecated("tcp.sendAsync")]
    private static StashValue TcpSendAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpSendAsync(ctx, args, "net.tcpSendAsync");

    /// <summary>Deprecated. Use tcp.sendBytesAsync.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "int")]
    [StashDeprecated("tcp.sendBytesAsync")]
    private static StashValue TcpSendBytesAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpSendBytesAsync(ctx, args, "net.tcpSendBytesAsync");

    /// <summary>Deprecated. Use tcp.recvAsync.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "string")]
    [StashDeprecated("tcp.recvAsync")]
    private static StashValue TcpRecvAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpRecvAsync(ctx, args, "net.tcpRecvAsync");

    /// <summary>Deprecated. Use tcp.recvBytesAsync.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "byte[]")]
    [StashDeprecated("tcp.recvBytesAsync")]
    private static StashValue TcpRecvBytesAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpRecvBytesAsync(ctx, args, "net.tcpRecvBytesAsync");

    /// <summary>Deprecated. Use tcp.closeAsync.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection) and returns StashFuture — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "null")]
    [StashDeprecated("tcp.closeAsync")]
    private static StashValue TcpCloseAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpCloseAsync(ctx, args, "net.tcpCloseAsync");

    /// <summary>Deprecated. Use tcp.isOpen.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection), not in typed table.
    [StashFn(Raw = true, ReturnType = "bool")]
    [StashDeprecated("tcp.isOpen")]
    private static StashValue TcpIsOpen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpIsOpen(ctx, args, "net.tcpIsOpen");

    /// <summary>Deprecated. Use tcp.state.</summary>
    // Raw = true: first arg is StashInstance (TcpConnection), not in typed table.
    [StashFn(Raw = true, ReturnType = "TcpConnectionState")]
    [StashDeprecated("tcp.state")]
    private static StashValue TcpState(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpState(ctx, args, "net.tcpState");

    /// <summary>Deprecated. Use tcp.listenAsync.</summary>
    // Raw = true: returns StashFuture wrapping StashInstance (TcpServer) — Phase A limitations.
    [StashFn(Raw = true, ReturnType = "TcpServer")]
    [StashDeprecated("tcp.listenAsync")]
    private static StashValue TcpListenAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpListenAsync(ctx, args, "net.tcpListenAsync");

    /// <summary>Deprecated. Use tcp.serverClose.</summary>
    // Raw = true: first arg is StashInstance (TcpServer), not in typed table.
    [StashFn(Raw = true, ReturnType = "null")]
    [StashDeprecated("tcp.serverClose")]
    private static StashValue TcpServerClose(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpServerClose(ctx, args, "net.tcpServerClose");

    // ── Private helpers ─────────────────────────────────────────────────────────────────────────────


    private static (byte[] MaskBytes, byte[] NetworkBytes, byte[] BroadcastBytes, byte[] WildcardBytes) ComputeSubnetComponents(StashIpAddress ip, string funcName = "net.subnetInfo")
    {
        if (ip.PrefixLength is null)
            throw new RuntimeError($"'{funcName}' requires a CIDR IP address (e.g., @192.168.1.0/24).", errorType: StashErrorTypes.TypeError);

        int prefix = ip.PrefixLength.Value;
        byte[] addrBytes = ip.GetBytes();
        int totalBits = addrBytes.Length * 8;

        byte[] maskBytes = new byte[addrBytes.Length];
        for (int i = 0; i < totalBits; i++)
        {
            if (i < prefix)
                maskBytes[i / 8] |= (byte)(0x80 >> (i % 8));
        }

        byte[] wildcardBytes = new byte[addrBytes.Length];
        for (int i = 0; i < maskBytes.Length; i++)
            wildcardBytes[i] = (byte)~maskBytes[i];

        byte[] networkBytes = new byte[addrBytes.Length];
        for (int i = 0; i < addrBytes.Length; i++)
            networkBytes[i] = (byte)(addrBytes[i] & maskBytes[i]);

        byte[] broadcastBytes = new byte[addrBytes.Length];
        for (int i = 0; i < addrBytes.Length; i++)
            broadcastBytes[i] = (byte)(networkBytes[i] | wildcardBytes[i]);

        return (maskBytes, networkBytes, broadcastBytes, wildcardBytes);
    }

    private static long ComputeHostCount(StashIpAddress ip)
    {
        int prefix = ip.PrefixLength!.Value;
        int totalBits = ip.Version == 4 ? 32 : 128;
        int hostBits = totalBits - prefix;

        if (ip.Version == 4)
        {
            if (hostBits == 0) return 1;
            if (hostBits == 1) return 2;
            return (1L << hostBits) - 2;
        }
        else
        {
            if (hostBits >= 63) return long.MaxValue;
            return 1L << hostBits;
        }
    }

    private static List<StashValue> BuildInterfaceList(IEnumerable<NetworkInterface> interfaces)
    {
        var result = new List<StashValue>();

        foreach (var ni in interfaces)
        {
            var props = ni.GetIPProperties();
            StashIpAddress? ipv4 = null;
            StashIpAddress? ipv6 = null;
            StashIpAddress? subnetAddr = null;

            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork && ipv4 is null)
                {
                    ipv4 = new StashIpAddress(addr.Address, null);
                    int prefixLen = addr.PrefixLength;
                    byte[] addrBytes = addr.Address.GetAddressBytes();
                    byte[] maskBytes = new byte[4];
                    for (int i = 0; i < 32; i++)
                    {
                        if (i < prefixLen)
                            maskBytes[i / 8] |= (byte)(0x80 >> (i % 8));
                    }
                    byte[] networkBytes = new byte[4];
                    for (int j = 0; j < 4; j++)
                        networkBytes[j] = (byte)(addrBytes[j] & maskBytes[j]);
                    subnetAddr = new StashIpAddress(networkBytes, prefixLen);
                }
                else if (addr.Address.AddressFamily == AddressFamily.InterNetworkV6 && ipv6 is null)
                {
                    ipv6 = new StashIpAddress(addr.Address, null);
                }
            }

            StashIpAddress? gateway = null;
            foreach (var gw in props.GatewayAddresses)
            {
                gateway = new StashIpAddress(gw.Address, null);
                break;
            }

            string mac = ni.GetPhysicalAddress().ToString();
            if (mac.Length == 12)
                mac = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));

            result.Add(StashValue.FromObj(new StashInstance("InterfaceInfo", new Dictionary<string, StashValue>
            {
                ["name"] = StashValue.FromObj(ni.Name),
                ["ip"] = ipv4 is not null ? StashValue.FromObj(ipv4) : StashValue.Null,
                ["ipv6"] = ipv6 is not null ? StashValue.FromObj(ipv6) : StashValue.Null,
                ["mac"] = StashValue.FromObj(mac),
                ["gateway"] = gateway is not null ? StashValue.FromObj(gateway) : StashValue.Null,
                ["subnet"] = subnetAddr is not null ? StashValue.FromObj(subnetAddr) : StashValue.Null,
                ["status"] = StashValue.FromObj(ni.OperationalStatus.ToString()),
                ["type"] = StashValue.FromObj(ni.NetworkInterfaceType.ToString()),
                ["up"] = StashValue.FromBool(ni.OperationalStatus == OperationalStatus.Up),
            })));
        }

        return result;
    }
}

