namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>ws</c> namespace built-in functions for WebSocket communication.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class WsBuiltIns
{
    /// <summary>A WebSocket connection handle.</summary>
    [StashStruct]
    public sealed record WsConnection(string Url, string Protocol);

    /// <summary>A received WebSocket message.</summary>
    [StashStruct]
    public sealed record WsMessage(string Data, string Type, bool Close);

    /// <summary>The state of a WebSocket connection.</summary>
    [StashEnum]
    public enum WsConnectionState { Connecting, Open, Closing, Closed }

    /// <summary>Opens a WebSocket connection to the given URL.</summary>
    /// <param name="url">The WebSocket URL (must start with 'ws://' or 'wss://').</param>
    /// <param name="options">Optional dict with 'headers' (dict), 'timeout' (duration), 'subprotocol' (string).</param>
    /// <returns>A Future resolving to a WsConnection.</returns>
    [StashFn(Raw = true, ReturnType = "WsConnection")]
    private static StashValue Connect(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsConnect(ctx, args, "ws.connect");

    /// <summary>Sends a UTF-8 text message over a WebSocket connection.</summary>
    /// <param name="conn">The WsConnection.</param>
    /// <param name="data">The string data to send.</param>
    /// <returns>A Future resolving to the number of bytes sent.</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Send(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsSend(ctx, args, "ws.send");

    /// <summary>Sends binary data (base64-encoded) over a WebSocket connection.</summary>
    /// <param name="conn">The WsConnection.</param>
    /// <param name="data">Base64-encoded string of bytes to send.</param>
    /// <returns>A Future resolving to the number of bytes sent.</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue SendBinary(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsSendBinary(ctx, args, "ws.sendBinary");

    /// <summary>Receives the next complete message from a WebSocket connection.</summary>
    /// <param name="conn">The WsConnection.</param>
    /// <param name="timeout">Optional timeout duration (default 30s). Returns null on timeout.</param>
    /// <returns>A Future resolving to a WsMessage or null on timeout.</returns>
    [StashFn(Raw = true, ReturnType = "WsMessage")]
    private static StashValue Recv(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsRecv(ctx, args, "ws.recv");

    /// <summary>Performs a graceful WebSocket close handshake.</summary>
    /// <param name="conn">The WsConnection.</param>
    /// <param name="code">Optional close code (1000-4999, default 1000).</param>
    /// <param name="reason">Optional close reason string (default empty).</param>
    /// <returns>A Future resolving to null.</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Close(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsClose(ctx, args, "ws.close");

    /// <summary>Returns the current connection state of a WebSocket.</summary>
    /// <param name="conn">The WsConnection.</param>
    /// <returns>A WsConnectionState enum value.</returns>
    [StashFn(Raw = true, ReturnType = "WsConnectionState")]
    private static StashValue State(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsState(ctx, args, "ws.state");

    /// <summary>Returns true if the WebSocket connection is open.</summary>
    /// <param name="conn">The WsConnection.</param>
    /// <returns>True if the connection state is Open.</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue IsOpen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => NetSocketImpl.WsIsOpen(ctx, args, "ws.isOpen");
}
