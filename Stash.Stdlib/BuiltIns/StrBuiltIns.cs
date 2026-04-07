namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

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
public static class StrBuiltIns
{
    /// <summary>
    /// Registers all <c>str</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        // ── str namespace ───────────────────────────────────────────────
        var ns = new NamespaceBuilder("str");

        // str.upper(s) — Returns a copy of s with all characters converted to uppercase
        // using invariant culture rules.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("upper", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.upper");
            return StashValue.FromObj(s.ToUpperInvariant());
        },
            returnType: "string",
            documentation: "Returns the string converted to uppercase.\n@param s The string\n@return Uppercase string");

        // str.lower(s) — Returns a copy of s with all characters converted to lowercase
        // using invariant culture rules.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("lower", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.lower");
            return StashValue.FromObj(s.ToLowerInvariant());
        },
            returnType: "string",
            documentation: "Returns the string converted to lowercase.\n@param s The string\n@return Lowercase string");

        // str.trim(s) — Returns a copy of s with leading and trailing whitespace removed.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("trim", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.trim");
            return StashValue.FromObj(s.Trim());
        },
            returnType: "string",
            documentation: "Returns the string with leading and trailing whitespace removed.\n@param s The string\n@return Trimmed string");

        // str.trimStart(s) — Returns a copy of s with leading whitespace removed.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("trimStart", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.trimStart");
            return StashValue.FromObj(s.TrimStart());
        },
            returnType: "string",
            documentation: "Returns the string with leading whitespace removed.\n@param s The string\n@return Left-trimmed string");

        // str.trimEnd(s) — Returns a copy of s with trailing whitespace removed.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("trimEnd", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.trimEnd");
            return StashValue.FromObj(s.TrimEnd());
        },
            returnType: "string",
            documentation: "Returns the string with trailing whitespace removed.\n@param s The string\n@return Right-trimmed string");

        // str.contains(s, substring) — Returns true if s contains substring using ordinal
        // (case-sensitive) comparison, false otherwise.
        // Throws RuntimeError if either argument is not a string.
        ns.Function("contains", [Param("s", "string"), Param("substring", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.contains");
            string sub = SvArgs.String(args, 1, "str.contains");
            return StashValue.FromBool(s.Contains(sub, StringComparison.Ordinal));
        },
            returnType: "bool",
            documentation: "Returns true if the string contains the substring (case-sensitive).\n@param s The string\n@param substring The substring to search for\n@return true if found");

        // str.startsWith(s, prefix) — Returns true if s begins with prefix using ordinal
        // comparison, false otherwise.
        // Throws RuntimeError if either argument is not a string.
        ns.Function("startsWith", [Param("s", "string"), Param("prefix", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.startsWith");
            string prefix = SvArgs.String(args, 1, "str.startsWith");
            return StashValue.FromBool(s.StartsWith(prefix, StringComparison.Ordinal));
        },
            returnType: "bool",
            documentation: "Returns true if the string starts with the prefix (case-sensitive).\n@param s The string\n@param prefix The prefix\n@return true if starts with prefix");

        // str.endsWith(s, suffix) — Returns true if s ends with suffix using ordinal
        // comparison, false otherwise.
        // Throws RuntimeError if either argument is not a string.
        ns.Function("endsWith", [Param("s", "string"), Param("suffix", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.endsWith");
            string suffix = SvArgs.String(args, 1, "str.endsWith");
            return StashValue.FromBool(s.EndsWith(suffix, StringComparison.Ordinal));
        },
            returnType: "bool",
            documentation: "Returns true if the string ends with the suffix (case-sensitive).\n@param s The string\n@param suffix The suffix\n@return true if ends with suffix");

        // str.indexOf(s, substring) — Returns the zero-based index of the first occurrence of
        // substring in s using ordinal comparison, or -1 if not found.
        // Throws RuntimeError if either argument is not a string.
        ns.Function("indexOf", [Param("s", "string"), Param("substring", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.indexOf");
            string sub = SvArgs.String(args, 1, "str.indexOf");
            return StashValue.FromInt((long)s.IndexOf(sub, StringComparison.Ordinal));
        },
            returnType: "int",
            documentation: "Returns the index of the first occurrence of substring, or -1 if not found.\n@param s The string\n@param substring The substring\n@return Zero-based index or -1");

        // str.lastIndexOf(s, substring) — Returns the zero-based index of the last occurrence
        // of substring in s using ordinal comparison, or -1 if not found.
        // Throws RuntimeError if either argument is not a string.
        ns.Function("lastIndexOf", [Param("s", "string"), Param("substring", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.lastIndexOf");
            string sub = SvArgs.String(args, 1, "str.lastIndexOf");
            return StashValue.FromInt((long)s.LastIndexOf(sub, StringComparison.Ordinal));
        },
            returnType: "int",
            documentation: "Returns the index of the last occurrence of substring, or -1 if not found.\n@param s The string\n@param substring The substring\n@return Zero-based index or -1");

        // str.substring(s, start[, end]) — Returns the portion of s from index start (inclusive)
        // to end (exclusive). When end is omitted, returns the remainder of the string from start.
        // Indices must be within [0, len(s)]. Throws RuntimeError on out-of-range or wrong types.
        ns.Function("substring", [Param("s", "string"), Param("start", "int"), Param("end", "int")], (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
            {
                throw new RuntimeError("'str.substring' requires 2 or 3 arguments.");
            }

            var s = Args.String(args, 0, "str.substring");
            var start = Args.Long(args, 1, "str.substring");

            if (start < 0 || start > s.Length)
            {
                throw new RuntimeError($"'str.substring' start index {start} is out of range for string of length {s.Length}.");
            }

            if (args.Count == 3)
            {
                var end = Args.Long(args, 2, "str.substring");

                if (end < start || end > s.Length)
                {
                    throw new RuntimeError($"'str.substring' end index {end} is out of range.");
                }

                return s.Substring((int)start, (int)(end - start));
            }
            return s.Substring((int)start);
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Returns a portion of the string from start (inclusive) to end (exclusive).\n@param s The string\n@param start Start index (inclusive)\n@param end Optional end index (exclusive)\n@return Substring");

        // str.replace(s, old, new) — Returns a copy of s with the first occurrence of old
        // replaced by new using ordinal comparison. Returns s unchanged if old is not found.
        // Throws RuntimeError if any argument is not a string.
        ns.Function("replace", [Param("s", "string"), Param("old", "string"), Param("new", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.replace");
            var oldStr = Args.String(args, 1, "str.replace");
            var newStr = Args.String(args, 2, "str.replace");
            int idx = s.IndexOf(oldStr, StringComparison.Ordinal);
            if (idx < 0)
            {
                return s;
            }

            return s.Substring(0, idx) + newStr + s.Substring(idx + oldStr.Length);
        },
            returnType: "string",
            documentation: "Returns the string with the first occurrence of old replaced by new.\n@param s The string\n@param old The substring to replace\n@param new The replacement\n@return Modified string");

        // str.replaceAll(s, old, new) — Returns a copy of s with all occurrences of old
        // replaced by new using ordinal comparison.
        // Throws RuntimeError if any argument is not a string.
        ns.Function("replaceAll", [Param("s", "string"), Param("old", "string"), Param("new", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.replaceAll");
            var oldStr = Args.String(args, 1, "str.replaceAll");
            var newStr = Args.String(args, 2, "str.replaceAll");
            return s.Replace(oldStr, newStr, StringComparison.Ordinal);
        },
            returnType: "string",
            documentation: "Returns the string with all occurrences of old replaced by new.\n@param s The string\n@param old The substring to replace\n@param new The replacement\n@return Modified string");

        // str.split(s, delimiter) — Splits s into an array of substrings around each
        // occurrence of delimiter. Empty substrings are preserved.
        // Throws RuntimeError if either argument is not a string.
        ns.Function("split", [Param("s", "string"), Param("delimiter", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.split");
            var delimiter = Args.String(args, 1, "str.split");
            var parts = s.Split(new[] { delimiter }, StringSplitOptions.None);
            return parts.Select(p => (object?)p).ToList();
        },
            returnType: "array",
            documentation: "Splits the string by delimiter. Returns an array of strings.\n@param s The string\n@param delimiter The delimiter\n@return Array of substrings");

        // str.repeat(s, count) — Returns s repeated count times. Count must be >= 0.
        // Throws RuntimeError if s is not a string, count is not a non-negative integer.
        ns.Function("repeat", [Param("s", "string"), Param("count", "int")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.repeat");
            var count = Args.Long(args, 1, "str.repeat");

            if (count < 0)
            {
                throw new RuntimeError("'str.repeat' count must be >= 0.");
            }

            return string.Concat(Enumerable.Repeat(s, (int)count));
        },
            returnType: "string",
            documentation: "Returns the string repeated count times.\n@param s The string\n@param count Number of repetitions (>= 0)\n@return Repeated string");

        // str.reverse(s) — Returns a new string with the characters of s in reverse order.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("reverse", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.reverse");
            return new string(s.Reverse().ToArray());
        },
            returnType: "string",
            documentation: "Returns the string with characters in reverse order.\n@param s The string\n@return Reversed string");

        // str.chars(s) — Returns an array of single-character strings, one for each
        // character in s.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("chars", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.chars");
            return s.Select(c => (object?)c.ToString()).ToList();
        },
            returnType: "array",
            documentation: "Returns an array of single-character strings from the string.\n@param s The string\n@return Array of characters");

        // str.padStart(s, width[, padChar]) — Returns s left-padded with padChar (default " ")
        // to at least width characters. If s is already >= width, returns s unchanged.
        // Throws RuntimeError on invalid argument types.
        ns.Function("padStart", [Param("s", "string"), Param("width", "int"), Param("padChar", "string")], (_, args) =>
            RuntimeValues.PadString("str.padStart", args, padLeft: true),
            returnType: "string",
            isVariadic: true,
            documentation: "Left-pads the string with padChar to at least width characters.\n@param s The string\n@param width Target width\n@param padChar Optional pad character (default ' ')\n@return Padded string");

        // str.padEnd(s, width[, padChar]) — Returns s right-padded with padChar (default " ")
        // to at least width characters. If s is already >= width, returns s unchanged.
        // Throws RuntimeError on invalid argument types.
        ns.Function("padEnd", [Param("s", "string"), Param("width", "int"), Param("padChar", "string")], (_, args) =>
            RuntimeValues.PadString("str.padEnd", args, padLeft: false),
            returnType: "string",
            isVariadic: true,
            documentation: "Right-pads the string with padChar to at least width characters.\n@param s The string\n@param width Target width\n@param padChar Optional pad character (default ' ')\n@return Padded string");

        // str.isDigit(s) — Returns true if s is non-empty and every character is a decimal digit.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("isDigit", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.isDigit");
            return s.Length > 0 && s.All(char.IsDigit);
        },
            returnType: "bool",
            documentation: "Returns true if the string is non-empty and all characters are decimal digits.\n@param s The string\n@return true if all digits");

        // str.isAlpha(s) — Returns true if s is non-empty and every character is a letter.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("isAlpha", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.isAlpha");
            return s.Length > 0 && s.All(char.IsLetter);
        },
            returnType: "bool",
            documentation: "Returns true if the string is non-empty and all characters are letters.\n@param s The string\n@return true if all letters");

        // str.isAlphaNum(s) — Returns true if s is non-empty and every character is a letter
        // or decimal digit. Throws RuntimeError if the argument is not a string.
        ns.Function("isAlphaNum", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.isAlphaNum");
            return s.Length > 0 && s.All(char.IsLetterOrDigit);
        },
            returnType: "bool",
            documentation: "Returns true if the string is non-empty and all characters are letters or digits.\n@param s The string\n@return true if all alphanumeric");

        // str.isUpper(s) — Returns true if s contains at least one letter and all letters are
        // uppercase. Non-letter characters are ignored. Throws RuntimeError if not a string.
        ns.Function("isUpper", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.isUpper");
            var letters = s.Where(char.IsLetter).ToList();
            return letters.Count > 0 && letters.All(char.IsUpper);
        },
            returnType: "bool",
            documentation: "Returns true if the string has letters and all are uppercase.\n@param s The string\n@return true if all uppercase letters");

        // str.isLower(s) — Returns true if s contains at least one letter and all letters are
        // lowercase. Non-letter characters are ignored. Throws RuntimeError if not a string.
        ns.Function("isLower", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.isLower");
            var letters = s.Where(char.IsLetter).ToList();
            return letters.Count > 0 && letters.All(char.IsLower);
        },
            returnType: "bool",
            documentation: "Returns true if the string has letters and all are lowercase.\n@param s The string\n@return true if all lowercase letters");

        // str.isEmpty(s) — Returns true if s is empty or contains only whitespace characters.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("isEmpty", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.isEmpty");
            return string.IsNullOrWhiteSpace(s);
        },
            returnType: "bool",
            documentation: "Returns true if the string is empty or contains only whitespace.\n@param s The string\n@return true if empty or whitespace");

        // str.match(s, pattern) — Returns the first substring of s matching the regex pattern,
        // or null if no match is found. Uses a 5-second timeout.
        // Throws RuntimeError if either argument is not a string, the pattern is invalid,
        // or the match times out.
        ns.Function("match", [Param("s", "string"), Param("pattern", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.match");
            var pattern = Args.String(args, 1, "str.match");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                var m = regex.Match(s);
                return m.Success ? m.Value : null;
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.match' regex match timed out.");
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.match' invalid regex pattern: {ex.Message}");
            }
        },
            returnType: "string",
            documentation: "Returns the first regex match in the string, or null if none.\n@param s The string\n@param pattern Regex pattern\n@return Matched string or null");

        // str.matchAll(s, pattern) — Returns an array of all substrings in s that match the
        // regex pattern. Returns an empty array if no matches are found. Uses a 5-second timeout.
        // Throws RuntimeError if either argument is not a string, the pattern is invalid,
        // or the match times out.
        ns.Function("matchAll", [Param("s", "string"), Param("pattern", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.matchAll");
            var pattern = Args.String(args, 1, "str.matchAll");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                var matches = regex.Matches(s);
                var result = new List<object?>();
                foreach (Match m in matches)
                {
                    result.Add(m.Value);
                }

                return result;
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.matchAll' regex match timed out.");
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.matchAll' invalid regex pattern: {ex.Message}");
            }
        },
            returnType: "array",
            documentation: "Returns an array of all regex matches in the string.\n@param s The string\n@param pattern Regex pattern\n@return Array of matched strings");

        // str.isMatch(s, pattern) — Returns true if s has at least one match for the regex
        // pattern, false otherwise. Uses a 5-second timeout.
        // Throws RuntimeError if either argument is not a string, the pattern is invalid,
        // or the match times out.
        ns.Function("isMatch", [Param("s", "string"), Param("pattern", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.isMatch");
            var pattern = Args.String(args, 1, "str.isMatch");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                return regex.IsMatch(s);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.isMatch' regex match timed out.");
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.isMatch' invalid regex pattern: {ex.Message}");
            }
        },
            returnType: "bool",
            documentation: "Returns true if the string matches the regex pattern.\n@param s The string\n@param pattern Regex pattern\n@return true if the string matches");

        // str.replaceRegex(s, pattern, replacement) — Returns a copy of s with all matches of
        // the regex pattern replaced by replacement. Supports backreference syntax in replacement.
        // Uses a 5-second timeout. Throws RuntimeError if any argument is not a string, the
        // pattern is invalid, or the match times out.
        ns.Function("replaceRegex", [Param("s", "string"), Param("pattern", "string"), Param("replacement", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.replaceRegex");
            var pattern = Args.String(args, 1, "str.replaceRegex");
            var replacement = Args.String(args, 2, "str.replaceRegex");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                return regex.Replace(s, replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.replaceRegex' regex match timed out.");
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.replaceRegex' invalid regex pattern: {ex.Message}");
            }
        },
            returnType: "string",
            documentation: "Returns the string with all regex matches replaced by replacement.\n@param s The string\n@param pattern Regex pattern\n@param replacement Replacement string (backreferences supported)\n@return Modified string");

        // str.count(s, substring) — Returns the number of non-overlapping occurrences of
        // substring in s using ordinal comparison. Substring must not be empty.
        // Throws RuntimeError if either argument is not a string or if substring is empty.
        ns.Function("count", [Param("s", "string"), Param("substring", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.count");
            var sub = Args.String(args, 1, "str.count");
            if (sub.Length == 0)
            {
                throw new RuntimeError("'str.count' substring must not be empty.");
            }

            long count = 0;
            int idx = 0;
            while ((idx = s.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += sub.Length;
            }
            return count;
        },
            returnType: "int",
            documentation: "Returns the count of non-overlapping occurrences of substring.\n@param s The string\n@param substring The substring to count\n@return Count of occurrences");

        // str.format(template, ...args) — Returns a string formed by replacing {0}, {1}, ...
        // placeholders in template with the stringified values of the corresponding extra arguments.
        // At least 1 argument (the template) is required. Throws RuntimeError if the template
        // is not a string, a placeholder index is out of range, or the regex times out.
        ns.Function("format", [Param("template", "string"), Param("args", "any")], (_, args) =>
        {
            if (args.Count < 1)
            {
                throw new RuntimeError("'str.format' requires at least 1 argument.");
            }

            var template = Args.String(args, 0, "str.format");
            try
            {
                var placeholderRegex = new Regex(@"\{(\d+)\}", RegexOptions.None, TimeSpan.FromSeconds(5));
                string? error = null;
                string result = placeholderRegex.Replace(template, m =>
                {
                    int index = int.Parse(m.Groups[1].Value);
                    int argIndex = index + 1;
                    if (argIndex >= args.Count)
                    {
                        error = $"'str.format' placeholder {{{index}}} is out of range (only {args.Count - 1} substitution argument(s) provided).";
                        return m.Value;
                    }
                    return RuntimeValues.Stringify(args[argIndex]);
                });
                if (error != null)
                {
                    throw new RuntimeError(error);
                }

                return result;
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.format' regex match timed out.");
            }
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Replaces {0}, {1}, ... placeholders in template with the given arguments.\n@param template The template string\n@param args Values to substitute\n@return Formatted string");

        // ── Additional string utilities ──────────────────────────────────

        // str.capitalize(s) — Returns a copy of s with the first character uppercased and the
        // remainder lowercased. Returns s unchanged if it is empty.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("capitalize", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.capitalize");
            if (s.Length == 0)
            {
                return s;
            }

            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        },
            returnType: "string",
            documentation: "Returns the string with first character uppercase and the rest lowercase.\n@param s The string\n@return Capitalized string");

        // str.title(s) — Returns a copy of s in title case: the first letter of each
        // whitespace-delimited word is uppercased and the remaining letters are lowercased.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("title", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.title");
            if (s.Length == 0)
            {
                return s;
            }

            var chars = s.ToCharArray();
            bool capitalizeNext = true;
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsWhiteSpace(chars[i]))
                {
                    capitalizeNext = true;
                }
                else if (capitalizeNext)
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                    capitalizeNext = false;
                }
                else
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }
            }
            return new string(chars);
        },
            returnType: "string",
            documentation: "Returns the string in title case (first letter of each word capitalized).\n@param s The string\n@return Title-cased string");

        // str.lines(s) — Splits s into an array of lines on "\r\n", "\n", or "\r" boundaries.
        // Empty lines are preserved. Throws RuntimeError if the argument is not a string.
        ns.Function("lines", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.lines");
            var lines = s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            return lines.Select(l => (object?)l).ToList();
        },
            returnType: "array",
            documentation: "Splits the string into lines. Preserves empty lines.\n@param s The string\n@return Array of lines");

        // str.words(s) — Splits s into an array of words by whitespace, discarding empty
        // entries. Throws RuntimeError if the argument is not a string.
        ns.Function("words", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.words");
            var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return words.Select(w => (object?)w).ToList();
        },
            returnType: "array",
            documentation: "Splits the string into words by whitespace.\n@param s The string\n@return Array of words");

        // str.truncate(s, maxLen[, suffix]) — Returns s truncated to maxLen characters.
        // If s is longer than maxLen, the suffix (default "...") is appended to the cut string.
        // The total result length may be less than maxLen if suffix is longer than maxLen.
        // Throws RuntimeError on wrong types or if maxLen < 0.
        ns.Function("truncate", [Param("s", "string"), Param("maxLen", "int"), Param("suffix", "string")], (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
            {
                throw new RuntimeError("'str.truncate' requires 2 or 3 arguments.");
            }

            var s = Args.String(args, 0, "str.truncate");
            var maxLen = Args.Long(args, 1, "str.truncate");

            if (maxLen < 0)
            {
                throw new RuntimeError("'str.truncate' maxLen must be >= 0.");
            }

            string suffix = args.Count == 3 && args[2] is string sfx ? sfx : "...";
            if (s.Length <= (int)maxLen)
            {
                return s;
            }

            int cutLen = (int)maxLen - suffix.Length;
            if (cutLen < 0)
            {
                cutLen = 0;
            }

            return s.Substring(0, cutLen) + suffix;
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Truncates the string to maxLen characters, appending suffix (default '...') if truncated.\n@param s The string\n@param maxLen Maximum length\n@param suffix Optional suffix (default '...')\n@return Truncated string");

        // str.slug(s) — Returns a URL-friendly slug from s: lowercased, non-alphanumeric
        // characters removed, spaces and hyphens collapsed to a single hyphen, and leading
        // and trailing hyphens stripped.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("slug", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.slug");
            var slug = s.ToLowerInvariant();
            slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
            slug = Regex.Replace(slug, @"[\s-]+", "-");
            slug = slug.Trim('-');
            return slug;
        },
            returnType: "string",
            documentation: "Returns a URL-friendly slug: lowercase, hyphens for non-alphanumeric chars.\n@param s The string\n@return URL slug");

        // str.wrap(s, width) — Wraps s at word boundaries so that no line exceeds width
        // characters. Newlines already present in s are preserved as paragraph breaks.
        // Width must be > 0. Throws RuntimeError if s is not a string or width is not a
        // positive integer.
        ns.Function("wrap", [Param("s", "string"), Param("width", "int")], (_, args) =>
        {
            var s = Args.String(args, 0, "str.wrap");
            var width = Args.Long(args, 1, "str.wrap");

            if (width <= 0)
            {
                throw new RuntimeError("'str.wrap' width must be > 0.");
            }

            var result = new System.Text.StringBuilder();
            int w = (int)width;
            foreach (var paragraph in s.Split('\n'))
            {
                if (result.Length > 0)
                {
                    result.Append('\n');
                }

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
        },
            returnType: "string",
            documentation: "Wraps the string at word boundaries so no line exceeds width characters.\n@param s The string\n@param width Maximum line width\n@return Wrapped string");

        return ns.Build();
    }
}
