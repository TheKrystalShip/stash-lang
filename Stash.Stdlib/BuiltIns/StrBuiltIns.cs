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

        // str.trim(s[, chars]) — Returns a copy of s with leading and trailing whitespace (or
        // the specified chars) removed.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("trim", [Param("s", "string"), Param("chars", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'str.trim' requires 1 or 2 arguments.");
            string s = SvArgs.String(args, 0, "str.trim");
            if (args.Length == 2)
            {
                var chars = SvArgs.String(args, 1, "str.trim");
                return StashValue.FromObj(s.Trim(chars.ToCharArray()));
            }
            return StashValue.FromObj(s.Trim());
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Returns the string with leading and trailing whitespace (or specified chars) removed.\n@param s The string\n@param chars Optional string of characters to trim\n@return Trimmed string");

        // str.trimStart(s[, chars]) — Returns a copy of s with leading whitespace (or the
        // specified chars) removed.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("trimStart", [Param("s", "string"), Param("chars", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'str.trimStart' requires 1 or 2 arguments.");
            string s = SvArgs.String(args, 0, "str.trimStart");
            if (args.Length == 2)
            {
                var chars = SvArgs.String(args, 1, "str.trimStart");
                return StashValue.FromObj(s.TrimStart(chars.ToCharArray()));
            }
            return StashValue.FromObj(s.TrimStart());
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Returns the string with leading whitespace (or specified chars) removed.\n@param s The string\n@param chars Optional string of characters to trim\n@return Left-trimmed string");

        // str.trimEnd(s[, chars]) — Returns a copy of s with trailing whitespace (or the
        // specified chars) removed.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("trimEnd", [Param("s", "string"), Param("chars", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'str.trimEnd' requires 1 or 2 arguments.");
            string s = SvArgs.String(args, 0, "str.trimEnd");
            if (args.Length == 2)
            {
                var chars = SvArgs.String(args, 1, "str.trimEnd");
                return StashValue.FromObj(s.TrimEnd(chars.ToCharArray()));
            }
            return StashValue.FromObj(s.TrimEnd());
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Returns the string with trailing whitespace (or specified chars) removed.\n@param s The string\n@param chars Optional string of characters to trim\n@return Right-trimmed string");

        // str.contains(s, substring[, ignoreCase]) — Returns true if s contains substring.
        // When ignoreCase is true, uses case-insensitive ordinal comparison.
        // Throws RuntimeError if either string argument is not a string.
        ns.Function("contains", [Param("s", "string"), Param("substring", "string"), Param("ignoreCase", "bool")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.contains' requires 2 or 3 arguments.");
            string s = SvArgs.String(args, 0, "str.contains");
            string sub = SvArgs.String(args, 1, "str.contains");
            var comparison = args.Length == 3 && SvArgs.Bool(args, 2, "str.contains")
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return StashValue.FromBool(s.Contains(sub, comparison));
        },
            returnType: "bool",
            isVariadic: true,
            documentation: "Returns true if the string contains the substring.\n@param s The string\n@param substring The substring to search for\n@param ignoreCase Optional; when true, comparison is case-insensitive\n@return true if found");

        // str.startsWith(s, prefix[, ignoreCase]) — Returns true if s begins with prefix.
        // When ignoreCase is true, uses case-insensitive ordinal comparison.
        // Throws RuntimeError if either string argument is not a string.
        ns.Function("startsWith", [Param("s", "string"), Param("prefix", "string"), Param("ignoreCase", "bool")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.startsWith' requires 2 or 3 arguments.");
            string s = SvArgs.String(args, 0, "str.startsWith");
            string prefix = SvArgs.String(args, 1, "str.startsWith");
            var comparison = args.Length == 3 && SvArgs.Bool(args, 2, "str.startsWith")
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return StashValue.FromBool(s.StartsWith(prefix, comparison));
        },
            returnType: "bool",
            isVariadic: true,
            documentation: "Returns true if the string starts with the prefix.\n@param s The string\n@param prefix The prefix\n@param ignoreCase Optional; when true, comparison is case-insensitive\n@return true if starts with prefix");

        // str.endsWith(s, suffix[, ignoreCase]) — Returns true if s ends with suffix.
        // When ignoreCase is true, uses case-insensitive ordinal comparison.
        // Throws RuntimeError if either string argument is not a string.
        ns.Function("endsWith", [Param("s", "string"), Param("suffix", "string"), Param("ignoreCase", "bool")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.endsWith' requires 2 or 3 arguments.");
            string s = SvArgs.String(args, 0, "str.endsWith");
            string suffix = SvArgs.String(args, 1, "str.endsWith");
            var comparison = args.Length == 3 && SvArgs.Bool(args, 2, "str.endsWith")
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return StashValue.FromBool(s.EndsWith(suffix, comparison));
        },
            returnType: "bool",
            isVariadic: true,
            documentation: "Returns true if the string ends with the suffix.\n@param s The string\n@param suffix The suffix\n@param ignoreCase Optional; when true, comparison is case-insensitive\n@return true if ends with suffix");

        // str.indexOf(s, substring[, startIndex]) — Returns the zero-based index of the first
        // occurrence of substring in s at or after startIndex, or -1 if not found.
        // Throws RuntimeError if either string argument is not a string.
        ns.Function("indexOf", [Param("s", "string"), Param("substring", "string"), Param("startIndex", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.indexOf' requires 2 or 3 arguments.");
            string s = SvArgs.String(args, 0, "str.indexOf");
            string sub = SvArgs.String(args, 1, "str.indexOf");
            if (args.Length == 3)
            {
                int startIndex = (int)SvArgs.Long(args, 2, "str.indexOf");
                return StashValue.FromInt((long)s.IndexOf(sub, startIndex, StringComparison.Ordinal));
            }
            return StashValue.FromInt((long)s.IndexOf(sub, StringComparison.Ordinal));
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the index of the first occurrence of substring at or after startIndex, or -1 if not found.\n@param s The string\n@param substring The substring\n@param startIndex Optional start position for the search\n@return Zero-based index or -1");

        // str.lastIndexOf(s, substring[, startIndex]) — Returns the zero-based index of the
        // last occurrence of substring in s, searching backwards from startIndex, or -1 if
        // not found. Throws RuntimeError if either string argument is not a string.
        ns.Function("lastIndexOf", [Param("s", "string"), Param("substring", "string"), Param("startIndex", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.lastIndexOf' requires 2 or 3 arguments.");
            string s = SvArgs.String(args, 0, "str.lastIndexOf");
            string sub = SvArgs.String(args, 1, "str.lastIndexOf");
            if (args.Length == 3)
            {
                int startIndex = (int)SvArgs.Long(args, 2, "str.lastIndexOf");
                return StashValue.FromInt((long)s.LastIndexOf(sub, startIndex, StringComparison.Ordinal));
            }
            return StashValue.FromInt((long)s.LastIndexOf(sub, StringComparison.Ordinal));
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the index of the last occurrence of substring, searching backwards from startIndex, or -1 if not found.\n@param s The string\n@param substring The substring\n@param startIndex Optional position to begin searching backwards from\n@return Zero-based index or -1");

        // str.substring(s, start[, end]) — Returns the portion of s from index start (inclusive)
        // to end (exclusive). When end is omitted, returns the remainder of the string from start.
        // Indices must be within [0, len(s)]. Throws RuntimeError on out-of-range or wrong types.
        ns.Function("substring", [Param("s", "string"), Param("start", "int"), Param("end", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.substring' requires 2 or 3 arguments.");
            var s = SvArgs.String(args, 0, "str.substring");
            var start = SvArgs.Long(args, 1, "str.substring");
            if (start < 0 || start > s.Length)
                throw new RuntimeError($"'str.substring' start index {start} is out of range for string of length {s.Length}.", errorType: StashErrorTypes.IndexError);
            if (args.Length == 3)
            {
                var end = SvArgs.Long(args, 2, "str.substring");
                if (end < start || end > s.Length)
                    throw new RuntimeError($"'str.substring' end index {end} is out of range.", errorType: StashErrorTypes.IndexError);
                return StashValue.FromObj(s.Substring((int)start, (int)(end - start)));
            }
            return StashValue.FromObj(s.Substring((int)start));
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Returns a portion of the string from start (inclusive) to end (exclusive).\n@param s The string\n@param start Start index (inclusive)\n@param end Optional end index (exclusive)\n@return Substring");

        // str.replace(s, old, new[, count]) — Returns a copy of s with up to count occurrences
        // of old replaced by new using ordinal comparison. When count is omitted, replaces the
        // first occurrence only. Throws RuntimeError if any string argument is not a string.
        ns.Function("replace", [Param("s", "string"), Param("old", "string"), Param("new", "string"), Param("count", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 3 || args.Length > 4)
                throw new RuntimeError("'str.replace' requires 3 or 4 arguments.");
            var s = SvArgs.String(args, 0, "str.replace");
            var oldStr = SvArgs.String(args, 1, "str.replace");
            var newVal = SvArgs.String(args, 2, "str.replace");
            int count = args.Length == 4 ? (int)SvArgs.Long(args, 3, "str.replace") : 1;
            if (count <= 0) count = 1;
            int startIndex = 0;
            for (int i = 0; i < count; i++)
            {
                int pos = s.IndexOf(oldStr, startIndex, StringComparison.Ordinal);
                if (pos < 0) break;
                s = s.Substring(0, pos) + newVal + s.Substring(pos + oldStr.Length);
                startIndex = pos + newVal.Length;
            }
            return StashValue.FromObj(s);
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Returns the string with up to count occurrences of old replaced by new (default 1).\n@param s The string\n@param old The substring to replace\n@param new The replacement\n@param count Optional maximum number of replacements (default 1)\n@return Modified string");

        // str.replaceAll(s, old, new) — Returns a copy of s with all occurrences of old
        // replaced by new using ordinal comparison.
        // Throws RuntimeError if any argument is not a string.
        ns.Function("replaceAll", [Param("s", "string"), Param("old", "string"), Param("new", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.replaceAll");
            var oldStr = SvArgs.String(args, 1, "str.replaceAll");
            var newStr = SvArgs.String(args, 2, "str.replaceAll");
            return StashValue.FromObj(s.Replace(oldStr, newStr, StringComparison.Ordinal));
        },
            returnType: "string",
            documentation: "Returns the string with all occurrences of old replaced by new.\n@param s The string\n@param old The substring to replace\n@param new The replacement\n@return Modified string");

        // str.split(s, delimiter[, limit]) — Splits s into an array of substrings around each
        // occurrence of delimiter. When limit is positive, produces at most limit+1 pieces.
        // Empty substrings are preserved. Throws RuntimeError if string arguments are not strings.
        ns.Function("split", [Param("s", "string"), Param("delimiter", "string"), Param("limit", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.split' requires 2 or 3 arguments.");
            var s = SvArgs.String(args, 0, "str.split");
            var delimiter = SvArgs.String(args, 1, "str.split");
            string[] parts;
            if (args.Length == 3)
            {
                int limit = (int)SvArgs.Long(args, 2, "str.split");
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
            return StashValue.FromObj(result);
        },
            returnType: "array",
            isVariadic: true,
            documentation: "Splits the string by delimiter. When limit is positive, produces at most limit+1 pieces.\n@param s The string\n@param delimiter The delimiter\n@param limit Optional maximum number of splits\n@return Array of substrings");

        // str.repeat(s, count) — Returns s repeated count times. Count must be >= 0.
        // Throws RuntimeError if s is not a string, count is not a non-negative integer.
        ns.Function("repeat", [Param("s", "string"), Param("count", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.repeat");
            var count = SvArgs.Long(args, 1, "str.repeat");
            if (count < 0)
                throw new RuntimeError("'str.repeat' count must be >= 0.", errorType: StashErrorTypes.ValueError);
            return StashValue.FromObj(string.Concat(Enumerable.Repeat(s, (int)count)));
        },
            returnType: "string",
            documentation: "Returns the string repeated count times.\n@param s The string\n@param count Number of repetitions (>= 0)\n@return Repeated string");

        // str.reverse(s) — Returns a new string with the characters of s in reverse order.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("reverse", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.reverse");
            return StashValue.FromObj(new string(s.Reverse().ToArray()));
        },
            returnType: "string",
            documentation: "Returns the string with characters in reverse order.\n@param s The string\n@return Reversed string");

        // str.chars(s) — Returns an array of single-character strings, one for each
        // character in s.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("chars", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.chars");
            var result = new List<StashValue>(s.Length);
            foreach (var c in s)
                result.Add(StashValue.FromObj(c.ToString()));
            return StashValue.FromObj(result);
        },
            returnType: "array",
            documentation: "Returns an array of single-character strings from the string.\n@param s The string\n@return Array of characters");

        // str.padStart(s, width[, padChar]) — Returns s left-padded with padChar (default " ")
        // to at least width characters. If s is already >= width, returns s unchanged.
        // Throws RuntimeError on invalid argument types.
        ns.Function("padStart", [Param("s", "string"), Param("width", "int"), Param("padChar", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.padStart' requires 2 or 3 arguments.");
            var s = SvArgs.String(args, 0, "str.padStart");
            var length = SvArgs.Long(args, 1, "str.padStart");
            char fillChar = ' ';
            if (args.Length == 3)
            {
                var fill = SvArgs.String(args, 2, "str.padStart");
                if (fill.Length != 1)
                    throw new RuntimeError("Third argument to 'str.padStart' must be a single-character string.", errorType: StashErrorTypes.ValueError);
                fillChar = fill[0];
            }
            return StashValue.FromObj(s.PadLeft((int)length, fillChar));
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Left-pads the string with padChar to at least width characters.\n@param s The string\n@param width Target width\n@param padChar Optional pad character (default ' ')\n@return Padded string");

        // str.padEnd(s, width[, padChar]) — Returns s right-padded with padChar (default " ")
        // to at least width characters. If s is already >= width, returns s unchanged.
        // Throws RuntimeError on invalid argument types.
        ns.Function("padEnd", [Param("s", "string"), Param("width", "int"), Param("padChar", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.padEnd' requires 2 or 3 arguments.");
            var s = SvArgs.String(args, 0, "str.padEnd");
            var length = SvArgs.Long(args, 1, "str.padEnd");
            char fillChar = ' ';
            if (args.Length == 3)
            {
                var fill = SvArgs.String(args, 2, "str.padEnd");
                if (fill.Length != 1)
                    throw new RuntimeError("Third argument to 'str.padEnd' must be a single-character string.", errorType: StashErrorTypes.ValueError);
                fillChar = fill[0];
            }
            return StashValue.FromObj(s.PadRight((int)length, fillChar));
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Right-pads the string with padChar to at least width characters.\n@param s The string\n@param width Target width\n@param padChar Optional pad character (default ' ')\n@return Padded string");

        // str.isDigit(s) — Returns true if s is non-empty and every character is a decimal digit.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("isDigit", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.isDigit");
            return StashValue.FromBool(s.Length > 0 && s.All(char.IsDigit));
        },
            returnType: "bool",
            documentation: "Returns true if the string is non-empty and all characters are decimal digits.\n@param s The string\n@return true if all digits");

        // str.isAlpha(s) — Returns true if s is non-empty and every character is a letter.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("isAlpha", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.isAlpha");
            return StashValue.FromBool(s.Length > 0 && s.All(char.IsLetter));
        },
            returnType: "bool",
            documentation: "Returns true if the string is non-empty and all characters are letters.\n@param s The string\n@return true if all letters");

        // str.isAlphaNum(s) — Returns true if s is non-empty and every character is a letter
        // or decimal digit. Throws RuntimeError if the argument is not a string.
        ns.Function("isAlphaNum", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.isAlphaNum");
            return StashValue.FromBool(s.Length > 0 && s.All(char.IsLetterOrDigit));
        },
            returnType: "bool",
            documentation: "Returns true if the string is non-empty and all characters are letters or digits.\n@param s The string\n@return true if all alphanumeric");

        // str.isUpper(s) — Returns true if s contains at least one letter and all letters are
        // uppercase. Non-letter characters are ignored. Throws RuntimeError if not a string.
        ns.Function("isUpper", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.isUpper");
            var letters = s.Where(char.IsLetter).ToList();
            return StashValue.FromBool(letters.Count > 0 && letters.All(char.IsUpper));
        },
            returnType: "bool",
            documentation: "Returns true if the string has letters and all are uppercase.\n@param s The string\n@return true if all uppercase letters");

        // str.isLower(s) — Returns true if s contains at least one letter and all letters are
        // lowercase. Non-letter characters are ignored. Throws RuntimeError if not a string.
        ns.Function("isLower", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.isLower");
            var letters = s.Where(char.IsLetter).ToList();
            return StashValue.FromBool(letters.Count > 0 && letters.All(char.IsLower));
        },
            returnType: "bool",
            documentation: "Returns true if the string has letters and all are lowercase.\n@param s The string\n@return true if all lowercase letters");

        // str.charCode(s) — Returns the Unicode code point of the first character in the string.
        ns.Function("charCode", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "str.charCode");
            if (s.Length == 0)
                throw new RuntimeError("Argument to 'str.charCode' must be a non-empty string.", errorType: StashErrorTypes.ValueError);
            return StashValue.FromInt((long)s[0]);
        },
            returnType: "int",
            documentation: "Returns the Unicode code point of the first character in the string.\n@param s A non-empty string\n@return The Unicode code point as an integer");

        // str.fromCharCode(n) — Returns a single-character string from a Unicode code point.
        ns.Function("fromCharCode", [Param("n", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            long n = SvArgs.Long(args, 0, "str.fromCharCode");
            if (n < 0 || n > 0x10FFFF)
                throw new RuntimeError($"Code point {n} is out of the valid Unicode range (0–0x10FFFF).", errorType: StashErrorTypes.ValueError);
            return StashValue.FromObj(((char)(int)n).ToString());
        },
            returnType: "string",
            documentation: "Returns a single-character string from a Unicode code point.\n@param n The Unicode code point\n@return A string containing the character");

        // str.isEmpty(s) — Returns true if s is empty or contains only whitespace characters.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("isEmpty", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.isEmpty");
            return StashValue.FromBool(string.IsNullOrWhiteSpace(s));
        },
            returnType: "bool",
            documentation: "Returns true if the string is empty or contains only whitespace.\n@param s The string\n@return true if empty or whitespace");

        // str.match(s, pattern) — Deprecated. Use re.match.
        ns.Function("match", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.match");
            var pattern = SvArgs.String(args, 1, "str.match");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                var m = regex.Match(s);
                return m.Success ? StashValue.FromObj(m.Value) : StashValue.Null;
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.match' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.match' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
            }
        },
            returnType: "string",
            documentation: "Deprecated. Use re.match.",
            deprecation: new DeprecationInfo("re.match"));

        // str.matchAll(s, pattern) — Deprecated. Use re.matchAll.
        ns.Function("matchAll", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.matchAll");
            var pattern = SvArgs.String(args, 1, "str.matchAll");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                var matches = regex.Matches(s);
                var result = new List<StashValue>(matches.Count);
                foreach (Match m in matches)
                    result.Add(StashValue.FromObj(m.Value));
                return StashValue.FromObj(result);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.matchAll' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.matchAll' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
            }
        },
            returnType: "array",
            documentation: "Deprecated. Use re.matchAll.",
            deprecation: new DeprecationInfo("re.matchAll"));

        // str.isMatch(s, pattern) — Deprecated. Use re.test.
        ns.Function("isMatch", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.isMatch");
            var pattern = SvArgs.String(args, 1, "str.isMatch");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                return StashValue.FromBool(regex.IsMatch(s));
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.isMatch' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.isMatch' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
            }
        },
            returnType: "bool",
            documentation: "Deprecated. Use re.test.",
            deprecation: new DeprecationInfo("re.test"));

        // str.replaceRegex(s, pattern, replacement) — Deprecated. Use re.replace.
        ns.Function("replaceRegex", [Param("s", "string"), Param("pattern", "string"), Param("replacement", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.replaceRegex");
            var pattern = SvArgs.String(args, 1, "str.replaceRegex");
            var replacement = SvArgs.String(args, 2, "str.replaceRegex");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                return StashValue.FromObj(regex.Replace(s, replacement));
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.replaceRegex' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.replaceRegex' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
            }
        },
            returnType: "string",
            documentation: "Deprecated. Use re.replace.",
            deprecation: new DeprecationInfo("re.replace"));

        // str.capture(s, pattern) — Deprecated. Use re.capture.
        ns.Function("capture", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.capture");
            var pattern = SvArgs.String(args, 1, "str.capture");
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                var m = regex.Match(s);
                if (!m.Success) return StashValue.Null;
                return StashValue.FromObj(RegexImpl.BuildRegexMatch(m));
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.capture' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.capture' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
            }
        },
            returnType: "RegexMatch",
            documentation: "Deprecated. Use re.capture.",
            deprecation: new DeprecationInfo("re.capture"));

        // str.captureAll(s, pattern) — Deprecated. Use re.captureAll.
        ns.Function("captureAll", [Param("s", "string"), Param("pattern", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.captureAll");
            var pattern = SvArgs.String(args, 1, "str.captureAll");
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
                throw new RuntimeError("'str.captureAll' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
            }
            catch (ArgumentException ex)
            {
                throw new RuntimeError($"'str.captureAll' invalid regex pattern: {ex.Message}", errorType: StashErrorTypes.ParseError);
            }
        },
            returnType: "array",
            documentation: "Deprecated. Use re.captureAll.",
            deprecation: new DeprecationInfo("re.captureAll"));

        // str.count(s, substring) — Returns the number of non-overlapping occurrences of
        // substring in s using ordinal comparison. Substring must not be empty.
        // Throws RuntimeError if either argument is not a string or if substring is empty.
        ns.Function("count", [Param("s", "string"), Param("substring", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.count");
            var sub = SvArgs.String(args, 1, "str.count");
            if (sub.Length == 0)
                throw new RuntimeError("'str.count' substring must not be empty.", errorType: StashErrorTypes.ValueError);
            long count = 0;
            int idx = 0;
            while ((idx = s.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += sub.Length;
            }
            return StashValue.FromInt(count);
        },
            returnType: "int",
            documentation: "Returns the count of non-overlapping occurrences of substring.\n@param s The string\n@param substring The substring to count\n@return Count of occurrences");

        // str.format(template, ...args) — Returns a string formed by replacing {0}, {1}, ...
        // placeholders in template with the stringified values of the corresponding extra arguments.
        // At least 1 argument (the template) is required. Throws RuntimeError if the template
        // is not a string, a placeholder index is out of range, or the regex times out.
        ns.Function("format", [Param("template", "string"), Param("args", "any")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1)
                throw new RuntimeError("'str.format' requires at least 1 argument.");
            var template = SvArgs.String(args, 0, "str.format");
            var argsArray = args.ToArray();
            try
            {
                var placeholderRegex = new Regex(@"\{(\d+)\}", RegexOptions.None, TimeSpan.FromSeconds(5));
                string? error = null;
                string result = placeholderRegex.Replace(template, m =>
                {
                    int index = int.Parse(m.Groups[1].Value);
                    int argIndex = index + 1;
                    if (argIndex >= argsArray.Length)
                    {
                        error = $"'str.format' placeholder {{{index}}} is out of range (only {argsArray.Length - 1} substitution argument(s) provided).";
                        return m.Value;
                    }
                    return RuntimeValues.Stringify(argsArray[argIndex].ToObject());
                });
                if (error != null)
                    throw new RuntimeError(error);
                return StashValue.FromObj(result);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RuntimeError("'str.format' regex match timed out.", errorType: StashErrorTypes.TimeoutError);
            }
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Replaces {0}, {1}, ... placeholders in template with the given arguments.\n@param template The template string\n@param args Values to substitute\n@return Formatted string");

        // ── Additional string utilities ──────────────────────────────────

        // str.capitalize(s) — Returns a copy of s with the first character uppercased and the
        // remainder lowercased. Returns s unchanged if it is empty.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("capitalize", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.capitalize");
            if (s.Length == 0) return StashValue.FromObj(s);
            return StashValue.FromObj(char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant());
        },
            returnType: "string",
            documentation: "Returns the string with first character uppercase and the rest lowercase.\n@param s The string\n@return Capitalized string");

        // str.title(s) — Returns a copy of s in title case: the first letter of each
        // whitespace-delimited word is uppercased and the remaining letters are lowercased.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("title", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.title");
            if (s.Length == 0) return StashValue.FromObj(s);
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
            return StashValue.FromObj(new string(chars));
        },
            returnType: "string",
            documentation: "Returns the string in title case (first letter of each word capitalized).\n@param s The string\n@return Title-cased string");

        // str.lines(s) — Splits s into an array of lines on "\r\n", "\n", or "\r" boundaries.
        // Empty lines are preserved. Throws RuntimeError if the argument is not a string.
        ns.Function("lines", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.lines");
            var lines = s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var result = new List<StashValue>(lines.Length);
            foreach (var l in lines)
                result.Add(StashValue.FromObj(l));
            return StashValue.FromObj(result);
        },
            returnType: "array",
            documentation: "Splits the string into lines. Preserves empty lines.\n@param s The string\n@return Array of lines");

        // str.words(s) — Splits s into an array of words by whitespace, discarding empty
        // entries. Throws RuntimeError if the argument is not a string.
        ns.Function("words", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.words");
            var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<StashValue>(words.Length);
            foreach (var w in words)
                result.Add(StashValue.FromObj(w));
            return StashValue.FromObj(result);
        },
            returnType: "array",
            documentation: "Splits the string into words by whitespace.\n@param s The string\n@return Array of words");

        // str.truncate(s, maxLen[, suffix]) — Returns s truncated to maxLen characters.
        // If s is longer than maxLen, the suffix (default "...") is appended to the cut string.
        // The total result length may be less than maxLen if suffix is longer than maxLen.
        // Throws RuntimeError on wrong types or if maxLen < 0.
        ns.Function("truncate", [Param("s", "string"), Param("maxLen", "int"), Param("suffix", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'str.truncate' requires 2 or 3 arguments.");
            var s = SvArgs.String(args, 0, "str.truncate");
            var maxLen = SvArgs.Long(args, 1, "str.truncate");
            if (maxLen < 0)
                throw new RuntimeError("'str.truncate' maxLen must be >= 0.", errorType: StashErrorTypes.ValueError);
            string suffix = args.Length == 3 ? SvArgs.String(args, 2, "str.truncate") : "...";
            if (s.Length <= (int)maxLen) return StashValue.FromObj(s);
            int cutLen = (int)maxLen - suffix.Length;
            if (cutLen < 0) cutLen = 0;
            return StashValue.FromObj(s.Substring(0, cutLen) + suffix);
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Truncates the string to maxLen characters, appending suffix (default '...') if truncated.\n@param s The string\n@param maxLen Maximum length\n@param suffix Optional suffix (default '...')\n@return Truncated string");

        // str.slug(s) — Returns a URL-friendly slug from s: lowercased, non-alphanumeric
        // characters removed, spaces and hyphens collapsed to a single hyphen, and leading
        // and trailing hyphens stripped.
        // Throws RuntimeError if the argument is not a string.
        ns.Function("slug", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.slug");
            var slug = s.ToLowerInvariant();
            slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
            slug = Regex.Replace(slug, @"[\s-]+", "-");
            slug = slug.Trim('-');
            return StashValue.FromObj(slug);
        },
            returnType: "string",
            documentation: "Returns a URL-friendly slug: lowercase, hyphens for non-alphanumeric chars.\n@param s The string\n@return URL slug");

        // str.wrap(s, width) — Wraps s at word boundaries so that no line exceeds width
        // characters. Newlines already present in s are preserved as paragraph breaks.
        // Width must be > 0. Throws RuntimeError if s is not a string or width is not a
        // positive integer.
        ns.Function("wrap", [Param("s", "string"), Param("width", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "str.wrap");
            var width = SvArgs.Long(args, 1, "str.wrap");
            if (width <= 0)
                throw new RuntimeError("'str.wrap' width must be > 0.", errorType: StashErrorTypes.ValueError);
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
            return StashValue.FromObj(result.ToString());
        },
            returnType: "string",
            documentation: "Wraps the string at word boundaries so no line exceeds width characters.\n@param s The string\n@param width Maximum line width\n@return Wrapped string");

        return ns.Build();
    }

    private static StashInstance BuildRegexMatch(Match m) => RegexImpl.BuildRegexMatch(m);
}
