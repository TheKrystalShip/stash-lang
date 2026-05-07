namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>tcp</c> namespace built-in functions for TCP socket communication.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class TcpBuiltIns
{
    /// <summary>A TCP connection handle.</summary>
    [StashStruct]
    public sealed record TcpConnection(string Host, long Port, long LocalPort);

    /// <summary>Options for tcp.connect and tcp.connectAsync.</summary>
    [StashStruct]
    public sealed record TcpConnectOptions(long TimeoutMs, bool Tls, bool NoDelay, bool KeepAlive, bool TlsVerify, string TlsSni);

    /// <summary>Options for tcp.recv and tcp.recvAsync.</summary>
    [StashStruct]
    public sealed record TcpRecvOptions(long MaxBytes, long TimeoutMs);

    /// <summary>A TCP server handle returned by tcp.listenAsync.</summary>
    [StashStruct]
    public sealed record TcpServer(long Port, bool Active);

    /// <summary>The state of a TCP connection.</summary>
    [StashEnum]
    public enum TcpConnectionState { Open, Closed }

    /// <summary>Creates a TCP connection to a host and port.</summary>
    /// <param name="host">The hostname or IP address.</param>
    /// <param name="port">The port number (1-65535).</param>
    /// <param name="timeout">Optional timeout in milliseconds (default 5000).</param>
    /// <returns>A TcpConnection struct.</returns>
    [StashFn(Raw = true, ReturnType = "TcpConnection")]
    private static StashValue Connect(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpConnect(ctx, args, "tcp.connect");

    /// <summary>Sends string data over a TCP connection.</summary>
    /// <param name="conn">The TcpConnection.</param>
    /// <param name="data">The string data to send.</param>
    /// <returns>The number of bytes sent.</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Send(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpSend(ctx, args, "tcp.send");

    /// <summary>Receives data from a TCP connection.</summary>
    /// <param name="conn">The TcpConnection.</param>
    /// <param name="maxBytes">Optional maximum bytes to read (default 4096).</param>
    /// <returns>The received data as a string.</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Recv(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpRecv(ctx, args, "tcp.recv");

    /// <summary>Closes a TCP connection and releases its resources.</summary>
    /// <param name="conn">The TcpConnection to close.</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Close(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpClose(ctx, args, "tcp.close");

    /// <summary>Starts a TCP listener on a port, accepts one connection, invokes the handler with a TcpConnection, then stops.</summary>
    /// <param name="port">The port to listen on (1-65535).</param>
    /// <param name="handler">A function that receives the TcpConnection.</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Listen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpListen(ctx, args, "tcp.listen");

    /// <summary>Async. Creates a TCP connection to a host and port.</summary>
    /// <param name="host">The hostname or IP address.</param>
    /// <param name="port">The port number (1-65535).</param>
    /// <param name="options">Optional TcpConnectOptions struct.</param>
    /// <returns>A Future resolving to a TcpConnection.</returns>
    [StashFn(Raw = true, ReturnType = "TcpConnection")]
    private static StashValue ConnectAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpConnectAsync(ctx, args, "tcp.connectAsync");

    /// <summary>Async. Sends string data over a TCP connection.</summary>
    /// <param name="conn">The TcpConnection.</param>
    /// <param name="data">The string data to send.</param>
    /// <returns>A Future resolving to the number of bytes sent.</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue SendAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpSendAsync(ctx, args, "tcp.sendAsync");

    /// <summary>Async. Sends binary data (byte[]) over a TCP connection.</summary>
    /// <param name="conn">The TcpConnection.</param>
    /// <param name="data">The byte[] data to send.</param>
    /// <returns>A Future resolving to the number of bytes sent.</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue SendBytesAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpSendBytesAsync(ctx, args, "tcp.sendBytesAsync");

    /// <summary>Async. Receives string data from a TCP connection.</summary>
    /// <param name="conn">The TcpConnection.</param>
    /// <param name="options">Optional TcpRecvOptions struct with maxBytes and timeout.</param>
    /// <returns>A Future resolving to a string, or null on timeout.</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue RecvAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpRecvAsync(ctx, args, "tcp.recvAsync");

    /// <summary>Async. Receives binary data from a TCP connection.</summary>
    /// <param name="conn">The TcpConnection.</param>
    /// <param name="options">Optional TcpRecvOptions struct with maxBytes and timeout.</param>
    /// <returns>A Future resolving to a byte[], or null on timeout.</returns>
    [StashFn(Raw = true, ReturnType = "byte[]")]
    private static StashValue RecvBytesAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpRecvBytesAsync(ctx, args, "tcp.recvBytesAsync");

    /// <summary>Async. Gracefully closes a TCP connection.</summary>
    /// <param name="conn">The TcpConnection to close.</param>
    /// <returns>A Future resolving to null.</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue CloseAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpCloseAsync(ctx, args, "tcp.closeAsync");

    /// <summary>Returns true if the TCP connection is open.</summary>
    /// <param name="conn">The TcpConnection.</param>
    /// <returns>True if the connection is open.</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue IsOpen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpIsOpen(ctx, args, "tcp.isOpen");

    /// <summary>Returns the current connection state of a TCP connection.</summary>
    /// <param name="conn">The TcpConnection.</param>
    /// <returns>A TcpConnectionState enum value.</returns>
    [StashFn(Raw = true, ReturnType = "TcpConnectionState")]
    private static StashValue State(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpState(ctx, args, "tcp.state");

    /// <summary>Async. Starts a multi-client TCP server on a port.</summary>
    /// <param name="port">The port to listen on (1-65535, or 0 for auto).</param>
    /// <param name="handler">A function that receives each TcpConnection.</param>
    /// <returns>A Future resolving to a TcpServer handle.</returns>
    [StashFn(Raw = true, ReturnType = "TcpServer")]
    private static StashValue ListenAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpListenAsync(ctx, args, "tcp.listenAsync");

    /// <summary>Stops a TCP server and closes the listener.</summary>
    /// <param name="server">The TcpServer handle to stop.</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue ServerClose(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.TcpServerClose(ctx, args, "tcp.serverClose");
}
