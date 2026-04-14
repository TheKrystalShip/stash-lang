using System.Net;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class NetBuiltInsTests : StashTestBase
{


    private static new void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        Assert.ThrowsAny<RuntimeError>(() => vm.Execute(chunk));
    }

    // ── net.subnetInfo ────────────────────────────────────────────────────────

    [Fact]
    public void SubnetInfo_ClassC_ReturnsCorrectFields()
    {
        var result = Run("let result = net.subnetInfo(@192.168.1.100/24);");
        var info = Assert.IsType<StashInstance>(result);

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null).ToObject());
        Assert.Equal("192.168.1.0/24", network.ToString());

        var broadcast = Assert.IsType<StashIpAddress>(info.GetField("broadcast", null).ToObject());
        Assert.Equal("192.168.1.255", broadcast.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null).ToObject());
        Assert.Equal("255.255.255.0", mask.ToString());

        var wildcard = Assert.IsType<StashIpAddress>(info.GetField("wildcard", null).ToObject());
        Assert.Equal("0.0.0.255", wildcard.ToString());

        Assert.Equal(254L, info.GetField("hostCount", null).ToObject());

        var firstHost = Assert.IsType<StashIpAddress>(info.GetField("firstHost", null).ToObject());
        Assert.Equal("192.168.1.1", firstHost.ToString());

        var lastHost = Assert.IsType<StashIpAddress>(info.GetField("lastHost", null).ToObject());
        Assert.Equal("192.168.1.254", lastHost.ToString());
    }

    [Fact]
    public void SubnetInfo_ClassA_ReturnsCorrectFields()
    {
        var result = Run("let result = net.subnetInfo(@10.0.0.0/8);");
        var info = Assert.IsType<StashInstance>(result);

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null).ToObject());
        Assert.Equal("10.0.0.0/8", network.ToString());

        var broadcast = Assert.IsType<StashIpAddress>(info.GetField("broadcast", null).ToObject());
        Assert.Equal("10.255.255.255", broadcast.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null).ToObject());
        Assert.Equal("255.0.0.0", mask.ToString());

        var wildcard = Assert.IsType<StashIpAddress>(info.GetField("wildcard", null).ToObject());
        Assert.Equal("0.255.255.255", wildcard.ToString());

        Assert.Equal(16777214L, info.GetField("hostCount", null).ToObject());

        var firstHost = Assert.IsType<StashIpAddress>(info.GetField("firstHost", null).ToObject());
        Assert.Equal("10.0.0.1", firstHost.ToString());

        var lastHost = Assert.IsType<StashIpAddress>(info.GetField("lastHost", null).ToObject());
        Assert.Equal("10.255.255.254", lastHost.ToString());
    }

    [Fact]
    public void SubnetInfo_Slash32_SingleHost()
    {
        var result = Run("let result = net.subnetInfo(@192.168.1.1/32);");
        var info = Assert.IsType<StashInstance>(result);

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null).ToObject());
        Assert.Equal("192.168.1.1/32", network.ToString());

        var broadcast = Assert.IsType<StashIpAddress>(info.GetField("broadcast", null).ToObject());
        Assert.Equal("192.168.1.1", broadcast.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null).ToObject());
        Assert.Equal("255.255.255.255", mask.ToString());

        var wildcard = Assert.IsType<StashIpAddress>(info.GetField("wildcard", null).ToObject());
        Assert.Equal("0.0.0.0", wildcard.ToString());

        Assert.Equal(1L, info.GetField("hostCount", null).ToObject());

        var firstHost = Assert.IsType<StashIpAddress>(info.GetField("firstHost", null).ToObject());
        Assert.Equal("192.168.1.1", firstHost.ToString());

        var lastHost = Assert.IsType<StashIpAddress>(info.GetField("lastHost", null).ToObject());
        Assert.Equal("192.168.1.1", lastHost.ToString());
    }

    [Fact]
    public void SubnetInfo_Slash31_PointToPoint()
    {
        var result = Run("let result = net.subnetInfo(@10.0.0.0/31);");
        var info = Assert.IsType<StashInstance>(result);

        Assert.Equal(2L, info.GetField("hostCount", null).ToObject());

        var firstHost = Assert.IsType<StashIpAddress>(info.GetField("firstHost", null).ToObject());
        Assert.Equal("10.0.0.0", firstHost.ToString());

        var lastHost = Assert.IsType<StashIpAddress>(info.GetField("lastHost", null).ToObject());
        Assert.Equal("10.0.0.1", lastHost.ToString());
    }

    [Fact]
    public void SubnetInfo_ClassB_ReturnsCorrectFields()
    {
        var result = Run("let result = net.subnetInfo(@172.16.5.4/16);");
        var info = Assert.IsType<StashInstance>(result);

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null).ToObject());
        Assert.Equal("172.16.0.0/16", network.ToString());

        var broadcast = Assert.IsType<StashIpAddress>(info.GetField("broadcast", null).ToObject());
        Assert.Equal("172.16.255.255", broadcast.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null).ToObject());
        Assert.Equal("255.255.0.0", mask.ToString());

        Assert.Equal(65534L, info.GetField("hostCount", null).ToObject());
    }

    [Fact]
    public void SubnetInfo_NoCidr_ThrowsError()
    {
        RunExpectingError("net.subnetInfo(@192.168.1.1);");
    }

    [Fact]
    public void SubnetInfo_IPv6_Slash64()
    {
        var result = Run("let result = net.subnetInfo(@fe80::1/64);");
        var info = Assert.IsType<StashInstance>(result);

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null).ToObject());
        Assert.Equal("fe80::/64", network.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null).ToObject());
        Assert.Equal("ffff:ffff:ffff:ffff::", mask.ToString());

        Assert.Equal(long.MaxValue, info.GetField("hostCount", null).ToObject());
    }

    // ── net.mask ──────────────────────────────────────────────────────────────

    [Fact]
    public void Mask_ClassC_Returns255_255_255_0()
    {
        var result = Run("let result = net.mask(@192.168.1.0/24);");
        var ip = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("255.255.255.0", ip.ToString());
    }

    [Fact]
    public void Mask_ClassA_Returns255_0_0_0()
    {
        var result = Run("let result = net.mask(@10.0.0.0/8);");
        var ip = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("255.0.0.0", ip.ToString());
    }

    [Fact]
    public void Mask_Slash30_Returns255_255_255_252()
    {
        var result = Run("let result = net.mask(@10.0.0.0/30);");
        var ip = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("255.255.255.252", ip.ToString());
    }

    [Fact]
    public void Mask_NoCidr_ThrowsError()
    {
        RunExpectingError("net.mask(@10.0.0.1);");
    }

    [Fact]
    public void Mask_Slash0_ReturnsAllZeros()
    {
        var result = Run("let result = net.mask(@0.0.0.0/0);");
        var ip = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("0.0.0.0", ip.ToString());
    }

    // ── net.network ───────────────────────────────────────────────────────────

    [Fact]
    public void Network_HostInSubnet_ReturnsNetworkAddr()
    {
        var result = Run("let result = net.network(@192.168.1.100/24);");
        var ip = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.1.0/24", ip.ToString());
    }

    [Fact]
    public void Network_AlreadyNetwork_ReturnsSame()
    {
        var result = Run("let result = net.network(@10.0.0.0/8);");
        var ip = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("10.0.0.0/8", ip.ToString());
    }

    [Fact]
    public void Network_NoCidr_ThrowsError()
    {
        RunExpectingError("net.network(@192.168.1.1);");
    }

    // ── net.broadcast ─────────────────────────────────────────────────────────

    [Fact]
    public void Broadcast_ClassC_Returns255()
    {
        var result = Run("let result = net.broadcast(@192.168.1.0/24);");
        var ip = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.1.255", ip.ToString());
    }

    [Fact]
    public void Broadcast_ClassA_ReturnsCorrect()
    {
        var result = Run("let result = net.broadcast(@10.0.0.0/8);");
        var ip = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("10.255.255.255", ip.ToString());
    }

    // ── net.hostCount ─────────────────────────────────────────────────────────

    [Fact]
    public void HostCount_ClassC_Returns254()
    {
        var result = Run("let result = net.hostCount(@192.168.1.0/24);");
        Assert.Equal(254L, result);
    }

    [Fact]
    public void HostCount_Slash32_Returns1()
    {
        var result = Run("let result = net.hostCount(@192.168.1.0/32);");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void HostCount_Slash31_Returns2()
    {
        var result = Run("let result = net.hostCount(@10.0.0.0/31);");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void HostCount_Slash8_ReturnsLargeValue()
    {
        var result = Run("let result = net.hostCount(@10.0.0.0/8);");
        Assert.Equal(16777214L, result);
    }

    [Fact]
    public void HostCount_NoCidr_ThrowsError()
    {
        RunExpectingError("net.hostCount(@192.168.1.1);");
    }

    [Fact]
    public void HostCount_Slash0_ReturnsMax()
    {
        var result = Run("let result = net.hostCount(@0.0.0.0/0);");
        Assert.Equal(4294967294L, result);
    }

    // ── net.interfaces ────────────────────────────────────────────────────────

    [Fact]
    public void Interfaces_ReturnsArray()
    {
        var result = Run("let ifaces = net.interfaces(); let result = len(ifaces) > 0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Interfaces_LoopbackExists()
    {
        var result = Run("let result = net.interfaces();");
        var list = Assert.IsType<List<object?>>(result);
        Assert.NotEmpty(list);
    }

    // ── net.isPortOpen (validation) ───────────────────────────────────────────

    [Fact]
    public void IsPortOpen_InvalidPort_ThrowsError()
    {
        RunExpectingError("let result = net.isPortOpen(@127.0.0.1, 0);");
    }

    [Fact]
    public void IsPortOpen_PortTooHigh_ThrowsError()
    {
        RunExpectingError("let result = net.isPortOpen(@127.0.0.1, 99999);");
    }

    // ── struct field access ───────────────────────────────────────────────────

    [Fact]
    public void SubnetInfo_FieldAccess_Works()
    {
        var result = Run(@"
let info = net.subnetInfo(@192.168.1.0/24);
let result = info.mask;
");
        var mask = Assert.IsType<StashIpAddress>(result);
        Assert.Equal("255.255.255.0", mask.ToString());
    }

    [Fact]
    public void SubnetInfo_HostCountField_ReturnsInt()
    {
        var result = Run(@"
let info = net.subnetInfo(@10.0.0.0/24);
let result = info.hostCount;
");
        Assert.Equal(254L, result);
    }

    // ── net.tcpConnect / tcpSend / tcpRecv / tcpClose ───────────────────────

    [Fact]
    public void TcpConnect_InvalidPort_ThrowsError()
    {
        RunExpectingError(@"
            let result = net.tcpConnect(""localhost"", 99999);
        ");
    }

    [Fact]
    public void TcpConnect_InvalidHost_ThrowsError()
    {
        RunExpectingError(@"
            let result = net.tcpConnect(""this.host.does.not.exist.invalid"", 80);
        ");
    }

    [Fact]
    public void TcpConnect_PortZero_ThrowsError()
    {
        RunExpectingError(@"
            let result = net.tcpConnect(""localhost"", 0);
        ");
    }

    [Fact]
    public void TcpEcho_SendRecv_ReturnsData()
    {
        // Use task.run for the server, connect from main thread
        var output = RunCapturingOutput(@"
            let serverReady = false;
            let port = 19876;

            // Server task
            let server = task.run(() => {
                net.tcpListen(port, (conn) => {
                    let data = net.tcpRecv(conn);
                    net.tcpSend(conn, ""echo:"" + data);
                    net.tcpClose(conn);
                });
            });

            // Give server time to start
            time.sleep(0.1);

            // Client
            let conn = net.tcpConnect(""127.0.0.1"", port);
            net.tcpSend(conn, ""hello"");
            let response = net.tcpRecv(conn);
            net.tcpClose(conn);

            io.println(response);
            task.await(server);
        ");
        Assert.Contains("echo:hello", output.Trim());
    }

    [Fact]
    public void TcpConnect_ReturnsStruct()
    {
        // Start a temp listener, connect, check fields
        var output = RunCapturingOutput(@"
            let port = 19877;
            let server = task.run(() => {
                net.tcpListen(port, (conn) => {
                    net.tcpClose(conn);
                });
            });
            time.sleep(0.1);
            let conn = net.tcpConnect(""127.0.0.1"", port);
            io.println(conn.host);
            io.println(conn.port);
            io.println(conn.localPort > 0);
            net.tcpClose(conn);
            task.await(server);
        ");
        var lines = output.Trim().Split('\n');
        Assert.Equal("127.0.0.1", lines[0].Trim());
        Assert.Equal("19877", lines[1].Trim());
        Assert.Equal("true", lines[2].Trim());
    }

    [Fact]
    public void TcpSend_ReturnsByteCount()
    {
        var output = RunCapturingOutput(@"
            let port = 19878;
            let server = task.run(() => {
                net.tcpListen(port, (conn) => {
                    net.tcpRecv(conn);
                    net.tcpClose(conn);
                });
            });
            time.sleep(0.1);
            let conn = net.tcpConnect(""127.0.0.1"", port);
            let sent = net.tcpSend(conn, ""hello"");
            io.println(sent);
            net.tcpClose(conn);
            task.await(server);
        ");
        Assert.Equal("5", output.Trim());
    }

    // ── net.udpSend / udpRecv ──────────────────────────────────────────────

    [Fact]
    public void UdpSendRecv_Loopback_ReturnsData()
    {
        var output = RunCapturingOutput(@"
            let port = 19879;

            // Start receiver in a task
            let receiver = task.run(() => {
                let msg = net.udpRecv(port, 5000);
                return msg.data;
            });

            // Give receiver time to bind
            time.sleep(0.1);

            net.udpSend(""127.0.0.1"", port, ""hello udp"");

            let result = task.await(receiver);
            io.println(result);
        ");
        Assert.Equal("hello udp", output.Trim());
    }

    [Fact]
    public void UdpRecv_ReturnsUdpMessageStruct()
    {
        var output = RunCapturingOutput(@"
            let port = 19880;
            let receiver = task.run(() => {
                let msg = net.udpRecv(port, 5000);
                return msg;
            });
            time.sleep(0.1);
            net.udpSend(""127.0.0.1"", port, ""test"");
            let msg = task.await(receiver);
            io.println(msg.data);
            io.println(msg.host);
            io.println(msg.port > 0);
        ");
        var lines = output.Trim().Split('\n');
        Assert.Equal("test", lines[0].Trim());
        Assert.Equal("127.0.0.1", lines[1].Trim());
        Assert.Equal("true", lines[2].Trim());
    }

    [Fact]
    public void UdpSend_ReturnsByteCount()
    {
        var result = Run(@"
            let result = net.udpSend(""127.0.0.1"", 19881, ""hello"");
        ");
        Assert.Equal(5L, result);
    }

    [Fact]
    public void UdpSend_InvalidPort_ThrowsError()
    {
        RunExpectingError(@"
            net.udpSend(""127.0.0.1"", 0, ""data"");
        ");
    }

    [Fact]
    public void UdpRecv_InvalidPort_ThrowsError()
    {
        RunExpectingError(@"
            net.udpRecv(0);
        ");
    }

    // ── net.resolveMx ──────────────────────────────────────────────────────

    [Fact]
    public void ResolveMx_GoogleCom_ReturnsRecords()
    {
        var result = Run(@"
            let records = net.resolveMx(""google.com"");
            let result = len(records) > 0;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ResolveMx_HasPriorityAndExchange()
    {
        var output = RunCapturingOutput(@"
            let records = net.resolveMx(""google.com"");
            let first = records[0];
            io.println(typeof(first.priority));
            io.println(typeof(first.exchange));
            io.println(first.priority >= 0);
            io.println(str.contains(first.exchange, "".""));
        ");
        var lines = output.Trim().Split('\n');
        Assert.Equal("int", lines[0].Trim());
        Assert.Equal("string", lines[1].Trim());
        Assert.Equal("true", lines[2].Trim());
        Assert.Equal("true", lines[3].Trim());
    }

    // ── net.resolveTxt ─────────────────────────────────────────────────────

    [Fact]
    public void ResolveTxt_GoogleCom_ReturnsRecords()
    {
        var result = Run(@"
            let records = net.resolveTxt(""google.com"");
            let result = len(records) > 0;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ResolveTxt_GoogleCom_RecordsAreStrings()
    {
        var result = Run(@"
            let records = net.resolveTxt(""google.com"");
            let result = typeof(records[0]);
        ");
        Assert.Equal("string", result);
    }

    #region WebSocket

    private static (HttpListener listener, Task serverTask) StartWsEchoServer(int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }

                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                    var ws = wsContext.WebSocket;
                    var buffer = new byte[4096];
                    try
                    {
                        while (ws.State == System.Net.WebSockets.WebSocketState.Open)
                        {
                            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                            {
                                await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                                break;
                            }
                            // Echo back
                            await ws.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
                        }
                    }
                    catch (System.Net.WebSockets.WebSocketException) { }
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        });

        return (listener, serverTask);
    }

    private static void StopWsEchoServer(HttpListener listener, Task serverTask)
    {
        listener.Stop();
        listener.Close();
        try {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            serverTask.Wait(TimeSpan.FromSeconds(2));
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
        } catch
        {

        }
    }

    [Fact]
    public void WsConnect_ValidUrl_ReturnsWsConnection()
    {
        int port = 19900;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                io.println(ws.url);
                io.println(typeof(ws.protocol));
                await net.wsClose(ws);
            ");
            var lines = output.Trim().Split('\n');
            Assert.Equal($"ws://localhost:{port}/", lines[0].Trim());
            Assert.Equal("string", lines[1].Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsSend_TextMessage_ReturnsByteCount()
    {
        int port = 19901;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                let bytes = await net.wsSend(ws, ""hello"");
                io.println(bytes);
                await net.wsClose(ws);
            ");
            Assert.Equal("5", output.Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsRecv_EchoServer_ReturnsTextMessage()
    {
        int port = 19902;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                await net.wsSend(ws, ""hello world"");
                let msg = await net.wsRecv(ws, 5s);
                io.println(msg.data);
                io.println(msg.type);
                io.println(msg.close);
                await net.wsClose(ws);
            ");
            var lines = output.Trim().Split('\n');
            Assert.Equal("hello world", lines[0].Trim());
            Assert.Equal("text", lines[1].Trim());
            Assert.Equal("false", lines[2].Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsRecv_Timeout_ReturnsNull()
    {
        int port = 19903;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                let msg = await net.wsRecv(ws, 100ms);
                io.println(msg == null);
                await net.wsClose(ws);
            ");
            Assert.Equal("true", output.Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsClose_GracefulClose_Succeeds()
    {
        int port = 19904;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                await net.wsClose(ws);
                io.println(net.wsState(ws));
            ");
            Assert.Equal("WsConnectionState.Closed", output.Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsClose_Idempotent_NoError()
    {
        int port = 19905;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                await net.wsClose(ws);
                await net.wsClose(ws);
            ");
            // No exception = pass
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsState_Open_ReturnsEnumValue()
    {
        int port = 19906;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                io.println(net.wsState(ws));
                io.println(net.wsState(ws) == net.WsConnectionState.Open);
                await net.wsClose(ws);
            ");
            var lines = output.Trim().Split('\n');
            Assert.Equal("WsConnectionState.Open", lines[0].Trim());
            Assert.Equal("true", lines[1].Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsIsOpen_Open_ReturnsTrue()
    {
        int port = 19907;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                io.println(net.wsIsOpen(ws));
                await net.wsClose(ws);
                io.println(net.wsIsOpen(ws));
            ");
            var lines = output.Trim().Split('\n');
            Assert.Equal("true", lines[0].Trim());
            Assert.Equal("false", lines[1].Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsSendBinary_Base64Data_Succeeds()
    {
        int port = 19908;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                let payload = encoding.base64Encode(""binary data"");
                let bytes = await net.wsSendBinary(ws, payload);
                io.println(bytes > 0);
                let msg = await net.wsRecv(ws, 5s);
                io.println(msg.type);
                await net.wsClose(ws);
            ");
            var lines = output.Trim().Split('\n');
            Assert.Equal("true", lines[0].Trim());
            Assert.Equal("binary", lines[1].Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsConnect_InvalidScheme_ThrowsError()
    {
        var ex = RunCapturingError(@"
            let ws = await net.wsConnect(""http://localhost:8080"");
        ");
        Assert.Contains("ws://", ex.Message);
    }

    [Fact]
    public void WsConnect_Unreachable_ThrowsError()
    {
        RunExpectingError(@"
            let ws = await net.wsConnect(""ws://localhost:1"", { timeout: 1s });
        ");
    }

    [Fact]
    public void WsSend_WrongType_ThrowsError()
    {
        RunExpectingError(@"
            await net.wsSend(""not a connection"", ""data"");
        ");
    }

    [Fact]
    public void WsSendBinary_InvalidBase64_ThrowsError()
    {
        int port = 19909;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            RunExpectingError($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                await net.wsSendBinary(ws, ""not-valid-base64!!!"");
            ");
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsClose_InvalidCode_ThrowsError()
    {
        int port = 19910;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            RunExpectingError($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                await net.wsClose(ws, 999);
            ");
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsRecv_DurationLiteral_Accepted()
    {
        int port = 19911;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"");
                await net.wsSend(ws, ""ping"");
                let msg = await net.wsRecv(ws, 5s);
                io.println(msg.data);
                await net.wsClose(ws);
            ");
            Assert.Equal("ping", output.Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    [Fact]
    public void WsConnect_DurationTimeout_Accepted()
    {
        int port = 19912;
        var (listener, serverTask) = StartWsEchoServer(port);
        try
        {
            var output = RunCapturingOutput($@"
                let ws = await net.wsConnect(""ws://localhost:{port}/"", {{ timeout: 5s }});
                io.println(net.wsIsOpen(ws));
                await net.wsClose(ws);
            ");
            Assert.Equal("true", output.Trim());
        }
        finally { StopWsEchoServer(listener, serverTask); }
    }

    #endregion
}
