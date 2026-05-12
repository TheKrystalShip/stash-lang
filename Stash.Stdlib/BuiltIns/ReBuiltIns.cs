namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>re</c> namespace built-in functions for regular expressions.
/// </summary>
[StashNamespace]
public static partial class ReBuiltIns
{
    [StashStruct]
    public sealed record RegexGroup
    {
        public string Value { get; init; } = "";
        public long Index { get; init; }
        public long Length { get; init; }
        public string Name { get; init; } = "";
    }

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

    /// <summary>Returns the first regex match in the string, or null if none.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match does not complete within the configured timeout</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Matched string or null</returns>
    [StashFn(ReturnType = "string")]
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
            throw new TimeoutError("'re.match' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'re.match' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Returns an array of all regex matches in the string.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match does not complete within the configured timeout</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Array of matched strings</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> MatchAll(string s, string pattern)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var matches = regex.Matches(s);
            var result = new List<StashValue>(matches.Count);
            foreach (Match match in matches)
                result.Add(StashValue.FromObj(match.Value));
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'re.matchAll' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'re.matchAll' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Returns true if the string matches the regex pattern.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match does not complete within the configured timeout</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>true if the string matches</returns>
    [StashFn]
    private static bool Test(string s, string pattern)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            return regex.IsMatch(s);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'re.test' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'re.test' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Returns the string with all regex matches replaced by replacement.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <param name="replacement">Replacement string (backreferences supported)</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match does not complete within the configured timeout</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Modified string</returns>
    [StashFn]
    private static string Replace(string s, string pattern, string replacement)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            return regex.Replace(s, replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new TimeoutError("'re.replace' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'re.replace' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Returns a RegexMatch struct for the first regex match with capture groups, or null if none.</summary>
    /// <param name="s">The string to search</param>
    /// <param name="pattern">Regex pattern (supports named groups via (?&lt;name&gt;...) syntax)</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match does not complete within the configured timeout</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>RegexMatch struct or null</returns>
    [StashFn(ReturnType = "RegexMatch")]
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
            throw new TimeoutError("'re.capture' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'re.capture' invalid regex pattern: {ex.Message}");
        }
    }

    /// <summary>Returns an array of RegexMatch structs for all regex matches with capture groups.</summary>
    /// <param name="s">The string to search</param>
    /// <param name="pattern">Regex pattern (supports named groups via (?&lt;name&gt;...) syntax)</param>
    /// <exception cref="ParseError">if the regex pattern is invalid</exception>
    /// <exception cref="TimeoutError">if the regex match does not complete within the configured timeout</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>Array of RegexMatch structs</returns>
    [StashFn(ReturnType = "array")]
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
            throw new TimeoutError("'re.captureAll' regex match timed out.");
        }
        catch (ArgumentException ex)
        {
            throw new ParseError($"'re.captureAll' invalid regex pattern: {ex.Message}");
        }
    }
}
