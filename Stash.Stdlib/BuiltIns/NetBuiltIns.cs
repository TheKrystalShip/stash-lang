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
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

public static class NetBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("net");
        ns.RequiresCapability(StashCapabilities.Network);

        // net.subnetInfo(ip) — Returns subnet details for a CIDR IP address.
        ns.Function("subnetInfo", [Param("ip", "ip")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
        }, returnType: "SubnetInfo", documentation: "Returns subnet details for a CIDR IP address.");

        // net.mask(ip) — Returns the subnet mask for a CIDR IP address.
        ns.Function("mask", [Param("ip", "ip")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var ip = SvArgs.IpAddress(args, 0, "net.mask");
            var (maskBytes, _, _, _) = ComputeSubnetComponents(ip, "net.mask");
            return StashValue.FromObj(new StashIpAddress(maskBytes, null));
        }, returnType: "ip", documentation: "Returns the subnet mask for a CIDR IP address.");

        // net.network(ip) — Returns the network address for a CIDR IP address.
        ns.Function("network", [Param("ip", "ip")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var ip = SvArgs.IpAddress(args, 0, "net.network");
            var (_, networkBytes, _, _) = ComputeSubnetComponents(ip, "net.network");
            return StashValue.FromObj(new StashIpAddress(networkBytes, ip.PrefixLength!.Value));
        }, returnType: "ip", documentation: "Returns the network address for a CIDR IP address.");

        // net.broadcast(ip) — Returns the broadcast address for a CIDR IP address.
        ns.Function("broadcast", [Param("ip", "ip")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var ip = SvArgs.IpAddress(args, 0, "net.broadcast");
            var (_, _, broadcastBytes, _) = ComputeSubnetComponents(ip, "net.broadcast");
            return StashValue.FromObj(new StashIpAddress(broadcastBytes, null));
        }, returnType: "ip", documentation: "Returns the broadcast address for a CIDR IP address.");

        // net.hostCount(ip) — Returns the number of usable host addresses in a CIDR subnet.
        ns.Function("hostCount", [Param("ip", "ip")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var ip = SvArgs.IpAddress(args, 0, "net.hostCount");
            ComputeSubnetComponents(ip, "net.hostCount"); // validates PrefixLength
            return StashValue.FromInt(ComputeHostCount(ip));
        }, returnType: "int", documentation: "Returns the number of usable host addresses in a CIDR subnet.");

        // net.resolve(hostname) — Deprecated. Use dns.resolve.
        ns.Function("resolve", [Param("hostname", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsResolve(ctx, args, "net.resolve"),
            returnType: "ip",
            documentation: "Deprecated. Use dns.resolve.",
            deprecation: new DeprecationInfo("dns.resolve"));

        // net.resolveAll(hostname) — Deprecated. Use dns.resolveAll.
        ns.Function("resolveAll", [Param("hostname", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsResolveAll(ctx, args, "net.resolveAll"),
            returnType: "array",
            documentation: "Deprecated. Use dns.resolveAll.",
            deprecation: new DeprecationInfo("dns.resolveAll"));

        // net.reverseLookup(ip) — Deprecated. Use dns.reverseLookup.
        ns.Function("reverseLookup", [Param("ip", "ip")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsReverseLookup(ctx, args, "net.reverseLookup"),
            returnType: "string",
            documentation: "Deprecated. Use dns.reverseLookup.",
            deprecation: new DeprecationInfo("dns.reverseLookup"));

        // net.ping(host) — Sends an ICMP ping to a host and returns the result.
        ns.Function("ping", [Param("host", "ip")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
        }, returnType: "PingResult", documentation: "Sends an ICMP ping to a host and returns the result. On Linux, requires root or CAP_NET_RAW capability.\n@param host The IP address to ping.\n@return A PingResult with alive, latency, and ttl fields.");

        // net.isPortOpen(host, port, ?timeout) — Checks if a TCP port is open on a host.
        ns.Function("isPortOpen", [Param("host", "string|ip"), Param("port", "int"), Param("timeout", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        }, returnType: "bool", documentation: "Checks if a TCP port is open on a host.\n@param host The IP address or hostname string to check.\n@param port The port number (1-65535).\n@param timeout Optional timeout in milliseconds (default 3000).");

        // net.interfaces() — Returns information about all network interfaces.
        ns.Function("interfaces", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
            StashValue.FromObj(BuildInterfaceList(NetworkInterface.GetAllNetworkInterfaces())),
            returnType: "array", documentation: "Returns information about all network interfaces.\n@return An array of InterfaceInfo structs.");

        // net.interface(name) — Returns information about a specific network interface.
        ns.Function("interface", [Param("name", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var name = SvArgs.String(args, 0, "net.interface");
            var match = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.Name == name);
            if (match is null)
                throw new RuntimeError($"Network interface '{name}' not found.", errorType: StashErrorTypes.IOError);
            return StashValue.FromObj(BuildInterfaceList([match])[0]);
        }, returnType: "InterfaceInfo", documentation: "Returns information about a specific network interface.\n@param name The interface name (e.g., \"eth0\", \"wlan0\").\n@return An InterfaceInfo struct.");

        // net.tcpConnect(host, port, ?timeout) — Deprecated. Use tcp.connect.
        ns.Function("tcpConnect", [Param("host", "string"), Param("port", "int"), Param("timeout", "int")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpConnect(ctx, args, "net.tcpConnect"),
            returnType: "TcpConnection", isVariadic: true,
            documentation: "Deprecated. Use tcp.connect.",
            deprecation: new DeprecationInfo("tcp.connect"));

        // net.tcpSend(conn, data) — Deprecated. Use tcp.send.
        ns.Function("tcpSend", [Param("conn", "TcpConnection"), Param("data", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpSend(ctx, args, "net.tcpSend"),
            returnType: "int",
            documentation: "Deprecated. Use tcp.send.",
            deprecation: new DeprecationInfo("tcp.send"));

        // net.tcpRecv(conn, ?maxBytes) — Deprecated. Use tcp.recv.
        ns.Function("tcpRecv", [Param("conn", "TcpConnection"), Param("maxBytes", "int")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpRecv(ctx, args, "net.tcpRecv"),
            returnType: "string", isVariadic: true,
            documentation: "Deprecated. Use tcp.recv.",
            deprecation: new DeprecationInfo("tcp.recv"));

        // net.tcpClose(conn) — Deprecated. Use tcp.close.
        ns.Function("tcpClose", [Param("conn", "TcpConnection")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpClose(ctx, args, "net.tcpClose"),
            returnType: "null",
            documentation: "Deprecated. Use tcp.close.",
            deprecation: new DeprecationInfo("tcp.close"));

        // net.tcpListen(port, handler) — Deprecated. Use tcp.listen.
        ns.Function("tcpListen", [Param("port", "int"), Param("handler", "function")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpListen(ctx, args, "net.tcpListen"),
            returnType: "null",
            documentation: "Deprecated. Use tcp.listen.",
            deprecation: new DeprecationInfo("tcp.listen"));

        // net.udpSend(host, port, data) — Deprecated. Use udp.send.
        ns.Function("udpSend", [Param("host", "string"), Param("port", "int"), Param("data", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.UdpSend(ctx, args, "net.udpSend"),
            returnType: "int",
            documentation: "Deprecated. Use udp.send.",
            deprecation: new DeprecationInfo("udp.send"));

        // net.udpRecv(port, ?timeout) — Deprecated. Use udp.recv.
        ns.Function("udpRecv", [Param("port", "int"), Param("timeout", "int")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.UdpRecv(ctx, args, "net.udpRecv"),
            returnType: "UdpMessage", isVariadic: true,
            documentation: "Deprecated. Use udp.recv.",
            deprecation: new DeprecationInfo("udp.recv"));

        // net.resolveMx(domain) — Deprecated. Use dns.resolveMx.
        ns.Function("resolveMx", [Param("domain", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsResolveMx(ctx, args, "net.resolveMx"),
            returnType: "array",
            documentation: "Deprecated. Use dns.resolveMx.",
            deprecation: new DeprecationInfo("dns.resolveMx"));

        // net.resolveTxt(domain) — Deprecated. Use dns.resolveTxt.
        ns.Function("resolveTxt", [Param("domain", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.DnsResolveTxt(ctx, args, "net.resolveTxt"),
            returnType: "array",
            documentation: "Deprecated. Use dns.resolveTxt.",
            deprecation: new DeprecationInfo("dns.resolveTxt"));

        // net.wsConnect(url, ?options) — Deprecated. Use ws.connect.
        ns.Function("wsConnect", [Param("url", "string"), Param("options", "dict")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsConnect(ctx, args, "net.wsConnect"),
            returnType: "WsConnection", isVariadic: true,
            documentation: "Deprecated. Use ws.connect.",
            deprecation: new DeprecationInfo("ws.connect"));

        // net.wsSend(conn, data) — Deprecated. Use ws.send.
        ns.Function("wsSend", [Param("conn", "WsConnection"), Param("data", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsSend(ctx, args, "net.wsSend"),
            returnType: "int",
            documentation: "Deprecated. Use ws.send.",
            deprecation: new DeprecationInfo("ws.send"));

        // net.wsSendBinary(conn, data) — Deprecated. Use ws.sendBinary.
        ns.Function("wsSendBinary", [Param("conn", "WsConnection"), Param("data", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsSendBinary(ctx, args, "net.wsSendBinary"),
            returnType: "int",
            documentation: "Deprecated. Use ws.sendBinary.",
            deprecation: new DeprecationInfo("ws.sendBinary"));

        // net.wsRecv(conn, ?timeout) — Deprecated. Use ws.recv.
        ns.Function("wsRecv", [Param("conn", "WsConnection"), Param("timeout", "duration")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsRecv(ctx, args, "net.wsRecv"),
            returnType: "WsMessage", isVariadic: true,
            documentation: "Deprecated. Use ws.recv.",
            deprecation: new DeprecationInfo("ws.recv"));

        // net.wsClose(conn, ?code, ?reason) — Deprecated. Use ws.close.
        ns.Function("wsClose", [Param("conn", "WsConnection"), Param("code", "int"), Param("reason", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsClose(ctx, args, "net.wsClose"),
            returnType: "null", isVariadic: true,
            documentation: "Deprecated. Use ws.close.",
            deprecation: new DeprecationInfo("ws.close"));

        // net.wsState(conn) — Deprecated. Use ws.state.
        ns.Function("wsState", [Param("conn", "WsConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsState(ctx, args, "net.wsState"),
            returnType: "WsConnectionState",
            documentation: "Deprecated. Use ws.state.",
            deprecation: new DeprecationInfo("ws.state"));

        // net.wsIsOpen(conn) — Deprecated. Use ws.isOpen.
        ns.Function("wsIsOpen", [Param("conn", "WsConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsIsOpen(ctx, args, "net.wsIsOpen"),
            returnType: "bool",
            documentation: "Deprecated. Use ws.isOpen.",
            deprecation: new DeprecationInfo("ws.isOpen"));

        // net.tcpConnectAsync(host, port, ?options) — Deprecated. Use tcp.connectAsync.
        ns.Function("tcpConnectAsync", [Param("host", "string"), Param("port", "int"), Param("options", "TcpConnectOptions")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpConnectAsync(ctx, args, "net.tcpConnectAsync"),
            returnType: "TcpConnection", isVariadic: true,
            documentation: "Deprecated. Use tcp.connectAsync.",
            deprecation: new DeprecationInfo("tcp.connectAsync"));

        // net.tcpSendAsync(conn, data) — Deprecated. Use tcp.sendAsync.
        ns.Function("tcpSendAsync", [Param("conn", "TcpConnection"), Param("data", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpSendAsync(ctx, args, "net.tcpSendAsync"),
            returnType: "int",
            documentation: "Deprecated. Use tcp.sendAsync.",
            deprecation: new DeprecationInfo("tcp.sendAsync"));

        // net.tcpSendBytesAsync(conn, data) — Deprecated. Use tcp.sendBytesAsync.
        ns.Function("tcpSendBytesAsync", [Param("conn", "TcpConnection"), Param("data", "byte[]")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpSendBytesAsync(ctx, args, "net.tcpSendBytesAsync"),
            returnType: "int",
            documentation: "Deprecated. Use tcp.sendBytesAsync.",
            deprecation: new DeprecationInfo("tcp.sendBytesAsync"));

        // net.tcpRecvAsync(conn, ?options) — Deprecated. Use tcp.recvAsync.
        ns.Function("tcpRecvAsync", [Param("conn", "TcpConnection"), Param("options", "TcpRecvOptions")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpRecvAsync(ctx, args, "net.tcpRecvAsync"),
            returnType: "string", isVariadic: true,
            documentation: "Deprecated. Use tcp.recvAsync.",
            deprecation: new DeprecationInfo("tcp.recvAsync"));

        // net.tcpRecvBytesAsync(conn, ?options) — Deprecated. Use tcp.recvBytesAsync.
        ns.Function("tcpRecvBytesAsync", [Param("conn", "TcpConnection"), Param("options", "TcpRecvOptions")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpRecvBytesAsync(ctx, args, "net.tcpRecvBytesAsync"),
            returnType: "byte[]", isVariadic: true,
            documentation: "Deprecated. Use tcp.recvBytesAsync.",
            deprecation: new DeprecationInfo("tcp.recvBytesAsync"));

        // net.tcpCloseAsync(conn) — Deprecated. Use tcp.closeAsync.
        ns.Function("tcpCloseAsync", [Param("conn", "TcpConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpCloseAsync(ctx, args, "net.tcpCloseAsync"),
            returnType: "null",
            documentation: "Deprecated. Use tcp.closeAsync.",
            deprecation: new DeprecationInfo("tcp.closeAsync"));

        // net.tcpIsOpen(conn) — Deprecated. Use tcp.isOpen.
        ns.Function("tcpIsOpen", [Param("conn", "TcpConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpIsOpen(ctx, args, "net.tcpIsOpen"),
            returnType: "bool",
            documentation: "Deprecated. Use tcp.isOpen.",
            deprecation: new DeprecationInfo("tcp.isOpen"));

        // net.tcpState(conn) — Deprecated. Use tcp.state.
        ns.Function("tcpState", [Param("conn", "TcpConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpState(ctx, args, "net.tcpState"),
            returnType: "TcpConnectionState",
            documentation: "Deprecated. Use tcp.state.",
            deprecation: new DeprecationInfo("tcp.state"));

        // net.tcpListenAsync(port, handler) — Deprecated. Use tcp.listenAsync.
        ns.Function("tcpListenAsync", [Param("port", "int"), Param("handler", "function")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpListenAsync(ctx, args, "net.tcpListenAsync"),
            returnType: "TcpServer",
            documentation: "Deprecated. Use tcp.listenAsync.",
            deprecation: new DeprecationInfo("tcp.listenAsync"));

        // net.tcpServerClose(server) — Deprecated. Use tcp.serverClose.
        ns.Function("tcpServerClose", [Param("server", "TcpServer")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpServerClose(ctx, args, "net.tcpServerClose"),
            returnType: "null",
            documentation: "Deprecated. Use tcp.serverClose.",
            deprecation: new DeprecationInfo("tcp.serverClose"));

        // Struct definitions
        ns.Struct("SubnetInfo", [
            new BuiltInField("network", "ip"),
            new BuiltInField("broadcast", "ip"),
            new BuiltInField("mask", "ip"),
            new BuiltInField("wildcard", "ip"),
            new BuiltInField("hostCount", "int"),
            new BuiltInField("firstHost", "ip"),
            new BuiltInField("lastHost", "ip"),
        ]);

        ns.Struct("PingResult", [
            new BuiltInField("alive", "bool"),
            new BuiltInField("latency", "float"),
            new BuiltInField("ttl", "int"),
        ]);

        ns.Struct("InterfaceInfo", [
            new BuiltInField("name", "string"),
            new BuiltInField("ip", "ip"),
            new BuiltInField("ipv6", "ip"),
            new BuiltInField("mac", "string"),
            new BuiltInField("gateway", "ip"),
            new BuiltInField("subnet", "ip"),
            new BuiltInField("status", "string"),
            new BuiltInField("type", "string"),
            new BuiltInField("up", "bool"),
        ]);

        ns.Struct("TcpConnection", [
            new BuiltInField("host", "string"),
            new BuiltInField("port", "int"),
            new BuiltInField("localPort", "int"),
        ]);

        ns.Struct("UdpMessage", [
            new BuiltInField("data", "string"),
            new BuiltInField("host", "string"),
            new BuiltInField("port", "int"),
        ]);

        ns.Struct("MxRecord", [
            new BuiltInField("priority", "int"),
            new BuiltInField("exchange", "string"),
        ]);

        ns.Struct("WsConnection", [
            new BuiltInField("url", "string"),
            new BuiltInField("protocol", "string"),
        ]);

        ns.Struct("WsMessage", [
            new BuiltInField("data", "string"),
            new BuiltInField("type", "string"),
            new BuiltInField("close", "bool"),
        ]);

        ns.Struct("TcpConnectOptions", [
            new BuiltInField("timeoutMs", "int"),
            new BuiltInField("tls", "bool"),
            new BuiltInField("noDelay", "bool"),
            new BuiltInField("keepAlive", "bool"),
            new BuiltInField("tlsVerify", "bool"),
            new BuiltInField("tlsSni", "string"),
        ]);

        ns.Struct("TcpRecvOptions", [
            new BuiltInField("maxBytes", "int"),
            new BuiltInField("timeoutMs", "int"),
        ]);

        ns.Struct("TcpServer", [
            new BuiltInField("port", "int"),
            new BuiltInField("active", "bool"),
        ]);

        ns.Enum("WsConnectionState", ["Connecting", "Open", "Closing", "Closed"]);

        ns.Enum("TcpConnectionState", ["Open", "Closed"]);

        return ns.Build();
    }

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

