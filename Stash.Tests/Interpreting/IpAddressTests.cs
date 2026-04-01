using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class IpAddressTests
{
    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
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
}
