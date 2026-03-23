namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Interpreting.Types;

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
    public static void Register(Stash.Interpreting.Environment globals)
    {
        // ── str namespace ────────────────────────────────────────────────
        var str = new StashNamespace("str");

        // str.upper(s) — Returns a copy of s with all characters converted to uppercase
        // using invariant culture rules.
        // Throws RuntimeError if the argument is not a string.
        str.Define("upper", new BuiltInFunction("str.upper", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.upper' must be a string.");
            }

            return s.ToUpperInvariant();
        }));

        // str.lower(s) — Returns a copy of s with all characters converted to lowercase
        // using invariant culture rules.
        // Throws RuntimeError if the argument is not a string.
        str.Define("lower", new BuiltInFunction("str.lower", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.lower' must be a string.");
            }

            return s.ToLowerInvariant();
        }));

        // str.trim(s) — Returns a copy of s with leading and trailing whitespace removed.
        // Throws RuntimeError if the argument is not a string.
        str.Define("trim", new BuiltInFunction("str.trim", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trim' must be a string.");
            }

            return s.Trim();
        }));

        // str.trimStart(s) — Returns a copy of s with leading whitespace removed.
        // Throws RuntimeError if the argument is not a string.
        str.Define("trimStart", new BuiltInFunction("str.trimStart", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trimStart' must be a string.");
            }

            return s.TrimStart();
        }));

        // str.trimEnd(s) — Returns a copy of s with trailing whitespace removed.
        // Throws RuntimeError if the argument is not a string.
        str.Define("trimEnd", new BuiltInFunction("str.trimEnd", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trimEnd' must be a string.");
            }

            return s.TrimEnd();
        }));

        // str.contains(s, substring) — Returns true if s contains substring using ordinal
        // (case-sensitive) comparison, false otherwise.
        // Throws RuntimeError if either argument is not a string.
        str.Define("contains", new BuiltInFunction("str.contains", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.contains' must be a string.");
            }

            if (args[1] is not string sub)
            {
                throw new RuntimeError("Second argument to 'str.contains' must be a string.");
            }

            return s.Contains(sub, StringComparison.Ordinal);
        }));

        // str.startsWith(s, prefix) — Returns true if s begins with prefix using ordinal
        // comparison, false otherwise.
        // Throws RuntimeError if either argument is not a string.
        str.Define("startsWith", new BuiltInFunction("str.startsWith", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.startsWith' must be a string.");
            }

            if (args[1] is not string prefix)
            {
                throw new RuntimeError("Second argument to 'str.startsWith' must be a string.");
            }

            return s.StartsWith(prefix, StringComparison.Ordinal);
        }));

        // str.endsWith(s, suffix) — Returns true if s ends with suffix using ordinal
        // comparison, false otherwise.
        // Throws RuntimeError if either argument is not a string.
        str.Define("endsWith", new BuiltInFunction("str.endsWith", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.endsWith' must be a string.");
            }

            if (args[1] is not string suffix)
            {
                throw new RuntimeError("Second argument to 'str.endsWith' must be a string.");
            }

            return s.EndsWith(suffix, StringComparison.Ordinal);
        }));

        // str.indexOf(s, substring) — Returns the zero-based index of the first occurrence of
        // substring in s using ordinal comparison, or -1 if not found.
        // Throws RuntimeError if either argument is not a string.
        str.Define("indexOf", new BuiltInFunction("str.indexOf", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.indexOf' must be a string.");
            }

            if (args[1] is not string sub)
            {
                throw new RuntimeError("Second argument to 'str.indexOf' must be a string.");
            }

            return (long)s.IndexOf(sub, StringComparison.Ordinal);
        }));

        // str.lastIndexOf(s, substring) — Returns the zero-based index of the last occurrence
        // of substring in s using ordinal comparison, or -1 if not found.
        // Throws RuntimeError if either argument is not a string.
        str.Define("lastIndexOf", new BuiltInFunction("str.lastIndexOf", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.lastIndexOf' must be a string.");
            }

            if (args[1] is not string sub)
            {
                throw new RuntimeError("Second argument to 'str.lastIndexOf' must be a string.");
            }

            return (long)s.LastIndexOf(sub, StringComparison.Ordinal);
        }));

        // str.substring(s, start[, end]) — Returns the portion of s from index start (inclusive)
        // to end (exclusive). When end is omitted, returns the remainder of the string from start.
        // Indices must be within [0, len(s)]. Throws RuntimeError on out-of-range or wrong types.
        str.Define("substring", new BuiltInFunction("str.substring", -1, (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
            {
                throw new RuntimeError("'str.substring' requires 2 or 3 arguments.");
            }

            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.substring' must be a string.");
            }

            if (args[1] is not long start)
            {
                throw new RuntimeError("Second argument to 'str.substring' must be an integer.");
            }

            if (start < 0 || start > s.Length)
            {
                throw new RuntimeError($"'str.substring' start index {start} is out of range for string of length {s.Length}.");
            }

            if (args.Count == 3)
            {
                if (args[2] is not long end)
                {
                    throw new RuntimeError("Third argument to 'str.substring' must be an integer.");
                }

                if (end < start || end > s.Length)
                {
                    throw new RuntimeError($"'str.substring' end index {end} is out of range.");
                }

                return s.Substring((int)start, (int)(end - start));
            }
            return s.Substring((int)start);
        }));

        // str.replace(s, old, new) — Returns a copy of s with the first occurrence of old
        // replaced by new using ordinal comparison. Returns s unchanged if old is not found.
        // Throws RuntimeError if any argument is not a string.
        str.Define("replace", new BuiltInFunction("str.replace", 3, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.replace' must be a string.");
            }

            if (args[1] is not string oldStr)
            {
                throw new RuntimeError("Second argument to 'str.replace' must be a string.");
            }

            if (args[2] is not string newStr)
            {
                throw new RuntimeError("Third argument to 'str.replace' must be a string.");
            }

            int idx = s.IndexOf(oldStr, StringComparison.Ordinal);
            if (idx < 0)
            {
                return s;
            }

            return s.Substring(0, idx) + newStr + s.Substring(idx + oldStr.Length);
        }));

        // str.replaceAll(s, old, new) — Returns a copy of s with all occurrences of old
        // replaced by new using ordinal comparison.
        // Throws RuntimeError if any argument is not a string.
        str.Define("replaceAll", new BuiltInFunction("str.replaceAll", 3, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.replaceAll' must be a string.");
            }

            if (args[1] is not string oldStr)
            {
                throw new RuntimeError("Second argument to 'str.replaceAll' must be a string.");
            }

            if (args[2] is not string newStr)
            {
                throw new RuntimeError("Third argument to 'str.replaceAll' must be a string.");
            }

            return s.Replace(oldStr, newStr, StringComparison.Ordinal);
        }));

        // str.split(s, delimiter) — Splits s into an array of substrings around each
        // occurrence of delimiter. Empty substrings are preserved.
        // Throws RuntimeError if either argument is not a string.
        str.Define("split", new BuiltInFunction("str.split", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.split' must be a string.");
            }

            if (args[1] is not string delimiter)
            {
                throw new RuntimeError("Second argument to 'str.split' must be a string.");
            }

            var parts = s.Split(new[] { delimiter }, StringSplitOptions.None);
            return parts.Select(p => (object?)p).ToList();
        }));

        // str.repeat(s, count) — Returns s repeated count times. Count must be >= 0.
        // Throws RuntimeError if s is not a string, count is not a non-negative integer.
        str.Define("repeat", new BuiltInFunction("str.repeat", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.repeat' must be a string.");
            }

            if (args[1] is not long count)
            {
                throw new RuntimeError("Second argument to 'str.repeat' must be an integer.");
            }

            if (count < 0)
            {
                throw new RuntimeError("'str.repeat' count must be >= 0.");
            }

            return string.Concat(Enumerable.Repeat(s, (int)count));
        }));

        // str.reverse(s) — Returns a new string with the characters of s in reverse order.
        // Throws RuntimeError if the argument is not a string.
        str.Define("reverse", new BuiltInFunction("str.reverse", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.reverse' must be a string.");
            }

            return new string(s.Reverse().ToArray());
        }));

        // str.chars(s) — Returns an array of single-character strings, one for each
        // character in s.
        // Throws RuntimeError if the argument is not a string.
        str.Define("chars", new BuiltInFunction("str.chars", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.chars' must be a string.");
            }

            return s.Select(c => (object?)c.ToString()).ToList();
        }));

        // str.padStart(s, width[, padChar]) — Returns s left-padded with padChar (default " ")
        // to at least width characters. If s is already >= width, returns s unchanged.
        // Throws RuntimeError on invalid argument types.
        str.Define("padStart", new BuiltInFunction("str.padStart", -1, (_, args) =>
            RuntimeValues.PadString("str.padStart", args, padLeft: true)));

        // str.padEnd(s, width[, padChar]) — Returns s right-padded with padChar (default " ")
        // to at least width characters. If s is already >= width, returns s unchanged.
        // Throws RuntimeError on invalid argument types.
        str.Define("padEnd", new BuiltInFunction("str.padEnd", -1, (_, args) =>
            RuntimeValues.PadString("str.padEnd", args, padLeft: false)));

        // str.isDigit(s) — Returns true if s is non-empty and every character is a decimal digit.
        // Throws RuntimeError if the argument is not a string.
        str.Define("isDigit", new BuiltInFunction("str.isDigit", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isDigit' must be a string.");
            }

            return s.Length > 0 && s.All(char.IsDigit);
        }));

        // str.isAlpha(s) — Returns true if s is non-empty and every character is a letter.
        // Throws RuntimeError if the argument is not a string.
        str.Define("isAlpha", new BuiltInFunction("str.isAlpha", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isAlpha' must be a string.");
            }

            return s.Length > 0 && s.All(char.IsLetter);
        }));

        // str.isAlphaNum(s) — Returns true if s is non-empty and every character is a letter
        // or decimal digit. Throws RuntimeError if the argument is not a string.
        str.Define("isAlphaNum", new BuiltInFunction("str.isAlphaNum", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isAlphaNum' must be a string.");
            }

            return s.Length > 0 && s.All(char.IsLetterOrDigit);
        }));

        // str.isUpper(s) — Returns true if s contains at least one letter and all letters are
        // uppercase. Non-letter characters are ignored. Throws RuntimeError if not a string.
        str.Define("isUpper", new BuiltInFunction("str.isUpper", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isUpper' must be a string.");
            }

            var letters = s.Where(char.IsLetter).ToList();
            return letters.Count > 0 && letters.All(char.IsUpper);
        }));

        // str.isLower(s) — Returns true if s contains at least one letter and all letters are
        // lowercase. Non-letter characters are ignored. Throws RuntimeError if not a string.
        str.Define("isLower", new BuiltInFunction("str.isLower", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isLower' must be a string.");
            }

            var letters = s.Where(char.IsLetter).ToList();
            return letters.Count > 0 && letters.All(char.IsLower);
        }));

        // str.isEmpty(s) — Returns true if s is empty or contains only whitespace characters.
        // Throws RuntimeError if the argument is not a string.
        str.Define("isEmpty", new BuiltInFunction("str.isEmpty", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isEmpty' must be a string.");
            }

            return string.IsNullOrWhiteSpace(s);
        }));

        // str.match(s, pattern) — Returns the first substring of s matching the regex pattern,
        // or null if no match is found. Uses a 5-second timeout.
        // Throws RuntimeError if either argument is not a string, the pattern is invalid,
        // or the match times out.
        str.Define("match", new BuiltInFunction("str.match", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.match' must be a string.");
            }

            if (args[1] is not string pattern)
            {
                throw new RuntimeError("Second argument to 'str.match' must be a string.");
            }

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
        }));

        // str.matchAll(s, pattern) — Returns an array of all substrings in s that match the
        // regex pattern. Returns an empty array if no matches are found. Uses a 5-second timeout.
        // Throws RuntimeError if either argument is not a string, the pattern is invalid,
        // or the match times out.
        str.Define("matchAll", new BuiltInFunction("str.matchAll", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.matchAll' must be a string.");
            }

            if (args[1] is not string pattern)
            {
                throw new RuntimeError("Second argument to 'str.matchAll' must be a string.");
            }

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
        }));

        // str.isMatch(s, pattern) — Returns true if s has at least one match for the regex
        // pattern, false otherwise. Uses a 5-second timeout.
        // Throws RuntimeError if either argument is not a string, the pattern is invalid,
        // or the match times out.
        str.Define("isMatch", new BuiltInFunction("str.isMatch", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isMatch' must be a string.");
            }

            if (args[1] is not string pattern)
            {
                throw new RuntimeError("Second argument to 'str.isMatch' must be a string.");
            }

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
        }));

        // str.replaceRegex(s, pattern, replacement) — Returns a copy of s with all matches of
        // the regex pattern replaced by replacement. Supports backreference syntax in replacement.
        // Uses a 5-second timeout. Throws RuntimeError if any argument is not a string, the
        // pattern is invalid, or the match times out.
        str.Define("replaceRegex", new BuiltInFunction("str.replaceRegex", 3, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.replaceRegex' must be a string.");
            }

            if (args[1] is not string pattern)
            {
                throw new RuntimeError("Second argument to 'str.replaceRegex' must be a string.");
            }

            if (args[2] is not string replacement)
            {
                throw new RuntimeError("Third argument to 'str.replaceRegex' must be a string.");
            }

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
        }));

        // str.count(s, substring) — Returns the number of non-overlapping occurrences of
        // substring in s using ordinal comparison. Substring must not be empty.
        // Throws RuntimeError if either argument is not a string or if substring is empty.
        str.Define("count", new BuiltInFunction("str.count", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.count' must be a string.");
            }

            if (args[1] is not string sub)
            {
                throw new RuntimeError("Second argument to 'str.count' must be a string.");
            }

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
        }));

        // str.format(template, ...args) — Returns a string formed by replacing {0}, {1}, ...
        // placeholders in template with the stringified values of the corresponding extra arguments.
        // At least 1 argument (the template) is required. Throws RuntimeError if the template
        // is not a string, a placeholder index is out of range, or the regex times out.
        str.Define("format", new BuiltInFunction("str.format", -1, (_, args) =>
        {
            if (args.Count < 1)
            {
                throw new RuntimeError("'str.format' requires at least 1 argument.");
            }

            if (args[0] is not string template)
            {
                throw new RuntimeError("First argument to 'str.format' must be a string.");
            }

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
        }));

        // ── Additional string utilities ──────────────────────────────────

        // str.capitalize(s) — Returns a copy of s with the first character uppercased and the
        // remainder lowercased. Returns s unchanged if it is empty.
        // Throws RuntimeError if the argument is not a string.
        str.Define("capitalize", new BuiltInFunction("str.capitalize", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.capitalize' must be a string.");
            }

            if (s.Length == 0)
            {
                return s;
            }

            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }));

        // str.title(s) — Returns a copy of s in title case: the first letter of each
        // whitespace-delimited word is uppercased and the remaining letters are lowercased.
        // Throws RuntimeError if the argument is not a string.
        str.Define("title", new BuiltInFunction("str.title", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.title' must be a string.");
            }

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
        }));

        // str.lines(s) — Splits s into an array of lines on "\r\n", "\n", or "\r" boundaries.
        // Empty lines are preserved. Throws RuntimeError if the argument is not a string.
        str.Define("lines", new BuiltInFunction("str.lines", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.lines' must be a string.");
            }

            var lines = s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            return lines.Select(l => (object?)l).ToList();
        }));

        // str.words(s) — Splits s into an array of words by whitespace, discarding empty
        // entries. Throws RuntimeError if the argument is not a string.
        str.Define("words", new BuiltInFunction("str.words", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.words' must be a string.");
            }

            var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return words.Select(w => (object?)w).ToList();
        }));

        // str.truncate(s, maxLen[, suffix]) — Returns s truncated to maxLen characters.
        // If s is longer than maxLen, the suffix (default "...") is appended to the cut string.
        // The total result length may be less than maxLen if suffix is longer than maxLen.
        // Throws RuntimeError on wrong types or if maxLen < 0.
        str.Define("truncate", new BuiltInFunction("str.truncate", -1, (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
            {
                throw new RuntimeError("'str.truncate' requires 2 or 3 arguments.");
            }

            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.truncate' must be a string.");
            }

            if (args[1] is not long maxLen)
            {
                throw new RuntimeError("Second argument to 'str.truncate' must be an integer.");
            }

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
        }));

        // str.slug(s) — Returns a URL-friendly slug from s: lowercased, non-alphanumeric
        // characters removed, spaces and hyphens collapsed to a single hyphen, and leading
        // and trailing hyphens stripped.
        // Throws RuntimeError if the argument is not a string.
        str.Define("slug", new BuiltInFunction("str.slug", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.slug' must be a string.");
            }

            var slug = s.ToLowerInvariant();
            slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
            slug = Regex.Replace(slug, @"[\s-]+", "-");
            slug = slug.Trim('-');
            return slug;
        }));

        // str.wrap(s, width) — Wraps s at word boundaries so that no line exceeds width
        // characters. Newlines already present in s are preserved as paragraph breaks.
        // Width must be > 0. Throws RuntimeError if s is not a string or width is not a
        // positive integer.
        str.Define("wrap", new BuiltInFunction("str.wrap", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.wrap' must be a string.");
            }

            if (args[1] is not long width)
            {
                throw new RuntimeError("Second argument to 'str.wrap' must be an integer.");
            }

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
        }));

        globals.Define("str", str);
    }
}
