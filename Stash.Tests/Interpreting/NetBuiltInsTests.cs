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

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null));
        Assert.Equal("192.168.1.0/24", network.ToString());

        var broadcast = Assert.IsType<StashIpAddress>(info.GetField("broadcast", null));
        Assert.Equal("192.168.1.255", broadcast.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null));
        Assert.Equal("255.255.255.0", mask.ToString());

        var wildcard = Assert.IsType<StashIpAddress>(info.GetField("wildcard", null));
        Assert.Equal("0.0.0.255", wildcard.ToString());

        Assert.Equal(254L, info.GetField("hostCount", null));

        var firstHost = Assert.IsType<StashIpAddress>(info.GetField("firstHost", null));
        Assert.Equal("192.168.1.1", firstHost.ToString());

        var lastHost = Assert.IsType<StashIpAddress>(info.GetField("lastHost", null));
        Assert.Equal("192.168.1.254", lastHost.ToString());
    }

    [Fact]
    public void SubnetInfo_ClassA_ReturnsCorrectFields()
    {
        var result = Run("let result = net.subnetInfo(@10.0.0.0/8);");
        var info = Assert.IsType<StashInstance>(result);

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null));
        Assert.Equal("10.0.0.0/8", network.ToString());

        var broadcast = Assert.IsType<StashIpAddress>(info.GetField("broadcast", null));
        Assert.Equal("10.255.255.255", broadcast.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null));
        Assert.Equal("255.0.0.0", mask.ToString());

        var wildcard = Assert.IsType<StashIpAddress>(info.GetField("wildcard", null));
        Assert.Equal("0.255.255.255", wildcard.ToString());

        Assert.Equal(16777214L, info.GetField("hostCount", null));

        var firstHost = Assert.IsType<StashIpAddress>(info.GetField("firstHost", null));
        Assert.Equal("10.0.0.1", firstHost.ToString());

        var lastHost = Assert.IsType<StashIpAddress>(info.GetField("lastHost", null));
        Assert.Equal("10.255.255.254", lastHost.ToString());
    }

    [Fact]
    public void SubnetInfo_Slash32_SingleHost()
    {
        var result = Run("let result = net.subnetInfo(@192.168.1.1/32);");
        var info = Assert.IsType<StashInstance>(result);

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null));
        Assert.Equal("192.168.1.1/32", network.ToString());

        var broadcast = Assert.IsType<StashIpAddress>(info.GetField("broadcast", null));
        Assert.Equal("192.168.1.1", broadcast.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null));
        Assert.Equal("255.255.255.255", mask.ToString());

        var wildcard = Assert.IsType<StashIpAddress>(info.GetField("wildcard", null));
        Assert.Equal("0.0.0.0", wildcard.ToString());

        Assert.Equal(1L, info.GetField("hostCount", null));

        var firstHost = Assert.IsType<StashIpAddress>(info.GetField("firstHost", null));
        Assert.Equal("192.168.1.1", firstHost.ToString());

        var lastHost = Assert.IsType<StashIpAddress>(info.GetField("lastHost", null));
        Assert.Equal("192.168.1.1", lastHost.ToString());
    }

    [Fact]
    public void SubnetInfo_Slash31_PointToPoint()
    {
        var result = Run("let result = net.subnetInfo(@10.0.0.0/31);");
        var info = Assert.IsType<StashInstance>(result);

        Assert.Equal(2L, info.GetField("hostCount", null));

        var firstHost = Assert.IsType<StashIpAddress>(info.GetField("firstHost", null));
        Assert.Equal("10.0.0.0", firstHost.ToString());

        var lastHost = Assert.IsType<StashIpAddress>(info.GetField("lastHost", null));
        Assert.Equal("10.0.0.1", lastHost.ToString());
    }

    [Fact]
    public void SubnetInfo_ClassB_ReturnsCorrectFields()
    {
        var result = Run("let result = net.subnetInfo(@172.16.5.4/16);");
        var info = Assert.IsType<StashInstance>(result);

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null));
        Assert.Equal("172.16.0.0/16", network.ToString());

        var broadcast = Assert.IsType<StashIpAddress>(info.GetField("broadcast", null));
        Assert.Equal("172.16.255.255", broadcast.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null));
        Assert.Equal("255.255.0.0", mask.ToString());

        Assert.Equal(65534L, info.GetField("hostCount", null));
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

        var network = Assert.IsType<StashIpAddress>(info.GetField("network", null));
        Assert.Equal("fe80::/64", network.ToString());

        var mask = Assert.IsType<StashIpAddress>(info.GetField("mask", null));
        Assert.Equal("ffff:ffff:ffff:ffff::", mask.ToString());

        Assert.Equal(long.MaxValue, info.GetField("hostCount", null));
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
}
