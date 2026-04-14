using Stash.Common;
using Stash.Lexing;

namespace Stash.Tests.Lexing;

public class IpAddressValidationTests
{
    private static (List<string> Errors, List<DiagnosticError> StructuredErrors) Lex(string source)
    {
        var lexer = new Lexer(source);
        lexer.ScanTokens();
        return (lexer.Errors, lexer.StructuredErrors);
    }

    private static string GetDiagnosticMessage(string source)
    {
        var (_, structured) = Lex(source);
        Assert.NotEmpty(structured);
        return structured[0].Message;
    }

    // ── IPv4 validation ──────────────────────────────────────────────

    [Fact]
    public void IpValidation_IPv4_TooFewOctets_SpecificError()
    {
        // 400 > 255 for the first 8-bit part in POSIX 3-part form, so TryParse fails
        // and ValidateFormat is reached, reporting the octet count.
        string msg = GetDiagnosticMessage("@400.1.1");
        Assert.Contains("3", msg);     // mentions "found 3"
        Assert.Contains("4", msg);     // mentions "exactly 4"
    }

    [Fact]
    public void IpValidation_IPv4_TooManyOctets_SpecificError()
    {
        string msg = GetDiagnosticMessage("@192.168.1.1.5");
        Assert.Contains("5", msg);     // mentions "found 5"
    }

    [Fact]
    public void IpValidation_IPv4_OctetOutOfRange_SpecificError()
    {
        string msg = GetDiagnosticMessage("@192.168.1.999");
        Assert.Contains("999", msg);
        Assert.Contains("255", msg);   // mentions upper bound
    }

    [Fact]
    public void IpValidation_IPv4_LeadingZero_SpecificError()
    {
        // .NET IPAddress.TryParse accepts leading zeros on Linux (treats them as decimal).
        // Combining a leading-zero octet with an out-of-range octet forces TryParse to fail,
        // so ValidateFormat is called and reports the leading-zero error first.
        string msg = GetDiagnosticMessage("@192.168.01.256");
        Assert.Contains("01", msg);
        Assert.Contains("leading zero", msg);
    }

    [Fact]
    public void IpValidation_IPv4_CIDROutOfRange_SpecificError()
    {
        string msg = GetDiagnosticMessage("@10.0.0.0/33");
        Assert.Contains("33", msg);
        Assert.Contains("32", msg);    // mentions upper bound
    }

    [Fact]
    public void IpValidation_IPv4_InvalidChar_SpecificError()
    {
        // 'a' is a hex digit so the lexer consumes it as part of the IP literal,
        // but TryParse fails because 'a' is not valid in an IPv4 decimal octet.
        string msg = GetDiagnosticMessage("@192.168.1.a");
        Assert.Contains("a", msg);
        Assert.Contains("Invalid character", msg);
    }

    // ── IPv6 validation ──────────────────────────────────────────────

    [Fact]
    public void IpValidation_IPv6_MultipleDoubleColon_SpecificError()
    {
        string msg = GetDiagnosticMessage("@fe80::1::2");
        Assert.Contains("::", msg);
        Assert.Contains("only one", msg);
    }

    [Fact]
    public void IpValidation_IPv6_CIDROutOfRange_SpecificError()
    {
        string msg = GetDiagnosticMessage("@fe80::/200");
        Assert.Contains("200", msg);
        Assert.Contains("128", msg);   // mentions upper bound
    }

    [Fact]
    public void IpValidation_IPv6_GroupTooLong_SpecificError()
    {
        string msg = GetDiagnosticMessage("@abcde::1");
        Assert.Contains("abcde", msg);
        Assert.Contains("4", msg);     // exceeds 4 hex digits
    }

    // ── General validation ───────────────────────────────────────────

    [Fact]
    public void IpValidation_ValidIPv4_NoErrors()
    {
        var (errors, structured) = Lex("@192.168.1.1");
        Assert.Empty(errors);
        Assert.Empty(structured);
    }

    [Fact]
    public void IpValidation_ValidIPv6_NoErrors()
    {
        var (errors, structured) = Lex("@::1");
        Assert.Empty(errors);
        Assert.Empty(structured);
    }

    [Fact]
    public void IpValidation_ValidCIDR_NoErrors()
    {
        var (errors, structured) = Lex("@10.0.0.0/24");
        Assert.Empty(errors);
        Assert.Empty(structured);
    }

    [Fact]
    public void IpValidation_Diagnostic_HasSourceSpan()
    {
        var (_, structured) = Lex("@192.168.1.999");
        Assert.NotEmpty(structured);

        var diag = structured[0];
        Assert.Equal(1, diag.Span.StartLine);    // Starts on line 1
        Assert.Equal(1, diag.Span.StartColumn);  // Starts at column 1
    }

    // ── Review fix coverage ──────────────────────────────────────────

    [Fact]
    public void IpValidation_IPv4_WithZoneId_SpecificError()
    {
        string msg = GetDiagnosticMessage("@10.0.0.1%eth0");
        Assert.Contains("Zone ID", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IPv6", msg);
    }

    [Fact]
    public void IpValidation_IPv6_TripleColon_SpecificError()
    {
        string msg = GetDiagnosticMessage("@:::1");
        Assert.Contains(":::", msg);
    }

    [Fact]
    public void IpValidation_IPv4Mapped_BadSuffix_SpecificError()
    {
        string msg = GetDiagnosticMessage("@::ffff:999.1.1.1");
        Assert.Contains("999", msg);
    }

    [Fact]
    public void IpValidation_MissingCIDRPrefix_SpecificError()
    {
        string msg = GetDiagnosticMessage("@10.0.0.0/");
        Assert.Contains("Missing", msg, StringComparison.OrdinalIgnoreCase);
    }
}
