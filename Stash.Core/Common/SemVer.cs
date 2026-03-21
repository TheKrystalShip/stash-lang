using System;
using System.Collections.Generic;
using System.Text;

namespace Stash.Common;

public sealed class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    public static readonly SemVer Zero = new(0, 0, 0);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string[] PreRelease { get; }
    public string BuildMetadata { get; }

    public bool IsPreRelease => PreRelease.Length > 0;

    public SemVer(int major, int minor, int patch, string[]? preRelease = null, string? buildMetadata = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease ?? Array.Empty<string>();
        BuildMetadata = buildMetadata ?? "";
    }

    public static SemVer Parse(string version)
    {
        if (!TryParse(version, out SemVer? result))
        {
            throw new FormatException($"Invalid semantic version: '{version}'");
        }

        return result!;
    }

    public static bool TryParse(string? version, out SemVer? result)
    {
        result = null;
        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        ReadOnlySpan<char> span = version.AsSpan().Trim();

        // Strip build metadata (after '+')
        string buildMetadata = "";
        int plusIdx = span.IndexOf('+');
        if (plusIdx >= 0)
        {
            buildMetadata = span.Slice(plusIdx + 1).ToString();
            if (buildMetadata.Length == 0)
            {
                return false;
            }

            span = span.Slice(0, plusIdx);
        }

        // Strip pre-release (after '-')
        string[] preRelease = Array.Empty<string>();
        int dashIdx = span.IndexOf('-');
        if (dashIdx >= 0)
        {
            string preStr = span.Slice(dashIdx + 1).ToString();
            if (preStr.Length == 0)
            {
                return false;
            }

            preRelease = preStr.Split('.');
            foreach (string id in preRelease)
            {
                if (id.Length == 0)
                {
                    return false;
                }

                foreach (char c in id)
                {
                    if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '-'))
                    {
                        return false;
                    }
                }
                if (IsNumeric(id) && id.Length > 1 && id[0] == '0')
                {
                    return false;
                }
            }
            span = span.Slice(0, dashIdx);
        }

        // Parse MAJOR.MINOR.PATCH
        int dot1 = span.IndexOf('.');
        if (dot1 < 0)
        {
            return false;
        }

        ReadOnlySpan<char> rest = span.Slice(dot1 + 1);
        int dot2 = rest.IndexOf('.');
        if (dot2 < 0)
        {
            return false;
        }

        if (!TryParseNonNegativeInt(span.Slice(0, dot1), out int major))
        {
            return false;
        }

        if (!TryParseNonNegativeInt(rest.Slice(0, dot2), out int minor))
        {
            return false;
        }

        if (!TryParseNonNegativeInt(rest.Slice(dot2 + 1), out int patch))
        {
            return false;
        }

        result = new SemVer(major, minor, patch, preRelease, buildMetadata.Length > 0 ? buildMetadata : null);
        return true;
    }

    private static bool IsNumeric(string s)
    {
        foreach (char c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }
        return s.Length > 0;
    }

    private static bool TryParseNonNegativeInt(ReadOnlySpan<char> span, out int value)
    {
        value = 0;
        if (span.IsEmpty)
        {
            return false;
        }

        foreach (char c in span)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }

            int digit = c - '0';
            if (value > (int.MaxValue - digit) / 10)
            {
                return false;
            }

            value = value * 10 + digit;
        }
        return true;
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null)
        {
            return 1;
        }

        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0)
        {
            return cmp;
        }

        // A version without pre-release is higher than one with pre-release
        if (PreRelease.Length == 0 && other.PreRelease.Length == 0)
        {
            return 0;
        }

        if (PreRelease.Length == 0)
        {
            return 1;
        }

        if (other.PreRelease.Length == 0)
        {
            return -1;
        }

        int count = Math.Min(PreRelease.Length, other.PreRelease.Length);
        for (int i = 0; i < count; i++)
        {
            cmp = ComparePreReleaseId(PreRelease[i], other.PreRelease[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return PreRelease.Length.CompareTo(other.PreRelease.Length);
    }

    private static int ComparePreReleaseId(string a, string b)
    {
        bool aIsNum = TryParseNonNegativeInt(a.AsSpan(), out int aNum);
        bool bIsNum = TryParseNonNegativeInt(b.AsSpan(), out int bNum);

        if (aIsNum && bIsNum)
        {
            return aNum.CompareTo(bNum);
        }

        if (aIsNum)
        {
            return -1;  // numeric < alphanumeric
        }

        if (bIsNum)
        {
            return 1;   // alphanumeric > numeric
        }

        return string.Compare(a, b, StringComparison.Ordinal);
    }

    public bool Equals(SemVer? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemVer other && Equals(other);

    public override int GetHashCode()
    {
        int hash = HashCode.Combine(Major, Minor, Patch);
        foreach (string id in PreRelease)
        {
            hash = HashCode.Combine(hash, id);
        }

        return hash;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Major).Append('.').Append(Minor).Append('.').Append(Patch);
        if (PreRelease.Length > 0)
        {
            sb.Append('-').Append(string.Join('.', PreRelease));
        }

        if (BuildMetadata.Length > 0)
        {
            sb.Append('+').Append(BuildMetadata);
        }

        return sb.ToString();
    }

    public static bool operator ==(SemVer? left, SemVer? right)
        => left is null ? right is null : left.Equals(right);
    public static bool operator !=(SemVer? left, SemVer? right) => !(left == right);
    public static bool operator <(SemVer left, SemVer right) => left.CompareTo(right) < 0;
    public static bool operator >(SemVer left, SemVer right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemVer left, SemVer right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemVer left, SemVer right) => left.CompareTo(right) >= 0;
}

public sealed class SemVerRange
{
    private enum CompOp { Eq, Gte, Lte, Gt, Lt, Any }

    private readonly struct Comparator
    {
        public readonly CompOp Op;
        public readonly SemVer? Version;

        public Comparator(CompOp op, SemVer? version)
        {
            Op = op;
            Version = version;
        }

        public bool Matches(SemVer v)
        {
            if (Op == CompOp.Any)
            {
                return true;
            }

            int cmp = v.CompareTo(Version);
            return Op switch
            {
                CompOp.Eq  => cmp == 0,
                CompOp.Gte => cmp >= 0,
                CompOp.Lte => cmp <= 0,
                CompOp.Gt  => cmp > 0,
                CompOp.Lt  => cmp < 0,
                _          => false,
            };
        }
    }

    private readonly List<Comparator> _comparators;
    private readonly string _original;

    private SemVerRange(List<Comparator> comparators, string original)
    {
        _comparators = comparators;
        _original = original;
    }

    public static SemVerRange Parse(string constraint)
    {
        if (!TryParse(constraint, out SemVerRange? result))
        {
            throw new FormatException($"Invalid version constraint: '{constraint}'");
        }

        return result!;
    }

    public static bool TryParse(string? constraint, out SemVerRange? range)
    {
        range = null;
        if (constraint is null)
        {
            return false;
        }

        string trimmed = constraint.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var comparators = new List<Comparator>();

        if (trimmed == "*")
        {
            comparators.Add(new Comparator(CompOp.Any, null));
            range = new SemVerRange(comparators, trimmed);
            return true;
        }

        // Caret: ^MAJOR.MINOR.PATCH — compatible range
        if (trimmed.Length > 1 && trimmed[0] == '^')
        {
            if (!SemVer.TryParse(trimmed.Substring(1), out SemVer? v))
            {
                return false;
            }

            SemVer upper = v!.Major > 0
                ? new SemVer(v.Major + 1, 0, 0)
                : v.Minor > 0
                    ? new SemVer(0, v.Minor + 1, 0)
                    : new SemVer(0, 0, v.Patch + 1);
            comparators.Add(new Comparator(CompOp.Gte, v));
            comparators.Add(new Comparator(CompOp.Lt, upper));
            range = new SemVerRange(comparators, trimmed);
            return true;
        }

        // Tilde: ~MAJOR.MINOR.PATCH — approximate range (same minor)
        if (trimmed.Length > 1 && trimmed[0] == '~')
        {
            if (!SemVer.TryParse(trimmed.Substring(1), out SemVer? v))
            {
                return false;
            }

            SemVer upper = new SemVer(v!.Major, v.Minor + 1, 0);
            comparators.Add(new Comparator(CompOp.Gte, v));
            comparators.Add(new Comparator(CompOp.Lt, upper));
            range = new SemVerRange(comparators, trimmed);
            return true;
        }

        // Space-separated comparators: ">=1.0.0 <2.0.0" or single "1.2.3"
        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (!TryParseComparator(part, out Comparator comp))
            {
                return false;
            }

            comparators.Add(comp);
        }

        if (comparators.Count == 0)
        {
            return false;
        }

        range = new SemVerRange(comparators, trimmed);
        return true;
    }

    private static bool TryParseComparator(string s, out Comparator comp)
    {
        comp = default;

        if (s == "*")
        {
            comp = new Comparator(CompOp.Any, null);
            return true;
        }

        ReadOnlySpan<char> span = s.AsSpan();
        CompOp op;
        int skip;

        if (span.StartsWith(">="))      { op = CompOp.Gte; skip = 2; }
        else if (span.StartsWith("<=")) { op = CompOp.Lte; skip = 2; }
        else if (span.StartsWith(">"))  { op = CompOp.Gt;  skip = 1; }
        else if (span.StartsWith("<"))  { op = CompOp.Lt;  skip = 1; }
        else if (span.StartsWith("="))  { op = CompOp.Eq;  skip = 1; }
        else                            { op = CompOp.Eq;  skip = 0; }

        if (!SemVer.TryParse(span.Slice(skip).ToString(), out SemVer? version))
        {
            return false;
        }

        comp = new Comparator(op, version);
        return true;
    }

    public bool IsSatisfiedBy(SemVer version)
    {
        // Pre-releases are only matched when the constraint explicitly opts in
        // by specifying a pre-release on the same MAJOR.MINOR.PATCH.
        if (version.IsPreRelease)
        {
            bool optedIn = false;
            foreach (Comparator c in _comparators)
            {
                if (c.Op == CompOp.Any)
                {
                    optedIn = true;
                    break;
                }
                if (c.Version is not null && c.Version.IsPreRelease
                    && c.Version.Major == version.Major
                    && c.Version.Minor == version.Minor
                    && c.Version.Patch == version.Patch)
                {
                    optedIn = true;
                    break;
                }
            }
            if (!optedIn)
            {
                return false;
            }
        }

        foreach (Comparator c in _comparators)
        {
            if (!c.Matches(version))
            {
                return false;
            }
        }

        return true;
    }

    public SemVer? FindBestMatch(IEnumerable<SemVer> versions)
    {
        SemVer? best = null;
        foreach (SemVer v in versions)
        {
            if (IsSatisfiedBy(v) && (best is null || v.CompareTo(best) > 0))
            {
                best = v;
            }
        }
        return best;
    }

    public override string ToString() => _original;
}
