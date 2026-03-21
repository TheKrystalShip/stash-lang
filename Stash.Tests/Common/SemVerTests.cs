using Stash.Common;

namespace Stash.Tests.Common;

public class SemVerTests
{
    // ─── Parsing ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SimpleVersion_ReturnsCorrectComponents()
    {
        var v = SemVer.Parse("1.2.3");
        Assert.Equal(1, v.Major);
        Assert.Equal(2, v.Minor);
        Assert.Equal(3, v.Patch);
        Assert.Empty(v.PreRelease);
        Assert.Equal("", v.BuildMetadata);
        Assert.False(v.IsPreRelease);
    }

    [Fact]
    public void Parse_PreReleaseVersion_ReturnsCorrectPreRelease()
    {
        var v = SemVer.Parse("1.0.0-beta.1");
        Assert.Equal(1, v.Major);
        Assert.Equal(0, v.Minor);
        Assert.Equal(0, v.Patch);
        Assert.Equal(new[] { "beta", "1" }, v.PreRelease);
        Assert.True(v.IsPreRelease);
    }

    [Fact]
    public void Parse_PreReleaseWithBuildMetadata_ReturnsCorrectFields()
    {
        var v = SemVer.Parse("2.0.0-rc.3+build.456");
        Assert.Equal(2, v.Major);
        Assert.Equal(0, v.Minor);
        Assert.Equal(0, v.Patch);
        Assert.Equal(new[] { "rc", "3" }, v.PreRelease);
        Assert.Equal("build.456", v.BuildMetadata);
    }

    [Fact]
    public void Parse_ZeroVersion_ReturnsZero()
    {
        var v = SemVer.Parse("0.0.0");
        Assert.Equal(0, v.Major);
        Assert.Equal(0, v.Minor);
        Assert.Equal(0, v.Patch);
        Assert.Empty(v.PreRelease);
    }

    [Fact]
    public void Parse_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => SemVer.Parse("abc"));
        Assert.Throws<FormatException>(() => SemVer.Parse("1.2"));
        Assert.Throws<FormatException>(() => SemVer.Parse("1"));
        Assert.Throws<FormatException>(() => SemVer.Parse("-1.0.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("abc")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+")]
    [InlineData("-1.0.0")]
    public void TryParse_InvalidInputs_ReturnsFalse(string? input)
    {
        bool ok = SemVer.TryParse(input, out SemVer? result);
        Assert.False(ok);
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_NullInput_ReturnsFalse()
    {
        bool ok = SemVer.TryParse(null, out SemVer? result);
        Assert.False(ok);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.0.0-beta.1")]
    [InlineData("2.0.0-rc.3+build.456")]
    public void ToString_RoundTrips_OriginalString(string input)
    {
        Assert.Equal(input, SemVer.Parse(input).ToString());
    }

    // ─── Comparison ─────────────────────────────────────────────────────────

    [Fact]
    public void CompareTo_DifferentMajor_LesserMajorIsSmaller()
    {
        var v1 = SemVer.Parse("1.0.0");
        var v2 = SemVer.Parse("2.0.0");
        Assert.True(v1 < v2);
        Assert.True(v2 > v1);
    }

    [Fact]
    public void CompareTo_DifferentMinor_LesserMinorIsSmaller()
    {
        var v1 = SemVer.Parse("1.1.0");
        var v2 = SemVer.Parse("1.2.0");
        Assert.True(v1 < v2);
    }

    [Fact]
    public void CompareTo_DifferentPatch_LesserPatchIsSmaller()
    {
        var v1 = SemVer.Parse("1.2.3");
        var v2 = SemVer.Parse("1.2.4");
        Assert.True(v1 < v2);
    }

    [Fact]
    public void CompareTo_PreReleaseVsRelease_PreReleaseIsSmaller()
    {
        var pre = SemVer.Parse("1.0.0-alpha");
        var release = SemVer.Parse("1.0.0");
        Assert.True(pre < release);
    }

    [Fact]
    public void CompareTo_AlphaVsBeta_AlphaIsSmaller()
    {
        var alpha = SemVer.Parse("1.0.0-alpha");
        var beta = SemVer.Parse("1.0.0-beta");
        Assert.True(alpha < beta);
    }

    [Fact]
    public void CompareTo_NumericPreRelease_LowerNumberIsSmaller()
    {
        var v1 = SemVer.Parse("1.0.0-1");
        var v2 = SemVer.Parse("1.0.0-2");
        Assert.True(v1 < v2);
    }

    [Fact]
    public void CompareTo_NumericVsStringPreRelease_NumericIsSmaller()
    {
        var numeric = SemVer.Parse("1.0.0-1");
        var alpha = SemVer.Parse("1.0.0-alpha");
        Assert.True(numeric < alpha);
    }

    [Fact]
    public void CompareTo_FewerPreReleaseFields_IsSmaller()
    {
        var fewer = SemVer.Parse("1.0.0-alpha");
        var more = SemVer.Parse("1.0.0-alpha.1");
        Assert.True(fewer < more);
    }

    [Fact]
    public void CompareTo_BuildMetadataDiffers_VersionsAreEqual()
    {
        var v1 = SemVer.Parse("1.0.0+build1");
        var v2 = SemVer.Parse("1.0.0+build2");
        Assert.True(v1 == v2);
        Assert.Equal(0, v1.CompareTo(v2));
    }

    [Fact]
    public void EqualityOperator_SameVersion_IsEqual()
    {
        var v1 = SemVer.Parse("1.2.3");
        var v2 = SemVer.Parse("1.2.3");
        Assert.True(v1 == v2);
        Assert.False(v1 != v2);
    }

    [Fact]
    public void InequalityOperator_DifferentVersions_AreNotEqual()
    {
        var v1 = SemVer.Parse("1.2.3");
        var v2 = SemVer.Parse("1.2.4");
        Assert.True(v1 != v2);
    }

    [Fact]
    public void GteLteOperators_BoundaryVersions_MatchCorrectly()
    {
        var v1 = SemVer.Parse("1.0.0");
        var v2 = SemVer.Parse("1.0.0");
        var v3 = SemVer.Parse("2.0.0");
        Assert.True(v1 >= v2);
        Assert.True(v1 <= v2);
        Assert.True(v3 >= v1);
        Assert.True(v1 <= v3);
    }

    // ─── SemVerRange Parsing ─────────────────────────────────────────────────

    [Theory]
    [InlineData("*")]
    [InlineData("^1.0.0")]
    [InlineData("~1.2.3")]
    [InlineData(">=1.0.0")]
    [InlineData("<2.0.0")]
    [InlineData(">=1.0.0 <2.0.0")]
    [InlineData("1.2.3")]
    public void RangeParse_ValidConstraints_DoesNotThrow(string constraint)
    {
        var range = SemVerRange.Parse(constraint);
        Assert.NotNull(range);
    }

    [Theory]
    [InlineData("^")]
    [InlineData("~")]
    [InlineData(">=abc")]
    public void RangeTryParse_InvalidConstraints_ReturnsFalse(string? constraint)
    {
        bool ok = SemVerRange.TryParse(constraint, out SemVerRange? range);
        Assert.False(ok);
        Assert.Null(range);
    }

    [Theory]
    [InlineData("")]
    public void RangeTryParse_EmptyString_ReturnsFalse(string? constraint)
    {
        bool ok = SemVerRange.TryParse(constraint, out SemVerRange? range);
        Assert.False(ok);
        Assert.Null(range);
    }

    [Fact]
    public void RangeTryParse_Null_ReturnsFalse()
    {
        bool ok = SemVerRange.TryParse(null, out SemVerRange? range);
        Assert.False(ok);
        Assert.Null(range);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("^1.0.0")]
    [InlineData(">=1.0.0 <2.0.0")]
    public void RangeToString_ReturnsOriginalConstraint(string constraint)
    {
        Assert.Equal(constraint, SemVerRange.Parse(constraint).ToString());
    }

    // ─── Caret Range ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.3.0", true)]
    [InlineData("1.9.9", true)]
    [InlineData("2.0.0", false)]
    [InlineData("1.2.2", false)]
    public void CaretRange_Major1_MatchesCompatibleVersions(string version, bool expected)
    {
        var range = SemVerRange.Parse("^1.2.3");
        Assert.Equal(expected, range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    [Theory]
    [InlineData("0.2.3", true)]
    [InlineData("0.2.9", true)]
    [InlineData("0.3.0", false)]
    public void CaretRange_ZeroMajorNonZeroMinor_MinorIsBreaking(string version, bool expected)
    {
        var range = SemVerRange.Parse("^0.2.3");
        Assert.Equal(expected, range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    [Theory]
    [InlineData("0.0.3", true)]
    [InlineData("0.0.4", false)]
    public void CaretRange_ZeroMajorZeroMinor_PatchIsBreaking(string version, bool expected)
    {
        var range = SemVerRange.Parse("^0.0.3");
        Assert.Equal(expected, range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    [Fact]
    public void CaretRange_DoesNotMatchLowerVersion()
    {
        var range = SemVerRange.Parse("^1.0.0");
        Assert.False(range.IsSatisfiedBy(SemVer.Parse("0.9.9")));
    }

    // ─── Tilde Range ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.9", true)]
    [InlineData("1.3.0", false)]
    public void TildeRange_SameMinorOnly_MatchesCorrectly(string version, bool expected)
    {
        var range = SemVerRange.Parse("~1.2.3");
        Assert.Equal(expected, range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    [Theory]
    [InlineData("0.2.3", true)]
    [InlineData("0.2.9", true)]
    [InlineData("0.3.0", false)]
    public void TildeRange_ZeroMajor_SameMinorOnly(string version, bool expected)
    {
        var range = SemVerRange.Parse("~0.2.3");
        Assert.Equal(expected, range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    // ─── Exact Version ───────────────────────────────────────────────────────

    [Fact]
    public void ExactVersion_MatchesOnlyExactVersion()
    {
        var range = SemVerRange.Parse("1.2.3");
        Assert.True(range.IsSatisfiedBy(SemVer.Parse("1.2.3")));
    }

    [Fact]
    public void ExactVersion_DoesNotMatchOtherVersion()
    {
        var range = SemVerRange.Parse("1.2.3");
        Assert.False(range.IsSatisfiedBy(SemVer.Parse("1.2.4")));
    }

    // ─── Comparison Operator Ranges ──────────────────────────────────────────

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.5.0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("0.9.9", false)]
    public void GteRange_MatchesVersionsAtOrAbove(string version, bool expected)
    {
        var range = SemVerRange.Parse(">=1.0.0");
        Assert.Equal(expected, range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    [Theory]
    [InlineData("1.9.9", true)]
    [InlineData("2.0.0", false)]
    public void LtRange_MatchesVersionsBelow(string version, bool expected)
    {
        var range = SemVerRange.Parse("<2.0.0");
        Assert.Equal(expected, range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.9.9", true)]
    [InlineData("0.9.9", false)]
    [InlineData("2.0.0", false)]
    public void CompoundRange_GteLtCombined_MatchesCorrectly(string version, bool expected)
    {
        var range = SemVerRange.Parse(">=1.0.0 <2.0.0");
        Assert.Equal(expected, range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    // ─── Wildcard ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0.0.1")]
    [InlineData("1.0.0")]
    [InlineData("99.99.99")]
    public void Wildcard_MatchesAnyVersion(string version)
    {
        var range = SemVerRange.Parse("*");
        Assert.True(range.IsSatisfiedBy(SemVer.Parse(version)));
    }

    [Fact]
    public void Wildcard_MatchesPreReleaseVersions()
    {
        var range = SemVerRange.Parse("*");
        Assert.True(range.IsSatisfiedBy(SemVer.Parse("1.0.0-alpha.1")));
    }

    // ─── Pre-release Semantics ───────────────────────────────────────────────

    [Fact]
    public void CaretRange_WithoutExplicitPreRelease_ExcludesPreReleaseVersions()
    {
        var range = SemVerRange.Parse("^1.0.0");
        Assert.False(range.IsSatisfiedBy(SemVer.Parse("1.1.0-beta.1")));
    }

    [Fact]
    public void CaretRange_WithExplicitPreRelease_IncludesPreReleaseOnSameTriplet()
    {
        var range = SemVerRange.Parse("^1.0.0-beta.1");
        Assert.True(range.IsSatisfiedBy(SemVer.Parse("1.0.0-beta.2")));
    }

    [Fact]
    public void CaretRange_WithExplicitPreRelease_IncludesReleaseAfterPreRelease()
    {
        var range = SemVerRange.Parse("^1.0.0-beta.1");
        Assert.True(range.IsSatisfiedBy(SemVer.Parse("1.0.0")));
    }

    [Fact]
    public void CaretRange_WithExplicitPreRelease_ExcludesPreReleaseOnDifferentTriplet()
    {
        var range = SemVerRange.Parse("^1.0.0-beta.1");
        Assert.False(range.IsSatisfiedBy(SemVer.Parse("1.1.0-beta.1")));
    }

    [Fact]
    public void Wildcard_MatchesPreRelease()
    {
        var range = SemVerRange.Parse("*");
        Assert.True(range.IsSatisfiedBy(SemVer.Parse("2.0.0-rc.1")));
    }

    // ─── FindBestMatch ───────────────────────────────────────────────────────

    [Fact]
    public void FindBestMatch_ReturnsHighestMatchingVersion()
    {
        var range = SemVerRange.Parse("^1.0.0");
        var versions = new[]
        {
            SemVer.Parse("1.0.0"),
            SemVer.Parse("1.1.0"),
            SemVer.Parse("1.2.0"),
            SemVer.Parse("2.0.0"),
        };
        Assert.Equal(SemVer.Parse("1.2.0"), range.FindBestMatch(versions));
    }

    [Fact]
    public void FindBestMatch_NoMatch_ReturnsNull()
    {
        var range = SemVerRange.Parse("^3.0.0");
        var versions = new[]
        {
            SemVer.Parse("1.0.0"),
            SemVer.Parse("2.0.0"),
        };
        Assert.Null(range.FindBestMatch(versions));
    }

    [Fact]
    public void FindBestMatch_CaretRange_ReturnsLatestCompatible()
    {
        var range = SemVerRange.Parse("^1.0.0");
        var versions = new[]
        {
            SemVer.Parse("1.0.0"),
            SemVer.Parse("1.1.0"),
            SemVer.Parse("1.2.0"),
            SemVer.Parse("2.0.0"),
        };
        Assert.Equal(SemVer.Parse("1.2.0"), range.FindBestMatch(versions));
    }

    [Fact]
    public void FindBestMatch_CaretZeroMinorRange_ReturnsLatestCompatiblePatch()
    {
        var range = SemVerRange.Parse("^0.2.3");
        var versions = new[]
        {
            SemVer.Parse("0.2.3"),
            SemVer.Parse("0.2.5"),
            SemVer.Parse("0.3.0"),
        };
        Assert.Equal(SemVer.Parse("0.2.5"), range.FindBestMatch(versions));
    }
}
