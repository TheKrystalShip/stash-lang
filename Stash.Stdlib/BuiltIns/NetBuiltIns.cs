namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

public static class NetBuiltIns
{
    /// <summary>Maps WsConnection instances to their underlying ClientWebSocket. Uses weak references to allow GC.</summary>
    private static readonly ConditionalWeakTable<StashInstance, ClientWebSocket> _wsClients = new();

    /// <summary>Maps async TcpConnection instances to their underlying TcpClient.</summary>
    private static readonly ConditionalWeakTable<StashInstance, TcpClient> _tcpAsyncClients = new();

    /// <summary>Maps TcpServer instances to their underlying TcpListener.</summary>
    private static readonly ConditionalWeakTable<StashInstance, TcpListener> _tcpServers = new();

    public static NamespaceDefinition Define()
    {
        var wsStateEnum = new StashEnum("WsConnectionState", new List<string> { "Connecting", "Open", "Closing", "Closed" });
        var tcpStateEnum = new StashEnum("TcpConnectionState", new List<string> { "Open", "Closed" });
        var ns = new NamespaceBuilder("net");
        ns.RequiresCapability(StashCapabilities.Network);

        // net.subnetInfo(ip) — Returns subnet details for a CIDR IP address.
        ns.Function("subnetInfo", [Param("ip", "ip")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var ip = SvArgs.IpAddress(args, 0, "net.subnetInfo");
            if (ip.PrefixLength is null)
                throw new RuntimeError("'net.subnetInfo' requires a CIDR IP address (e.g., @192.168.1.0/24).");

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

        // net.resolve(hostname) — Resolves a hostname to its first IP address via DNS.
        ns.Function("resolve", [Param("hostname", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var hostname = SvArgs.String(args, 0, "net.resolve");
            try
            {
                var entry = Dns.GetHostEntry(hostname);
                if (entry.AddressList.Length == 0)
                    throw new RuntimeError($"No DNS records found for '{hostname}'.");
                return StashValue.FromObj(new StashIpAddress(entry.AddressList[0], null));
            }
            catch (SocketException ex)
            {
                throw new RuntimeError($"DNS resolution failed for '{hostname}': {ex.Message}");
            }
        }, returnType: "ip", documentation: "Resolves a hostname to its first IP address via DNS.\n@param hostname The hostname to resolve.\n@return The first resolved IP address.");

        // net.resolveAll(hostname) — Resolves a hostname to all IP addresses via DNS.
        ns.Function("resolveAll", [Param("hostname", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var hostname = SvArgs.String(args, 0, "net.resolveAll");
            try
            {
                var entry = Dns.GetHostEntry(hostname);
                return StashValue.FromObj(entry.AddressList.Select(a => (object?)new StashIpAddress(a, null)).ToList());
            }
            catch (SocketException ex)
            {
                throw new RuntimeError($"DNS resolution failed for '{hostname}': {ex.Message}");
            }
        }, returnType: "array", documentation: "Resolves a hostname to all IP addresses via DNS.\n@param hostname The hostname to resolve.\n@return An array of resolved IP addresses.");

        // net.reverseLookup(ip) — Performs reverse DNS lookup for an IP address.
        ns.Function("reverseLookup", [Param("ip", "ip")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var ip = SvArgs.IpAddress(args, 0, "net.reverseLookup");
            try
            {
                var entry = Dns.GetHostEntry(ip.Address);
                return StashValue.FromObj(entry.HostName);
            }
            catch (SocketException ex)
            {
                throw new RuntimeError($"Reverse DNS lookup failed for '{ip}': {ex.Message}");
            }
        }, returnType: "string", documentation: "Performs reverse DNS lookup for an IP address.\n@param ip The IP address to lookup.\n@return The hostname associated with the IP.");

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
                throw new RuntimeError("Port must be between 1 and 65535.");
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
                    throw new RuntimeError("First argument to 'net.isPortOpen' must be an IP address or hostname string.");
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
                throw new RuntimeError($"Network interface '{name}' not found.");
            return StashValue.FromObj(BuildInterfaceList([match])[0]);
        }, returnType: "InterfaceInfo", documentation: "Returns information about a specific network interface.\n@param name The interface name (e.g., \"eth0\", \"wlan0\").\n@return An InterfaceInfo struct.");

        // net.tcpConnect(host, port, ?timeout) — Creates a TCP connection. Returns a TcpConnection struct.
        ns.Function("tcpConnect", [Param("host", "string"), Param("port", "int"), Param("timeout", "int")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("net.tcpConnect: expected 2 or 3 arguments.");
            var host = SvArgs.String(args, 0, "net.tcpConnect");
            var port = SvArgs.Long(args, 1, "net.tcpConnect");
            if (port < 1 || port > 65535)
                throw new RuntimeError("net.tcpConnect: port must be between 1 and 65535.");
            int timeout = args.Length > 2 ? (int)SvArgs.Long(args, 2, "net.tcpConnect") : 5000;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
                cts.CancelAfter(timeout);
                var client = new TcpClient();
                client.ConnectAsync(host, (int)port, cts.Token).GetAwaiter().GetResult();
                int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
                var conn = new StashInstance("TcpConnection", new Dictionary<string, StashValue>
                {
                    ["host"] = StashValue.FromObj(host),
                    ["port"] = StashValue.FromInt(port),
                    ["localPort"] = StashValue.FromInt(localPort),
                    ["_client"] = StashValue.FromObj(client),
                });
                return StashValue.FromObj(conn);
            }
            catch (RuntimeError) { throw; }
            catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                throw new RuntimeError($"net.tcpConnect: failed to connect to '{host}:{port}': {ex.Message}");
            }
        }, returnType: "TcpConnection", isVariadic: true, documentation: "Creates a TCP connection to a host and port.\n@param host The hostname or IP address.\n@param port The port number (1-65535).\n@param timeout Optional timeout in milliseconds (default 5000).\n@return A TcpConnection struct.");

        // net.tcpSend(conn, data) — Sends string data over a TCP connection.
        ns.Function("tcpSend", [Param("conn", "TcpConnection"), Param("data", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 2)
                throw new RuntimeError("net.tcpSend: expected 2 arguments.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpSend: first argument must be a TcpConnection.");
            var data = SvArgs.String(args, 1, "net.tcpSend");

            if (conn.GetField("_client", null).ToObject() is not TcpClient client)
                throw new RuntimeError("net.tcpSend: invalid or closed TcpConnection.");

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                var stream = client.GetStream();
                stream.Write(bytes, 0, bytes.Length);
                return StashValue.FromInt(bytes.Length);
            }
            catch (Exception ex)
            {
                throw new RuntimeError($"net.tcpSend: send failed: {ex.Message}");
            }
        }, returnType: "int", documentation: "Sends string data over a TCP connection.\n@param conn The TcpConnection to send data on.\n@param data The string data to send.\n@return The number of bytes sent.");

        // net.tcpRecv(conn, ?maxBytes) — Receives data from a TCP connection.
        ns.Function("tcpRecv", [Param("conn", "TcpConnection"), Param("maxBytes", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("net.tcpRecv: expected 1 or 2 arguments.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpRecv: first argument must be a TcpConnection.");
            int maxBytes = args.Length > 1 ? (int)SvArgs.Long(args, 1, "net.tcpRecv") : 4096;

            if (conn.GetField("_client", null).ToObject() is not TcpClient client)
                throw new RuntimeError("net.tcpRecv: invalid or closed TcpConnection.");

            try
            {
                var buffer = new byte[maxBytes];
                var stream = client.GetStream();
                int bytesRead = stream.Read(buffer, 0, maxBytes);
                return StashValue.FromObj(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            }
            catch (Exception ex)
            {
                throw new RuntimeError($"net.tcpRecv: receive failed: {ex.Message}");
            }
        }, returnType: "string", isVariadic: true, documentation: "Receives data from a TCP connection.\n@param conn The TcpConnection to receive data from.\n@param maxBytes Optional maximum bytes to read (default 4096).\n@return The received data as a string.");

        // net.tcpClose(conn) — Closes a TCP connection.
        ns.Function("tcpClose", [Param("conn", "TcpConnection")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 1)
                throw new RuntimeError("net.tcpClose: expected 1 argument.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpClose: argument must be a TcpConnection.");

            if (conn.GetField("_client", null).ToObject() is TcpClient client)
                client.Dispose();

            return StashValue.Null;
        }, returnType: "null", documentation: "Closes a TCP connection and releases its resources.\n@param conn The TcpConnection to close.");

        // net.tcpListen(port, handler) — Starts a TCP server, accepts one connection, invokes handler, then stops.
        ns.Function("tcpListen", [Param("port", "int"), Param("handler", "function")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 2)
                throw new RuntimeError("net.tcpListen: expected 2 arguments.");
            var port = SvArgs.Long(args, 0, "net.tcpListen");
            if (port < 1 || port > 65535)
                throw new RuntimeError("net.tcpListen: port must be between 1 and 65535.");
            var handler = SvArgs.Callable(args, 1, "net.tcpListen");

            var listener = new TcpListener(IPAddress.Any, (int)port);
            try
            {
                listener.Start();
                using var clientSocket = listener.AcceptTcpClient();
                int localPort = ((IPEndPoint)clientSocket.Client.LocalEndPoint!).Port;
                string remoteHost = ((IPEndPoint)clientSocket.Client.RemoteEndPoint!).Address.ToString();
                int remotePort = ((IPEndPoint)clientSocket.Client.RemoteEndPoint!).Port;

                var conn = new StashInstance("TcpConnection", new Dictionary<string, StashValue>
                {
                    ["host"] = StashValue.FromObj(remoteHost),
                    ["port"] = StashValue.FromInt(remotePort),
                    ["localPort"] = StashValue.FromInt(localPort),
                    ["_client"] = StashValue.FromObj(clientSocket),
                });
                ctx.InvokeCallbackDirect(handler, new StashValue[] { StashValue.FromObj(conn) });
            }
            catch (RuntimeError) { throw; }
            catch (Exception ex)
            {
                throw new RuntimeError($"net.tcpListen: failed on port {port}: {ex.Message}");
            }
            finally
            {
                listener.Stop();
            }
            return StashValue.Null;
        }, returnType: "null", documentation: "Starts a TCP listener on a port, accepts one connection, invokes the handler with a TcpConnection, then stops.\n@param port The port to listen on (1-65535).\n@param handler A function that receives the TcpConnection.");

        // net.udpSend(host, port, data) — Sends a UDP datagram.
        ns.Function("udpSend", [Param("host", "string"), Param("port", "int"), Param("data", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 3)
                throw new RuntimeError("net.udpSend: expected 3 arguments.");
            var host = SvArgs.String(args, 0, "net.udpSend");
            var port = SvArgs.Long(args, 1, "net.udpSend");
            if (port < 1 || port > 65535)
                throw new RuntimeError("net.udpSend: port must be between 1 and 65535.");
            var data = SvArgs.String(args, 2, "net.udpSend");

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                using var udp = new UdpClient();
                udp.Send(bytes, bytes.Length, host, (int)port);
                return StashValue.FromInt(bytes.Length);
            }
            catch (Exception ex)
            {
                throw new RuntimeError($"net.udpSend: send failed: {ex.Message}");
            }
        }, returnType: "int", documentation: "Sends a UDP datagram to a host and port.\n@param host The destination hostname or IP address.\n@param port The destination port (1-65535).\n@param data The string data to send.\n@return The number of bytes sent.");

        // net.udpRecv(port, ?timeout) — Listens for one UDP datagram on a port.
        ns.Function("udpRecv", [Param("port", "int"), Param("timeout", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("net.udpRecv: expected 1 or 2 arguments.");
            var port = SvArgs.Long(args, 0, "net.udpRecv");
            if (port < 1 || port > 65535)
                throw new RuntimeError("net.udpRecv: port must be between 1 and 65535.");
            int timeout = args.Length > 1 ? (int)SvArgs.Long(args, 1, "net.udpRecv") : 5000;

            try
            {
                using var udp = new UdpClient((int)port);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
                cts.CancelAfter(timeout);
                var result = udp.ReceiveAsync(cts.Token).AsTask().GetAwaiter().GetResult();
                string data = Encoding.UTF8.GetString(result.Buffer);
                string senderHost = result.RemoteEndPoint.Address.ToString();
                int senderPort = result.RemoteEndPoint.Port;
                return StashValue.FromObj(new StashInstance("UdpMessage", new Dictionary<string, StashValue>
                {
                    ["data"] = StashValue.FromObj(data),
                    ["host"] = StashValue.FromObj(senderHost),
                    ["port"] = StashValue.FromInt(senderPort),
                }));
            }
            catch (RuntimeError) { throw; }
            catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                throw new RuntimeError($"net.udpRecv: receive failed: {ex.Message}");
            }
        }, returnType: "UdpMessage", isVariadic: true, documentation: "Listens on a UDP port and receives one datagram.\n@param port The port to listen on (1-65535).\n@param timeout Optional timeout in milliseconds (default 5000).\n@return A UdpMessage struct with data, host, and port fields.");

        // net.resolveMx(domain) — Resolves MX records for a domain.
        ns.Function("resolveMx", [Param("domain", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var domain = SvArgs.String(args, 0, "net.resolveMx");
            try
            {
                var records = DnsQueryHelper.QueryRecords(domain, DnsQueryHelper.TypeMx);
                var list = new List<StashValue>();
                foreach (var (priority, data) in records)
                {
                    list.Add(StashValue.FromObj(new StashInstance("MxRecord", new Dictionary<string, StashValue>
                    {
                        ["priority"] = StashValue.FromInt(priority),
                        ["exchange"] = StashValue.FromObj(data),
                    })));
                }
                return StashValue.FromObj(list);
            }
            catch (RuntimeError) { throw; }
            catch (Exception ex)
            {
                throw new RuntimeError($"net.resolveMx: DNS query failed for '{domain}': {ex.Message}");
            }
        }, returnType: "array", documentation: "Resolves MX records for a domain.\n@param domain The domain to query.\n@return An array of MxRecord structs with priority and exchange fields.");

        // net.resolveTxt(domain) — Resolves TXT records for a domain.
        ns.Function("resolveTxt", [Param("domain", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var domain = SvArgs.String(args, 0, "net.resolveTxt");
            try
            {
                var records = DnsQueryHelper.QueryRecords(domain, DnsQueryHelper.TypeTxt);
                var list = records.Select(r => StashValue.FromObj(r.Data)).ToList();
                return StashValue.FromObj(list);
            }
            catch (RuntimeError) { throw; }
            catch (Exception ex)
            {
                throw new RuntimeError($"net.resolveTxt: DNS query failed for '{domain}': {ex.Message}");
            }
        }, returnType: "array", documentation: "Resolves TXT records for a domain.\n@param domain The domain to query.\n@return An array of strings containing the TXT record values.");

        // net.wsConnect(url, ?options) — Async. Opens a WebSocket connection.
        ns.Function("wsConnect", [Param("url", "string"), Param("options", "dict")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("net.wsConnect: expected 1 or 2 arguments.");
            var url = SvArgs.String(args, 0, "net.wsConnect");
            if (!url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                throw new RuntimeError("net.wsConnect: url must start with 'ws://' or 'wss://'.");

            StashDictionary? options = args.Length > 1 ? SvArgs.Dict(args, 1, "net.wsConnect") : null;

            long timeoutMs = 10_000;
            string? subprotocol = null;
            StashDictionary? headers = null;

            if (options is not null)
            {
                var timeoutVal = options.Get("timeout");
                if (!timeoutVal.IsNull && timeoutVal.IsObj && timeoutVal.AsObj is StashDuration td)
                    timeoutMs = td.TotalMilliseconds;
                var spVal = options.Get("subprotocol");
                if (!spVal.IsNull && spVal.IsObj && spVal.AsObj is string sp)
                    subprotocol = sp;
                var headersVal = options.Get("headers");
                if (!headersVal.IsNull && headersVal.IsObj && headersVal.AsObj is StashDictionary hd)
                    headers = hd;
            }

            var capturedUrl = url;
            var capturedSubprotocol = subprotocol;
            var capturedHeaders = headers;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
            if (timeoutMs > 0)
                cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            var dotnetTask = Task.Run<object?>(async () =>
            {
                var client = new ClientWebSocket();
                if (capturedSubprotocol is not null)
                    client.Options.AddSubProtocol(capturedSubprotocol);
                if (capturedHeaders is not null)
                {
                    foreach (var rawKey in capturedHeaders.RawKeys())
                    {
                        var val = capturedHeaders.Get(rawKey);
                        if (rawKey is string headerName && val.IsObj && val.AsObj is string headerVal)
                            client.Options.SetRequestHeader(headerName, headerVal);
                    }
                }
                await client.ConnectAsync(new Uri(capturedUrl), cts.Token);
                var instance = new StashInstance("WsConnection", new Dictionary<string, StashValue>
                {
                    ["url"] = StashValue.FromObj(capturedUrl),
                    ["protocol"] = StashValue.FromObj(client.SubProtocol ?? ""),
                });
                _wsClients.Add(instance, client);
                return (object?)instance;
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "WsConnection", isVariadic: true, documentation: "Opens a WebSocket connection to the given URL.\n@param url The WebSocket URL (must start with 'ws://' or 'wss://').\n@param options Optional dict with 'headers' (dict), 'timeout' (duration), 'subprotocol' (string).\n@return A Future resolving to a WsConnection.");

        // net.wsSend(conn, data) — Async. Sends a UTF-8 text message.
        ns.Function("wsSend", [Param("conn", "WsConnection"), Param("data", "string")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 2)
                throw new RuntimeError("net.wsSend: expected 2 arguments.");
            var client = GetWsClient(args[0].ToObject(), "net.wsSend");
            var data = SvArgs.String(args, 1, "net.wsSend");

            var capturedData = data;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                byte[] bytes = Encoding.UTF8.GetBytes(capturedData);
                await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cts.Token);
                return (object?)(long)bytes.Length;
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "int", documentation: "Sends a UTF-8 text message over a WebSocket connection.\n@param conn The WsConnection.\n@param data The string data to send.\n@return A Future resolving to the number of bytes sent.");

        // net.wsSendBinary(conn, data) — Async. Sends binary data (base64-encoded string decoded to bytes).
        ns.Function("wsSendBinary", [Param("conn", "WsConnection"), Param("data", "string")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 2)
                throw new RuntimeError("net.wsSendBinary: expected 2 arguments.");
            var client = GetWsClient(args[0].ToObject(), "net.wsSendBinary");
            var data = SvArgs.String(args, 1, "net.wsSendBinary");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                throw new RuntimeError("net.wsSendBinary: invalid base64 data");
            }

            var capturedBytes = bytes;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                await client.SendAsync(new ArraySegment<byte>(capturedBytes), WebSocketMessageType.Binary, endOfMessage: true, cts.Token);
                return (object?)(long)capturedBytes.Length;
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "int", documentation: "Sends binary data (base64-encoded) over a WebSocket connection.\n@param conn The WsConnection.\n@param data Base64-encoded string of bytes to send.\n@return A Future resolving to the number of bytes sent.");

        // net.wsRecv(conn, ?timeout) — Async. Receives the next complete message.
        ns.Function("wsRecv", [Param("conn", "WsConnection"), Param("timeout", "duration")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("net.wsRecv: expected 1 or 2 arguments.");
            var client = GetWsClient(args[0].ToObject(), "net.wsRecv");
            long timeoutMs = 30_000;
            if (args.Length > 1)
                timeoutMs = SvArgs.Duration(args, 1, "net.wsRecv").TotalMilliseconds;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                if (timeoutMs > 0)
                    recvCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
                try
                {
                    var buffer = new byte[4096];
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), recvCts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return (object?)new StashInstance("WsMessage", new Dictionary<string, StashValue>
                            {
                                ["data"] = StashValue.FromObj(result.CloseStatusDescription ?? ""),
                                ["type"] = StashValue.FromObj("text"),
                                ["close"] = StashValue.True,
                            });
                        }
                        ms.Write(buffer, 0, result.Count);
                        if (ms.Length > 16 * 1024 * 1024)
                            throw new RuntimeError("net.wsRecv: message exceeds maximum size of 16MB.");
                    } while (!result.EndOfMessage);

                    string msgType;
                    string msgData;
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        msgType = "binary";
                        msgData = Convert.ToBase64String(ms.ToArray());
                    }
                    else
                    {
                        msgType = "text";
                        msgData = Encoding.UTF8.GetString(ms.ToArray());
                    }

                    return (object?)new StashInstance("WsMessage", new Dictionary<string, StashValue>
                    {
                        ["data"] = StashValue.FromObj(msgData),
                        ["type"] = StashValue.FromObj(msgType),
                        ["close"] = StashValue.False,
                    });
                }
                catch (OperationCanceledException) when (!cts.Token.IsCancellationRequested)
                {
                    return null; // per-recv timeout → return null
                }
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "WsMessage", isVariadic: true, documentation: "Receives the next complete message from a WebSocket connection.\n@param conn The WsConnection.\n@param timeout Optional timeout duration (default 30s). Returns null on timeout.\n@return A Future resolving to a WsMessage or null on timeout.");

        // net.wsClose(conn, ?code, ?reason) — Async. Graceful WebSocket close handshake.
        ns.Function("wsClose", [Param("conn", "WsConnection"), Param("code", "int"), Param("reason", "string")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 3)
                throw new RuntimeError("net.wsClose: expected 1 to 3 arguments.");
            var client = GetWsClient(args[0].ToObject(), "net.wsClose");
            long code = args.Length > 1 ? SvArgs.Long(args, 1, "net.wsClose") : 1000;
            if (code < 1000 || code > 4999)
                throw new RuntimeError("net.wsClose: close code must be between 1000 and 4999.");
            string reason = args.Length > 2 ? SvArgs.String(args, 2, "net.wsClose") : "";

            var capturedReason = reason;
            var capturedCode = (int)code;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                if (client.State == WebSocketState.Closed || client.State == WebSocketState.Aborted)
                    return null;
                try
                {
                    using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    closeCts.CancelAfter(TimeSpan.FromSeconds(5));
                    await client.CloseAsync((WebSocketCloseStatus)capturedCode, capturedReason, closeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    client.Abort();
                }
                catch (WebSocketException)
                {
                    client.Abort();
                }
                return null;
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "null", isVariadic: true, documentation: "Performs a graceful WebSocket close handshake.\n@param conn The WsConnection.\n@param code Optional close code (1000-4999, default 1000).\n@param reason Optional close reason string (default empty).\n@return A Future resolving to null.");

        // net.wsState(conn) — Sync. Returns the WsConnectionState enum value.
        ns.Function("wsState", [Param("conn", "WsConnection")], (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 1)
                throw new RuntimeError("net.wsState: expected 1 argument.");
            var client = GetWsClient(args[0].ToObject(), "net.wsState");
            var member = wsStateEnum.GetMember(MapWsState(client.State));
            return member is not null ? StashValue.FromObj(member) : StashValue.Null;
        }, returnType: "WsConnectionState", documentation: "Returns the current connection state of a WebSocket.\n@param conn The WsConnection.\n@return A WsConnectionState enum value.");

        // net.wsIsOpen(conn) — Sync. Returns true if the WebSocket is in the Open state.
        ns.Function("wsIsOpen", [Param("conn", "WsConnection")], (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 1)
                throw new RuntimeError("net.wsIsOpen: expected 1 argument.");
            var client = GetWsClient(args[0].ToObject(), "net.wsIsOpen");
            return StashValue.FromBool(client.State == WebSocketState.Open);
        }, returnType: "bool", documentation: "Returns true if the WebSocket connection is open.\n@param conn The WsConnection.\n@return True if the connection state is Open.");

        // net.tcpConnectAsync(host, port, ?options) — Async. Creates a TCP connection.
        ns.Function("tcpConnectAsync", [Param("host", "string"), Param("port", "int"), Param("options", "TcpConnectOptions")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("net.tcpConnectAsync: expected 2 or 3 arguments.");
            var host = SvArgs.String(args, 0, "net.tcpConnectAsync");
            var port = SvArgs.Long(args, 1, "net.tcpConnectAsync");
            if (port < 1 || port > 65535)
                throw new RuntimeError("net.tcpConnectAsync: port must be between 1 and 65535.");

            int timeout = 5000;
            bool noDelay = false;
            bool keepAlive = false;
            bool tls = false;

            if (args.Length > 2 && args[2].IsObj && args[2].AsObj is StashInstance opts && opts.TypeName == "TcpConnectOptions")
            {
                if (opts.GetField("timeoutMs", null).ToObject() is long t) timeout = (int)t;
                if (opts.GetField("noDelay", null).ToObject() is bool nd) noDelay = nd;
                if (opts.GetField("keepAlive", null).ToObject() is bool ka) keepAlive = ka;
                if (opts.GetField("tls", null).ToObject() is bool tlsVal) tls = tlsVal;
            }

            if (tls)
                throw new RuntimeError("net.tcpConnectAsync: TLS is not yet supported. See future release.");

            var capturedHost = host;
            var capturedPort = (int)port;
            var capturedTimeout = timeout;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                var client = new TcpClient();
                if (noDelay) client.NoDelay = true;
                if (keepAlive) client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                using var timeoutCts = new CancellationTokenSource(capturedTimeout);
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
                try
                {
                    await client.ConnectAsync(capturedHost, capturedPort, connectCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cts.Token.IsCancellationRequested)
                {
                    client.Dispose();
                    throw new RuntimeError($"net.tcpConnectAsync: connection timed out after {capturedTimeout}ms.");
                }
                catch (OperationCanceledException)
                {
                    client.Dispose();
                    throw new RuntimeError("net.tcpConnectAsync: connection was cancelled.");
                }
                catch (Exception ex)
                {
                    client.Dispose();
                    throw new RuntimeError($"net.tcpConnectAsync: failed to connect to '{capturedHost}:{capturedPort}': {ex.Message}");
                }
                int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
                var conn = new StashInstance("TcpConnection", new Dictionary<string, StashValue>
                {
                    ["host"] = StashValue.FromObj(capturedHost),
                    ["port"] = StashValue.FromInt(capturedPort),
                    ["localPort"] = StashValue.FromInt(localPort),
                });
                _tcpAsyncClients.AddOrUpdate(conn, client);
                return (object?)conn;
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "TcpConnection", isVariadic: true, documentation: "Async. Creates a TCP connection to a host and port.\n@param host The hostname or IP address.\n@param port The port number (1-65535).\n@param options Optional TcpConnectOptions struct.\n@return A Future resolving to a TcpConnection.");

        // net.tcpSendAsync(conn, data) — Async. Sends string data over a TCP connection.
        ns.Function("tcpSendAsync", [Param("conn", "TcpConnection"), Param("data", "string")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 2)
                throw new RuntimeError("net.tcpSendAsync: expected 2 arguments.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpSendAsync: first argument must be a TcpConnection.");
            var data = SvArgs.String(args, 1, "net.tcpSendAsync");

            TcpClient client = GetTcpClient(conn, "net.tcpSendAsync");
            var capturedData = data;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(capturedData);
                    var stream = client.GetStream();
                    await stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
                    return (object?)(long)bytes.Length;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new RuntimeError($"net.tcpSendAsync: send failed: {ex.Message}");
                }
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "int", documentation: "Async. Sends string data over a TCP connection.\n@param conn The TcpConnection.\n@param data The string data to send.\n@return A Future resolving to the number of bytes sent.");

        // net.tcpSendBytesAsync(conn, data) — Async. Sends binary data over a TCP connection.
        ns.Function("tcpSendBytesAsync", [Param("conn", "TcpConnection"), Param("data", "byte[]")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 2)
                throw new RuntimeError("net.tcpSendBytesAsync: expected 2 arguments.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpSendBytesAsync: first argument must be a TcpConnection.");
            if (args[1].ToObject() is not StashByteArray byteArr)
                throw new RuntimeError("net.tcpSendBytesAsync: second argument must be a byte[].");

            TcpClient client = GetTcpClient(conn, "net.tcpSendBytesAsync");
            byte[] data = byteArr.AsSpan().ToArray();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                try
                {
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length, cts.Token);
                    return (object?)(long)data.Length;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new RuntimeError($"net.tcpSendBytesAsync: send failed: {ex.Message}");
                }
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "int", documentation: "Async. Sends binary data (byte[]) over a TCP connection.\n@param conn The TcpConnection.\n@param data The byte[] data to send.\n@return A Future resolving to the number of bytes sent.");

        // net.tcpRecvAsync(conn, ?options) — Async. Receives string data from a TCP connection.
        ns.Function("tcpRecvAsync", [Param("conn", "TcpConnection"), Param("options", "TcpRecvOptions")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("net.tcpRecvAsync: expected 1 or 2 arguments.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpRecvAsync: first argument must be a TcpConnection.");

            int maxBytes = 4096;
            int timeout = 30000;

            if (args.Length > 1 && args[1].IsObj && args[1].AsObj is StashInstance opts && opts.TypeName == "TcpRecvOptions")
            {
                if (opts.GetField("maxBytes", null).ToObject() is long mb) maxBytes = (int)Math.Min(mb, 16_777_216);
                if (opts.GetField("timeoutMs", null).ToObject() is long t) timeout = (int)t;
            }

            TcpClient client = GetTcpClient(conn, "net.tcpRecvAsync");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                var stream = client.GetStream();
                var buffer = new byte[maxBytes];
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, maxBytes, linkedCts.Token);
                    return (object?)Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cts.Token.IsCancellationRequested)
                {
                    return null; // Timeout — not an error
                }
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "string", isVariadic: true, documentation: "Async. Receives string data from a TCP connection.\n@param conn The TcpConnection.\n@param options Optional TcpRecvOptions struct with maxBytes and timeout.\n@return A Future resolving to a string, or null on timeout.");

        // net.tcpRecvBytesAsync(conn, ?options) — Async. Receives binary data from a TCP connection.
        ns.Function("tcpRecvBytesAsync", [Param("conn", "TcpConnection"), Param("options", "TcpRecvOptions")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("net.tcpRecvBytesAsync: expected 1 or 2 arguments.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpRecvBytesAsync: first argument must be a TcpConnection.");

            int maxBytes = 4096;
            int timeout = 30000;

            if (args.Length > 1 && args[1].IsObj && args[1].AsObj is StashInstance opts && opts.TypeName == "TcpRecvOptions")
            {
                if (opts.GetField("maxBytes", null).ToObject() is long mb) maxBytes = (int)Math.Min(mb, 16_777_216);
                if (opts.GetField("timeoutMs", null).ToObject() is long t) timeout = (int)t;
            }

            TcpClient client = GetTcpClient(conn, "net.tcpRecvBytesAsync");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            var dotnetTask = Task.Run<object?>(async () =>
            {
                var stream = client.GetStream();
                var buffer = new byte[maxBytes];
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, maxBytes, linkedCts.Token);
                    if (bytesRead == 0) return (object?)new StashByteArray(Array.Empty<byte>());
                    return (object?)new StashByteArray(buffer.AsSpan(0, bytesRead).ToArray());
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cts.Token.IsCancellationRequested)
                {
                    return null; // Timeout — not an error
                }
            });
            return StashValue.FromObj(new StashFuture(dotnetTask, cts));
        }, returnType: "byte[]", isVariadic: true, documentation: "Async. Receives binary data from a TCP connection.\n@param conn The TcpConnection.\n@param options Optional TcpRecvOptions struct with maxBytes and timeout.\n@return A Future resolving to a byte[], or null on timeout.");

        // net.tcpCloseAsync(conn) — Async. Gracefully closes a TCP connection.
        ns.Function("tcpCloseAsync", [Param("conn", "TcpConnection")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 1)
                throw new RuntimeError("net.tcpCloseAsync: expected 1 argument.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpCloseAsync: argument must be a TcpConnection.");

            if (_tcpAsyncClients.TryGetValue(conn, out TcpClient? client))
            {
                try { client.Client.Shutdown(SocketShutdown.Both); } catch { }
                client.Dispose();
                _tcpAsyncClients.Remove(conn);
            }
            else
            {
                try
                {
                    if (conn.GetField("_client", null).ToObject() is TcpClient syncClient)
                    {
                        try { syncClient.Client.Shutdown(SocketShutdown.Both); } catch { }
                        syncClient.Dispose();
                    }
                }
                catch { /* field doesn't exist — already closed or not a sync connection */ }
            }
            return StashValue.FromObj(StashFuture.Resolved(null));
        }, returnType: "null", documentation: "Async. Gracefully closes a TCP connection.\n@param conn The TcpConnection to close.\n@return A Future resolving to null.");

        // net.tcpIsOpen(conn) — Sync. Returns true if the TCP connection is open.
        ns.Function("tcpIsOpen", [Param("conn", "TcpConnection")], (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 1)
                throw new RuntimeError("net.tcpIsOpen: expected 1 argument.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpIsOpen: argument must be a TcpConnection.");

            try
            {
                TcpClient client = GetTcpClient(conn, "net.tcpIsOpen");
                return StashValue.FromBool(client.Connected);
            }
            catch
            {
                return StashValue.False;
            }
        }, returnType: "bool", documentation: "Returns true if the TCP connection is open.\n@param conn The TcpConnection.\n@return True if the connection is open.");

        // net.tcpState(conn) — Sync. Returns the TcpConnectionState enum value.
        ns.Function("tcpState", [Param("conn", "TcpConnection")], (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 1)
                throw new RuntimeError("net.tcpState: expected 1 argument.");
            if (args[0].ToObject() is not StashInstance conn)
                throw new RuntimeError("net.tcpState: argument must be a TcpConnection.");

            try
            {
                TcpClient client = GetTcpClient(conn, "net.tcpState");
                var member = tcpStateEnum.GetMember(client.Connected ? "Open" : "Closed");
                return member is not null ? StashValue.FromObj(member) : StashValue.Null;
            }
            catch
            {
                var member = tcpStateEnum.GetMember("Closed");
                return member is not null ? StashValue.FromObj(member) : StashValue.Null;
            }
        }, returnType: "TcpConnectionState", documentation: "Returns the current connection state of a TCP connection.\n@param conn The TcpConnection.\n@return A TcpConnectionState enum value.");

        // net.tcpListenAsync(port, handler) — Async. Starts a multi-client TCP server.
        ns.Function("tcpListenAsync", [Param("port", "int"), Param("handler", "function")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 2)
                throw new RuntimeError("net.tcpListenAsync: expected 2 arguments.");
            var port = SvArgs.Long(args, 0, "net.tcpListenAsync");
            if (port < 0 || port > 65535)
                throw new RuntimeError("net.tcpListenAsync: port must be between 0 and 65535.");
            var handler = SvArgs.Callable(args, 1, "net.tcpListenAsync");

            var listener = new TcpListener(IPAddress.Any, (int)port);
            listener.Start();

            int actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverInst = new StashInstance("TcpServer", new Dictionary<string, StashValue>
            {
                ["port"] = StashValue.FromInt(actualPort),
                ["active"] = StashValue.True,
            });

            _tcpServers.AddOrUpdate(serverInst, listener);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

            // Spawn accept loop
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        TcpClient clientSocket;
                        try
                        {
                            clientSocket = await listener.AcceptTcpClientAsync(cts.Token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (ObjectDisposedException) { break; }

                        int localPort = ((IPEndPoint)clientSocket.Client.LocalEndPoint!).Port;
                        string remoteHost = ((IPEndPoint)clientSocket.Client.RemoteEndPoint!).Address.ToString();
                        int remotePort = ((IPEndPoint)clientSocket.Client.RemoteEndPoint!).Port;

                        var connInst = new StashInstance("TcpConnection", new Dictionary<string, StashValue>
                        {
                            ["host"] = StashValue.FromObj(remoteHost),
                            ["port"] = StashValue.FromInt(remotePort),
                            ["localPort"] = StashValue.FromInt(localPort),
                        });
                        _tcpAsyncClients.AddOrUpdate(connInst, clientSocket);

                        // Fire-and-forget: handle each connection in its own forked context
                        _ = Task.Run(() =>
                        {
                            try { ctx.InvokeCallbackDirect(handler, new StashValue[] { StashValue.FromObj(connInst) }); }
                            catch { /* handler errors don't kill the server */ }
                        });
                    }
                }
                catch (OperationCanceledException) { /* normal shutdown */ }
                catch (ObjectDisposedException) { /* listener was stopped */ }
                finally
                {
                    try { listener.Stop(); } catch { }
                    serverInst.SetField("active", StashValue.False, null);
                }
            });

            // Resolve immediately with the server handle
            return StashValue.FromObj(new StashFuture(Task.FromResult<object?>(serverInst), cts));
        }, returnType: "TcpServer", documentation: "Async. Starts a multi-client TCP server on a port.\n@param port The port to listen on (1-65535, or 0 for auto).\n@param handler A function that receives each TcpConnection.\n@return A Future resolving to a TcpServer handle.");

        // net.tcpServerClose(server) — Sync. Stops a TCP server.
        ns.Function("tcpServerClose", [Param("server", "TcpServer")], (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length != 1)
                throw new RuntimeError("net.tcpServerClose: expected 1 argument.");
            if (args[0].ToObject() is not StashInstance server || server.TypeName != "TcpServer")
                throw new RuntimeError("net.tcpServerClose: argument must be a TcpServer.");

            if (_tcpServers.TryGetValue(server, out TcpListener? listener))
            {
                try { listener.Stop(); } catch { }
                _tcpServers.Remove(server);
            }
            server.SetField("active", StashValue.False, null);
            return StashValue.Null;
        }, returnType: "null", documentation: "Stops a TCP server and closes the listener.\n@param server The TcpServer handle to stop.");

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
            throw new RuntimeError($"'{funcName}' requires a CIDR IP address (e.g., @192.168.1.0/24).");

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

    private static ClientWebSocket GetWsClient(object? arg, string funcName)
    {
        if (arg is not StashInstance inst || inst.TypeName != "WsConnection")
            throw new RuntimeError($"First argument to '{funcName}' must be a WsConnection.");
        if (!_wsClients.TryGetValue(inst, out ClientWebSocket? ws))
            throw new RuntimeError($"{funcName}: connection is invalid or closed.");
        return ws;
    }

    private static TcpClient GetTcpClient(StashInstance conn, string funcName)
    {
        // Try async storage first (ConditionalWeakTable)
        if (_tcpAsyncClients.TryGetValue(conn, out TcpClient? asyncClient))
            return asyncClient;

        // Fall back to sync hidden field
        try
        {
            var clientField = conn.GetField("_client", null);
            if (clientField.ToObject() is TcpClient syncClient)
                return syncClient;
        }
        catch { }

        throw new RuntimeError($"{funcName}: invalid or closed TcpConnection.");
    }

    private static string MapWsState(WebSocketState state) => state switch
    {
        WebSocketState.None or WebSocketState.Connecting => "Connecting",
        WebSocketState.Open => "Open",
        WebSocketState.CloseSent or WebSocketState.CloseReceived => "Closing",
        WebSocketState.Closed or WebSocketState.Aborted => "Closed",
        _ => "Closed",
    };

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

    private static class DnsQueryHelper
    {
        internal const ushort TypeMx = 15;
        internal const ushort TypeTxt = 16;

        internal record DnsRecord(int Priority, string Data);

        internal static List<DnsRecord> QueryRecords(string domain, ushort queryType)
        {
            // Resolve system nameserver, fall back to Google Public DNS
            string nameserver = GetSystemNameserver() ?? "8.8.8.8";

            byte[] query = BuildQuery(domain, queryType);
            byte[] response = SendDnsQuery(nameserver, 53, query, timeoutMs: 5000);
            return ParseResponse(response, queryType);
        }

        private static string? GetSystemNameserver()
        {
            // On Unix, parse /etc/resolv.conf
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    foreach (var line in System.IO.File.ReadLines("/etc/resolv.conf"))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("nameserver ", StringComparison.OrdinalIgnoreCase))
                        {
                            var ns = trimmed.Substring("nameserver ".Length).Trim();
                            if (IPAddress.TryParse(ns, out _))
                                return ns;
                        }
                    }
                }
                catch { /* ignore */ }
            }
            return null;
        }

        private static byte[] BuildQuery(string domain, ushort queryType)
        {
            // DNS header: 12 bytes
            // ID=1, QR=0, Opcode=0, AA=0, TC=0, RD=1, RA=0, Z=0, RCODE=0
            // QDCOUNT=1, ANCOUNT=0, NSCOUNT=0, ARCOUNT=0
            var packet = new List<byte>
            {
                0x00, 0x01, // Transaction ID
                0x01, 0x00, // Flags: standard query, recursion desired
                0x00, 0x01, // QDCOUNT = 1
                0x00, 0x00, // ANCOUNT = 0
                0x00, 0x00, // NSCOUNT = 0
                0x00, 0x00, // ARCOUNT = 0
            };

            // Encode domain name as length-prefixed labels
            foreach (var label in domain.Split('.'))
            {
                byte[] labelBytes = Encoding.ASCII.GetBytes(label);
                packet.Add((byte)labelBytes.Length);
                packet.AddRange(labelBytes);
            }
            packet.Add(0x00); // root label terminator

            // QTYPE
            packet.Add((byte)(queryType >> 8));
            packet.Add((byte)(queryType & 0xFF));
            // QCLASS = IN (1)
            packet.Add(0x00);
            packet.Add(0x01);

            return [.. packet];
        }

        private static byte[] SendDnsQuery(string nameserver, int port, byte[] query, int timeoutMs)
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = timeoutMs;
            udp.Send(query, query.Length, nameserver, port);
            var remote = new IPEndPoint(IPAddress.Any, 0);
            return udp.Receive(ref remote);
        }

        private static List<DnsRecord> ParseResponse(byte[] response, ushort queryType)
        {
            var records = new List<DnsRecord>();
            if (response.Length < 12)
                return records;

            int ancount = (response[6] << 8) | response[7];
            if (ancount == 0)
                return records;

            // Skip header (12 bytes) and question section
            int offset = 12;
            offset = SkipQuestion(response, offset);

            for (int i = 0; i < ancount && offset < response.Length; i++)
            {
                // Skip name (may be a pointer or label sequence)
                offset = SkipName(response, offset);
                if (offset + 10 > response.Length) break;

                ushort type = (ushort)((response[offset] << 8) | response[offset + 1]);
                // class (2), TTL (4)
                offset += 8; // type(2) + class(2) + TTL(4)
                int rdlength = (response[offset] << 8) | response[offset + 1];
                offset += 2;

                if (offset + rdlength > response.Length) break;

                if (type == TypeMx && rdlength >= 3)
                {
                    int priority = (response[offset] << 8) | response[offset + 1];
                    string exchange = ReadName(response, offset + 2);
                    records.Add(new DnsRecord(priority, exchange));
                }
                else if (type == TypeTxt && rdlength >= 1)
                {
                    // TXT rdata: one or more <length><string> segments
                    int pos = offset;
                    int end = offset + rdlength;
                    var segments = new List<string>();
                    while (pos < end)
                    {
                        int segLen = response[pos++];
                        if (pos + segLen > end) break;
                        segments.Add(Encoding.UTF8.GetString(response, pos, segLen));
                        pos += segLen;
                    }
                    records.Add(new DnsRecord(0, string.Concat(segments)));
                }

                offset += rdlength;
            }

            return records;
        }

        private static int SkipQuestion(byte[] buf, int offset)
        {
            offset = SkipName(buf, offset);
            return offset + 4; // QTYPE (2) + QCLASS (2)
        }

        private static int SkipName(byte[] buf, int offset)
        {
            while (offset < buf.Length)
            {
                byte len = buf[offset];
                if (len == 0)
                    return offset + 1;
                if ((len & 0xC0) == 0xC0)
                    return offset + 2; // pointer: 2-byte reference
                offset += 1 + len;
            }
            return offset;
        }

        private static string ReadName(byte[] buf, int offset)
        {
            var labels = new List<string>();
            int visited = 0;
            while (offset < buf.Length)
            {
                byte len = buf[offset];
                if (len == 0) break;
                if ((len & 0xC0) == 0xC0)
                {
                    // Pointer: follow it (once, to avoid loops)
                    if (visited++ > 10 || offset + 1 >= buf.Length) break;
                    offset = ((len & 0x3F) << 8) | buf[offset + 1];
                    continue;
                }
                offset++;
                if (offset + len > buf.Length) break;
                labels.Add(Encoding.ASCII.GetString(buf, offset, len));
                offset += len;
            }
            return string.Join(".", labels);
        }
    }
}
