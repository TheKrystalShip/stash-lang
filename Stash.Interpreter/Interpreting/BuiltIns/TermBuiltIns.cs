namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Interpreting.Types;

/// <summary>Registers the <c>term</c> namespace providing terminal formatting and introspection functions.</summary>
public static class TermBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var term = new StashNamespace("term");

        // Color constants
        term.Define("BLACK", "black");
        term.Define("RED", "red");
        term.Define("GREEN", "green");
        term.Define("YELLOW", "yellow");
        term.Define("BLUE", "blue");
        term.Define("MAGENTA", "magenta");
        term.Define("CYAN", "cyan");
        term.Define("WHITE", "white");
        term.Define("GRAY", "gray");

        term.Define("color", new BuiltInFunction("term.color", 2, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("First argument to 'term.color' must be a string.");
            }

            if (args[1] is not string color)
            {
                throw new RuntimeError("Second argument to 'term.color' must be a string.");
            }

            string code = color.ToLowerInvariant() switch
            {
                "black" => "30",
                "red" => "31",
                "green" => "32",
                "yellow" => "33",
                "blue" => "34",
                "magenta" => "35",
                "cyan" => "36",
                "white" => "37",
                "gray" or "grey" => "90",
                _ => throw new RuntimeError($"Unknown color '{color}'. Use term color constants: term.BLACK, term.RED, term.GREEN, term.YELLOW, term.BLUE, term.MAGENTA, term.CYAN, term.WHITE, term.GRAY.")
            };
            return $"\x1b[{code}m{text}\x1b[0m";
        }));

        term.Define("bold", new BuiltInFunction("term.bold", 1, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("Argument to 'term.bold' must be a string.");
            }

            return $"\x1b[1m{text}\x1b[0m";
        }));

        term.Define("dim", new BuiltInFunction("term.dim", 1, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("Argument to 'term.dim' must be a string.");
            }

            return $"\x1b[2m{text}\x1b[0m";
        }));

        term.Define("underline", new BuiltInFunction("term.underline", 1, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("Argument to 'term.underline' must be a string.");
            }

            return $"\x1b[4m{text}\x1b[0m";
        }));

        term.Define("style", new BuiltInFunction("term.style", 2, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("First argument to 'term.style' must be a string.");
            }

            if (args[1] is not StashDictionary opts)
            {
                throw new RuntimeError("Second argument to 'term.style' must be a dict.");
            }

            var codes = new List<string>();

            var boldVal = opts.Get("bold");
            if (boldVal is true)
            {
                codes.Add("1");
            }

            var dimVal = opts.Get("dim");
            if (dimVal is true)
            {
                codes.Add("2");
            }

            var underlineVal = opts.Get("underline");
            if (underlineVal is true)
            {
                codes.Add("4");
            }

            var colorVal = opts.Get("color");
            if (colorVal is string color)
            {
                string colorCode = color.ToLowerInvariant() switch
                {
                    "black" => "30",
                    "red" => "31",
                    "green" => "32",
                    "yellow" => "33",
                    "blue" => "34",
                    "magenta" => "35",
                    "cyan" => "36",
                    "white" => "37",
                    "gray" or "grey" => "90",
                    _ => throw new RuntimeError($"Unknown color '{color}'. Use term color constants: term.BLACK, term.RED, etc.")
                };
                codes.Add(colorCode);
            }

            if (codes.Count == 0)
            {
                return text;
            }

            return $"\x1b[{string.Join(";", codes)}m{text}\x1b[0m";
        }));

        term.Define("strip", new BuiltInFunction("term.strip", 1, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("Argument to 'term.strip' must be a string.");
            }

            return Regex.Replace(text, @"\x1b\[[0-9;]*m", "");
        }));

        term.Define("width", new BuiltInFunction("term.width", 0, (_, _) =>
        {
            try { return (long)Console.WindowWidth; }
            catch { return 80L; }
        }));

        term.Define("isInteractive", new BuiltInFunction("term.isInteractive", 0, (_, _) =>
        {
            try { return !Console.IsInputRedirected; }
            catch { return (object?)false; }
        }));

        term.Define("clear", new BuiltInFunction("term.clear", 0, (interp, _) =>
        {
            interp.Output.Write("\x1b[2J\x1b[H");
            return null;
        }));

        term.Define("table", new BuiltInFunction("term.table", -1, (_, args) =>
        {
            if (args.Count < 1 || args.Count > 2)
            {
                throw new RuntimeError("'term.table' expects 1 or 2 arguments.");
            }

            if (args[0] is not List<object?> rows)
            {
                throw new RuntimeError("First argument to 'term.table' must be an array of arrays.");
            }

            List<object?>? headers = null;
            if (args.Count == 2 && args[1] is List<object?> h)
            {
                headers = h;
            }

            var allRows = new List<string[]>();
            if (headers != null)
            {
                allRows.Add(headers.Select(x => RuntimeValues.Stringify(x)).ToArray());
            }

            foreach (var row in rows)
            {
                if (row is not List<object?> cols)
                {
                    throw new RuntimeError("Each row in 'term.table' must be an array.");
                }

                allRows.Add(cols.Select(c => RuntimeValues.Stringify(c)).ToArray());
            }

            if (allRows.Count == 0)
            {
                return "";
            }

            int colCount = allRows.Max(r => r.Length);
            var widths = new int[colCount];
            foreach (var row in allRows)
            {
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i].Length > widths[i])
                    {
                        widths[i] = row[i].Length;
                    }
                }
            }

            var sb = new System.Text.StringBuilder();
            string separator = "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";

            sb.AppendLine(separator);
            for (int r = 0; r < allRows.Count; r++)
            {
                var row = allRows[r];
                sb.Append('|');
                for (int c = 0; c < colCount; c++)
                {
                    string cell = c < row.Length ? row[c] : "";
                    sb.Append(' ');
                    sb.Append(cell.PadRight(widths[c]));
                    sb.Append(" |");
                }
                sb.AppendLine();
                if (r == 0 && headers != null)
                {
                    sb.AppendLine(separator);
                }
            }
            sb.Append(separator);

            return sb.ToString();
        }));

        globals.Define("term", term);
    }
}
