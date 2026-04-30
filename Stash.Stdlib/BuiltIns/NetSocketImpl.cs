namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Shared state and implementations for TCP, UDP, WebSocket, and DNS built-ins.
/// Used by both the canonical new namespaces and the deprecated <c>net.*</c> shims.
/// </summary>
internal static class NetSocketImpl
{
    // ── Shared state ─────────────────────────────────────────────────────────

    /// <summary>Maps WsConnection instances to their underlying ClientWebSocket.</summary>
    internal static readonly ConditionalWeakTable<StashInstance, ClientWebSocket> WsClients = new();

    /// <summary>Maps async TcpConnection instances to their underlying TcpClient.</summary>
    internal static readonly ConditionalWeakTable<StashInstance, TcpClient> TcpAsyncClients = new();

    /// <summary>Maps TcpServer instances to their underlying TcpListener.</summary>
    internal static readonly ConditionalWeakTable<StashInstance, TcpListener> TcpServers = new();

    /// <summary>Maps async TLS TcpConnection instances to their SslStream.</summary>
    internal static readonly ConditionalWeakTable<StashInstance, SslStream> SslStreams = new();

    internal static readonly StashEnum WsStateEnum  = new("WsConnectionState",  new List<string> { "Connecting", "Open", "Closing", "Closed" });
    internal static readonly StashEnum TcpStateEnum = new("TcpConnectionState", new List<string> { "Open", "Closed" });

    // ── TCP implementations ───────────────────────────────────────────────────

    internal static StashValue TcpConnect(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 2 || args.Length > 3)
            throw new RuntimeError($"{callerQualified}: expected 2 or 3 arguments.");
        var host = SvArgs.String(args, 0, callerQualified);
        var port = SvArgs.Long(args, 1, callerQualified);
        if (port < 1 || port > 65535)
            throw new RuntimeError($"{callerQualified}: port must be between 1 and 65535.", errorType: StashErrorTypes.ValueError);
        int timeout = args.Length > 2 ? (int)SvArgs.Long(args, 2, callerQualified) : 5000;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
            cts.CancelAfter(timeout);
            var client = new TcpClient();
            client.ConnectAsync(host, (int)port, cts.Token).GetAwaiter().GetResult();
            int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
            var conn = new StashInstance("TcpConnection", new Dictionary<string, StashValue>
            {
                ["host"]      = StashValue.FromObj(host),
                ["port"]      = StashValue.FromInt(port),
                ["localPort"] = StashValue.FromInt(localPort),
                ["_client"]   = StashValue.FromObj(client),
            });
            return StashValue.FromObj(conn);
        }
        catch (RuntimeError) { throw; }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new RuntimeError($"{callerQualified}: failed to connect to '{host}:{port}': {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    internal static StashValue TcpSend(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 2)
            throw new RuntimeError($"{callerQualified}: expected 2 arguments.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: first argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);
        var data = SvArgs.String(args, 1, callerQualified);

        if (conn.GetField("_client", null).ToObject() is not TcpClient client)
            throw new RuntimeError($"{callerQualified}: invalid or closed TcpConnection.", errorType: StashErrorTypes.IOError);

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            var stream = client.GetStream();
            stream.Write(bytes, 0, bytes.Length);
            return StashValue.FromInt(bytes.Length);
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"{callerQualified}: send failed: {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    internal static StashValue TcpRecv(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError($"{callerQualified}: expected 1 or 2 arguments.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: first argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);
        int maxBytes = args.Length > 1 ? (int)SvArgs.Long(args, 1, callerQualified) : 4096;

        if (conn.GetField("_client", null).ToObject() is not TcpClient client)
            throw new RuntimeError($"{callerQualified}: invalid or closed TcpConnection.", errorType: StashErrorTypes.IOError);

        try
        {
            var buffer = new byte[maxBytes];
            var stream = client.GetStream();
            int bytesRead = stream.Read(buffer, 0, maxBytes);
            return StashValue.FromObj(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"{callerQualified}: receive failed: {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    internal static StashValue TcpClose(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 1)
            throw new RuntimeError($"{callerQualified}: expected 1 argument.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);

        if (conn.GetField("_client", null).ToObject() is TcpClient client)
            client.Dispose();

        return StashValue.Null;
    }

    internal static StashValue TcpListen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 2)
            throw new RuntimeError($"{callerQualified}: expected 2 arguments.");
        var port = SvArgs.Long(args, 0, callerQualified);
        if (port < 1 || port > 65535)
            throw new RuntimeError($"{callerQualified}: port must be between 1 and 65535.", errorType: StashErrorTypes.ValueError);
        var handler = SvArgs.Callable(args, 1, callerQualified);

        var listener = new TcpListener(IPAddress.Any, (int)port);
        try
        {
            listener.Start();
            using var clientSocket = listener.AcceptTcpClient();
            int localPort  = ((IPEndPoint)clientSocket.Client.LocalEndPoint!).Port;
            string remoteHost = ((IPEndPoint)clientSocket.Client.RemoteEndPoint!).Address.ToString();
            int remotePort = ((IPEndPoint)clientSocket.Client.RemoteEndPoint!).Port;

            var conn = new StashInstance("TcpConnection", new Dictionary<string, StashValue>
            {
                ["host"]      = StashValue.FromObj(remoteHost),
                ["port"]      = StashValue.FromInt(remotePort),
                ["localPort"] = StashValue.FromInt(localPort),
                ["_client"]   = StashValue.FromObj(clientSocket),
            });
            ctx.InvokeCallbackDirect(handler, new StashValue[] { StashValue.FromObj(conn) });
        }
        catch (RuntimeError) { throw; }
        catch (Exception ex)
        {
            throw new RuntimeError($"{callerQualified}: failed on port {port}: {ex.Message}", errorType: StashErrorTypes.IOError);
        }
        finally
        {
            listener.Stop();
        }
        return StashValue.Null;
    }

    internal static StashValue TcpConnectAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 2 || args.Length > 3)
            throw new RuntimeError($"{callerQualified}: expected 2 or 3 arguments.");
        var host = SvArgs.String(args, 0, callerQualified);
        var port = SvArgs.Long(args, 1, callerQualified);
        if (port < 1 || port > 65535)
            throw new RuntimeError($"{callerQualified}: port must be between 1 and 65535.", errorType: StashErrorTypes.ValueError);

        int timeout  = 5000;
        bool noDelay  = false;
        bool keepAlive = false;
        bool tls       = false;
        bool tlsVerify = true;
        string? tlsSni = null;

        if (args.Length > 2 && args[2].IsObj && args[2].AsObj is StashInstance opts && opts.TypeName == "TcpConnectOptions")
        {
            if (opts.GetField("timeoutMs", null).ToObject() is long t)  timeout   = (int)t;
            if (opts.GetField("noDelay",   null).ToObject() is bool nd) noDelay   = nd;
            if (opts.GetField("keepAlive", null).ToObject() is bool ka) keepAlive = ka;
            if (opts.GetField("tls",       null).ToObject() is bool tv) tls       = tv;
            if (opts.GetField("tlsVerify", null).ToObject() is bool tv2) tlsVerify = tv2;
            if (opts.GetField("tlsSni",    null).ToObject() is string sni && !string.IsNullOrEmpty(sni)) tlsSni = sni;
        }

        var capturedHost    = host;
        var capturedPort    = (int)port;
        var capturedTimeout = timeout;
        var capturedTls     = tls;
        var capturedVerify  = tlsVerify;
        var capturedSni     = tlsSni;
        var capturedQual    = callerQualified;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

        var dotnetTask = Task.Run<object?>(async () =>
        {
            var client = new TcpClient();
            if (noDelay)   client.NoDelay = true;
            if (keepAlive) client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            using var timeoutCts  = new CancellationTokenSource(capturedTimeout);
            using var connectCts  = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
            try
            {
                await client.ConnectAsync(capturedHost, capturedPort, connectCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cts.Token.IsCancellationRequested)
            {
                client.Dispose();
                throw new RuntimeError($"{capturedQual}: connection timed out after {capturedTimeout}ms.", errorType: StashErrorTypes.TimeoutError);
            }
            catch (OperationCanceledException)
            {
                client.Dispose();
                throw new RuntimeError($"{capturedQual}: connection was cancelled.", errorType: StashErrorTypes.IOError);
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new RuntimeError($"{capturedQual}: failed to connect to '{capturedHost}:{capturedPort}': {ex.Message}", errorType: StashErrorTypes.IOError);
            }

            SslStream? sslStream = null;
            if (capturedTls)
            {
                string targetHost = capturedSni ?? capturedHost;
                sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                try
                {
                    var sslOptions = new SslClientAuthenticationOptions
                    {
                        TargetHost = targetHost,
                        RemoteCertificateValidationCallback = capturedVerify ? null : (_, _, _, _) => true,
                    };
                    await sslStream.AuthenticateAsClientAsync(sslOptions, connectCts.Token);
                }
                catch (AuthenticationException ex)
                {
                    sslStream.Dispose();
                    client.Dispose();
                    throw new RuntimeError($"{capturedQual}: TLS handshake failed — {ex.Message}.", errorType: StashErrorTypes.IOError);
                }
                catch (IOException ex) when (ex.InnerException is AuthenticationException innerEx)
                {
                    sslStream.Dispose();
                    client.Dispose();
                    if (capturedVerify)
                        throw new RuntimeError($"{capturedQual}: TLS certificate validation failed — {innerEx.Message}. Set tlsVerify: false to skip validation (insecure).", errorType: StashErrorTypes.IOError);
                    throw new RuntimeError($"{capturedQual}: TLS handshake failed — {ex.Message}.", errorType: StashErrorTypes.IOError);
                }
            }

            int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
            var conn = new StashInstance("TcpConnection", new Dictionary<string, StashValue>
            {
                ["host"]      = StashValue.FromObj(capturedHost),
                ["port"]      = StashValue.FromInt(capturedPort),
                ["localPort"] = StashValue.FromInt(localPort),
            });
            TcpAsyncClients.AddOrUpdate(conn, client);
            if (sslStream is not null)
                SslStreams.Add(conn, sslStream);
            return (object?)conn;
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue TcpSendAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 2)
            throw new RuntimeError($"{callerQualified}: expected 2 arguments.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: first argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);
        var data = SvArgs.String(args, 1, callerQualified);

        TcpClient client = GetTcpClient(conn, callerQualified);
        var capturedData = data;
        var capturedQual = callerQualified;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

        var dotnetTask = Task.Run<object?>(async () =>
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(capturedData);
                var stream = GetTcpStream(conn, capturedQual);
                await stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
                return (object?)(long)bytes.Length;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new RuntimeError($"{capturedQual}: send failed: {ex.Message}", errorType: StashErrorTypes.IOError);
            }
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue TcpSendBytesAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 2)
            throw new RuntimeError($"{callerQualified}: expected 2 arguments.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: first argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);
        if (args[1].ToObject() is not StashByteArray byteArr)
            throw new RuntimeError($"{callerQualified}: second argument must be a byte[].", errorType: StashErrorTypes.TypeError);

        TcpClient client = GetTcpClient(conn, callerQualified);
        byte[] data = byteArr.AsSpan().ToArray();
        var capturedQual = callerQualified;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

        var dotnetTask = Task.Run<object?>(async () =>
        {
            try
            {
                var stream = GetTcpStream(conn, capturedQual);
                await stream.WriteAsync(data, 0, data.Length, cts.Token);
                return (object?)(long)data.Length;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new RuntimeError($"{capturedQual}: send failed: {ex.Message}", errorType: StashErrorTypes.IOError);
            }
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue TcpRecvAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError($"{callerQualified}: expected 1 or 2 arguments.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: first argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);

        int maxBytes = 4096;
        int timeout  = 30000;

        if (args.Length > 1 && args[1].IsObj && args[1].AsObj is StashInstance opts && opts.TypeName == "TcpRecvOptions")
        {
            if (opts.GetField("maxBytes",  null).ToObject() is long mb) maxBytes = (int)Math.Min(mb, 16_777_216);
            if (opts.GetField("timeoutMs", null).ToObject() is long t)  timeout  = (int)t;
        }

        TcpClient client = GetTcpClient(conn, callerQualified);
        var capturedQual = callerQualified;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

        var dotnetTask = Task.Run<object?>(async () =>
        {
            var stream = GetTcpStream(conn, capturedQual);
            var buffer = new byte[maxBytes];
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
            try
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, maxBytes, linkedCts.Token);
                return (object?)Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cts.Token.IsCancellationRequested)
            {
                return null;
            }
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue TcpRecvBytesAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError($"{callerQualified}: expected 1 or 2 arguments.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: first argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);

        int maxBytes = 4096;
        int timeout  = 30000;

        if (args.Length > 1 && args[1].IsObj && args[1].AsObj is StashInstance opts && opts.TypeName == "TcpRecvOptions")
        {
            if (opts.GetField("maxBytes",  null).ToObject() is long mb) maxBytes = (int)Math.Min(mb, 16_777_216);
            if (opts.GetField("timeoutMs", null).ToObject() is long t)  timeout  = (int)t;
        }

        TcpClient client = GetTcpClient(conn, callerQualified);
        var capturedQual = callerQualified;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

        var dotnetTask = Task.Run<object?>(async () =>
        {
            var stream = GetTcpStream(conn, capturedQual);
            var buffer = new byte[maxBytes];
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
            try
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, maxBytes, linkedCts.Token);
                if (bytesRead == 0) return (object?)new StashByteArray(Array.Empty<byte>());
                return (object?)new StashByteArray(buffer.AsSpan(0, bytesRead).ToArray());
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cts.Token.IsCancellationRequested)
            {
                return null;
            }
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue TcpCloseAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 1)
            throw new RuntimeError($"{callerQualified}: expected 1 argument.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);

        if (SslStreams.TryGetValue(conn, out SslStream? ssl))
        {
            try { ssl.Dispose(); } catch { }
            SslStreams.Remove(conn);
        }

        if (TcpAsyncClients.TryGetValue(conn, out TcpClient? client))
        {
            try { client.Client.Shutdown(SocketShutdown.Both); } catch { }
            client.Dispose();
            TcpAsyncClients.Remove(conn);
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
            catch { }
        }
        return StashValue.FromObj(StashFuture.Resolved(null));
    }

    internal static StashValue TcpIsOpen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 1)
            throw new RuntimeError($"{callerQualified}: expected 1 argument.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);

        try
        {
            TcpClient client = GetTcpClient(conn, callerQualified);
            return StashValue.FromBool(client.Connected);
        }
        catch
        {
            return StashValue.False;
        }
    }

    internal static StashValue TcpState(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 1)
            throw new RuntimeError($"{callerQualified}: expected 1 argument.");
        if (args[0].ToObject() is not StashInstance conn)
            throw new RuntimeError($"{callerQualified}: argument must be a TcpConnection.", errorType: StashErrorTypes.TypeError);

        try
        {
            TcpClient client = GetTcpClient(conn, callerQualified);
            var member = TcpStateEnum.GetMember(client.Connected ? "Open" : "Closed");
            return member is not null ? StashValue.FromObj(member) : StashValue.Null;
        }
        catch
        {
            var member = TcpStateEnum.GetMember("Closed");
            return member is not null ? StashValue.FromObj(member) : StashValue.Null;
        }
    }

    internal static StashValue TcpListenAsync(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 2)
            throw new RuntimeError($"{callerQualified}: expected 2 arguments.");
        var port = SvArgs.Long(args, 0, callerQualified);
        if (port < 0 || port > 65535)
            throw new RuntimeError($"{callerQualified}: port must be between 0 and 65535.", errorType: StashErrorTypes.ValueError);
        var handler = SvArgs.Callable(args, 1, callerQualified);

        var listener = new TcpListener(IPAddress.Any, (int)port);
        listener.Start();

        int actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverInst = new StashInstance("TcpServer", new Dictionary<string, StashValue>
        {
            ["port"]   = StashValue.FromInt(actualPort),
            ["active"] = StashValue.True,
        });

        TcpServers.AddOrUpdate(serverInst, listener);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

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

                    int localPort  = ((IPEndPoint)clientSocket.Client.LocalEndPoint!).Port;
                    string remoteHost = ((IPEndPoint)clientSocket.Client.RemoteEndPoint!).Address.ToString();
                    int remotePort = ((IPEndPoint)clientSocket.Client.RemoteEndPoint!).Port;

                    var connInst = new StashInstance("TcpConnection", new Dictionary<string, StashValue>
                    {
                        ["host"]      = StashValue.FromObj(remoteHost),
                        ["port"]      = StashValue.FromInt(remotePort),
                        ["localPort"] = StashValue.FromInt(localPort),
                    });
                    TcpAsyncClients.AddOrUpdate(connInst, clientSocket);

                    _ = Task.Run(() =>
                    {
                        try { ctx.InvokeCallbackDirect(handler, new StashValue[] { StashValue.FromObj(connInst) }); }
                        catch { }
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                try { listener.Stop(); } catch { }
                serverInst.SetField("active", StashValue.False, null);
            }
        });

        return StashValue.FromObj(new StashFuture(Task.FromResult<object?>(serverInst), cts));
    }

    internal static StashValue TcpServerClose(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 1)
            throw new RuntimeError($"{callerQualified}: expected 1 argument.");
        if (args[0].ToObject() is not StashInstance server || server.TypeName != "TcpServer")
            throw new RuntimeError($"{callerQualified}: argument must be a TcpServer.", errorType: StashErrorTypes.TypeError);

        if (TcpServers.TryGetValue(server, out TcpListener? listener))
        {
            try { listener.Stop(); } catch { }
            TcpServers.Remove(server);
        }
        server.SetField("active", StashValue.False, null);
        return StashValue.Null;
    }

    // ── UDP implementations ───────────────────────────────────────────────────

    internal static StashValue UdpSend(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 3)
            throw new RuntimeError($"{callerQualified}: expected 3 arguments.");
        var host = SvArgs.String(args, 0, callerQualified);
        var port = SvArgs.Long(args, 1, callerQualified);
        if (port < 1 || port > 65535)
            throw new RuntimeError($"{callerQualified}: port must be between 1 and 65535.", errorType: StashErrorTypes.ValueError);
        var data = SvArgs.String(args, 2, callerQualified);

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            using var udp = new UdpClient();
            udp.Send(bytes, bytes.Length, host, (int)port);
            return StashValue.FromInt(bytes.Length);
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"{callerQualified}: send failed: {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    internal static StashValue UdpRecv(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError($"{callerQualified}: expected 1 or 2 arguments.");
        var port = SvArgs.Long(args, 0, callerQualified);
        if (port < 1 || port > 65535)
            throw new RuntimeError($"{callerQualified}: port must be between 1 and 65535.", errorType: StashErrorTypes.ValueError);
        int timeout = args.Length > 1 ? (int)SvArgs.Long(args, 1, callerQualified) : 5000;

        try
        {
            using var udp = new UdpClient((int)port);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
            cts.CancelAfter(timeout);
            var result = udp.ReceiveAsync(cts.Token).AsTask().GetAwaiter().GetResult();
            string msgData   = Encoding.UTF8.GetString(result.Buffer);
            string senderHost = result.RemoteEndPoint.Address.ToString();
            int senderPort    = result.RemoteEndPoint.Port;
            return StashValue.FromObj(new StashInstance("UdpMessage", new Dictionary<string, StashValue>
            {
                ["data"] = StashValue.FromObj(msgData),
                ["host"] = StashValue.FromObj(senderHost),
                ["port"] = StashValue.FromInt(senderPort),
            }));
        }
        catch (RuntimeError) { throw; }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new RuntimeError($"{callerQualified}: receive failed: {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    // ── WebSocket implementations ─────────────────────────────────────────────

    internal static StashValue WsConnect(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError($"{callerQualified}: expected 1 or 2 arguments.");
        var url = SvArgs.String(args, 0, callerQualified);
        if (!url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            throw new RuntimeError($"{callerQualified}: url must start with 'ws://' or 'wss://'.", errorType: StashErrorTypes.ValueError);

        StashDictionary? options = args.Length > 1 ? SvArgs.Dict(args, 1, callerQualified) : null;

        long timeoutMs     = 10_000;
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

        var capturedUrl  = url;
        var capturedSp   = subprotocol;
        var capturedHdrs = headers;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        if (timeoutMs > 0)
            cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        var dotnetTask = Task.Run<object?>(async () =>
        {
            var client = new ClientWebSocket();
            if (capturedSp is not null)
                client.Options.AddSubProtocol(capturedSp);
            if (capturedHdrs is not null)
            {
                foreach (var rawKey in capturedHdrs.RawKeys())
                {
                    var val = capturedHdrs.Get(rawKey);
                    if (rawKey is string headerName && val.IsObj && val.AsObj is string headerVal)
                        client.Options.SetRequestHeader(headerName, headerVal);
                }
            }
            await client.ConnectAsync(new Uri(capturedUrl), cts.Token);
            var instance = new StashInstance("WsConnection", new Dictionary<string, StashValue>
            {
                ["url"]      = StashValue.FromObj(capturedUrl),
                ["protocol"] = StashValue.FromObj(client.SubProtocol ?? ""),
            });
            WsClients.Add(instance, client);
            return (object?)instance;
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue WsSend(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 2)
            throw new RuntimeError($"{callerQualified}: expected 2 arguments.");
        var client = GetWsClient(args[0].ToObject(), callerQualified);
        var data = SvArgs.String(args, 1, callerQualified);

        var capturedData = data;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

        var dotnetTask = Task.Run<object?>(async () =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(capturedData);
            await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cts.Token);
            return (object?)(long)bytes.Length;
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue WsSendBinary(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 2)
            throw new RuntimeError($"{callerQualified}: expected 2 arguments.");
        var client = GetWsClient(args[0].ToObject(), callerQualified);
        var data = SvArgs.String(args, 1, callerQualified);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            throw new RuntimeError($"{callerQualified}: invalid base64 data", errorType: StashErrorTypes.ParseError);
        }

        var capturedBytes = bytes;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

        var dotnetTask = Task.Run<object?>(async () =>
        {
            await client.SendAsync(new ArraySegment<byte>(capturedBytes), WebSocketMessageType.Binary, endOfMessage: true, cts.Token);
            return (object?)(long)capturedBytes.Length;
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue WsRecv(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError($"{callerQualified}: expected 1 or 2 arguments.");
        var client = GetWsClient(args[0].ToObject(), callerQualified);
        long timeoutMs = 30_000;
        if (args.Length > 1)
            timeoutMs = SvArgs.Duration(args, 1, callerQualified).TotalMilliseconds;

        var capturedQual = callerQualified;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);

        var dotnetTask = Task.Run<object?>(async () =>
        {
            using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            if (timeoutMs > 0)
                recvCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
            try
            {
                var buffer = new byte[4096];
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), recvCts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return (object?)new StashInstance("WsMessage", new Dictionary<string, StashValue>
                        {
                            ["data"]  = StashValue.FromObj(result.CloseStatusDescription ?? ""),
                            ["type"]  = StashValue.FromObj("text"),
                            ["close"] = StashValue.True,
                        });
                    }
                    ms.Write(buffer, 0, result.Count);
                    if (ms.Length > 16 * 1024 * 1024)
                        throw new RuntimeError($"{capturedQual}: message exceeds maximum size of 16MB.", errorType: StashErrorTypes.ValueError);
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
                    ["data"]  = StashValue.FromObj(msgData),
                    ["type"]  = StashValue.FromObj(msgType),
                    ["close"] = StashValue.False,
                });
            }
            catch (OperationCanceledException) when (!cts.Token.IsCancellationRequested)
            {
                return null;
            }
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue WsClose(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length < 1 || args.Length > 3)
            throw new RuntimeError($"{callerQualified}: expected 1 to 3 arguments.");
        var client = GetWsClient(args[0].ToObject(), callerQualified);
        long code = args.Length > 1 ? SvArgs.Long(args, 1, callerQualified) : 1000;
        if (code < 1000 || code > 4999)
            throw new RuntimeError($"{callerQualified}: close code must be between 1000 and 4999.", errorType: StashErrorTypes.ValueError);
        string reason = args.Length > 2 ? SvArgs.String(args, 2, callerQualified) : "";

        var capturedReason = reason;
        var capturedCode   = (int)code;
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
            catch (OperationCanceledException)  { client.Abort(); }
            catch (WebSocketException)          { client.Abort(); }
            return null;
        });
        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    internal static StashValue WsState(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 1)
            throw new RuntimeError($"{callerQualified}: expected 1 argument.");
        var client = GetWsClient(args[0].ToObject(), callerQualified);
        var member = WsStateEnum.GetMember(MapWsState(client.State));
        return member is not null ? StashValue.FromObj(member) : StashValue.Null;
    }

    internal static StashValue WsIsOpen(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        if (args.Length != 1)
            throw new RuntimeError($"{callerQualified}: expected 1 argument.");
        var client = GetWsClient(args[0].ToObject(), callerQualified);
        return StashValue.FromBool(client.State == WebSocketState.Open);
    }

    // ── DNS implementations ───────────────────────────────────────────────────

    internal static StashValue DnsResolve(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var hostname = SvArgs.String(args, 0, callerQualified);
        try
        {
            var entry = Dns.GetHostEntry(hostname);
            if (entry.AddressList.Length == 0)
                throw new RuntimeError($"No DNS records found for '{hostname}'.", errorType: StashErrorTypes.IOError);
            return StashValue.FromObj(new StashIpAddress(entry.AddressList[0], null));
        }
        catch (SocketException ex)
        {
            throw new RuntimeError($"DNS resolution failed for '{hostname}': {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    internal static StashValue DnsResolveAll(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var hostname = SvArgs.String(args, 0, callerQualified);
        try
        {
            var entry = Dns.GetHostEntry(hostname);
            return StashValue.FromObj(entry.AddressList.Select(a => (object?)new StashIpAddress(a, null)).ToList());
        }
        catch (SocketException ex)
        {
            throw new RuntimeError($"DNS resolution failed for '{hostname}': {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    internal static StashValue DnsReverseLookup(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var ip = SvArgs.IpAddress(args, 0, callerQualified);
        try
        {
            var entry = Dns.GetHostEntry(ip.Address);
            return StashValue.FromObj(entry.HostName);
        }
        catch (SocketException ex)
        {
            throw new RuntimeError($"Reverse DNS lookup failed for '{ip}': {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    internal static StashValue DnsResolveMx(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var domain = SvArgs.String(args, 0, callerQualified);
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
            throw new RuntimeError($"{callerQualified}: DNS query failed for '{domain}': {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    internal static StashValue DnsResolveTxt(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var domain = SvArgs.String(args, 0, callerQualified);
        try
        {
            var records = DnsQueryHelper.QueryRecords(domain, DnsQueryHelper.TypeTxt);
            var list = records.Select(r => StashValue.FromObj(r.Data)).ToList();
            return StashValue.FromObj(list);
        }
        catch (RuntimeError) { throw; }
        catch (Exception ex)
        {
            throw new RuntimeError($"{callerQualified}: DNS query failed for '{domain}': {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static ClientWebSocket GetWsClient(object? arg, string funcName)
    {
        if (arg is not StashInstance inst || inst.TypeName != "WsConnection")
            throw new RuntimeError($"First argument to '{funcName}' must be a WsConnection.", errorType: StashErrorTypes.TypeError);
        if (!WsClients.TryGetValue(inst, out ClientWebSocket? ws))
            throw new RuntimeError($"{funcName}: connection is invalid or closed.", errorType: StashErrorTypes.IOError);
        return ws;
    }

    internal static Stream GetTcpStream(StashInstance conn, string funcName)
    {
        if (SslStreams.TryGetValue(conn, out SslStream? ssl))
            return ssl;
        return GetTcpClient(conn, funcName).GetStream();
    }

    internal static TcpClient GetTcpClient(StashInstance conn, string funcName)
    {
        if (TcpAsyncClients.TryGetValue(conn, out TcpClient? asyncClient))
            return asyncClient;

        try
        {
            var clientField = conn.GetField("_client", null);
            if (clientField.ToObject() is TcpClient syncClient)
                return syncClient;
        }
        catch { }

        throw new RuntimeError($"{funcName}: invalid or closed TcpConnection.", errorType: StashErrorTypes.IOError);
    }

    internal static string MapWsState(WebSocketState state) => state switch
    {
        WebSocketState.None or WebSocketState.Connecting => "Connecting",
        WebSocketState.Open                              => "Open",
        WebSocketState.CloseSent or WebSocketState.CloseReceived => "Closing",
        WebSocketState.Closed or WebSocketState.Aborted  => "Closed",
        _ => "Closed",
    };

    // ── DNS query helper (moved from NetBuiltIns) ─────────────────────────────

    internal static class DnsQueryHelper
    {
        internal const ushort TypeMx  = 15;
        internal const ushort TypeTxt = 16;

        internal record DnsRecord(int Priority, string Data);

        internal static List<DnsRecord> QueryRecords(string domain, ushort queryType)
        {
            string nameserver = GetSystemNameserver() ?? "8.8.8.8";
            byte[] query    = BuildQuery(domain, queryType);
            byte[] response = SendDnsQuery(nameserver, 53, query, timeoutMs: 5000);
            return ParseResponse(response, queryType);
        }

        private static string? GetSystemNameserver()
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    foreach (var line in File.ReadLines("/etc/resolv.conf"))
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
                catch { }
            }
            return null;
        }

        private static byte[] BuildQuery(string domain, ushort queryType)
        {
            using var ms = new MemoryStream();
            // Transaction ID
            ms.Write(new byte[] { 0x12, 0x34 }, 0, 2);
            // Flags: Standard query
            ms.Write(new byte[] { 0x01, 0x00 }, 0, 2);
            // Questions: 1
            ms.Write(new byte[] { 0x00, 0x01 }, 0, 2);
            // Answer/Authority/Additional RRs: 0
            ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0, 6);

            // QNAME
            foreach (var label in domain.Split('.'))
            {
                byte[] lbl = Encoding.ASCII.GetBytes(label);
                ms.WriteByte((byte)lbl.Length);
                ms.Write(lbl, 0, lbl.Length);
            }
            ms.WriteByte(0); // root label

            // QTYPE
            ms.WriteByte((byte)(queryType >> 8));
            ms.WriteByte((byte)(queryType & 0xFF));
            // QCLASS: IN
            ms.Write(new byte[] { 0x00, 0x01 }, 0, 2);

            return ms.ToArray();
        }

        private static byte[] SendDnsQuery(string nameserver, int port, byte[] query, int timeoutMs)
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = timeoutMs;
            udp.Client.SendTimeout    = timeoutMs;
            udp.Send(query, query.Length, nameserver, port);
            var ep = new IPEndPoint(IPAddress.Any, 0);
            return udp.Receive(ref ep);
        }

        private static List<DnsRecord> ParseResponse(byte[] response, ushort queryType)
        {
            var records = new List<DnsRecord>();
            if (response.Length < 12) return records;

            int anCount = (response[6] << 8) | response[7];
            if (anCount == 0) return records;

            int pos = 12;
            // Skip question section
            while (pos < response.Length && response[pos] != 0)
            {
                if ((response[pos] & 0xC0) == 0xC0) { pos += 2; break; }
                pos += response[pos] + 1;
            }
            if (pos < response.Length && response[pos] == 0) pos++;
            pos += 4; // skip QTYPE + QCLASS

            for (int i = 0; i < anCount && pos < response.Length; i++)
            {
                // Skip name
                while (pos < response.Length)
                {
                    if ((response[pos] & 0xC0) == 0xC0) { pos += 2; break; }
                    if (response[pos] == 0) { pos++; break; }
                    pos += response[pos] + 1;
                }

                if (pos + 10 > response.Length) break;
                ushort rType  = (ushort)((response[pos] << 8) | response[pos + 1]);
                int rdLen = (response[pos + 8] << 8) | response[pos + 9];
                pos += 10;

                if (pos + rdLen > response.Length) break;

                if (rType == queryType)
                {
                    if (queryType == TypeMx && rdLen >= 3)
                    {
                        int priority = (response[pos] << 8) | response[pos + 1];
                        string exchange = ParseDnsName(response, pos + 2);
                        records.Add(new DnsRecord(priority, exchange));
                    }
                    else if (queryType == TypeTxt && rdLen >= 1)
                    {
                        int txtPos = pos;
                        int end    = pos + rdLen;
                        var sb = new StringBuilder();
                        while (txtPos < end)
                        {
                            int len = response[txtPos++];
                            if (txtPos + len <= end)
                                sb.Append(Encoding.UTF8.GetString(response, txtPos, len));
                            txtPos += len;
                        }
                        records.Add(new DnsRecord(0, sb.ToString()));
                    }
                }

                pos += rdLen;
            }

            return records;
        }

        private static string ParseDnsName(byte[] response, int pos)
        {
            var parts = new List<string>();
            int safetyLimit = 128;
            while (pos < response.Length && safetyLimit-- > 0)
            {
                if ((response[pos] & 0xC0) == 0xC0)
                {
                    int ptr = ((response[pos] & 0x3F) << 8) | response[pos + 1];
                    parts.Add(ParseDnsName(response, ptr));
                    break;
                }
                if (response[pos] == 0) break;
                int len = response[pos++];
                if (pos + len > response.Length) break;
                parts.Add(Encoding.ASCII.GetString(response, pos, len));
                pos += len;
            }
            return string.Join(".", parts);
        }
    }
}
