namespace Stash.Tests.Interpreting;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class AsyncTcpBuiltInsTests : StashTestBase
{
    // ── C# TCP Echo Server Helper ────────────────────────────────────────────

    private static (TcpListener listener, Task serverTask, int port) StartTcpEchoServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var stream = client.GetStream();
                        var buffer = new byte[4096];
                        int bytesRead = await stream.ReadAsync(buffer);
                        if (bytesRead > 0)
                            await stream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    }
                    catch { }
                    finally { client.Dispose(); }
                });
            }
        });

        return (listener, serverTask, port);
    }

    /// <summary>Server that sends data first, then closes.</summary>
    private static (TcpListener listener, Task serverTask, int port) StartTcpSendServer(string data)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var stream = client.GetStream();
                        byte[] bytes = Encoding.UTF8.GetBytes(data);
                        await stream.WriteAsync(bytes);
                    }
                    catch { }
                    finally { client.Dispose(); }
                });
            }
        });

        return (listener, serverTask, port);
    }

    /// <summary>Server that immediately closes the connection (no data).</summary>
    private static (TcpListener listener, Task serverTask, int port) StartTcpCloseServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }

                client.Dispose();
            }
        });

        return (listener, serverTask, port);
    }

    /// <summary>Server that accepts and holds the connection (for timeout tests).</summary>
    private static (TcpListener listener, Task serverTask, int port) StartTcpSilentServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clients = new System.Collections.Concurrent.ConcurrentBag<TcpClient>();

        var serverTask = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }

                clients.Add(client); // Hold connection open, don't send anything
            }
        });

        return (listener, serverTask, port);
    }

    private static void StopTcpServer(TcpListener listener, Task serverTask)
    {
        listener.Stop();
        try { serverTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }

    // ── net.tcpConnectAsync — reserved ports: 19940-19941 ────────────────────

    // ── net.tcpConnectAsync ───────────────────────────────────────────────────

    [Fact]
    public void TcpConnectAsync_Loopback_ReturnsConnection()
    {
        var (listener, serverTask, port) = StartTcpEchoServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                io.println(conn.host);
                io.println(conn.port);
                io.println(conn.localPort > 0);
                await net.tcpCloseAsync(conn);
            ");
            var lines = output.Trim().Split('\n');
            Assert.Equal("127.0.0.1", lines[0].Trim());
            Assert.Equal(port.ToString(), lines[1].Trim());
            Assert.Equal("true", lines[2].Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpConnectAsync_InvalidPort_Throws()
    {
        RunExpectingError(@"
            let conn = await net.tcpConnectAsync(""127.0.0.1"", 99999);
        ");
    }

    [Fact]
    public void TcpConnectAsync_WithOptions_AppliesSettings()
    {
        var (listener, serverTask, port) = StartTcpEchoServer();
        try
        {
            var output = RunCapturingOutput($@"
                let opts = net.TcpConnectOptions {{ noDelay: true, keepAlive: true }};
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port}, opts);
                io.println(net.tcpIsOpen(conn));
                await net.tcpCloseAsync(conn);
            ");
            Assert.Equal("true", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpConnectAsync_TlsOption_ThrowsNotSupported()
    {
        RunExpectingError(@"
            let opts = net.TcpConnectOptions { tls: true };
            let conn = await net.tcpConnectAsync(""127.0.0.1"", 12345, opts);
        ");
    }

    // ── net.tcpSendAsync ─────────────────────────────────────────────────────

    [Fact]
    public void TcpSendAsync_StringData_ReturnsByteCount()
    {
        var (listener, serverTask, port) = StartTcpEchoServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                let sent = await net.tcpSendAsync(conn, ""hello"");
                io.println(sent);
                await net.tcpCloseAsync(conn);
            ");
            Assert.Equal("5", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpSendBytesAsync_ByteArray_ReturnsByteCount()
    {
        var (listener, serverTask, port) = StartTcpEchoServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                let data = buf.from(""hello"");
                let sent = await net.tcpSendBytesAsync(conn, data);
                io.println(sent);
                await net.tcpCloseAsync(conn);
            ");
            Assert.Equal("5", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    // ── net.tcpRecvAsync ─────────────────────────────────────────────────────

    [Fact]
    public void TcpRecvAsync_ReceivesData_ReturnsString()
    {
        var (listener, serverTask, port) = StartTcpEchoServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                await net.tcpSendAsync(conn, ""hello"");
                let response = await net.tcpRecvAsync(conn);
                io.println(response);
                await net.tcpCloseAsync(conn);
            ");
            Assert.Equal("hello", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpRecvAsync_Timeout_ReturnsNull()
    {
        var (listener, serverTask, port) = StartTcpSilentServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                let opts = net.TcpRecvOptions {{}};
                opts.timeoutMs = 200;
                let result = await net.tcpRecvAsync(conn, opts);
                io.println(result == null);
                await net.tcpCloseAsync(conn);
            ");
            Assert.Equal("true", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpRecvAsync_PeerClosed_ReturnsEmptyString()
    {
        var (listener, serverTask, port) = StartTcpCloseServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                time.sleep(0.1);
                let result = await net.tcpRecvAsync(conn);
                io.println(result == """");
                await net.tcpCloseAsync(conn);
            ");
            Assert.Equal("true", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpRecvBytesAsync_ReceivesBinary_ReturnsByteArray()
    {
        var (listener, serverTask, port) = StartTcpSendServer("binary");
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                let received = await net.tcpRecvBytesAsync(conn);
                io.println(buf.len(received));
                await net.tcpCloseAsync(conn);
            ");
            Assert.Equal("6", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    // ── net.tcpCloseAsync ────────────────────────────────────────────────────

    [Fact]
    public void TcpCloseAsync_OpenConnection_Succeeds()
    {
        var (listener, serverTask, port) = StartTcpSilentServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                io.println(""connected"");
                await net.tcpCloseAsync(conn);
                io.println(""closed"");
            ");
            Assert.Contains("connected", output);
            // No exception = pass
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpCloseAsync_AlreadyClosed_Idempotent()
    {
        var (listener, serverTask, port) = StartTcpSilentServer();
        try
        {
            RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                await net.tcpCloseAsync(conn);
                await net.tcpCloseAsync(conn);
            ");
            // No exception = pass
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    // ── net.tcpIsOpen ────────────────────────────────────────────────────────

    [Fact]
    public void TcpIsOpen_OpenConnection_ReturnsTrue()
    {
        var (listener, serverTask, port) = StartTcpSilentServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                io.println(net.tcpIsOpen(conn));
                await net.tcpCloseAsync(conn);
            ");
            Assert.Equal("true", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpIsOpen_ClosedConnection_ReturnsFalse()
    {
        var (listener, serverTask, port) = StartTcpSilentServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                await net.tcpCloseAsync(conn);
                io.println(net.tcpIsOpen(conn));
            ");
            Assert.Equal("false", output.Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    // ── net.tcpState ─────────────────────────────────────────────────────────

    [Fact]
    public void TcpState_OpenConnection_ReturnsOpen()
    {
        var (listener, serverTask, port) = StartTcpSilentServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                io.println(net.tcpState(conn));
                io.println(net.tcpState(conn) == net.TcpConnectionState.Open);
                await net.tcpCloseAsync(conn);
            ");
            var lines = output.Trim().Split('\n');
            Assert.Equal("TcpConnectionState.Open", lines[0].Trim());
            Assert.Equal("true", lines[1].Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    [Fact]
    public void TcpState_ClosedConnection_ReturnsClosed()
    {
        var (listener, serverTask, port) = StartTcpSilentServer();
        try
        {
            var output = RunCapturingOutput($@"
                let conn = await net.tcpConnectAsync(""127.0.0.1"", {port});
                await net.tcpCloseAsync(conn);
                io.println(net.tcpState(conn));
                io.println(net.tcpState(conn) == net.TcpConnectionState.Closed);
            ");
            var lines = output.Trim().Split('\n');
            Assert.Equal("TcpConnectionState.Closed", lines[0].Trim());
            Assert.Equal("true", lines[1].Trim());
        }
        finally { StopTcpServer(listener, serverTask); }
    }

    // ── net.tcpListenAsync ───────────────────────────────────────────────────

    [Fact]
    public void TcpListenAsync_StartsAndReturnsServer()
    {
        // Test that tcpListenAsync starts a server and returns a TcpServer handle
        // Use a simple handler that doesn't require concurrent await
        var output = RunCapturingOutput(@"
            let server = await net.tcpListenAsync(19940, (c) => {
                net.tcpClose(c);
            });
            io.println(server.port);
            io.println(server.active);
            net.tcpServerClose(server);
        ");
        var lines = output.Trim().Split('\n');
        Assert.Equal("19940", lines[0].Trim());
        Assert.Equal("true", lines[1].Trim());
    }

    // ── net.tcpServerClose ───────────────────────────────────────────────────

    [Fact]
    public void TcpServerClose_StopsAccepting()
    {
        var output = RunCapturingOutput(@"
            let server = await net.tcpListenAsync(19941, (c) => {
                net.tcpClose(c);
            });
            io.println(server.active);
            net.tcpServerClose(server);
            io.println(server.active);
        ");
        var lines = output.Trim().Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("false", lines[1].Trim());
    }
}
