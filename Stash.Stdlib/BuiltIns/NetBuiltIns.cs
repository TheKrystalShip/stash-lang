namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
