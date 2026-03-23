using System;
using System.Collections.Generic;
using System.Text;

namespace Stash.Common;

/// <summary>
/// An immutable, comparable representation of a <see href="https://semver.org">Semantic
/// Versioning 2.0.0</see> version number of the form
/// <c>MAJOR.MINOR.PATCH[-pre-release][+build]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IComparable{SemVer}"/> and <see cref="IEquatable{SemVer}"/> using
/// the SemVer 2.0 precedence rules:
/// <list type="number">
///   <item>Compare <see cref="Major"/>, <see cref="Minor"/>, <see cref="Patch"/> numerically.</item>
///   <item>A release version (no pre-release) has higher precedence than a pre-release on the
///   same <c>MAJOR.MINOR.PATCH</c>.</item>
///   <item>Pre-release identifiers are compared left to right; numeric identifiers sort lower
///   than alphanumeric identifiers of the same length.</item>
/// </list>
/// Build metadata (<see cref="BuildMetadata"/>) is ignored for precedence comparisons and
/// equality, consistent with the SemVer specification.
/// </para>
/// <para>
/// Use <see cref="Parse"/> to convert a string and throw on failure, or
/// <see cref="TryParse"/> for a non-throwing alternative. Compare instances with the
/// overloaded operators (<c>==</c>, <c>!=</c>, <c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>,
/// <c>&gt;=</c>) or via <see cref="CompareTo"/>.
/// </para>
/// </remarks>
public sealed class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    /// <summary>
    /// The canonical zero version <c>0.0.0</c>, useful as a sentinel or default value.
    /// </summary>
    public static readonly SemVer Zero = new(0, 0, 0);

    /// <summary>
    /// The major version component. Incremented when incompatible API changes are made.
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// The minor version component. Incremented when new backwards-compatible functionality
    /// is added.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// The patch version component. Incremented for backwards-compatible bug fixes.
    /// </summary>
    public int Patch { get; }

    /// <summary>
    /// The pre-release identifiers split on <c>'.'</c>, e.g. <c>["alpha", "1"]</c> for
    /// a version like <c>1.0.0-alpha.1</c>. Empty when this is a release version.
    /// </summary>
    public string[] PreRelease { get; }

    /// <summary>
    /// The build metadata string following the <c>'+'</c> separator (e.g. <c>"20231001"</c>),
    /// or an empty string when no build metadata is present. Ignored for precedence comparisons.
    /// </summary>
    public string BuildMetadata { get; }

    /// <summary>
    /// Gets a value indicating whether this version has pre-release identifiers, meaning it
    /// has lower precedence than the corresponding release version.
    /// </summary>
    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>
    /// Creates a new <see cref="SemVer"/> instance with the specified components.
    /// </summary>
    /// <param name="major">The major version number (non-negative).</param>
    /// <param name="minor">The minor version number (non-negative).</param>
    /// <param name="patch">The patch version number (non-negative).</param>
    /// <param name="preRelease">
    /// Optional array of pre-release identifier strings. Defaults to an empty array when
    /// <c>null</c>, indicating a release version.
    /// </param>
    /// <param name="buildMetadata">
    /// Optional build metadata string (without the leading <c>'+'</c>). Defaults to an empty
    /// string when <c>null</c>.
    /// </param>
    public SemVer(int major, int minor, int patch, string[]? preRelease = null, string? buildMetadata = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease ?? Array.Empty<string>();
        BuildMetadata = buildMetadata ?? "";
    }

    /// <summary>
    /// Parses a semantic version string, throwing on failure.
    /// </summary>
    /// <param name="version">The version string to parse (e.g. <c>"1.2.3-alpha.1+build"</c>).</param>
    /// <returns>The parsed <see cref="SemVer"/> instance.</returns>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="version"/> is not a valid SemVer string.
    /// </exception>
    public static SemVer Parse(string version)
    {
        if (!TryParse(version, out SemVer? result))
        {
            throw new FormatException($"Invalid semantic version: '{version}'");
        }

        return result!;
    }

    /// <summary>
    /// Attempts to parse a semantic version string without throwing.
    /// </summary>
    /// <param name="version">The version string to parse. May be <c>null</c> or empty.</param>
    /// <param name="result">
    /// When this method returns <c>true</c>, contains the parsed <see cref="SemVer"/>;
    /// otherwise <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if <paramref name="version"/> was successfully parsed; <c>false</c> if
    /// it is <c>null</c>, empty, or does not conform to the SemVer 2.0 grammar.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Parsing proceeds in three stages:
    /// <list type="number">
    ///   <item>Strip build metadata after the last <c>'+'</c>.</item>
    ///   <item>Strip and validate pre-release identifiers after the first <c>'-'</c>.</item>
    ///   <item>Parse the remaining <c>MAJOR.MINOR.PATCH</c> triple using
    ///   <see cref="TryParseNonNegativeInt"/>.</item>
    /// </list>
    /// Numeric pre-release identifiers with leading zeros (e.g. <c>"01"</c>) are rejected,
    /// per the SemVer specification.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Checks whether a string consists entirely of ASCII decimal digits and is non-empty.
    /// </summary>
    /// <param name="s">The string to test.</param>
    /// <returns>
    /// <c>true</c> if every character in <paramref name="s"/> is a digit and the string is
    /// non-empty; <c>false</c> otherwise.
    /// </returns>
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

    /// <summary>
    /// Attempts to parse a non-negative integer from a character span, checking for
    /// overflow and non-digit characters.
    /// </summary>
    /// <param name="span">The character span containing the digits to parse.</param>
    /// <param name="value">
    /// When this method returns <c>true</c>, the parsed integer value; otherwise <c>0</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if <paramref name="span"/> is non-empty, contains only digit characters,
    /// and the resulting value fits within <see cref="int.MaxValue"/>; <c>false</c> otherwise.
    /// </returns>
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

    /// <summary>
    /// Compares this instance to another <see cref="SemVer"/> using SemVer 2.0 precedence
    /// rules.
    /// </summary>
    /// <param name="other">The other version to compare against, or <c>null</c>.</param>
    /// <returns>
    /// A negative integer if this version has lower precedence than <paramref name="other"/>,
    /// zero if they have identical precedence, or a positive integer if this version has
    /// higher precedence. A <c>null</c> <paramref name="other"/> is always considered less
    /// than any non-null version.
    /// </returns>
    /// <remarks>
    /// Build metadata is intentionally ignored, consistent with SemVer 2.0 §10.
    /// Pre-release precedence is resolved by <see cref="ComparePreReleaseId"/>.
    /// </remarks>
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

    /// <summary>
    /// Compares two individual pre-release identifier strings using SemVer 2.0 §11.4 rules.
    /// </summary>
    /// <param name="a">The first pre-release identifier.</param>
    /// <param name="b">The second pre-release identifier.</param>
    /// <returns>
    /// A negative integer, zero, or a positive integer if <paramref name="a"/> sorts before,
    /// equal to, or after <paramref name="b"/>, respectively. Numeric identifiers always sort
    /// lower than alphanumeric identifiers of the same position.
    /// </returns>
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

    /// <summary>
    /// Determines whether this version has the same precedence as <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The other <see cref="SemVer"/> to compare to, or <c>null</c>.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="other"/> is not <c>null</c> and
    /// <see cref="CompareTo"/> returns <c>0</c>; <c>false</c> otherwise.
    /// </returns>
    public bool Equals(SemVer? other) => other is not null && CompareTo(other) == 0;

    /// <summary>
    /// Determines whether this version equals the given object.
    /// </summary>
    /// <param name="obj">The object to compare to.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="obj"/> is a <see cref="SemVer"/> with the same
    /// precedence; <c>false</c> otherwise.
    /// </returns>
    public override bool Equals(object? obj) => obj is SemVer other && Equals(other);

    /// <summary>
    /// Returns a hash code based on <see cref="Major"/>, <see cref="Minor"/>,
    /// <see cref="Patch"/>, and each <see cref="PreRelease"/> identifier.
    /// Build metadata is excluded to be consistent with equality semantics.
    /// </summary>
    /// <returns>A hash code for use in hash-based collections.</returns>
    public override int GetHashCode()
    {
        int hash = HashCode.Combine(Major, Minor, Patch);
        foreach (string id in PreRelease)
        {
            hash = HashCode.Combine(hash, id);
        }

        return hash;
    }

    /// <summary>
    /// Returns the canonical SemVer string representation, e.g. <c>"1.2.3-alpha.1+build"</c>.
    /// </summary>
    /// <returns>
    /// A string of the form <c>MAJOR.MINOR.PATCH</c>, with <c>-pre.release</c> appended when
    /// <see cref="IsPreRelease"/> is <c>true</c>, and <c>+buildMetadata</c> appended when
    /// <see cref="BuildMetadata"/> is non-empty.
    /// </returns>
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

    /// <summary>Checks whether two <see cref="SemVer"/> instances have equal precedence.</summary>
    public static bool operator ==(SemVer? left, SemVer? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Checks whether two <see cref="SemVer"/> instances have different precedence.</summary>
    public static bool operator !=(SemVer? left, SemVer? right) => !(left == right);

    /// <summary>Returns <c>true</c> if <paramref name="left"/> has strictly lower precedence than <paramref name="right"/>.</summary>
    public static bool operator <(SemVer left, SemVer right) => left.CompareTo(right) < 0;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> has strictly higher precedence than <paramref name="right"/>.</summary>
    public static bool operator >(SemVer left, SemVer right) => left.CompareTo(right) > 0;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> has lower or equal precedence compared to <paramref name="right"/>.</summary>
    public static bool operator <=(SemVer left, SemVer right) => left.CompareTo(right) <= 0;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> has higher or equal precedence compared to <paramref name="right"/>.</summary>
    public static bool operator >=(SemVer left, SemVer right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// Represents a parsed SemVer version range constraint (e.g. <c>"^1.2.3"</c>,
/// <c>"~2.0.0"</c>, <c>"&gt;=1.0.0 &lt;2.0.0"</c>, or <c>"*"</c>) that can be used to
/// test whether a <see cref="SemVer"/> satisfies the constraint.
/// </summary>
/// <remarks>
/// <para>
/// Supported constraint syntax:
/// <list type="bullet">
///   <item><c>*</c> — any version.</item>
///   <item><c>^MAJOR.MINOR.PATCH</c> — caret range: compatible with the given version
///   (allows changes that do not modify the leftmost non-zero digit).</item>
///   <item><c>~MAJOR.MINOR.PATCH</c> — tilde range: approximately equivalent (same major
///   and minor, any patch).</item>
///   <item>Space-separated comparators, e.g. <c>"&gt;=1.0.0 &lt;2.0.0"</c>, each of the
///   form <c>[operator]VERSION</c> where operator is one of <c>&gt;=</c>, <c>&lt;=</c>,
///   <c>&gt;</c>, <c>&lt;</c>, <c>=</c>, or absent (exact match).</item>
/// </list>
/// </para>
/// <para>
/// Pre-release versions are only matched when the constraint itself explicitly references a
/// pre-release on the same <c>MAJOR.MINOR.PATCH</c>, or when the constraint is <c>"*"</c>.
/// This follows the behaviour of standard SemVer range libraries.
/// </para>
/// </remarks>
public sealed class SemVerRange
{
    /// <summary>
    /// Defines the comparison operators that a single <see cref="Comparator"/> can apply.
    /// </summary>
    private enum CompOp
    {
        /// <summary>Exact equality (<c>=</c>).</summary>
        Eq,
        /// <summary>Greater than or equal (<c>&gt;=</c>).</summary>
        Gte,
        /// <summary>Less than or equal (<c>&lt;=</c>).</summary>
        Lte,
        /// <summary>Strictly greater than (<c>&gt;</c>).</summary>
        Gt,
        /// <summary>Strictly less than (<c>&lt;</c>).</summary>
        Lt,
        /// <summary>Wildcard — matches any version (<c>*</c>).</summary>
        Any
    }

    /// <summary>
    /// Represents a single version comparator consisting of an operator and an optional
    /// version operand, used as a building block of a <see cref="SemVerRange"/>.
    /// </summary>
    private readonly struct Comparator
    {
        /// <summary>The comparison operator to apply when evaluating this comparator.</summary>
        public readonly CompOp Op;

        /// <summary>
        /// The version operand to compare against. <c>null</c> when <see cref="Op"/> is
        /// <see cref="CompOp.Any"/>.
        /// </summary>
        public readonly SemVer? Version;

        /// <summary>
        /// Creates a new <see cref="Comparator"/> with the given operator and version.
        /// </summary>
        /// <param name="op">The comparison operator.</param>
        /// <param name="version">
        /// The version to compare against, or <c>null</c> for a wildcard comparator.
        /// </param>
        public Comparator(CompOp op, SemVer? version)
        {
            Op = op;
            Version = version;
        }

        /// <summary>
        /// Tests whether the given version satisfies this comparator.
        /// </summary>
        /// <param name="v">The <see cref="SemVer"/> to test.</param>
        /// <returns>
        /// <c>true</c> if <paramref name="v"/> satisfies the operator/version pair;
        /// always <c>true</c> when <see cref="Op"/> is <see cref="CompOp.Any"/>.
        /// </returns>
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

    /// <summary>
    /// The ordered list of <see cref="Comparator"/> instances that must all be satisfied for
    /// a version to match this range. Populated by <see cref="TryParse"/>.
    /// </summary>
    private readonly List<Comparator> _comparators;

    /// <summary>
    /// The original constraint string as provided to <see cref="Parse"/> or
    /// <see cref="TryParse"/>. Returned verbatim by <see cref="ToString"/>.
    /// </summary>
    private readonly string _original;

    /// <summary>
    /// Creates a new <see cref="SemVerRange"/> from an already-parsed list of comparators
    /// and the original constraint string.
    /// </summary>
    /// <param name="comparators">The parsed list of <see cref="Comparator"/> instances.</param>
    /// <param name="original">The original constraint string.</param>
    private SemVerRange(List<Comparator> comparators, string original)
    {
        _comparators = comparators;
        _original = original;
    }

    /// <summary>
    /// Parses a version range constraint string, throwing on failure.
    /// </summary>
    /// <param name="constraint">The constraint string to parse.</param>
    /// <returns>The parsed <see cref="SemVerRange"/>.</returns>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="constraint"/> is not a valid range expression.
    /// </exception>
    public static SemVerRange Parse(string constraint)
    {
        if (!TryParse(constraint, out SemVerRange? result))
        {
            throw new FormatException($"Invalid version constraint: '{constraint}'");
        }

        return result!;
    }

    /// <summary>
    /// Attempts to parse a version range constraint string without throwing.
    /// </summary>
    /// <param name="constraint">
    /// The constraint string to parse (e.g. <c>"^1.0.0"</c>, <c>"~2.3.0"</c>,
    /// <c>"&gt;=1.0.0 &lt;2.0.0"</c>, <c>"*"</c>). May be <c>null</c>.
    /// </param>
    /// <param name="range">
    /// When this method returns <c>true</c>, contains the parsed <see cref="SemVerRange"/>;
    /// otherwise <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the constraint was successfully parsed; <c>false</c> if it is
    /// <c>null</c>, empty, or does not match any supported syntax.
    /// </returns>
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

    /// <summary>
    /// Attempts to parse a single comparator token such as <c>"&gt;=1.0.0"</c>,
    /// <c>"&lt;2.0.0"</c>, <c>"1.2.3"</c>, or <c>"*"</c>.
    /// </summary>
    /// <param name="s">The comparator token string to parse.</param>
    /// <param name="comp">
    /// When this method returns <c>true</c>, the parsed <see cref="Comparator"/>;
    /// otherwise the <c>default</c> value.
    /// </param>
    /// <returns>
    /// <c>true</c> if <paramref name="s"/> was successfully parsed; <c>false</c> if the
    /// version portion is not a valid <see cref="SemVer"/> string.
    /// </returns>
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

    /// <summary>
    /// Tests whether the given version satisfies this range, respecting pre-release opt-in
    /// semantics.
    /// </summary>
    /// <param name="version">The <see cref="SemVer"/> to test against the range.</param>
    /// <returns>
    /// <c>true</c> if every <see cref="Comparator"/> in this range matches
    /// <paramref name="version"/>; <c>false</c> if any comparator rejects it, or if
    /// <paramref name="version"/> is a pre-release and the range does not explicitly opt in
    /// to pre-releases on the same <c>MAJOR.MINOR.PATCH</c>.
    /// </returns>
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

    /// <summary>
    /// Finds the highest version from a collection that satisfies this range.
    /// </summary>
    /// <param name="versions">The candidate versions to search through.</param>
    /// <returns>
    /// The highest <see cref="SemVer"/> in <paramref name="versions"/> that satisfies
    /// <see cref="IsSatisfiedBy"/>, or <c>null</c> if no version satisfies the range.
    /// </returns>
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

    /// <summary>
    /// Returns the original constraint string that was used to create this range.
    /// </summary>
    /// <returns>The original constraint string passed to <see cref="Parse"/> or <see cref="TryParse"/>.</returns>
    public override string ToString() => _original;
}
