namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>csv</c> namespace built-in functions for RFC 4180 compliant CSV parsing and writing.
/// </summary>
public static class CsvBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("csv");

        // CsvOptions struct
        ns.Struct("CsvOptions", [
            new BuiltInField("delimiter", "string"),
            new BuiltInField("quote", "string"),
            new BuiltInField("escape", "string"),
            new BuiltInField("header", "bool"),
            new BuiltInField("columns", "array")
        ]);

        // csv.parse(text, options?) — Parse CSV string → array of arrays or array of dicts
        ns.Function("parse", [Param("text", "string"), Param("options?", "CsvOptions")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 1 || args.Length > 2)
                    throw new RuntimeError("csv.parse: expected 1 or 2 arguments.");

                var text = SvArgs.String(args, 0, "csv.parse");
                var opts = args.Length > 1 ? GetCsvOptions(args[1], "csv.parse") : DefaultOptions;

                var rows = ParseCsv(text, opts.Delimiter, opts.Quote, opts.Escape);

                if (opts.Header || opts.Columns != null)
                {
                    return StashValue.FromObj(BuildDictRows(rows, opts.Columns, "csv.parse"));
                }

                return StashValue.FromObj(BuildArrayRows(rows));
            },
            returnType: "array",
            isVariadic: true,
            documentation: "Parses a CSV string into an array of arrays (default) or an array of dictionaries (when header:true or columns is set).\n@param text The CSV string to parse\n@param options Optional CsvOptions struct\n@return Array of arrays or array of dictionaries");

        // csv.stringify(data, options?) — Array of arrays/dicts → CSV string
        ns.Function("stringify", [Param("data", "array"), Param("options?", "CsvOptions")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 1 || args.Length > 2)
                    throw new RuntimeError("csv.stringify: expected 1 or 2 arguments.");

                var opts = args.Length > 1 ? GetCsvOptions(args[1], "csv.stringify") : DefaultOptions;

                var data = args[0];
                if (data.IsNull)
                    throw new RuntimeError("csv.stringify: data must be an array.");

                if (data.ToObject() is not List<StashValue> rows)
                    throw new RuntimeError("csv.stringify: data must be an array.");

                return StashValue.FromObj(StringifyCsv(rows, opts.Delimiter, opts.Quote, opts.Escape, opts.Header, opts.Columns));
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Converts an array of arrays or dictionaries to a CSV string.\n@param data Array of arrays or array of dictionaries\n@param options Optional CsvOptions struct\n@return The CSV string");

        // csv.parseFile(path, options?) — Parse CSV file
        ns.Function("parseFile", [Param("path", "string"), Param("options?", "CsvOptions")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 1 || args.Length > 2)
                    throw new RuntimeError("csv.parseFile: expected 1 or 2 arguments.");

                var path = ctx.ExpandTilde(SvArgs.String(args, 0, "csv.parseFile"));
                var opts = args.Length > 1 ? GetCsvOptions(args[1], "csv.parseFile") : DefaultOptions;

                if (!File.Exists(path))
                    throw new RuntimeError($"csv.parseFile: file not found: '{path}'");

                string text;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch (UnauthorizedAccessException)
                {
                    throw new RuntimeError($"csv.parseFile: permission denied: '{path}'");
                }
                catch (Exception ex) when (ex is not RuntimeError)
                {
                    throw new RuntimeError($"csv.parseFile: failed to read file: {ex.Message}");
                }

                var rows = ParseCsv(text, opts.Delimiter, opts.Quote, opts.Escape);

                if (opts.Header || opts.Columns != null)
                {
                    return StashValue.FromObj(BuildDictRows(rows, opts.Columns, "csv.parseFile"));
                }

                return StashValue.FromObj(BuildArrayRows(rows));
            },
            returnType: "array",
            isVariadic: true,
            documentation: "Reads a CSV file and parses it into an array of arrays or dictionaries.\n@param path The path to the CSV file\n@param options Optional CsvOptions struct\n@return Array of arrays or array of dictionaries");

        // csv.writeFile(path, data, options?) — Write CSV file
        ns.Function("writeFile", [Param("path", "string"), Param("data", "array"), Param("options?", "CsvOptions")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 2 || args.Length > 3)
                    throw new RuntimeError("csv.writeFile: expected 2 or 3 arguments.");

                var path = ctx.ExpandTilde(SvArgs.String(args, 0, "csv.writeFile"));
                var opts = args.Length > 2 ? GetCsvOptions(args[2], "csv.writeFile") : DefaultOptions;

                var data = args[1];
                if (data.IsNull)
                    throw new RuntimeError("csv.writeFile: data must be an array.");

                if (data.ToObject() is not List<StashValue> rows)
                    throw new RuntimeError("csv.writeFile: data must be an array.");

                var csv = StringifyCsv(rows, opts.Delimiter, opts.Quote, opts.Escape, opts.Header, opts.Columns);

                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllText(path, csv, new UTF8Encoding(false));
                }
                catch (UnauthorizedAccessException)
                {
                    throw new RuntimeError($"csv.writeFile: permission denied: '{path}'");
                }
                catch (Exception ex) when (ex is not RuntimeError)
                {
                    throw new RuntimeError($"csv.writeFile: failed to write file: {ex.Message}");
                }

                return StashValue.FromObj(path);
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Writes an array of arrays or dictionaries to a CSV file.\n@param path The path to the output CSV file\n@param data Array of arrays or array of dictionaries\n@param options Optional CsvOptions struct\n@return The path to the written file");

        return ns.Build();
    }

    // ── Option extraction ─────────────────────────────────────────────────────

    private readonly record struct CsvOptionsRecord(char Delimiter, char Quote, char Escape, bool Header, List<string>? Columns);

    private static readonly CsvOptionsRecord DefaultOptions = new(',', '"', '"', false, null);

    private static CsvOptionsRecord GetCsvOptions(StashValue value, string funcName)
    {
        if (value.IsNull) return DefaultOptions;

        if (value.ToObject() is not StashInstance opts)
            throw new RuntimeError($"{funcName}: options must be a CsvOptions struct.");

        var delimiter = ',';
        var quote = '"';
        var escape = '"';
        var header = false;
        List<string>? columns = null;

        var delimVal = opts.GetField("delimiter", null);
        if (!delimVal.IsNull)
        {
            if (delimVal.ToObject() is not string delimStr)
                throw new RuntimeError($"{funcName}: invalid options: delimiter must be a string.");
            if (delimStr.Length != 1)
                throw new RuntimeError($"{funcName}: invalid options: delimiter must be a single character.");
            delimiter = delimStr[0];
        }

        var quoteVal = opts.GetField("quote", null);
        if (!quoteVal.IsNull)
        {
            if (quoteVal.ToObject() is not string quoteStr)
                throw new RuntimeError($"{funcName}: invalid options: quote must be a string.");
            if (quoteStr.Length != 1)
                throw new RuntimeError($"{funcName}: invalid options: quote must be a single character.");
            quote = quoteStr[0];
        }

        var escapeVal = opts.GetField("escape", null);
        if (!escapeVal.IsNull)
        {
            if (escapeVal.ToObject() is not string escapeStr)
                throw new RuntimeError($"{funcName}: invalid options: escape must be a string.");
            if (escapeStr.Length != 1)
                throw new RuntimeError($"{funcName}: invalid options: escape must be a single character.");
            escape = escapeStr[0];
        }

        var headerVal = opts.GetField("header", null);
        if (!headerVal.IsNull)
        {
            if (!headerVal.IsBool)
                throw new RuntimeError($"{funcName}: invalid options: header must be a boolean.");
            header = headerVal.AsBool;
        }

        var columnsVal = opts.GetField("columns", null);
        if (!columnsVal.IsNull)
        {
            if (columnsVal.ToObject() is not List<StashValue> colList)
                throw new RuntimeError($"{funcName}: invalid options: columns must be an array.");
            columns = new List<string>(colList.Count);
            foreach (var col in colList)
            {
                if (col.ToObject() is not string colStr)
                    throw new RuntimeError($"{funcName}: invalid options: columns array must contain only strings.");
                columns.Add(colStr);
            }
        }

        return new CsvOptionsRecord(delimiter, quote, escape, header, columns);
    }

    // ── RFC 4180 state-machine CSV parser ────────────────────────────────────

    /// <summary>
    /// Parses a CSV string using an RFC 4180 compliant state machine.
    /// Handles quoted fields, escaped quotes (doubled or backslash), embedded newlines,
    /// CRLF and LF line endings, BOM stripping, and empty fields.
    /// </summary>
    private static List<List<string>> ParseCsv(string text, char delimiter, char quoteChar, char escapeChar)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text))
            return rows;

        // Strip UTF-8 BOM if present
        int start = 0;
        if (text.Length >= 1 && text[0] == '\uFEFF')
            start = 1;

        var field = new StringBuilder();
        var currentRow = new List<string>();
        bool inQuotes = false;
        int row = 1;
        int i = start;

        while (i < text.Length)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == escapeChar && escapeChar != quoteChar && i + 1 < text.Length && text[i + 1] == quoteChar)
                {
                    // Backslash escape (when escape != quote): \<quote> → <quote>
                    field.Append(quoteChar);
                    i += 2;
                    continue;
                }

                if (c == quoteChar)
                {
                    // Check for doubled-quote escape: "" → "
                    if (i + 1 < text.Length && text[i + 1] == quoteChar)
                    {
                        field.Append(quoteChar);
                        i += 2;
                        continue;
                    }

                    // Closing quote
                    inQuotes = false;
                    i++;
                    continue;
                }

                // Embedded newline counts toward row number
                if (c == '\n')
                    row++;

                field.Append(c);
                i++;
                continue;
            }

            // Not in quotes
            if (c == quoteChar)
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (c == delimiter)
            {
                currentRow.Add(field.ToString());
                field.Clear();
                i++;
                continue;
            }

            if (c == '\r')
            {
                // CRLF or bare CR — both end the row
                currentRow.Add(field.ToString());
                field.Clear();
                rows.Add(currentRow);
                currentRow = new List<string>();
                row++;
                i++;
                if (i < text.Length && text[i] == '\n')
                    i++; // consume LF of CRLF
                continue;
            }

            if (c == '\n')
            {
                currentRow.Add(field.ToString());
                field.Clear();
                rows.Add(currentRow);
                currentRow = new List<string>();
                row++;
                i++;
                continue;
            }

            field.Append(c);
            i++;
        }

        if (inQuotes)
            throw new RuntimeError($"csv.parse: unterminated quoted field at row {row}");

        // Flush the last row — but skip a trailing empty row caused by a trailing newline
        bool hasContent = field.Length > 0 || currentRow.Count > 0;
        if (hasContent)
        {
            currentRow.Add(field.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    // ── Row → StashValue builders ─────────────────────────────────────────────

    private static List<StashValue> BuildArrayRows(List<List<string>> rows)
    {
        var result = new List<StashValue>(rows.Count);
        foreach (var row in rows)
        {
            var rowList = new List<StashValue>(row.Count);
            foreach (var field in row)
                rowList.Add(StashValue.FromObj(field));
            result.Add(StashValue.FromObj(rowList));
        }
        return result;
    }

    private static List<StashValue> BuildDictRows(List<List<string>> rows, List<string>? explicitColumns, string funcName)
    {
        if (rows.Count == 0)
            return [];

        List<string> keys;
        int dataStart;

        if (explicitColumns != null)
        {
            // Explicit columns override — data starts at row 0
            keys = explicitColumns;
            dataStart = 0;
        }
        else
        {
            // First row is the header
            keys = rows[0];
            dataStart = 1;
        }

        var result = new List<StashValue>(rows.Count - dataStart);
        for (int i = dataStart; i < rows.Count; i++)
        {
            var rowFields = rows[i];
            var dict = new StashDictionary();
            for (int j = 0; j < keys.Count; j++)
            {
                var fieldValue = j < rowFields.Count ? rowFields[j] : string.Empty;
                dict.Set(keys[j], StashValue.FromObj(fieldValue));
            }
            result.Add(StashValue.FromObj(dict));
        }
        return result;
    }

    // ── CSV stringifier ───────────────────────────────────────────────────────

    private static string StringifyCsv(
        List<StashValue> rows,
        char delimiter,
        char quoteChar,
        char escapeChar,
        bool includeHeader,
        List<string>? explicitColumns)
    {
        if (rows.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        // Determine if data is rows-of-dicts or rows-of-arrays
        bool isDictRows = rows.Count > 0 && rows[0].ToObject() is StashDictionary;

        if (isDictRows)
        {
            // Collect column names from first row or explicit columns
            List<string> columns;
            if (explicitColumns != null)
            {
                columns = explicitColumns;
            }
            else
            {
                var firstDict = (StashDictionary)rows[0].ToObject()!;
                var rawKeys = firstDict.RawKeys();
                columns = new List<string>(rawKeys.Count);
                foreach (var k in rawKeys)
                    columns.Add(k.ToString()!);
            }

            // Write header row
            if (includeHeader || explicitColumns != null)
            {
                AppendRow(sb, columns, delimiter, quoteChar, escapeChar);
                sb.Append('\n');
            }

            // Write data rows
            foreach (var rowVal in rows)
            {
                if (rowVal.ToObject() is not StashDictionary dict)
                    throw new RuntimeError("csv.stringify: mixed array/dict rows are not supported.");

                var fields = new List<string>(columns.Count);
                foreach (var col in columns)
                {
                    var val = dict.Get(col);
                    fields.Add(ValueToField(val));
                }
                AppendRow(sb, fields, delimiter, quoteChar, escapeChar);
                sb.Append('\n');
            }
        }
        else
        {
            // Array-of-arrays mode
            // Write header from explicit columns if provided
            if (explicitColumns != null)
            {
                AppendRow(sb, explicitColumns, delimiter, quoteChar, escapeChar);
                sb.Append('\n');
            }

            foreach (var rowVal in rows)
            {
                if (rowVal.ToObject() is not List<StashValue> rowList)
                    throw new RuntimeError("csv.stringify: each row must be an array.");

                var fields = new List<string>(rowList.Count);
                foreach (var field in rowList)
                    fields.Add(ValueToField(field));

                AppendRow(sb, fields, delimiter, quoteChar, escapeChar);
                sb.Append('\n');
            }
        }

        // Remove trailing newline to match common CSV behavior
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
            sb.Length--;

        return sb.ToString();
    }

    private static string ValueToField(StashValue val)
    {
        if (val.IsNull) return string.Empty;
        if (val.IsObj && val.AsObj is string s) return s;
        return val.ToObject()?.ToString() ?? string.Empty;
    }

    private static void AppendRow(StringBuilder sb, List<string> fields, char delimiter, char quoteChar, char escapeChar)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
                sb.Append(delimiter);
            AppendField(sb, fields[i], delimiter, quoteChar, escapeChar);
        }
    }

    private static void AppendField(StringBuilder sb, string field, char delimiter, char quoteChar, char escapeChar)
    {
        // Fields that need quoting: contain delimiter, quote char, CR, LF
        bool needsQuoting = field.IndexOf(delimiter) >= 0
            || field.IndexOf(quoteChar) >= 0
            || field.IndexOf('\n') >= 0
            || field.IndexOf('\r') >= 0;

        if (!needsQuoting)
        {
            sb.Append(field);
            return;
        }

        sb.Append(quoteChar);
        foreach (char c in field)
        {
            if (c == quoteChar)
            {
                // Escape using doubled-quote (RFC 4180) or escape character
                if (escapeChar == quoteChar)
                    sb.Append(quoteChar); // doubled-quote escape
                else
                    sb.Append(escapeChar);
                sb.Append(quoteChar);
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append(quoteChar);
    }

    // ── Config namespace integration ──────────────────────────────────────────

    /// <summary>Parses a CSV string using default options (comma delimiter, no header row). Called by config.parse/config.read.</summary>
    internal static List<StashValue> ParseCsvDefault(string text)
    {
        var rows = ParseCsv(text, DefaultOptions.Delimiter, DefaultOptions.Quote, DefaultOptions.Escape);
        return BuildArrayRows(rows);
    }

    /// <summary>Serializes a Stash array of arrays or dicts to a CSV string using default options. Called by config.stringify/config.write.</summary>
    internal static string StringifyCsvDefault(object? data, string callerName)
    {
        if (data is not List<StashValue> rows)
            throw new RuntimeError($"{callerName}: CSV format requires an array value.");
        return StringifyCsv(rows, DefaultOptions.Delimiter, DefaultOptions.Quote, DefaultOptions.Escape, DefaultOptions.Header, DefaultOptions.Columns);
    }
}
