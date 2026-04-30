namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>tcp</c> namespace built-in functions for TCP socket communication.
/// </summary>
public static class TcpBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("tcp");
        ns.RequiresCapability(StashCapabilities.Network);

        ns.Struct("TcpConnection", [
            new BuiltInField("host",      "string"),
            new BuiltInField("port",      "int"),
            new BuiltInField("localPort", "int"),
        ]);

        ns.Struct("TcpConnectOptions", [
            new BuiltInField("timeoutMs", "int"),
            new BuiltInField("tls",       "bool"),
            new BuiltInField("noDelay",   "bool"),
            new BuiltInField("keepAlive", "bool"),
            new BuiltInField("tlsVerify", "bool"),
            new BuiltInField("tlsSni",    "string"),
        ]);

        ns.Struct("TcpRecvOptions", [
            new BuiltInField("maxBytes",  "int"),
            new BuiltInField("timeoutMs", "int"),
        ]);

        ns.Struct("TcpServer", [
            new BuiltInField("port",   "int"),
            new BuiltInField("active", "bool"),
        ]);

        ns.Enum("TcpConnectionState", ["Open", "Closed"]);

        // tcp.connect(host, port, ?timeout) — Creates a synchronous TCP connection.
        ns.Function("connect", [Param("host", "string"), Param("port", "int"), Param("timeout", "int")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpConnect(ctx, args, "tcp.connect"),
            returnType: "TcpConnection", isVariadic: true,
            documentation: "Creates a TCP connection to a host and port.\n@param host The hostname or IP address.\n@param port The port number (1-65535).\n@param timeout Optional timeout in milliseconds (default 5000).\n@return A TcpConnection struct.");

        // tcp.send(conn, data) — Sends string data.
        ns.Function("send", [Param("conn", "TcpConnection"), Param("data", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpSend(ctx, args, "tcp.send"),
            returnType: "int",
            documentation: "Sends string data over a TCP connection.\n@param conn The TcpConnection.\n@param data The string data to send.\n@return The number of bytes sent.");

        // tcp.recv(conn, ?maxBytes) — Receives data.
        ns.Function("recv", [Param("conn", "TcpConnection"), Param("maxBytes", "int")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpRecv(ctx, args, "tcp.recv"),
            returnType: "string", isVariadic: true,
            documentation: "Receives data from a TCP connection.\n@param conn The TcpConnection.\n@param maxBytes Optional maximum bytes to read (default 4096).\n@return The received data as a string.");

        // tcp.close(conn) — Closes the connection.
        ns.Function("close", [Param("conn", "TcpConnection")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpClose(ctx, args, "tcp.close"),
            returnType: "null",
            documentation: "Closes a TCP connection and releases its resources.\n@param conn The TcpConnection to close.");

        // tcp.listen(port, handler) — Accepts one connection synchronously.
        ns.Function("listen", [Param("port", "int"), Param("handler", "function")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpListen(ctx, args, "tcp.listen"),
            returnType: "null",
            documentation: "Starts a TCP listener on a port, accepts one connection, invokes the handler with a TcpConnection, then stops.\n@param port The port to listen on (1-65535).\n@param handler A function that receives the TcpConnection.");

        // tcp.connectAsync(host, port, ?options) — Async TCP connection.
        ns.Function("connectAsync", [Param("host", "string"), Param("port", "int"), Param("options", "TcpConnectOptions")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpConnectAsync(ctx, args, "tcp.connectAsync"),
            returnType: "TcpConnection", isVariadic: true,
            documentation: "Async. Creates a TCP connection to a host and port.\n@param host The hostname or IP address.\n@param port The port number (1-65535).\n@param options Optional TcpConnectOptions struct.\n@return A Future resolving to a TcpConnection.");

        // tcp.sendAsync(conn, data) — Async send.
        ns.Function("sendAsync", [Param("conn", "TcpConnection"), Param("data", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpSendAsync(ctx, args, "tcp.sendAsync"),
            returnType: "int",
            documentation: "Async. Sends string data over a TCP connection.\n@param conn The TcpConnection.\n@param data The string data to send.\n@return A Future resolving to the number of bytes sent.");

        // tcp.sendBytesAsync(conn, data) — Async binary send.
        ns.Function("sendBytesAsync", [Param("conn", "TcpConnection"), Param("data", "byte[]")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpSendBytesAsync(ctx, args, "tcp.sendBytesAsync"),
            returnType: "int",
            documentation: "Async. Sends binary data (byte[]) over a TCP connection.\n@param conn The TcpConnection.\n@param data The byte[] data to send.\n@return A Future resolving to the number of bytes sent.");

        // tcp.recvAsync(conn, ?options) — Async receive string.
        ns.Function("recvAsync", [Param("conn", "TcpConnection"), Param("options", "TcpRecvOptions")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpRecvAsync(ctx, args, "tcp.recvAsync"),
            returnType: "string", isVariadic: true,
            documentation: "Async. Receives string data from a TCP connection.\n@param conn The TcpConnection.\n@param options Optional TcpRecvOptions struct with maxBytes and timeout.\n@return A Future resolving to a string, or null on timeout.");

        // tcp.recvBytesAsync(conn, ?options) — Async receive bytes.
        ns.Function("recvBytesAsync", [Param("conn", "TcpConnection"), Param("options", "TcpRecvOptions")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpRecvBytesAsync(ctx, args, "tcp.recvBytesAsync"),
            returnType: "byte[]", isVariadic: true,
            documentation: "Async. Receives binary data from a TCP connection.\n@param conn The TcpConnection.\n@param options Optional TcpRecvOptions struct with maxBytes and timeout.\n@return A Future resolving to a byte[], or null on timeout.");

        // tcp.closeAsync(conn) — Async graceful close.
        ns.Function("closeAsync", [Param("conn", "TcpConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpCloseAsync(ctx, args, "tcp.closeAsync"),
            returnType: "null",
            documentation: "Async. Gracefully closes a TCP connection.\n@param conn The TcpConnection to close.\n@return A Future resolving to null.");

        // tcp.isOpen(conn) — Returns true if the connection is open.
        ns.Function("isOpen", [Param("conn", "TcpConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpIsOpen(ctx, args, "tcp.isOpen"),
            returnType: "bool",
            documentation: "Returns true if the TCP connection is open.\n@param conn The TcpConnection.\n@return True if the connection is open.");

        // tcp.state(conn) — Returns the connection state enum value.
        ns.Function("state", [Param("conn", "TcpConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpState(ctx, args, "tcp.state"),
            returnType: "TcpConnectionState",
            documentation: "Returns the current connection state of a TCP connection.\n@param conn The TcpConnection.\n@return A TcpConnectionState enum value.");

        // tcp.listenAsync(port, handler) — Async multi-client server.
        ns.Function("listenAsync", [Param("port", "int"), Param("handler", "function")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpListenAsync(ctx, args, "tcp.listenAsync"),
            returnType: "TcpServer",
            documentation: "Async. Starts a multi-client TCP server on a port.\n@param port The port to listen on (1-65535, or 0 for auto).\n@param handler A function that receives each TcpConnection.\n@return A Future resolving to a TcpServer handle.");

        // tcp.serverClose(server) — Stops a TCP server.
        ns.Function("serverClose", [Param("server", "TcpServer")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.TcpServerClose(ctx, args, "tcp.serverClose"),
            returnType: "null",
            documentation: "Stops a TCP server and closes the listener.\n@param server The TcpServer handle to stop.");

        return ns.Build();
    }
}
