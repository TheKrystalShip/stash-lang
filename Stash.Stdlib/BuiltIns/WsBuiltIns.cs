namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>ws</c> namespace built-in functions for WebSocket communication.
/// </summary>
public static class WsBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("ws");
        ns.RequiresCapability(StashCapabilities.Network);

        ns.Struct("WsConnection", [
            new BuiltInField("url",      "string"),
            new BuiltInField("protocol", "string"),
        ]);

        ns.Struct("WsMessage", [
            new BuiltInField("data",  "string"),
            new BuiltInField("type",  "string"),
            new BuiltInField("close", "bool"),
        ]);

        ns.Enum("WsConnectionState", ["Connecting", "Open", "Closing", "Closed"]);

        // ws.connect(url, ?options) — Opens a WebSocket connection.
        ns.Function("connect", [Param("url", "string"), Param("options", "dict")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsConnect(ctx, args, "ws.connect"),
            returnType: "WsConnection", isVariadic: true,
            documentation: "Opens a WebSocket connection to the given URL.\n@param url The WebSocket URL (must start with 'ws://' or 'wss://').\n@param options Optional dict with 'headers' (dict), 'timeout' (duration), 'subprotocol' (string).\n@return A Future resolving to a WsConnection.");

        // ws.send(conn, data) — Sends a UTF-8 text message.
        ns.Function("send", [Param("conn", "WsConnection"), Param("data", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsSend(ctx, args, "ws.send"),
            returnType: "int",
            documentation: "Sends a UTF-8 text message over a WebSocket connection.\n@param conn The WsConnection.\n@param data The string data to send.\n@return A Future resolving to the number of bytes sent.");

        // ws.sendBinary(conn, data) — Sends binary data (base64-encoded).
        ns.Function("sendBinary", [Param("conn", "WsConnection"), Param("data", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsSendBinary(ctx, args, "ws.sendBinary"),
            returnType: "int",
            documentation: "Sends binary data (base64-encoded) over a WebSocket connection.\n@param conn The WsConnection.\n@param data Base64-encoded string of bytes to send.\n@return A Future resolving to the number of bytes sent.");

        // ws.recv(conn, ?timeout) — Receives the next complete message.
        ns.Function("recv", [Param("conn", "WsConnection"), Param("timeout", "duration")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsRecv(ctx, args, "ws.recv"),
            returnType: "WsMessage", isVariadic: true,
            documentation: "Receives the next complete message from a WebSocket connection.\n@param conn The WsConnection.\n@param timeout Optional timeout duration (default 30s). Returns null on timeout.\n@return A Future resolving to a WsMessage or null on timeout.");

        // ws.close(conn, ?code, ?reason) — Graceful close handshake.
        ns.Function("close", [Param("conn", "WsConnection"), Param("code", "int"), Param("reason", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsClose(ctx, args, "ws.close"),
            returnType: "null", isVariadic: true,
            documentation: "Performs a graceful WebSocket close handshake.\n@param conn The WsConnection.\n@param code Optional close code (1000-4999, default 1000).\n@param reason Optional close reason string (default empty).\n@return A Future resolving to null.");

        // ws.state(conn) — Returns the WsConnectionState enum value.
        ns.Function("state", [Param("conn", "WsConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsState(ctx, args, "ws.state"),
            returnType: "WsConnectionState",
            documentation: "Returns the current connection state of a WebSocket.\n@param conn The WsConnection.\n@return A WsConnectionState enum value.");

        // ws.isOpen(conn) — Returns true if the WebSocket is in the Open state.
        ns.Function("isOpen", [Param("conn", "WsConnection")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
                NetSocketImpl.WsIsOpen(ctx, args, "ws.isOpen"),
            returnType: "bool",
            documentation: "Returns true if the WebSocket connection is open.\n@param conn The WsConnection.\n@return True if the connection state is Open.");

        return ns.Build();
    }
}
