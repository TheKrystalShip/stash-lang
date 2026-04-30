namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>re</c> namespace built-in functions for regular expressions.
/// </summary>
public static class ReBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("re");

        ns.Struct("RegexGroup", [
            new BuiltInField("value", "string"),
            new BuiltInField("index", "int"),
            new BuiltInField("length", "int"),
            new BuiltInField("name", "string"),
        ]);

        ns.Struct("RegexMatch", [
            new BuiltInField("value", "string"),
            new BuiltInField("index", "int"),
            new BuiltInField("length", "int"),
            new BuiltInField("groups", "array"),
            new BuiltInField("namedGroups", "dict"),
        ]);

        // re.match(s, pattern) — Returns the first substring matching the regex, or null.
        ns.Function("match", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "string",
            documentation: "Returns the first regex match in the string, or null if none.\n@param s The string\n@param pattern Regex pattern\n@return Matched string or null");

        // re.matchAll(s, pattern) — Returns an array of all substrings matching the regex.
        ns.Function("matchAll", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "array",
            documentation: "Returns an array of all regex matches in the string.\n@param s The string\n@param pattern Regex pattern\n@return Array of matched strings");

        // re.test(s, pattern) — Returns true if the string contains at least one match.
        ns.Function("test", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "bool",
            documentation: "Returns true if the string matches the regex pattern.\n@param s The string\n@param pattern Regex pattern\n@return true if the string matches");

        // re.replace(s, pattern, replacement) — Replaces all matches with replacement.
        ns.Function("replace", [Param("s", "string"), Param("pattern", "string"), Param("replacement", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "string",
            documentation: "Returns the string with all regex matches replaced by replacement.\n@param s The string\n@param pattern Regex pattern\n@param replacement Replacement string (backreferences supported)\n@return Modified string");

        // re.capture(s, pattern) — Returns a RegexMatch struct for the first match, or null.
        ns.Function("capture", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "RegexMatch",
            documentation: "Returns a RegexMatch struct for the first regex match with capture groups, or null if none.\n@param s The string to search\n@param pattern Regex pattern (supports named groups via (?<name>...) syntax)\n@return RegexMatch struct or null");

        // re.captureAll(s, pattern) — Returns an array of RegexMatch structs for all matches.
        ns.Function("captureAll", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "array",
            documentation: "Returns an array of RegexMatch structs for all regex matches with capture groups.\n@param s The string to search\n@param pattern Regex pattern (supports named groups via (?<name>...) syntax)\n@return Array of RegexMatch structs");

        return ns.Build();
    }
}
