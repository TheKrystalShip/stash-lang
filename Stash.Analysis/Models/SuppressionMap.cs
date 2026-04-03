namespace Stash.Analysis;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// Tracks which diagnostic codes are suppressed at which source locations, supporting
/// line-level, same-line, and range-based suppression directives.
/// </summary>
public class SuppressionMap
{
    /// <summary>
    /// Line-level suppressions: maps a target line number to the set of suppressed codes.
    /// An empty set means ALL diagnostics are suppressed on that line.
    /// </summary>
    private readonly Dictionary<int, HashSet<string>?> _lineSuppressions = new();

    /// <summary>
    /// Range-based suppressions: each entry specifies a start line, optional end line
    /// (null = extends to end of file), and set of suppressed codes (null = all).
    /// </summary>
    private readonly List<(int StartLine, int? EndLine, HashSet<string>? Codes)> _rangeSuppressions = new();

    /// <summary>
    /// Diagnostics produced while parsing suppression directives (e.g., unknown codes).
    /// </summary>
    private readonly List<SemanticDiagnostic> _directiveDiagnostics = new();

    /// <summary>Gets any diagnostics generated during suppression parsing.</summary>
    public IReadOnlyList<SemanticDiagnostic> DirectiveDiagnostics => _directiveDiagnostics;

    /// <summary>
    /// Adds a line-level suppression for the given target line.
    /// </summary>
    /// <param name="targetLine">The 1-based line number to suppress.</param>
    /// <param name="codes">The codes to suppress, or <see langword="null"/> to suppress all.</param>
    public void AddLineSuppression(int targetLine, HashSet<string>? codes)
    {
        if (_lineSuppressions.TryGetValue(targetLine, out var existing))
        {
            if (existing == null || codes == null)
            {
                // Already suppressing all, or new suppression is for all
                _lineSuppressions[targetLine] = null;
            }
            else
            {
                existing.UnionWith(codes);
            }
        }
        else
        {
            _lineSuppressions[targetLine] = codes != null ? new HashSet<string>(codes) : null;
        }
    }

    /// <summary>
    /// Adds a range-based suppression starting at <paramref name="startLine"/>.
    /// </summary>
    /// <param name="startLine">The 1-based line where suppression begins.</param>
    /// <param name="endLine">The 1-based line where suppression ends, or <see langword="null"/> for end-of-file.</param>
    /// <param name="codes">The codes to suppress, or <see langword="null"/> for all.</param>
    public void AddRangeSuppression(int startLine, int? endLine, HashSet<string>? codes)
    {
        _rangeSuppressions.Add((startLine, endLine, codes != null ? new HashSet<string>(codes) : null));
    }

    /// <summary>
    /// Closes the most recent open range suppression for the given codes by setting its end line.
    /// </summary>
    public void RestoreRange(int restoreLine, HashSet<string>? codes)
    {
        // Walk backwards to find the matching open range
        for (int i = _rangeSuppressions.Count - 1; i >= 0; i--)
        {
            var (start, end, rangeCodes) = _rangeSuppressions[i];
            if (end != null) continue; // Already closed

            if (codes == null && rangeCodes == null)
            {
                // Restore all — close the all-suppressing range
                _rangeSuppressions[i] = (start, restoreLine, rangeCodes);
                return;
            }

            if (codes != null && rangeCodes != null && rangeCodes.Overlaps(codes))
            {
                // Close this range for the specified codes
                // If the range suppresses more codes than we're restoring, split
                var remaining = new HashSet<string>(rangeCodes);
                remaining.ExceptWith(codes);

                _rangeSuppressions[i] = (start, restoreLine, rangeCodes);

                if (remaining.Count > 0)
                {
                    // Continue suppressing the remaining codes
                    _rangeSuppressions.Add((restoreLine, null, remaining));
                }
                return;
            }

            if (codes != null && rangeCodes == null)
            {
                // Range suppresses all, but we're only restoring specific codes
                // Close the all-suppressing range and re-open without the restored codes
                _rangeSuppressions[i] = (start, restoreLine, null);
                // All codes except the restored ones continue
                // We can't enumerate "all minus specific" without a code set,
                // so we just close the range — this is a simplification
                return;
            }
        }
    }

    /// <summary>
    /// Adds a diagnostic produced while parsing a suppression directive.
    /// </summary>
    public void AddDirectiveDiagnostic(SemanticDiagnostic diagnostic)
    {
        _directiveDiagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Returns <see langword="true"/> if the given diagnostic code is suppressed at the given line.
    /// </summary>
    /// <param name="code">The diagnostic code (e.g., "SA0201").</param>
    /// <param name="line">The 1-based source line to check.</param>
    /// <returns><see langword="true"/> if suppressed.</returns>
    public bool IsSuppressed(string? code, int line)
    {
        // Check line-level suppressions
        if (_lineSuppressions.TryGetValue(line, out var lineCodes))
        {
            if (lineCodes == null) return true; // All suppressed
            if (code != null && lineCodes.Contains(code)) return true;
        }

        // Check range suppressions
        foreach (var (startLine, endLine, rangeCodes) in _rangeSuppressions)
        {
            if (line < startLine) continue;
            if (endLine != null && line > endLine) continue;

            if (rangeCodes == null) return true; // All suppressed
            if (code != null && rangeCodes.Contains(code)) return true;
        }

        return false;
    }

    /// <summary>
    /// Filters a list of diagnostics, removing those that are suppressed.
    /// Returns the filtered list.
    /// </summary>
    public List<SemanticDiagnostic> Filter(List<SemanticDiagnostic> diagnostics)
    {
        var result = new List<SemanticDiagnostic>(diagnostics.Count);
        foreach (var d in diagnostics)
        {
            if (!IsSuppressed(d.Code, d.Span.StartLine))
            {
                result.Add(d);
            }
        }
        // Add directive diagnostics (these are not suppressible)
        result.AddRange(_directiveDiagnostics);
        return result;
    }
}
