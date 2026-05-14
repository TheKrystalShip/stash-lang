namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>str</c> namespace built-in functions for string manipulation.
/// </summary>
/// <remarks>
/// <para>
/// Provides 38 functions including <c>str.upper</c>, <c>str.lower</c>, <c>str.trim</c>,
/// <c>str.split</c>, <c>str.replace</c>, <c>str.substring</c>, <c>str.match</c>,
/// <c>str.format</c>, <c>str.capitalize</c>, <c>str.slug</c>, <c>str.wrap</c>, and more.
/// All functions are registered as <see cref="BuiltInFunction"/> instances on a
/// <see cref="StashNamespace"/> in the global <see cref="Environment"/>.
/// </para>
/// <para>
/// All string functions are non-mutating and return new values. Regex-based functions
/// (e.g. <c>str.match</c>, <c>str.isMatch</c>, <c>str.replaceRegex</c>) use a 5-second
/// timeout to guard against catastrophic backtracking.
/// </para>
/// </remarks>
[StashNamespace]
public static partial class StrBuiltIns
{
    /// <summary>A single regex capture group with value, position, and optional name.</summary>
    [StashStruct]
    public sealed record RegexGroup
    {
        public string Value { get; init; } = "";
        public long Index { get; init; }
        public long Length { get; init; }
        public string Name { get; init; } = "";
    }

    /// <summary>A full regex match with capture groups and named groups.</summary>
    [StashStruct]
    public sealed record RegexMatch
    {
        public string Value { get; init; } = "";
        public long Index { get; init; }
        public long Length { get; init; }
        public List<StashValue> Groups { get; init; } = [];
        [StashField(Type = "dict")]
        public StashDictionary NamedGroups { get; init; } = new();
    }

    /// <summary>Returns the string converted to uppercase.</summary>
    /// <param name="s">The string</param>
    /// <returns>Uppercase string</returns>
    [StashFn]
    private static string Upper(string s) => s.ToUpperInvariant();

    /// <summary>Returns the string converted to lowercase.</summary>
    /// <param name="s">The string</param>
    /// <returns>Lowercase string</returns>
    [StashFn]
    private static string Lower(string s) => s.ToLowerInvariant();

    /// <summary>Returns the string with leading and trailing whitespace (or specified chars) removed.</summary>
    /// <param name="s">The string</param>
    /// <param name="chars">Optional string of characters to trim</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Trimmed string</returns>
    [StashFn]
    private static string Trim(string s, params StashValue[] rest)
    {
        if (rest.Length > 0)
        {
            var chars = SvArgs.String(rest, 0, "str.trim");
            return s.Trim(chars.ToCharArray());
        }
        return s.Trim();
    }

    /// <summary>Returns the string with leading whitespace (or specified chars) removed.</summary>
    /// <param name="s">The string</param>
    /// <param name="chars">Optional string of characters to trim</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Left-trimmed string</returns>
    [StashFn]
    private static string TrimStart(string s, params StashValue[] rest)
    {
        if (rest.Length > 0)
        {
            var chars = SvArgs.String(rest, 0, "str.trimStart");
            return s.TrimStart(chars.ToCharArray());
        }
        return s.TrimStart();
    }

    /// <summary>Returns the string with trailing whitespace (or specified chars) removed.</summary>
    /// <param name="s">The string</param>
    /// <param name="chars">Optional string of characters to trim</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Right-trimmed string</returns>
    [StashFn]
    private static string TrimEnd(string s, params StashValue[] rest)
    {
        if (rest.Length > 0)
        {
            var chars = SvArgs.String(rest, 0, "str.trimEnd");
            return s.TrimEnd(chars.ToCharArray());
        }
        return s.TrimEnd();
    }

    /// <summary>Returns true if the string contains the substring.</summary>
    /// <param name="s">The string</param>
    /// <param name="substring">The substring to search for</param>
    /// <param name="ignoreCase">Optional; when true, comparison is case-insensitive</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>true if found</returns>
    [StashFn]
    private static bool Contains(string s, string substring, params StashValue[] rest)
    {
        var comparison = rest.Length > 0 && SvArgs.Bool(rest, 0, "str.contains")
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return s.Contains(substring, comparison);
    }

    /// <summary>Returns true if the string starts with the prefix.</summary>
    /// <param name="s">The string</param>
    /// <param name="prefix">The prefix</param>
    /// <param name="ignoreCase">Optional; when true, comparison is case-insensitive</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>true if starts with prefix</returns>
    [StashFn]
    private static bool StartsWith(string s, string prefix, params StashValue[] rest)
    {
        var comparison = rest.Length > 0 && SvArgs.Bool(rest, 0, "str.startsWith")
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return s.StartsWith(prefix, comparison);
    }

    /// <summary>Returns true if the string ends with the suffix.</summary>
    /// <param name="s">The string</param>
    /// <param name="suffix">The suffix</param>
    /// <param name="ignoreCase">Optional; when true, comparison is case-insensitive</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>true if ends with suffix</returns>
    [StashFn]
    private static bool EndsWith(string s, string suffix, params StashValue[] rest)
    {
        var comparison = rest.Length > 0 && SvArgs.Bool(rest, 0, "str.endsWith")
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return s.EndsWith(suffix, comparison);
    }

    /// <summary>Returns the index of the first occurrence of substring at or after startIndex, or -1 if not found.</summary>
    /// <param name="s">The string</param>
    /// <param name="substring">The substring</param>
    /// <param name="startIndex">Optional start position for the search</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Zero-based index or -1</returns>
    [StashFn]
    private static long IndexOf(string s, string substring, params StashValue[] rest)
    {
        if (rest.Length > 0)
        {
            int startIndex = (int)SvArgs.Long(rest, 0, "str.indexOf");
            return (long)s.IndexOf(substring, startIndex, StringComparison.Ordinal);
        }
        return (long)s.IndexOf(substring, StringComparison.Ordinal);
    }

    /// <summary>Returns the index of the last occurrence of substring, searching backwards from startIndex, or -1 if not found.</summary>
    /// <param name="s">The string</param>
    /// <param name="substring">The substring</param>
    /// <param name="startIndex">Optional position to begin searching backwards from</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Zero-based index or -1</returns>
    [StashFn]
    private static long LastIndexOf(string s, string substring, params StashValue[] rest)
    {
        if (rest.Length > 0)
        {
            int startIndex = (int)SvArgs.Long(rest, 0, "str.lastIndexOf");
            return (long)s.LastIndexOf(substring, startIndex, StringComparison.Ordinal);
        }
        return (long)s.LastIndexOf(substring, StringComparison.Ordinal);
    }

    /// <summary>Returns a portion of the string from start (inclusive) to end (exclusive).</summary>
    /// <param name="s">The string</param>
    /// <param name="start">Start index (inclusive)</param>
    /// <param name="end">Optional end index (exclusive)</param>
    /// <exception cref="IndexError">if `start` or `end` is out of range</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Substring</returns>
    [StashFn]
    private static string Substring(string s, long start, params StashValue[] rest)
    {
        if (start < 0 || start > s.Length)
            throw new IndexError($"'str.substring' start index {start} is out of range for string of length {s.Length}.");
        if (rest.Length > 0)
        {
            var end = SvArgs.Long(rest, 0, "str.substring");
            if (end < start || end > s.Length)
                throw new IndexError($"'str.substring' end index {end} is out of range.");
            return s.Substring((int)start, (int)(end - start));
        }
        return s.Substring((int)start);
    }

    /// <summary>Returns the string with up to count occurrences of old replaced by new (default 1).</summary>
    /// <param name="s">The string</param>
    /// <param name="old">The substring to replace</param>
    /// <param name="newValue">The replacement</param>
    /// <param name="count">Optional maximum number of replacements (default 1)</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Modified string</returns>
    [StashFn]
    private static string Replace(string s, string old, [StashParam(Name = "new")] string newValue, params StashValue[] rest)
    {
        int count = rest.Length > 0 ? (int)SvArgs.Long(rest, 0, "str.replace") : 1;
        if (count <= 0) count = 1;
        int startIndex = 0;
        for (int i = 0; i < count; i++)
        {
            int pos = s.IndexOf(old, startIndex, StringComparison.Ordinal);
            if (pos < 0) break;
            s = s.Substring(0, pos) + newValue + s.Substring(pos + old.Length);
            startIndex = pos + newValue.Length;
        }
        return s;
    }

    /// <summary>Returns the string with all occurrences of old replaced by new.</summary>
    /// <param name="s">The string</param>
    /// <param name="old">The substring to replace</param>
    /// <param name="newValue">The replacement</param>
    /// <returns>Modified string</returns>
    [StashFn]
    private static string ReplaceAll(string s, string old, [StashParam(Name = "new")] string newValue)
        => s.Replace(old, newValue, StringComparison.Ordinal);

    /// <summary>Splits the string by delimiter. When limit is positive, produces at most limit+1 pieces.</summary>
    /// <param name="s">The string</param>
    /// <param name="delimiter">The delimiter</param>
    /// <param name="limit">Optional maximum number of splits</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Array of substrings</returns>
    [StashFn]
    private static List<StashValue> Split(string s, string delimiter, params StashValue[] rest)
    {
        string[] parts;
        if (rest.Length > 0)
        {
            int limit = (int)SvArgs.Long(rest, 0, "str.split");
            parts = limit > 0
                ? s.Split(new[] { delimiter }, limit + 1, StringSplitOptions.None)
                : s.Split(new[] { delimiter }, StringSplitOptions.None);
        }
        else
        {
            parts = s.Split(new[] { delimiter }, StringSplitOptions.None);
        }
        var result = new List<StashValue>(parts.Length);
        foreach (var p in parts)
            result.Add(StashValue.FromObj(p));
        return result;
    }

    /// <summary>Returns the string repeated count times.</summary>
    /// <param name="s">The string</param>
    /// <param name="count">Number of repetitions (&gt;= 0)</param>
    /// <exception cref="ValueError">if `count` is negative</exception>
    /// <returns>Repeated string</returns>
    [StashFn]
    private static string Repeat(string s, long count)
    {
        if (count < 0)
            throw new ValueError("'str.repeat' count must be >= 0.");
        return string.Concat(Enumerable.Repeat(s, (int)count));
    }

    /// <summary>Returns the string with characters in reverse order.</summary>
    /// <param name="s">The string</param>
    /// <returns>Reversed string</returns>
    [StashFn]
    private static string Reverse(string s) => new string(s.Reverse().ToArray());

    /// <summary>Returns an array of single-character strings from the string.</summary>
    /// <param name="s">The string</param>
    /// <returns>Array of characters</returns>
    [StashFn]
    private static List<StashValue> Chars(string s)
    {
        var result = new List<StashValue>(s.Length);
        foreach (var c in s)
            result.Add(StashValue.FromObj(c.ToString()));
        return result;
    }

    /// <summary>Left-pads the string with padChar to at least width characters.</summary>
    /// <param name="s">The string</param>
    /// <param name="width">Target width</param>
    /// <param name="padChar">Optional pad character (default ' ')</param>
    /// <exception cref="ValueError">if `padChar` is not a single character</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Padded string</returns>
    [StashFn]
    private static string PadStart(string s, long width, params StashValue[] rest)
    {
        char fillChar = ' ';
        if (rest.Length > 0)
        {
            var fill = SvArgs.String(rest, 0, "str.padStart");
            if (fill.Length != 1)
                throw new ValueError("Third argument to 'str.padStart' must be a single-character string.");
            fillChar = fill[0];
        }
        return s.PadLeft((int)width, fillChar);
    }

    /// <summary>Right-pads the string with padChar to at least width characters.</summary>
    /// <param name="s">The string</param>
    /// <param name="width">Target width</param>
    /// <param name="padChar">Optional pad character (default ' ')</param>
    /// <exception cref="ValueError">if `padChar` is not a single character</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Padded string</returns>
    [StashFn]
    private static string PadEnd(string s, long width, params StashValue[] rest)
    {
        char fillChar = ' ';
        if (rest.Length > 0)
        {
            var fill = SvArgs.String(rest, 0, "str.padEnd");
            if (fill.Length != 1)
                throw new ValueError("Third argument to 'str.padEnd' must be a single-character string.");
            fillChar = fill[0];
        }
        return s.PadRight((int)width, fillChar);
    }

    /// <summary>Returns true if the string is non-empty and all characters are decimal digits.</summary>
    /// <param name="s">The string</param>
    /// <returns>true if all digits</returns>
    [StashFn]
    private static bool IsDigit(string s) => s.Length > 0 && s.All(char.IsDigit);

    /// <summary>Returns true if the string is non-empty and all characters are letters.</summary>
    /// <param name="s">The string</param>
    /// <returns>true if all letters</returns>
    [StashFn]
    private static bool IsAlpha(string s) => s.Length > 0 && s.All(char.IsLetter);

    /// <summary>Returns true if the string is non-empty and all characters are letters or digits.</summary>
    /// <param name="s">The string</param>
    /// <returns>true if all alphanumeric</returns>
    [StashFn]
    private static bool IsAlphaNum(string s) => s.Length > 0 && s.All(char.IsLetterOrDigit);

    /// <summary>Returns true if the string has letters and all are uppercase.</summary>
    /// <param name="s">The string</param>
    /// <returns>true if all uppercase letters</returns>
    [StashFn]
    private static bool IsUpper(string s)
    {
        var letters = s.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsUpper);
    }

    /// <summary>Returns true if the string has letters and all are lowercase.</summary>
    /// <param name="s">The string</param>
    /// <returns>true if all lowercase letters</returns>
    [StashFn]
    private static bool IsLower(string s)
    {
        var letters = s.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsLower);
    }

    /// <summary>Returns the Unicode code point of the first character in the string.</summary>
    /// <param name="s">A non-empty string</param>
    /// <exception cref="ValueError">if `s` is empty</exception>
    /// <returns>The Unicode code point as an integer</returns>
    [StashFn]
    private static long CharCode(string s)
    {
        if (s.Length == 0)
            throw new ValueError("Argument to 'str.charCode' must be a non-empty string.");
        return (long)s[0];
    }

    /// <summary>Returns a single-character string from a Unicode code point.</summary>
    /// <param name="n">The Unicode code point</param>
    /// <exception cref="ValueError">if `n` is outside the valid Unicode range (0–0x10FFFF)</exception>
    /// <returns>A string containing the character</returns>
    [StashFn]
    private static string FromCharCode(long n)
    {
        if (n < 0 || n > 0x10FFFF)
            throw new ValueError($"Code point {n} is out of the valid Unicode range (0\u20130x10FFFF).");
        return ((char)(int)n).ToString();
    }

    /// <summary>Returns true if the string is empty or contains only whitespace.</summary>
    /// <param name="s">The string</param>
    /// <returns>true if empty or whitespace</returns>
    [StashFn]
    private static bool IsEmpty(string s) => string.IsNullOrWhiteSpace(s);

    /// <summary>Deprecated. Use re.match.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match times out</exception>
    /// <returns>Matched string or null</returns>
    [StashFn(ReturnType = "string")]
    [StashDeprecated("re.match")]
    private static StashValue Match(string s, string pattern)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var m = regex.Match(s);
            return m.Success ? StashValue.FromObj(m.Value) : StashValue.Null;
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'str.match' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'str.match' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Deprecated. Use re.matchAll.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match times out</exception>
    /// <returns>Array of matched strings</returns>
    [StashFn]
    [StashDeprecated("re.matchAll")]
    private static List<StashValue> MatchAll(string s, string pattern)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var matches = regex.Matches(s);
            var result = new List<StashValue>(matches.Count);
            foreach (Match m in matches)
                result.Add(StashValue.FromObj(m.Value));
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'str.matchAll' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'str.matchAll' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Deprecated. Use re.test.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match times out</exception>
    /// <returns>true if the pattern matches</returns>
    [StashFn]
    [StashDeprecated("re.test")]
    private static bool IsMatch(string s, string pattern)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            return regex.IsMatch(s);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'str.isMatch' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'str.isMatch' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Deprecated. Use re.replace.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <param name="replacement">Replacement string</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match times out</exception>
    /// <returns>Modified string</returns>
    [StashFn]
    [StashDeprecated("re.replace")]
    private static string ReplaceRegex(string s, string pattern, string replacement)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            return regex.Replace(s, replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'str.replaceRegex' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'str.replaceRegex' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Deprecated. Use re.capture.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match times out</exception>
    /// <returns>RegexMatch struct or null</returns>
    [StashFn(ReturnType = "RegexMatch")]
    [StashDeprecated("re.capture")]
    private static StashValue Capture(string s, string pattern)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var m = regex.Match(s);
            if (!m.Success) return StashValue.Null;
            return StashValue.FromObj(RegexImpl.BuildRegexMatch(m));
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'str.capture' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'str.capture' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Deprecated. Use re.captureAll.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match times out</exception>
    /// <returns>Array of RegexMatch structs</returns>
    [StashFn]
    [StashDeprecated("re.captureAll")]
    private static List<StashValue> CaptureAll(string s, string pattern)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var matches = regex.Matches(s);
            var result = new List<StashValue>(matches.Count);
            foreach (Match m in matches)
                result.Add(StashValue.FromObj(RegexImpl.BuildRegexMatch(m)));
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'str.captureAll' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'str.captureAll' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Returns the count of non-overlapping occurrences of substring.</summary>
    /// <param name="s">The string</param>
    /// <param name="substring">The substring to count</param>
    /// <exception cref="ValueError">if `substring` is empty</exception>
    /// <returns>Count of occurrences</returns>
    [StashFn]
    private static long Count(string s, string substring)
    {
        if (substring.Length == 0)
            throw new ValueError("'str.count' substring must not be empty.");
        long count = 0;
        int idx = 0;
        while ((idx = s.IndexOf(substring, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += substring.Length;
        }
        return count;
    }

    /// <summary>Replaces {0}, {1}, ... placeholders in template with the given arguments.</summary>
    /// <param name="template">The template string</param>
    /// <param name="args">Values to substitute</param>
    /// <exception cref="TimeoutError">if the internal regex match times out</exception>
    /// <returns>Formatted string</returns>
    [StashFn]
    private static string Format(string template, params StashValue[] rest)
    {
        try
        {
            var placeholderRegex = new Regex(@"\{(\d+)\}", RegexOptions.None, TimeSpan.FromSeconds(5));
            string? error = null;
            string result = placeholderRegex.Replace(template, m =>
            {
                int index = int.Parse(m.Groups[1].Value);
                if (index >= rest.Length)
                {
                    error = $"'str.format' placeholder {{{index}}} is out of range (only {rest.Length} substitution argument(s) provided).";
                    return m.Value;
                }
                return RuntimeValues.Stringify(rest[index].ToObject());
            });
            if (error != null)
                throw new RuntimeError(error);
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'str.format' regex match timed out.");
        }
    }

    /// <summary>Returns the string with first character uppercase and the rest lowercase.</summary>
    /// <param name="s">The string</param>
    /// <returns>Capitalized string</returns>
    [StashFn]
    private static string Capitalize(string s)
    {
        if (s.Length == 0) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
    }

    /// <summary>Returns the string in title case (first letter of each word capitalized).</summary>
    /// <param name="s">The string</param>
    /// <returns>Title-cased string</returns>
    [StashFn]
    private static string Title(string s)
    {
        if (s.Length == 0) return s;
        var chars = s.ToCharArray();
        bool capitalizeNext = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i]))
                capitalizeNext = true;
            else if (capitalizeNext)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                capitalizeNext = false;
            }
            else
                chars[i] = char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }

    /// <summary>Splits the string into lines. Preserves empty lines.</summary>
    /// <param name="s">The string</param>
    /// <returns>Array of lines</returns>
    [StashFn]
    private static List<StashValue> Lines(string s)
    {
        var lines = s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var result = new List<StashValue>(lines.Length);
        foreach (var l in lines)
            result.Add(StashValue.FromObj(l));
        return result;
    }

    /// <summary>Splits the string into words by Unicode whitespace. Empty entries are dropped.</summary>
    /// <param name="s">The string</param>
    /// <returns>Array of words</returns>
    [StashFn]
    private static List<StashValue> Words(string s)
    {
        var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<StashValue>(words.Length);
        foreach (var w in words)
            result.Add(StashValue.FromObj(w));
        return result;
    }

    /// <summary>POSIX-shell-style word splitting. Honors single and double quotes; backslash escapes outside single quotes. Throws ValueError on unterminated quotes.</summary>
    /// <param name="s">The string to tokenize</param>
    /// <exception cref="ValueError">if the string contains an unterminated quote</exception>
    /// <returns>Array of tokens</returns>
    [StashFn]
    private static List<StashValue> ShellSplit(string s)
    {
        var result = new List<StashValue>();
        var token = new System.Text.StringBuilder();
        bool inToken = false;
        int i = 0;
        int len = s.Length;

        while (i < len)
        {
            char c = s[i];

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    result.Add(StashValue.FromObj(token.ToString()));
                    token.Clear();
                    inToken = false;
                }
                i++;
                continue;
            }

            if (c == '\'')
            {
                inToken = true;
                i++; // consume opening quote
                int start = i;
                while (i < len && s[i] != '\'') i++;
                if (i >= len)
                    throw new ValueError("unterminated quote in str.shellSplit");
                token.Append(s, start, i - start);
                i++; // consume closing quote
                continue;
            }

            if (c == '"')
            {
                inToken = true;
                i++; // consume opening quote
                while (i < len && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < len)
                    {
                        char next = s[i + 1];
                        if (next == '"' || next == '\\')
                        {
                            token.Append(next);
                            i += 2;
                            continue;
                        }
                    }
                    token.Append(s[i]);
                    i++;
                }
                if (i >= len)
                    throw new ValueError("unterminated quote in str.shellSplit");
                i++; // consume closing quote
                continue;
            }

            if (c == '\\' && i + 1 < len)
            {
                char next = s[i + 1];
                if (next == '"' || next == '\\' || next == '\'' || char.IsWhiteSpace(next))
                {
                    inToken = true;
                    token.Append(next);
                    i += 2;
                    continue;
                }
                // Unknown escape — keep backslash literal.
                inToken = true;
                token.Append(c);
                i++;
                continue;
            }

            inToken = true;
            token.Append(c);
            i++;
        }

        if (inToken)
            result.Add(StashValue.FromObj(token.ToString()));

        return result;
    }

    /// <summary>Truncates the string to maxLen characters, appending suffix (default '...') if truncated.</summary>
    /// <param name="s">The string</param>
    /// <param name="maxLen">Maximum length</param>
    /// <param name="suffix">Optional suffix (default '...')</param>
    /// <exception cref="ValueError">if `maxLen` is negative</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Truncated string</returns>
    [StashFn]
    private static string Truncate(string s, long maxLen, params StashValue[] rest)
    {
        if (maxLen < 0)
            throw new ValueError("'str.truncate' maxLen must be >= 0.");
        string suffix = rest.Length > 0 ? SvArgs.String(rest, 0, "str.truncate") : "...";
        if (s.Length <= (int)maxLen) return s;
        int cutLen = (int)maxLen - suffix.Length;
        if (cutLen < 0) cutLen = 0;
        return s.Substring(0, cutLen) + suffix;
    }

    /// <summary>Returns a URL-friendly slug: lowercase, hyphens for non-alphanumeric chars.</summary>
    /// <param name="s">The string</param>
    /// <returns>URL slug</returns>
    [StashFn]
    private static string Slug(string s)
    {
        var slug = s.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"[\s-]+", "-");
        slug = slug.Trim('-');
        return slug;
    }

    /// <summary>Wraps the string at word boundaries so no line exceeds width characters.</summary>
    /// <param name="s">The string</param>
    /// <param name="width">Maximum line width</param>
    /// <exception cref="ValueError">if `width` is not positive</exception>
    /// <returns>Wrapped string</returns>
    [StashFn]
    private static string Wrap(string s, long width)
    {
        if (width <= 0)
            throw new ValueError("'str.wrap' width must be > 0.");
        var result = new System.Text.StringBuilder();
        int w = (int)width;
        foreach (var paragraph in s.Split('\n'))
        {
            if (result.Length > 0) result.Append('\n');
            var paragraphWords = paragraph.Split(' ');
            int lineLen = 0;
            bool firstWord = true;
            foreach (var word in paragraphWords)
            {
                if (!firstWord && lineLen + 1 + word.Length > w)
                {
                    result.Append('\n');
                    lineLen = 0;
                    firstWord = true;
                }
                if (!firstWord) { result.Append(' '); lineLen++; }
                result.Append(word);
                lineLen += word.Length;
                firstWord = false;
            }
        }
        return result.ToString();
    }

    private static StashInstance BuildRegexMatch(Match m) => RegexImpl.BuildRegexMatch(m);
}
