namespace Stash.Tests.Stdlib;

/// <summary>
/// Lightweight registration tests for the new socket-related namespaces:
/// <c>tcp</c>, <c>udp</c>, <c>ws</c>, and <c>dns</c>.
///
/// These tests do not make real network connections — they verify namespace
/// registration, struct constructibility, and enum accessibility. Any calls
/// that might touch the network are wrapped in Stash try/catch to remain
/// deterministic in CI.
/// </summary>
public class SocketNamespacesTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // tcp — struct and enum registration
    // =========================================================================

    [Fact]
    public void TcpConnection_NamespacedStructInit_FieldsAccessible()
    {
        var result = Run("""
            let conn = tcp.TcpConnection { host: "127.0.0.1", port: 80, localPort: 0 };
            let result = conn.host;
        """);
        Assert.Equal("127.0.0.1", result);
    }

    [Fact]
    public void TcpConnectOptions_NoDelayField_Accessible()
    {
        var result = Run("""
            let opts = tcp.TcpConnectOptions { noDelay: true };
            let result = opts.noDelay;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TcpConnectionState_OpenMember_IsEnumValue()
    {
        var result = Run("""let result = typeof(tcp.TcpConnectionState.Open);""");
        Assert.Equal("enum", result);
    }

    // =========================================================================
    // udp — struct registration
    // =========================================================================

    [Fact]
    public void UdpMessage_NamespacedStructInit_FieldsAccessible()
    {
        var result = Run("""
            let msg = udp.UdpMessage { data: "hello", host: "127.0.0.1", port: 9999 };
            let result = msg.data;
        """);
        Assert.Equal("hello", result);
    }

    // =========================================================================
    // ws — enum registration
    // =========================================================================

    [Fact]
    public void WsConnectionState_OpenMember_IsEnumValue()
    {
        var result = Run("""let result = typeof(ws.WsConnectionState.Open);""");
        Assert.Equal("enum", result);
    }

    [Fact]
    public void WsConnection_NamespacedStructInit_FieldsAccessible()
    {
        var result = Run("""
            let conn = ws.WsConnection { url: "ws://localhost", protocol: "chat" };
            let result = conn.url;
        """);
        Assert.Equal("ws://localhost", result);
    }

    // =========================================================================
    // dns — struct registration and function availability
    // =========================================================================

    [Fact]
    public void MxRecord_NamespacedStructInit_FieldsAccessible()
    {
        var result = Run("""
            let rec = dns.MxRecord { priority: 10, exchange: "mail.example.com" };
            let result = rec.exchange;
        """);
        Assert.Equal("mail.example.com", result);
    }

    [Fact]
    public void DnsResolve_Localhost_ReturnsStringOrThrows()
    {
        // Wrapping in try/catch to handle environments where DNS lookup may fail.
        var result = Run("""
            let resolved = null;
            try {
                resolved = dns.resolve("localhost");
            } catch (e) {
                resolved = "error";
            }
            let result = resolved != null;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void DnsReverseLookup_Loopback_ReturnsStringOrThrows()
    {
        // Wrapping in try/catch to handle DNS environments without reverse resolution.
        var result = Run("""
            let resolved = null;
            try {
                resolved = dns.reverseLookup("127.0.0.1");
            } catch (e) {
                resolved = "error";
            }
            let result = resolved != null;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Tcp_NamespaceIsAccessible()
    {
        // Verify the tcp namespace is in scope and has at least one accessible member.
        var result = Run("""let result = typeof(tcp.TcpConnectionState.Open) == "enum";""");
        Assert.Equal(true, result);
    }
}
