namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'str' namespace built-in functions.
/// </summary>
public static class StrBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        // ── str namespace ────────────────────────────────────────────────
        var str = new StashNamespace("str");

        str.Define("upper", new BuiltInFunction("str.upper", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.upper' must be a string.");
            }

            return s.ToUpperInvariant();
        }));

        str.Define("lower", new BuiltInFunction("str.lower", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.lower' must be a string.");
            }

            return s.ToLowerInvariant();
        }));

        str.Define("trim", new BuiltInFunction("str.trim", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trim' must be a string.");
            }

            return s.Trim();
        }));

        str.Define("trimStart", new BuiltInFunction("str.trimStart", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trimStart' must be a string.");
            }

            return s.TrimStart();
        }));

        str.Define("trimEnd", new BuiltInFunction("str.trimEnd", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trimEnd' must be a string.");
            }

            return s.TrimEnd();
        }));

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

        str.Define("reverse", new BuiltInFunction("str.reverse", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.reverse' must be a string.");
            }

            return new string(s.Reverse().ToArray());
        }));

        str.Define("chars", new BuiltInFunction("str.chars", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.chars' must be a string.");
            }

            return s.Select(c => (object?)c.ToString()).ToList();
        }));

        str.Define("padStart", new BuiltInFunction("str.padStart", -1, (_, args) =>
            RuntimeValues.PadString("str.padStart", args, padLeft: true)));

        str.Define("padEnd", new BuiltInFunction("str.padEnd", -1, (_, args) =>
            RuntimeValues.PadString("str.padEnd", args, padLeft: false)));

        str.Define("isDigit", new BuiltInFunction("str.isDigit", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isDigit' must be a string.");
            }

            return s.Length > 0 && s.All(char.IsDigit);
        }));

        str.Define("isAlpha", new BuiltInFunction("str.isAlpha", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isAlpha' must be a string.");
            }

            return s.Length > 0 && s.All(char.IsLetter);
        }));

        str.Define("isAlphaNum", new BuiltInFunction("str.isAlphaNum", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isAlphaNum' must be a string.");
            }

            return s.Length > 0 && s.All(char.IsLetterOrDigit);
        }));

        str.Define("isUpper", new BuiltInFunction("str.isUpper", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isUpper' must be a string.");
            }

            var letters = s.Where(char.IsLetter).ToList();
            return letters.Count > 0 && letters.All(char.IsUpper);
        }));

        str.Define("isLower", new BuiltInFunction("str.isLower", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isLower' must be a string.");
            }

            var letters = s.Where(char.IsLetter).ToList();
            return letters.Count > 0 && letters.All(char.IsLower);
        }));

        str.Define("isEmpty", new BuiltInFunction("str.isEmpty", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.isEmpty' must be a string.");
            }

            return string.IsNullOrWhiteSpace(s);
        }));

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

        str.Define("lines", new BuiltInFunction("str.lines", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.lines' must be a string.");
            }

            var lines = s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            return lines.Select(l => (object?)l).ToList();
        }));

        str.Define("words", new BuiltInFunction("str.words", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.words' must be a string.");
            }

            var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return words.Select(w => (object?)w).ToList();
        }));

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
