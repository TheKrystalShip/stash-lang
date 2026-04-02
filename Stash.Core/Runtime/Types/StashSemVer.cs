namespace Stash.Runtime.Types;

using System;

/// <summary>
/// Represents a semantic version value in Stash (e.g., <c>1.0.0</c>, <c>3.0.0-beta.2</c>, <c>2.x</c>).
/// Follows the SemVer 2.0.0 specification for parsing, comparison, and equality.
/// Build metadata is ignored in comparisons and equality per spec.
/// </summary>
public sealed class StashSemVer : IComparable<StashSemVer>, IEquatable<StashSemVer>
{
    public long Major { get; }
    public long Minor { get; }
    public long Patch { get; }
    public string? Prerelease { get; }
    public string? BuildMetadata { get; }
    public bool IsPrerelease => !string.IsNullOrEmpty(Prerelease);
    public bool IsWildcardMinor { get; }
    public bool IsWildcardPatch { get; }
    public bool IsWildcard => IsWildcardMinor || IsWildcardPatch;

    public StashSemVer(
        long major,
        long minor,
        long patch,
        string? prerelease = null,
        string? buildMetadata = null,
        bool isWildcardMinor = false,
        bool isWildcardPatch = false)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
        BuildMetadata = string.IsNullOrEmpty(buildMetadata) ? null : buildMetadata;
        IsWildcardMinor = isWildcardMinor;
        IsWildcardPatch = isWildcardPatch;
    }

    // --- Parsing ---

    /// <summary>
    /// Tries to parse a semver string (without <c>@v</c> prefix).
    /// Supports standard versions, prerelease, build metadata, and wildcards.
    /// </summary>
    public static bool TryParse(string text, out StashSemVer? result)
    {
        result = null;
        if (string.IsNullOrEmpty(text)) return false;

        // Strip build metadata first (appears after '+')
        string? buildMetadata = null;
        int buildIdx = text.IndexOf('+');
        if (buildIdx >= 0)
        {
            buildMetadata = text[(buildIdx + 1)..];
            if (string.IsNullOrEmpty(buildMetadata) || !IsValidBuildMetadata(buildMetadata))
                return false;
            text = text[..buildIdx];
        }

        // Strip prerelease (appears after first '-' in the remaining string)
        string? prerelease = null;
        int preIdx = text.IndexOf('-');
        if (preIdx >= 0)
        {
            prerelease = text[(preIdx + 1)..];
            if (string.IsNullOrEmpty(prerelease) || !IsValidPrerelease(prerelease))
                return false;
            text = text[..preIdx];
        }

        // Remaining text is the version core: MAJOR.MINOR.PATCH or MAJOR.x or MAJOR.MINOR.x
        string[] parts = text.Split('.');
        if (parts.Length < 2 || parts.Length > 3) return false;

        if (!TryParseVersionPart(parts[0], out long major)) return false;

        if (parts.Length == 2)
        {
            // Must be a wildcard minor: "2.x" or "2.*"
            if (parts[1] is "x" or "*")
            {
                if (prerelease != null || buildMetadata != null) return false;
                result = new StashSemVer(major, 0, 0, null, null, isWildcardMinor: true);
                return true;
            }
            // Two-part version without wildcard is not valid semver
            return false;
        }

        // parts.Length == 3
        if (parts[2] is "x" or "*")
        {
            // Wildcard patch: "2.4.x" or "2.4.*"
            if (!TryParseVersionPart(parts[1], out long minorW)) return false;
            if (prerelease != null || buildMetadata != null) return false;
            result = new StashSemVer(major, minorW, 0, null, null, isWildcardPatch: true);
            return true;
        }

        if (!TryParseVersionPart(parts[1], out long minor)) return false;
        if (!TryParseVersionPart(parts[2], out long patch)) return false;

        result = new StashSemVer(major, minor, patch, prerelease, buildMetadata);
        return true;
    }

    private static bool TryParseVersionPart(string s, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;
        // Reject leading zeros per semver spec ("01", "00" — but "0" is allowed)
        if (s.Length > 1 && s[0] == '0') return false;
        return long.TryParse(s, out value) && value >= 0;
    }

    private static bool IsValidPrerelease(string prerelease)
    {
        string[] identifiers = prerelease.Split('.');
        foreach (string id in identifiers)
        {
            if (string.IsNullOrEmpty(id)) return false;

            bool isNumeric = true;
            foreach (char c in id)
            {
                if (!char.IsAsciiDigit(c))
                {
                    isNumeric = false;
                    break;
                }
            }

            if (isNumeric)
            {
                // Numeric identifiers must not have leading zeros
                if (id.Length > 1 && id[0] == '0') return false;
            }
            else
            {
                // Alphanumeric identifiers: only [0-9A-Za-z-] allowed
                foreach (char c in id)
                {
                    if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
                }
            }
        }
        return true;
    }

    private static bool IsValidBuildMetadata(string build)
    {
        string[] identifiers = build.Split('.');
        foreach (string id in identifiers)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns a validation error message, or <c>null</c> if the text is a valid semver string.
    /// </summary>
    public static string? ValidateFormat(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "Empty version string.";

        return TryParse(text, out _)
            ? null
            : $"Invalid semantic version format '{text}'. Expected format: MAJOR.MINOR.PATCH[-prerelease][+build].";
    }

    // --- Comparison ---

    public int CompareTo(StashSemVer? other)
    {
        if (other is null) return 1;

        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;

        // SemVer 2.0.0: a version without prerelease has higher precedence than one with prerelease
        if (Prerelease is null && other.Prerelease is null) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string a, string b)
    {
        string[] aParts = a.Split('.');
        string[] bParts = b.Split('.');

        int len = Math.Min(aParts.Length, bParts.Length);
        for (int i = 0; i < len; i++)
        {
            int cmp = CompareIdentifier(aParts[i], bParts[i]);
            if (cmp != 0) return cmp;
        }

        // Shorter prerelease has lower precedence if all preceding identifiers are equal
        return aParts.Length.CompareTo(bParts.Length);
    }

    private static int CompareIdentifier(string a, string b)
    {
        bool aIsNumeric = long.TryParse(a, out long aNum);
        bool bIsNumeric = long.TryParse(b, out long bNum);

        // Both numeric: compare as integers
        if (aIsNumeric && bIsNumeric) return aNum.CompareTo(bNum);

        // Numeric always has lower precedence than alphanumeric
        if (aIsNumeric) return -1;
        if (bIsNumeric) return 1;

        // Both alphanumeric: compare lexicographically
        return string.Compare(a, b, StringComparison.Ordinal);
    }

    // --- Wildcard Matching ---

    /// <summary>
    /// Returns true if this version pattern matches <paramref name="other"/>.
    /// <list type="bullet">
    ///   <item><c>2.x</c> (IsWildcardMinor) matches any version with Major == 2.</item>
    ///   <item><c>2.4.x</c> (IsWildcardPatch) matches any version with Major == 2 and Minor == 4.</item>
    ///   <item>Non-wildcard performs exact equality.</item>
    /// </list>
    /// </summary>
    public bool Matches(StashSemVer other)
    {
        if (IsWildcardMinor) return Major == other.Major;
        if (IsWildcardPatch) return Major == other.Major && Minor == other.Minor;
        return Equals(other);
    }

    // --- Equality ---

    public bool Equals(StashSemVer? other)
    {
        if (other is null) return false;
        // Build metadata is intentionally ignored per SemVer 2.0.0
        return Major == other.Major &&
               Minor == other.Minor &&
               Patch == other.Patch &&
               Prerelease == other.Prerelease &&
               IsWildcardMinor == other.IsWildcardMinor &&
               IsWildcardPatch == other.IsWildcardPatch;
    }

    public override bool Equals(object? obj) => obj is StashSemVer other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease, IsWildcardMinor, IsWildcardPatch);

    public static bool operator ==(StashSemVer? left, StashSemVer? right) =>
        left is null ? right is null : left.Equals(right);
    public static bool operator !=(StashSemVer? left, StashSemVer? right) => !(left == right);

    // --- ToString ---

    public override string ToString()
    {
        if (IsWildcardMinor) return $"{Major}.x";
        if (IsWildcardPatch) return $"{Major}.{Minor}.x";

        string s = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Prerelease)) s += $"-{Prerelease}";
        if (!string.IsNullOrEmpty(BuildMetadata)) s += $"+{BuildMetadata}";
        return s;
    }
}
