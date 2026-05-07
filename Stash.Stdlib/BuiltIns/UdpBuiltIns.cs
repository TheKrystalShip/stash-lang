namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>udp</c> namespace built-in functions for UDP datagram communication.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class UdpBuiltIns
{
    /// <summary>A received UDP datagram.</summary>
    [StashStruct]
    public sealed record UdpMessage(string Data, string Host, long Port);

    /// <summary>Sends a UDP datagram to a host and port.</summary>
    /// <param name="host">The destination hostname or IP address.</param>
    /// <param name="port">The destination port (1-65535).</param>
    /// <param name="data">The string data to send.</param>
    /// <returns>The number of bytes sent.</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Send(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.UdpSend(ctx, args, "udp.send");

    /// <summary>Listens on a UDP port and receives one datagram.</summary>
    /// <param name="port">The port to listen on (1-65535).</param>
    /// <param name="timeout">Optional timeout in milliseconds (default 5000).</param>
    /// <returns>A UdpMessage struct with data, host, and port fields.</returns>
    [StashFn(Raw = true, ReturnType = "UdpMessage")]
    private static StashValue Recv(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.UdpRecv(ctx, args, "udp.recv");
}
