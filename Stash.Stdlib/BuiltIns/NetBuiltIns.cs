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
        ns.Function("subnetInfo", [Param("ip", "ip")], (_, args) =>
        {
            var ip = Args.IpAddress(args, 0, "net.subnetInfo");
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

            return new StashInstance("SubnetInfo", new Dictionary<string, object?>
            {
                ["network"] = network,
                ["broadcast"] = broadcast,
                ["mask"] = mask,
                ["wildcard"] = wildcard,
                ["hostCount"] = hostCount,
                ["firstHost"] = firstHost,
                ["lastHost"] = lastHost,
            });
        }, returnType: "SubnetInfo", documentation: "Returns subnet details for a CIDR IP address.");

        // net.mask(ip) — Returns the subnet mask for a CIDR IP address.
        ns.Function("mask", [Param("ip", "ip")], (_, args) =>
        {
            var ip = Args.IpAddress(args, 0, "net.mask");
            var (maskBytes, _, _, _) = ComputeSubnetComponents(ip, "net.mask");
            return new StashIpAddress(maskBytes, null);
        }, returnType: "ip", documentation: "Returns the subnet mask for a CIDR IP address.");

        // net.network(ip) — Returns the network address for a CIDR IP address.
        ns.Function("network", [Param("ip", "ip")], (_, args) =>
        {
            var ip = Args.IpAddress(args, 0, "net.network");
            var (_, networkBytes, _, _) = ComputeSubnetComponents(ip, "net.network");
            return new StashIpAddress(networkBytes, ip.PrefixLength!.Value);
        }, returnType: "ip", documentation: "Returns the network address for a CIDR IP address.");

        // net.broadcast(ip) — Returns the broadcast address for a CIDR IP address.
        ns.Function("broadcast", [Param("ip", "ip")], (_, args) =>
        {
            var ip = Args.IpAddress(args, 0, "net.broadcast");
            var (_, _, broadcastBytes, _) = ComputeSubnetComponents(ip, "net.broadcast");
            return new StashIpAddress(broadcastBytes, null);
        }, returnType: "ip", documentation: "Returns the broadcast address for a CIDR IP address.");

        // net.hostCount(ip) — Returns the number of usable host addresses in a CIDR subnet.
        ns.Function("hostCount", [Param("ip", "ip")], (_, args) =>
        {
            var ip = Args.IpAddress(args, 0, "net.hostCount");
            ComputeSubnetComponents(ip, "net.hostCount"); // validates PrefixLength
            return ComputeHostCount(ip);
        }, returnType: "int", documentation: "Returns the number of usable host addresses in a CIDR subnet.");

        // net.resolve(hostname) — Resolves a hostname to its first IP address via DNS.
        ns.Function("resolve", [Param("hostname", "string")], (_, args) =>
        {
            var hostname = Args.String(args, 0, "net.resolve");
            try
            {
                var entry = Dns.GetHostEntry(hostname);
                if (entry.AddressList.Length == 0)
                    throw new RuntimeError($"No DNS records found for '{hostname}'.");
                return new StashIpAddress(entry.AddressList[0], null);
            }
            catch (SocketException ex)
            {
                throw new RuntimeError($"DNS resolution failed for '{hostname}': {ex.Message}");
            }
        }, returnType: "ip", documentation: "Resolves a hostname to its first IP address via DNS.\n@param hostname The hostname to resolve.\n@return The first resolved IP address.");

        // net.resolveAll(hostname) — Resolves a hostname to all IP addresses via DNS.
        ns.Function("resolveAll", [Param("hostname", "string")], (_, args) =>
        {
            var hostname = Args.String(args, 0, "net.resolveAll");
            try
            {
                var entry = Dns.GetHostEntry(hostname);
                return entry.AddressList.Select(a => (object?)new StashIpAddress(a, null)).ToList();
            }
            catch (SocketException ex)
            {
                throw new RuntimeError($"DNS resolution failed for '{hostname}': {ex.Message}");
            }
        }, returnType: "array", documentation: "Resolves a hostname to all IP addresses via DNS.\n@param hostname The hostname to resolve.\n@return An array of resolved IP addresses.");

        // net.reverseLookup(ip) — Performs reverse DNS lookup for an IP address.
        ns.Function("reverseLookup", [Param("ip", "ip")], (_, args) =>
        {
            var ip = Args.IpAddress(args, 0, "net.reverseLookup");
            try
            {
                var entry = Dns.GetHostEntry(ip.Address);
                return entry.HostName;
            }
            catch (SocketException ex)
            {
                throw new RuntimeError($"Reverse DNS lookup failed for '{ip}': {ex.Message}");
            }
        }, returnType: "string", documentation: "Performs reverse DNS lookup for an IP address.\n@param ip The IP address to lookup.\n@return The hostname associated with the IP.");

        // net.ping(host) — Sends an ICMP ping to a host and returns the result.
        ns.Function("ping", [Param("host", "ip")], (_, args) =>
        {
            var host = Args.IpAddress(args, 0, "net.ping");
            using var pinger = new Ping();
            try
            {
                PingReply reply = pinger.Send(host.Address, 5000);
                return new StashInstance("PingResult", new Dictionary<string, object?>
                {
                    ["alive"] = reply.Status == IPStatus.Success,
                    ["latency"] = (double)reply.RoundtripTime,
                    ["ttl"] = reply.Status == IPStatus.Success ? (long)(reply.Options?.Ttl ?? 0) : 0L,
                });
            }
            catch (PingException)
            {
                return new StashInstance("PingResult", new Dictionary<string, object?>
                {
                    ["alive"] = false,
                    ["latency"] = 0.0,
                    ["ttl"] = 0L,
                });
            }
        }, returnType: "PingResult", documentation: "Sends an ICMP ping to a host and returns the result. On Linux, requires root or CAP_NET_RAW capability.\n@param host The IP address to ping.\n@return A PingResult with alive, latency, and ttl fields.");

        // net.isPortOpen(host, port, ?timeout) — Checks if a TCP port is open on a host.
        ns.Function("isPortOpen", [Param("host", "string|ip"), Param("port", "int"), Param("timeout", "int")], (_, args) =>
        {
            Args.Count(args, 2, 3, "net.isPortOpen");
            object hostArg = args[0]!;
            var port = Args.Long(args, 1, "net.isPortOpen");
            if (port < 1 || port > 65535)
                throw new RuntimeError("Port must be between 1 and 65535.");
            int timeout = args.Count > 2 ? (int)Args.Long(args, 2, "net.isPortOpen") : 3000;

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                using var client = new TcpClient();
                if (hostArg is StashIpAddress ip)
                    client.ConnectAsync(ip.Address, (int)port, cts.Token).GetAwaiter().GetResult();
                else if (hostArg is string hostname)
                    client.ConnectAsync(hostname, (int)port, cts.Token).GetAwaiter().GetResult();
                else
                    throw new RuntimeError("First argument to 'net.isPortOpen' must be an IP address or hostname string.");
                return client.Connected;
            }
            catch (RuntimeError) { throw; }
            catch
            {
                return false;
            }
        }, returnType: "bool", documentation: "Checks if a TCP port is open on a host.\n@param host The IP address or hostname string to check.\n@param port The port number (1-65535).\n@param timeout Optional timeout in milliseconds (default 3000).");

        // net.interfaces() — Returns information about all network interfaces.
        ns.Function("interfaces", [], (_, args) =>
        {
            return BuildInterfaceList(NetworkInterface.GetAllNetworkInterfaces());
        }, returnType: "array", documentation: "Returns information about all network interfaces.\n@return An array of InterfaceInfo structs.");

        // net.interface(name) — Returns information about a specific network interface.
        ns.Function("interface", [Param("name", "string")], (_, args) =>
        {
            var name = Args.String(args, 0, "net.interface");
            var match = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.Name == name);
            if (match is null)
                throw new RuntimeError($"Network interface '{name}' not found.");
            return BuildInterfaceList([match])[0];
        }, returnType: "InterfaceInfo", documentation: "Returns information about a specific network interface.\n@param name The interface name (e.g., \"eth0\", \"wlan0\").\n@return An InterfaceInfo struct.");

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

    private static List<object?> BuildInterfaceList(IEnumerable<NetworkInterface> interfaces)
    {
        var result = new List<object?>();

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

            result.Add(new StashInstance("InterfaceInfo", new Dictionary<string, object?>
            {
                ["name"] = ni.Name,
                ["ip"] = (object?)ipv4,
                ["ipv6"] = (object?)ipv6,
                ["mac"] = mac,
                ["gateway"] = (object?)gateway,
                ["subnet"] = (object?)subnetAddr,
                ["status"] = ni.OperationalStatus.ToString(),
                ["type"] = ni.NetworkInterfaceType.ToString(),
                ["up"] = ni.OperationalStatus == OperationalStatus.Up,
            }));
        }

        return result;
    }
}
