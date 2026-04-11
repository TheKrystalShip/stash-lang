namespace Stash.Scheduler;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

/// <summary>
/// Parses and validates standard 5-field cron expressions.
/// Fields: minute hour day-of-month month day-of-week
/// </summary>
public sealed class CronExpression
{
    private static readonly HashSet<char> _shellMetachars =
        new([';', '|', '&', '$', '`', '(', ')', '{', '}', '<', '>', '!', '\\']);

    private readonly string _original;

    /// <summary>Expanded set of valid minute values (0–59).</summary>
    public int[] Minutes { get; }

    /// <summary>Expanded set of valid hour values (0–23).</summary>
    public int[] Hours { get; }

    /// <summary>Expanded set of valid day-of-month values (1–31).</summary>
    public int[] DaysOfMonth { get; }

    /// <summary>Expanded set of valid month values (1–12).</summary>
    public int[] Months { get; }

    /// <summary>Expanded set of valid day-of-week values (0–6, where 0 = Sunday).</summary>
    public int[] DaysOfWeek { get; }

    private CronExpression(
        string original,
        int[] minutes,
        int[] hours,
        int[] daysOfMonth,
        int[] months,
        int[] daysOfWeek)
    {
        _original = original;
        Minutes = minutes;
        Hours = hours;
        DaysOfMonth = daysOfMonth;
        Months = months;
        DaysOfWeek = daysOfWeek;
    }

    /// <summary>
    /// Parses a 5-field cron expression.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the expression is invalid.</exception>
    public static CronExpression Parse(string expression)
    {
        if (!TryParseInternal(expression, out CronExpression? result, out string? error))
            throw new ArgumentException(error ?? "Invalid cron expression.", nameof(expression));
        return result;
    }

    /// <summary>
    /// Attempts to parse a 5-field cron expression without throwing.
    /// </summary>
    public static bool TryParse(string expression, [NotNullWhen(true)] out CronExpression? result)
        => TryParseInternal(expression, out result, out _);

    private static bool TryParseInternal(
        string expression,
        [NotNullWhen(true)] out CronExpression? result,
        out string? error)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Cron expression cannot be null or empty.";
            return false;
        }

        foreach (char c in expression)
        {
            if (_shellMetachars.Contains(c))
            {
                error = $"Cron expression contains illegal character '{c}'.";
                return false;
            }
        }

        string[] parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            error = $"Cron expression must have exactly 5 fields, got {parts.Length}.";
            return false;
        }

        if (!ParseField(parts[0], 0, 59, out int[]? minutes, out error)) return false;
        if (!ParseField(parts[1], 0, 23, out int[]? hours, out error)) return false;
        if (!ParseField(parts[2], 1, 31, out int[]? daysOfMonth, out error)) return false;
        if (!ParseField(parts[3], 1, 12, out int[]? months, out error)) return false;
        // day-of-week allows 0–7 (0 and 7 both = Sunday); normalize after parsing
        if (!ParseField(parts[4], 0, 7, out int[]? daysOfWeek, out error)) return false;

        // Normalize: 7 → 0 (both mean Sunday), deduplicate, re-sort
        daysOfWeek = daysOfWeek!
            .Select(d => d == 7 ? 0 : d)
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        result = new CronExpression(
            string.Join(' ', parts),
            minutes!, hours!, daysOfMonth!, months!, daysOfWeek);
        error = null;
        return true;
    }

    // ──────────────────────────── Field Parsing ────────────────────────────

    private static bool ParseField(
        string expr,
        int min,
        int max,
        [NotNullWhen(true)] out int[]? values,
        out string? error)
    {
        var list = new List<int>();
        foreach (string part in expr.Split(','))
        {
            if (!ParseFieldPart(part.Trim(), min, max, list, out error))
            {
                values = null;
                return false;
            }
        }

        values = list.Distinct().OrderBy(x => x).ToArray();
        error = null;
        return true;
    }

    private static bool ParseFieldPart(string part, int min, int max, List<int> values, out string? error)
    {
        // ── Step: */N  or  N/N  or  N-M/N ──
        if (part.Contains('/'))
        {
            string[] slashParts = part.Split('/', 2);
            if (!int.TryParse(slashParts[1], out int step) || step <= 0)
            {
                error = $"Invalid step value in '{part}'.";
                return false;
            }

            int from, to;
            string rangePart = slashParts[0];

            if (rangePart == "*")
            {
                from = min;
                to = max;
            }
            else if (rangePart.Contains('-'))
            {
                string[] rangeParts = rangePart.Split('-', 2);
                if (!int.TryParse(rangeParts[0], out from) || !int.TryParse(rangeParts[1], out to))
                {
                    error = $"Invalid range in '{part}'.";
                    return false;
                }
            }
            else if (int.TryParse(rangePart, out from))
            {
                to = max;
            }
            else
            {
                error = $"Invalid step base in '{part}'.";
                return false;
            }

            if (from < min || to > max || from > to)
            {
                error = $"Step range {from}-{to} is out of bounds [{min},{max}].";
                return false;
            }

            for (int v = from; v <= to; v += step)
                values.Add(v);
            error = null;
            return true;
        }

        // ── Range: N-M ──
        if (part.Contains('-'))
        {
            string[] rangeParts = part.Split('-', 2);
            if (!int.TryParse(rangeParts[0], out int from) || !int.TryParse(rangeParts[1], out int to))
            {
                error = $"Invalid range '{part}'.";
                return false;
            }
            if (from < min || to > max || from > to)
            {
                error = $"Range {from}-{to} is out of bounds [{min},{max}] or descending.";
                return false;
            }
            for (int v = from; v <= to; v++)
                values.Add(v);
            error = null;
            return true;
        }

        // ── Wildcard: * ──
        if (part == "*")
        {
            for (int v = min; v <= max; v++)
                values.Add(v);
            error = null;
            return true;
        }

        // ── Fixed value ──
        if (!int.TryParse(part, out int fixedVal))
        {
            error = $"Invalid cron field value '{part}'.";
            return false;
        }
        if (fixedVal < min || fixedVal > max)
        {
            error = $"Value {fixedVal} is out of range [{min},{max}].";
            return false;
        }
        values.Add(fixedVal);
        error = null;
        return true;
    }

    // ─────────────────────── Systemd Calendar Conversion ───────────────────────

    /// <summary>
    /// Converts this cron expression to a systemd OnCalendar expression.
    /// </summary>
    /// <remarks>
    /// Examples:
    ///   <c>* * * * *</c>       → <c>*-*-* *:*:00</c>
    ///   <c>*/5 * * * *</c>     → <c>*-*-* *:0/5:00</c>
    ///   <c>0 9-17 * * 1-5</c>  → <c>Mon..Fri *-*-* 09..17:00:00</c>
    /// </remarks>
    public string ToSystemdCalendar()
    {
        var sb = new StringBuilder();

        // Day-of-week prefix (only when not all 7 days)
        if (DaysOfWeek.Length < 7)
        {
            sb.Append(DowArrayToSystemd(DaysOfWeek));
            sb.Append(' ');
        }

        string monthStr = FieldToSystemd(Months, 1, 12);
        string domStr   = FieldToSystemd(DaysOfMonth, 1, 31);
        string hourStr  = FieldToSystemd(Hours, 0, 23);
        string minStr   = FieldToSystemd(Minutes, 0, 59);

        sb.Append($"*-{monthStr}-{domStr} {hourStr}:{minStr}:00");
        return sb.ToString();
    }

    /// <summary>
    /// Converts an expanded field value array to systemd calendar syntax.
    /// <list type="bullet">
    ///   <item>All values → <c>*</c></item>
    ///   <item>Single value → zero-padded (<c>05</c>)</item>
    ///   <item>Contiguous range → <c>N..M</c> (padded)</item>
    ///   <item>Step sequence filling to max → <c>start/step</c> (unpadded start)</item>
    ///   <item>Otherwise → comma-separated padded list</item>
    /// </list>
    /// </summary>
    private static string FieldToSystemd(int[] values, int min, int max)
    {
        int expectedCount = max - min + 1;

        if (values.Length == expectedCount)
            return "*";

        if (values.Length == 1)
            return values[0].ToString("D2");

        // Detect arithmetic sequence (constant positive step)
        bool isArithmetic = true;
        int step = values[1] - values[0];
        if (step <= 0)
        {
            isArithmetic = false;
        }
        else
        {
            for (int i = 2; i < values.Length; i++)
            {
                if (values[i] - values[i - 1] != step)
                {
                    isArithmetic = false;
                    break;
                }
            }
        }

        if (isArithmetic)
        {
            int from = values[0];
            int to   = values[^1];

            if (step == 1)
            {
                // Contiguous range: zero-padded N..M
                return $"{from:D2}..{to:D2}";
            }

            // Step sequence: use from/step only when the sequence fills to the end of the field
            // i.e., no additional value would fit (to + step > max).
            if (to + step > max)
                return $"{from}/{step}";  // systemd format: 0/5, no zero-padding on start

            // Bounded step range — list individual values
            return string.Join(',', values.Select(v => v.ToString("D2")));
        }

        // Non-arithmetic: list all values
        return string.Join(',', values.Select(v => v.ToString("D2")));
    }

    private static string DowArrayToSystemd(int[] dows)
    {
        // dows are 0–6 (already normalized, 0 = Sunday)
        if (dows.Length == 1)
            return DowName(dows[0]);

        // Sort in systemd weekday order: Mon=1 … Sat=6, Sun=7
        // Cron 0 (Sun) maps to systemd ordinal 7 for ordering.
        int[] sorted = dows.OrderBy(d => d == 0 ? 7 : d).ToArray();
        int[] systemdOrdinals = sorted.Select(d => d == 0 ? 7 : d).ToArray();

        bool contiguous = true;
        for (int i = 1; i < systemdOrdinals.Length; i++)
        {
            if (systemdOrdinals[i] - systemdOrdinals[i - 1] != 1)
            {
                contiguous = false;
                break;
            }
        }

        if (contiguous)
            return $"{DowName(sorted[0])}..{DowName(sorted[^1])}";

        return string.Join(',', sorted.Select(DowName));
    }

    private static string DowName(int cronDow) => cronDow switch
    {
        0 => "Sun",
        1 => "Mon",
        2 => "Tue",
        3 => "Wed",
        4 => "Thu",
        5 => "Fri",
        6 => "Sat",
        _ => throw new ArgumentOutOfRangeException(nameof(cronDow), $"Invalid day-of-week value: {cronDow}")
    };

    public override string ToString() => _original;
}
