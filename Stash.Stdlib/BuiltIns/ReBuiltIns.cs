namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

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
    /// <returns>Matched string or null</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Match(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var s = SvArgs.String(args, 0, "re.match");
        var pattern = SvArgs.String(args, 1, "re.match");
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var m = regex.Match(s);
            return m.Success ? StashValue.FromObj(m.Value) : StashValue.Null;
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuntimeError("'re.match' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
        }
        catch (ArgumentException ex)
        {
            throw new RuntimeError($"'re.match' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Returns an array of all regex matches in the string.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <returns>Array of matched strings</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue MatchAll(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var s = SvArgs.String(args, 0, "re.matchAll");
        var pattern = SvArgs.String(args, 1, "re.matchAll");
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var matches = regex.Matches(s);
            var result = new List<StashValue>(matches.Count);
            foreach (Match match in matches)
                result.Add(StashValue.FromObj(match.Value));
            return StashValue.FromObj(result);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuntimeError("'re.matchAll' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
        }
        catch (ArgumentException ex)
        {
            throw new RuntimeError($"'re.matchAll' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Returns true if the string matches the regex pattern.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <returns>true if the string matches</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Test(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var s = SvArgs.String(args, 0, "re.test");
        var pattern = SvArgs.String(args, 1, "re.test");
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            return StashValue.FromBool(regex.IsMatch(s));
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuntimeError("'re.test' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
        }
        catch (ArgumentException ex)
        {
            throw new RuntimeError($"'re.test' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Returns the string with all regex matches replaced by replacement.</summary>
    /// <param name="s">The string</param>
    /// <param name="pattern">Regex pattern</param>
    /// <param name="replacement">Replacement string (backreferences supported)</param>
    /// <returns>Modified string</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Replace(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var s = SvArgs.String(args, 0, "re.replace");
        var pattern = SvArgs.String(args, 1, "re.replace");
        var replacement = SvArgs.String(args, 2, "re.replace");
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            return StashValue.FromObj(regex.Replace(s, replacement));
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuntimeError("'re.replace' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
        }
        catch (ArgumentException ex)
        {
            throw new RuntimeError($"'re.replace' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Returns a RegexMatch struct for the first regex match with capture groups, or null if none.</summary>
    /// <param name="s">The string to search</param>
    /// <param name="pattern">Regex pattern (supports named groups via (?&lt;name&gt;...) syntax)</param>
    /// <returns>RegexMatch struct or null</returns>
    [StashFn(Raw = true, ReturnType = "RegexMatch")]
    private static StashValue Capture(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var s = SvArgs.String(args, 0, "re.capture");
        var pattern = SvArgs.String(args, 1, "re.capture");
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var m = regex.Match(s);
            if (!m.Success) return StashValue.Null;
            return StashValue.FromObj(RegexImpl.BuildRegexMatch(m));
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuntimeError("'re.capture' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
        }
        catch (ArgumentException ex)
        {
            throw new RuntimeError($"'re.capture' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Returns an array of RegexMatch structs for all regex matches with capture groups.</summary>
    /// <param name="s">The string to search</param>
    /// <param name="pattern">Regex pattern (supports named groups via (?&lt;name&gt;...) syntax)</param>
    /// <returns>Array of RegexMatch structs</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue CaptureAll(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var s = SvArgs.String(args, 0, "re.captureAll");
        var pattern = SvArgs.String(args, 1, "re.captureAll");
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
            var matches = regex.Matches(s);
            var result = new List<StashValue>(matches.Count);
            foreach (Match m in matches)
                result.Add(StashValue.FromObj(RegexImpl.BuildRegexMatch(m)));
            return StashValue.FromObj(result);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuntimeError("'re.captureAll' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
        }
        catch (ArgumentException ex)
        {
            throw new RuntimeError($"'re.captureAll' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
        }
    }
}
