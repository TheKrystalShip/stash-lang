namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>udp</c> namespace built-in functions for UDP datagram communication.
/// </summary>
public static class UdpBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("udp");
        ns.RequiresCapability(StashCapabilities.Network);

        ns.Struct("UdpMessage", [
            new BuiltInField("data", "string"),
            new BuiltInField("host", "string"),
            new BuiltInField("port", "int"),
        ]);

        // udp.send(host, port, data) — Sends a UDP datagram.
        ns.Function("send", [Param("host", "string"), Param("port", "int"), Param("data", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.UdpSend(ctx, args, "udp.send"),
            returnType: "int",
            documentation: "Sends a UDP datagram to a host and port.\n@param host The destination hostname or IP address.\n@param port The destination port (1-65535).\n@param data The string data to send.\n@return The number of bytes sent.");

        // udp.recv(port, ?timeout) — Listens for one UDP datagram.
        ns.Function("recv", [Param("port", "int"), Param("timeout", "int")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.UdpRecv(ctx, args, "udp.recv"),
            returnType: "UdpMessage", isVariadic: true,
            documentation: "Listens on a UDP port and receives one datagram.\n@param port The port to listen on (1-65535).\n@param timeout Optional timeout in milliseconds (default 5000).\n@return A UdpMessage struct with data, host, and port fields.");

        return ns.Build();
    }
}
