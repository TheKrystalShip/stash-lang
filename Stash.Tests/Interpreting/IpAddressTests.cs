using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class IpAddressTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    // ── Lexer: Token scanning ────────────────────────────────────────

    [Fact]
    public void IpLiteral_IPv4_ProducesToken()
    {
        var lexer = new Lexer("@192.168.1.1");
        List<Token> tokens = lexer.ScanTokens();
        Assert.Empty(lexer.Errors);
        Assert.Equal(TokenType.IpAddressLiteral, tokens[0].Type);
        Assert.Equal("@192.168.1.1", tokens[0].Lexeme);
        Assert.IsType<StashIpAddress>(tokens[0].Literal);
    }

    [Fact]
    public void IpLiteral_IPv6Loopback_ProducesToken()
    {
        var lexer = new Lexer("@::1");
        List<Token> tokens = lexer.ScanTokens();
        Assert.Empty(lexer.Errors);
        Assert.Equal(TokenType.IpAddressLiteral, tokens[0].Type);
    }

    [Fact]
    public void IpLiteral_CIDR_ProducesToken()
    {
        var lexer = new Lexer("@10.0.0.0/24");
        List<Token> tokens = lexer.ScanTokens();
        Assert.Empty(lexer.Errors);
        Assert.Equal(TokenType.IpAddressLiteral, tokens[0].Type);
        var ip = (StashIpAddress)tokens[0].Literal!;
        Assert.Equal(24, ip.PrefixLength);
    }

    [Fact]
    public void IpLiteral_IPv4MappedIPv6_ProducesToken()
    {
        var lexer = new Lexer("@::ffff:192.168.1.1");
        List<Token> tokens = lexer.ScanTokens();
        Assert.Empty(lexer.Errors);
        Assert.Equal(TokenType.IpAddressLiteral, tokens[0].Type);
    }

    [Fact]
    public void IpLiteral_Invalid_ProducesError()
    {
        var lexer = new Lexer("@999.999.999.999");
        lexer.ScanTokens();
        Assert.NotEmpty(lexer.Errors);
    }

    [Fact]
    public void IpLiteral_StandaloneAt_ProducesError()
    {
        var lexer = new Lexer("@");
        lexer.ScanTokens();
        Assert.NotEmpty(lexer.Errors);
    }

    [Fact]
    public void IpLiteral_AtFollowedByNonIp_ProducesError()
    {
        var lexer = new Lexer("@hello");
        lexer.ScanTokens();
        Assert.NotEmpty(lexer.Errors);
    }

    // ── Runtime: typeof and is ────────────────────────────────────────

    [Fact]
    public void IpLiteral_TypeOf_ReturnsIp()
    {
        object? result = Run("let result = typeof(@192.168.1.1);");
        Assert.Equal("ip", result);
    }

    [Fact]
    public void IpLiteral_IsIp_ReturnsTrue()
    {
        object? result = Run("let result = @192.168.1.1 is ip;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpLiteral_IsString_ReturnsFalse()
    {
        object? result = Run("let result = @192.168.1.1 is string;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpLiteral_IsInt_ReturnsFalse()
    {
        object? result = Run("let result = @192.168.1.1 is int;");
        Assert.Equal(false, result);
    }

    // ── Equality: value-based ────────────────────────────────────────

    [Fact]
    public void IpLiteral_Equality_SameAddress_ReturnsTrue()
    {
        object? result = Run("let result = @192.168.1.1 == @192.168.1.1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpLiteral_Equality_DifferentAddress_ReturnsFalse()
    {
        object? result = Run("let result = @192.168.1.1 == @192.168.1.2;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpLiteral_Inequality_DifferentAddress_ReturnsTrue()
    {
        object? result = Run("let result = @192.168.1.1 != @10.0.0.1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpLiteral_Equality_WithCIDR_ComparesPrefix()
    {
        object? result = Run("let result = @10.0.0.0/24 == @10.0.0.0/24;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpLiteral_Equality_DifferentCIDR_ReturnsFalse()
    {
        object? result = Run("let result = @10.0.0.0/24 == @10.0.0.0/16;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpLiteral_Equality_NoCIDRvsCIDR_ReturnsFalse()
    {
        object? result = Run("let result = @10.0.0.0 == @10.0.0.0/24;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpLiteral_NotEqualToString()
    {
        object? result = Run("let result = @192.168.1.1 == \"192.168.1.1\";");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpLiteral_NotEqualToInt()
    {
        object? result = Run("let result = @192.168.1.1 == 42;");
        Assert.Equal(false, result);
    }

    // ── String interpolation ─────────────────────────────────────────

    [Fact]
    public void IpLiteral_StringInterpolation_ShowsAddress()
    {
        object? result = Run("let addr = @192.168.1.1; let result = $\"Server: {addr}\";");
        Assert.Equal("Server: 192.168.1.1", result);
    }

    [Fact]
    public void IpLiteral_StringInterpolation_CIDR()
    {
        object? result = Run("let subnet = @10.0.0.0/24; let result = $\"Subnet: {subnet}\";");
        Assert.Equal("Subnet: 10.0.0.0/24", result);
    }

    [Fact]
    public void IpLiteral_StringConcatenation()
    {
        object? result = Run("let result = \"Host: \" + @192.168.1.1;");
        Assert.Equal("Host: 192.168.1.1", result);
    }

    // ── IPv6 variants ────────────────────────────────────────────────

    [Fact]
    public void IpLiteral_IPv6_Loopback()
    {
        object? result = Run("let result = typeof(@::1);");
        Assert.Equal("ip", result);
    }

    [Fact]
    public void IpLiteral_IPv6_FullAddress()
    {
        object? result = Run("let result = @::1 == @::1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpLiteral_IPv6_CIDR()
    {
        object? result = Run("let result = typeof(@fe80::/10);");
        Assert.Equal("ip", result);
    }

    // ── Variable assignment and usage ────────────────────────────────

    [Fact]
    public void IpLiteral_AssignToVariable()
    {
        object? result = Run("let addr = @192.168.1.1; let result = addr;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.1.1", result!.ToString());
    }

    [Fact]
    public void IpLiteral_InArray()
    {
        object? result = Run("let hosts = [@192.168.1.1, @10.0.0.1]; let result = len(hosts);");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void IpLiteral_Truthiness_IsTruthy()
    {
        object? result = Run("let result = @192.168.1.1 ? true : false;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpLiteral_NullCoalescing()
    {
        object? check = Run("let addr = null; let result = (addr ?? @127.0.0.1) is ip;");
        Assert.Equal(true, check);
    }

    [Fact]
    public void IpLiteral_Wildcard()
    {
        object? result = Run("let result = @0.0.0.0 == @0.0.0.0;");
        Assert.Equal(true, result);
    }

    // ── StashIpAddress property tests ────────────────────────────────

    [Fact]
    public void StashIpAddress_Loopback_IPv4()
    {
        StashIpAddress.TryParse("127.0.0.1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.True(ip!.IsLoopback);
    }

    [Fact]
    public void StashIpAddress_Loopback_IPv6()
    {
        StashIpAddress.TryParse("::1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.True(ip!.IsLoopback);
    }

    [Fact]
    public void StashIpAddress_LinkLocal_NotLoopback()
    {
        StashIpAddress.TryParse("fe80::1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.False(ip!.IsLoopback);
        Assert.True(ip.IsLinkLocal);
    }

    [Fact]
    public void StashIpAddress_Private_IPv4_10()
    {
        StashIpAddress.TryParse("10.0.0.1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.True(ip!.IsPrivate);
    }

    [Fact]
    public void StashIpAddress_Private_IPv4_172()
    {
        StashIpAddress.TryParse("172.16.0.1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.True(ip!.IsPrivate);
    }

    [Fact]
    public void StashIpAddress_Private_IPv4_192()
    {
        StashIpAddress.TryParse("192.168.1.1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.True(ip!.IsPrivate);
    }

    [Fact]
    public void StashIpAddress_Public_NotPrivate()
    {
        StashIpAddress.TryParse("8.8.8.8", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.False(ip!.IsPrivate);
    }

    [Fact]
    public void StashIpAddress_LinkLocal_IPv4()
    {
        StashIpAddress.TryParse("169.254.1.1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.True(ip!.IsLinkLocal);
    }

    [Fact]
    public void StashIpAddress_Version_IPv4()
    {
        StashIpAddress.TryParse("192.168.1.1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.Equal(4, ip!.Version);
    }

    [Fact]
    public void StashIpAddress_Version_IPv6()
    {
        StashIpAddress.TryParse("::1", out StashIpAddress? ip);
        Assert.NotNull(ip);
        Assert.Equal(6, ip!.Version);
    }

    // ── Zone ID scanning ────────────────────────────────────────────

    [Fact]
    public void IpLiteral_IPv6_ZoneId_Scans()
    {
        var lexer = new Lexer("@fe80::1%eth0", "<test>");
        List<Token> tokens = lexer.ScanTokens();
        Assert.Empty(lexer.Errors);
        Assert.Equal(TokenType.IpAddressLiteral, tokens[0].Type);
        Assert.Equal("@fe80::1%eth0", tokens[0].Lexeme);
    }

    // ── Phase 2: Operator Integration ───────────────────────────────

    // ── Bitwise: AND ─────────────────────────────────────────────────

    [Fact]
    public void IpBitwise_And_NetworkAddress()
    {
        // addr & mask → network address
        object? result = Run("let result = @192.168.1.100 & @255.255.255.0;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.1.0", result!.ToString());
    }

    [Fact]
    public void IpBitwise_And_SubnetMask()
    {
        object? result = Run("let result = @10.20.30.40 & @255.255.0.0;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("10.20.0.0", result!.ToString());
    }

    // ── Bitwise: OR ──────────────────────────────────────────────────

    [Fact]
    public void IpBitwise_Or_BroadcastAddress()
    {
        // network | ~mask → broadcast
        object? result = Run("let result = @192.168.1.0 | @0.0.0.255;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.1.255", result!.ToString());
    }

    [Fact]
    public void IpBitwise_Or_SetBits()
    {
        object? result = Run("let result = @10.0.0.0 | @0.0.0.42;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("10.0.0.42", result!.ToString());
    }

    // ── Bitwise: NOT (complement) ────────────────────────────────────

    [Fact]
    public void IpBitwise_Not_WildcardMask()
    {
        // ~mask → wildcard mask
        object? result = Run("let result = ~@255.255.255.0;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("0.0.0.255", result!.ToString());
    }

    [Fact]
    public void IpBitwise_Not_InvertsAllBits()
    {
        object? result = Run("let result = ~@0.0.0.0;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("255.255.255.255", result!.ToString());
    }

    // ── Bitwise: Combined (AND + OR + NOT) ──────────────────────────

    [Fact]
    public void IpBitwise_BroadcastFromAddrAndMask()
    {
        // broadcast = (addr & mask) | ~mask
        object? result = Run("let addr = @192.168.1.100; let mask = @255.255.255.0; let result = (addr & mask) | ~mask;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.1.255", result!.ToString());
    }

    // ── Comparison: Less Than ────────────────────────────────────────

    [Fact]
    public void IpComparison_LessThan_True()
    {
        object? result = Run("let result = @10.0.0.1 < @10.0.0.254;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpComparison_LessThan_False()
    {
        object? result = Run("let result = @10.0.0.254 < @10.0.0.1;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpComparison_LessThan_Equal_ReturnsFalse()
    {
        object? result = Run("let result = @10.0.0.1 < @10.0.0.1;");
        Assert.Equal(false, result);
    }

    // ── Comparison: Greater Than ─────────────────────────────────────

    [Fact]
    public void IpComparison_GreaterThan_True()
    {
        object? result = Run("let result = @192.168.1.100 > @192.168.1.1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpComparison_GreaterThan_False()
    {
        object? result = Run("let result = @10.0.0.1 > @10.0.0.254;");
        Assert.Equal(false, result);
    }

    // ── Comparison: Less Than or Equal ───────────────────────────────

    [Fact]
    public void IpComparison_LessEqual_WhenLess()
    {
        object? result = Run("let result = @10.0.0.1 <= @10.0.0.2;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpComparison_LessEqual_WhenEqual()
    {
        object? result = Run("let result = @10.0.0.1 <= @10.0.0.1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpComparison_LessEqual_WhenGreater()
    {
        object? result = Run("let result = @10.0.0.2 <= @10.0.0.1;");
        Assert.Equal(false, result);
    }

    // ── Comparison: Greater Than or Equal ────────────────────────────

    [Fact]
    public void IpComparison_GreaterEqual_WhenGreater()
    {
        object? result = Run("let result = @10.0.0.2 >= @10.0.0.1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpComparison_GreaterEqual_WhenEqual()
    {
        object? result = Run("let result = @10.0.0.1 >= @10.0.0.1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpComparison_GreaterEqual_WhenLess()
    {
        object? result = Run("let result = @10.0.0.1 >= @10.0.0.2;");
        Assert.Equal(false, result);
    }

    // ── Comparison: Cross-octet ordering ─────────────────────────────

    [Fact]
    public void IpComparison_CrossOctet_HigherFirstOctet()
    {
        // 192.x.x.x > 10.x.x.x
        object? result = Run("let result = @192.168.1.1 > @10.0.0.1;");
        Assert.Equal(true, result);
    }

    // ── CIDR: in operator ────────────────────────────────────────────

    [Fact]
    public void IpIn_AddressInSubnet_ReturnsTrue()
    {
        object? result = Run("let result = @192.168.1.50 in @192.168.1.0/24;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpIn_AddressNotInSubnet_ReturnsFalse()
    {
        object? result = Run("let result = @192.168.2.1 in @192.168.1.0/24;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpIn_DifferentNetwork_ReturnsFalse()
    {
        object? result = Run("let result = @10.0.0.1 in @192.168.1.0/24;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpIn_NetworkAddress_ReturnsTrue()
    {
        // Network address itself is in the subnet
        object? result = Run("let result = @192.168.1.0 in @192.168.1.0/24;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpIn_BroadcastAddress_ReturnsTrue()
    {
        // Broadcast address is in the subnet
        object? result = Run("let result = @192.168.1.255 in @192.168.1.0/24;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpIn_LargerSubnet()
    {
        object? result = Run("let result = @10.20.30.40 in @10.0.0.0/8;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpIn_SmallSubnet()
    {
        object? result = Run("let result = @192.168.1.5 in @192.168.1.0/28;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpIn_OutsideSmallSubnet()
    {
        // /28 = .0-.15, so .20 is outside
        object? result = Run("let result = @192.168.1.20 in @192.168.1.0/28;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IpIn_HostAddress_ExactMatch()
    {
        // When RHS has no CIDR, it's an exact match (Contains returns Equals)
        object? result = Run("let result = @192.168.1.1 in @192.168.1.1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpIn_HostAddress_NoMatch()
    {
        object? result = Run("let result = @192.168.1.2 in @192.168.1.1;");
        Assert.Equal(false, result);
    }

    // ── Arithmetic: Addition ─────────────────────────────────────────

    [Fact]
    public void IpArithmetic_AddOffset()
    {
        object? result = Run("let result = @10.0.0.0 + 42;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("10.0.0.42", result!.ToString());
    }

    [Fact]
    public void IpArithmetic_AddOffset_Commutative()
    {
        // 42 + @10.0.0.0 should also work
        object? result = Run("let result = 42 + @10.0.0.0;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("10.0.0.42", result!.ToString());
    }

    [Fact]
    public void IpArithmetic_AddOffset_CrossOctet()
    {
        // @192.168.1.254 + 2 → crosses into next octet
        object? result = Run("let result = @192.168.1.254 + 2;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.2.0", result!.ToString());
    }

    [Fact]
    public void IpArithmetic_AddOne()
    {
        object? result = Run("let result = @192.168.1.1 + 1;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.1.2", result!.ToString());
    }

    // ── Arithmetic: Subtraction ──────────────────────────────────────

    [Fact]
    public void IpArithmetic_SubtractIps_Distance()
    {
        // ip - ip → long distance
        object? result = Run("let result = @10.0.0.42 - @10.0.0.0;");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void IpArithmetic_SubtractIps_Zero()
    {
        object? result = Run("let result = @10.0.0.1 - @10.0.0.1;");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void IpArithmetic_SubtractOffset()
    {
        // ip - long → ip
        object? result = Run("let result = @10.0.0.42 - 42;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("10.0.0.0", result!.ToString());
    }

    [Fact]
    public void IpArithmetic_SubtractOffset_CrossOctet()
    {
        // @192.168.2.0 - 1 → @192.168.1.255
        object? result = Run("let result = @192.168.2.0 - 1;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("192.168.1.255", result!.ToString());
    }

    // ── Edge cases: Negative subtraction ─────────────────────────────

    [Fact]
    public void IpArithmetic_SubtractIps_NegativeDistance()
    {
        object? result = Run("let result = @10.0.0.0 - @10.0.0.42;");
        Assert.Equal(-42L, result);
    }

    // ── Edge cases: Address wraparound ───────────────────────────────

    [Fact]
    public void IpArithmetic_AddOverflow_Wraps()
    {
        object? result = Run("let result = @255.255.255.255 + 1;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("0.0.0.0", result!.ToString());
    }

    [Fact]
    public void IpArithmetic_SubtractUnderflow_Wraps()
    {
        object? result = Run("let result = @0.0.0.0 - 1;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("255.255.255.255", result!.ToString());
    }

    // ── IPv6 operator coverage ───────────────────────────────────────

    [Fact]
    public void IpBitwise_Not_IPv6()
    {
        object? result = Run("let result = ~@::1;");
        Assert.IsType<StashIpAddress>(result);
        // ::1 = 0000...0001, ~::1 = ffff:ffff:ffff:ffff:ffff:ffff:ffff:fffe
        Assert.Equal("ffff:ffff:ffff:ffff:ffff:ffff:ffff:fffe", result!.ToString());
    }

    [Fact]
    public void IpBitwise_And_IPv6()
    {
        // AND two IPv6 addresses
        object? result = Run("let result = @fe80::1 & @ffff::;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("fe80::", result!.ToString());
    }

    [Fact]
    public void IpComparison_IPv6_LessThan()
    {
        object? result = Run("let result = @::1 < @::2;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpComparison_IPv6_GreaterThan()
    {
        object? result = Run("let result = @::2 > @::1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpArithmetic_IPv6_AddOffset()
    {
        object? result = Run("let result = @::1 + 1;");
        Assert.IsType<StashIpAddress>(result);
        Assert.Equal("::2", result!.ToString());
    }

    [Fact]
    public void IpIn_IPv6_Subnet()
    {
        object? result = Run("let result = @fe80::1 in @fe80::/16;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IpIn_IPv6_NotInSubnet()
    {
        object? result = Run("let result = @2001:db8::1 in @fe80::/16;");
        Assert.Equal(false, result);
    }
}
