using System;
using System.ComponentModel.DataAnnotations;
using Stash.Common;
using Stash.Registry.Contracts.Validation;
using Stash.Registry.Endpoints;
using Xunit;

namespace Stash.Tests.Registry.Validation;

/// <summary>
/// Meta-tests that assert the two inlined grammar/parse copies in
/// <c>Stash.Registry.Contracts</c> agree with their canonical homes in
/// <c>Stash.Core</c> and <c>Stash.Registry</c>.
/// <para>
/// Because <c>Stash.Registry.Contracts</c> is dependency-free it cannot reference
/// <c>PackageManifest.IsValidScopeName</c> or <c>AuthHelper.ParseTokenExpiry</c> directly —
/// it inlines the logic. These tests exercise both homes over the same corpus so that
/// any future drift (a regex change in one place, a parse rule change in another) fails CI
/// immediately rather than silently diverging.
/// </para>
/// </summary>
public sealed class GrammarParseAgreementMetaTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool AttrAcceptsScopeGrammar(string? v)
        => new ScopeGrammarAttribute().GetValidationResult(
                v,
                new ValidationContext(v ?? new object()))
            == ValidationResult.Success;

    private static bool CoreAcceptsScopeName(string? v)
    {
        // Attribute treats null/empty as Success (combined with [Required]);
        // align the cross-check formula so null/empty is treated as "core also accepts".
        if (string.IsNullOrEmpty(v))
            return true;
        return PackageManifest.IsValidScopeName(v);
    }

    private static bool AttrAcceptsTokenExpiry(string? v)
        => new TokenExpiryAttribute().GetValidationResult(
                v,
                new ValidationContext(v ?? new object()))
            == ValidationResult.Success;

    private static bool HelperParsesTokenExpiry(string v)
    {
        try
        {
            AuthHelper.ParseTokenExpiry(v);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // ── 1. Scope-grammar agreement ────────────────────────────────────────────

    /// <summary>
    /// For every input in the corpus, <see cref="ScopeGrammarAttribute"/> must agree with
    /// <see cref="PackageManifest.IsValidScopeName"/>. The formula accounts for the
    /// null/empty pass-through convention of the attribute (it pairs with <c>[Required]</c>).
    /// </summary>
    [Theory]
    [InlineData("alice")]           // valid lowercase name
    [InlineData("my-org")]          // valid with hyphen
    [InlineData("MyOrg")]           // uppercase — both reject
    [InlineData("a_b")]             // underscore — both reject (not in grammar)
    [InlineData("")]                // empty — both treat as "accept" (attr + formula aligned)
    [InlineData(" alice ")]         // leading/trailing whitespace — both reject
    public void ScopeGrammarAgreement_CorpusInput_AttributeAgreesWithPackageManifest(string? v)
    {
        bool attrValid = AttrAcceptsScopeGrammar(v);
        bool coreValid = CoreAcceptsScopeName(v);
        Assert.Equal(coreValid, attrValid);
    }

    [Fact]
    public void ScopeGrammarAgreement_NullInput_AttributeAgreesWithPackageManifest()
    {
        // null: attribute returns Success; formula aligns (IsNullOrEmpty → true).
        bool attrValid = AttrAcceptsScopeGrammar(null);
        bool coreValid = CoreAcceptsScopeName(null);
        Assert.Equal(coreValid, attrValid);
    }

    [Fact]
    public void ScopeGrammarAgreement_LeadingDigit_AttributeAgreesWithPackageManifest()
    {
        // "1abc": starts with digit — both reject
        const string v = "1abc";
        Assert.Equal(CoreAcceptsScopeName(v), AttrAcceptsScopeGrammar(v));
    }

    [Fact]
    public void ScopeGrammarAgreement_TooLong_AttributeAgreesWithPackageManifest()
    {
        // 40 chars: max is 39 — both reject
        string v = new string('a', 40);
        Assert.Equal(CoreAcceptsScopeName(v), AttrAcceptsScopeGrammar(v));
    }

    // ── 2. Token-expiry parse agreement ──────────────────────────────────────

    // Bucket 1: helper parses successfully AND value ≥ 1h → attribute ACCEPTS.
    // If the parse logic is changed in either home without the other, these flip.
    [Theory]
    [InlineData("1h")]     // exactly 1h
    [InlineData("2h")]     // 2h > floor
    [InlineData("1d")]     // 1 day — well above floor
    [InlineData("24h")]    // 24h — same as 1d
    [InlineData("60m")]    // 60 minutes = exactly 1h floor
    public void TokenExpiryAgreement_HelperParsesAndAboveFloor_AttributeAccepts(string v)
    {
        Assert.True(HelperParsesTokenExpiry(v),
            $"AuthHelper.ParseTokenExpiry should parse '{v}' without throwing.");
        Assert.True(AttrAcceptsTokenExpiry(v),
            $"TokenExpiryAttribute should accept '{v}' (parses successfully and >= 1h).");
    }

    // Bucket 2: helper parses successfully BUT value < 1h → attribute REJECTS (floor).
    // This documents that the attribute intentionally adds a ≥1h floor that the helper lacks.
    // A change that removes the floor from the attribute, or changes what "parses" means
    // for sub-hour values in the helper, will flip one of the two assertions here.
    [Theory]
    [InlineData("59m")]    // 59 minutes < 1h: helper parses OK, attribute rejects (floor)
    [InlineData("30m")]    // 30 minutes < 1h
    [InlineData("0d")]     // 0 days: attribute rejects at parse stage (days <= 0), helper accepts (AddDays(0))
    public void TokenExpiryAgreement_HelperParsesButBelowFloor_AttributeRejectsDivergenceIsIntentional(string v)
    {
        // Helper parses these fine (they are syntactically valid duration strings).
        Assert.True(HelperParsesTokenExpiry(v),
            $"AuthHelper.ParseTokenExpiry should parse '{v}' without throwing; " +
            "the ≥1h floor is an attribute-only concern, not a parse concern.");

        // Attribute rejects them: they are either below the 1h floor or explicitly 0 (invalid magnitude).
        Assert.False(AttrAcceptsTokenExpiry(v),
            $"TokenExpiryAttribute should reject '{v}': value is below the required ≥1h floor. " +
            "This divergence from the helper is intentional and must be preserved.");
    }

    // Bucket 3: input is malformed — helper throws FormatException, attribute also rejects.
    // If the parse logic is changed in one home (e.g. "1y" is added as valid), these flip.
    [Theory]
    [InlineData("abc")]    // no digits/suffix at all
    [InlineData("1y")]     // unrecognised suffix
    [InlineData("10x")]    // unrecognised suffix
    public void TokenExpiryAgreement_MalformedInput_HelperRejectsAndAttributeRejects(string v)
    {
        Assert.False(HelperParsesTokenExpiry(v),
            $"AuthHelper.ParseTokenExpiry should throw FormatException for malformed input '{v}'.");
        Assert.False(AttrAcceptsTokenExpiry(v),
            $"TokenExpiryAttribute should reject malformed input '{v}'.");
    }

    // Empty string is a special divergence: helper throws, but attribute accepts (null/empty
    // pass-through convention, combined with [Required]). Documented explicitly here.
    [Fact]
    public void TokenExpiryAgreement_EmptyString_HelperRejectsButAttributeAccepts()
    {
        // AuthHelper throws on "": no suffix matches and int.TryParse("") is false.
        Assert.False(HelperParsesTokenExpiry(string.Empty),
            "AuthHelper.ParseTokenExpiry should throw FormatException for empty string.");

        // Attribute treats empty as Success (like null — pairs with [Required]).
        Assert.True(AttrAcceptsTokenExpiry(string.Empty),
            "TokenExpiryAttribute should accept empty string (null/empty pass-through; combine with [Required]).");
    }
}
