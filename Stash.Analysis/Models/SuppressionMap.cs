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

    // ── File-Level Suppression ───────────────────────────────────────────────

    /// <summary>When <see langword="true"/>, ALL diagnostics in the file are suppressed.</summary>
    private bool _fileSuppressAll;

    /// <summary>Specific codes suppressed for the entire file (populated by <c>stash-disable-file CODE</c>).</summary>
    private readonly HashSet<string> _fileSuppressedCodes = new();

    /// <summary>Gets whether any file-level suppression directive was encountered.</summary>
    public bool HasFileLevelSuppression => _fileSuppressAll || _fileSuppressedCodes.Count > 0;

    /// <summary>
    /// Registers a file-level suppression directive.
    /// </summary>
    /// <param name="codes">The codes to suppress, or <see langword="null"/> to suppress all diagnostics.</param>
    internal void SetFileLevelSuppression(HashSet<string>? codes)
    {
        if (codes == null)
            _fileSuppressAll = true;
        else
            _fileSuppressedCodes.UnionWith(codes);
    }

    private bool IsFileLevelSuppressed(string? code)
    {
        if (!HasFileLevelSuppression) return false;
        if (_fileSuppressAll) return true;
        return code != null && _fileSuppressedCodes.Contains(code);
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
        // Track which suppressions are actually used
        var usedLineSuppressions = new Dictionary<int, HashSet<string>>();
        var usedRangeIndices = new HashSet<int>();

        var result = new List<SemanticDiagnostic>(diagnostics.Count);
        foreach (var d in diagnostics)
        {
            // File-level suppression is checked first and does not generate SA0003 warnings
            if (IsFileLevelSuppressed(d.Code)) continue;

            if (IsSuppressedWithTracking(d.Code, d.Span.StartLine, usedLineSuppressions, usedRangeIndices))
            {
                continue;
            }
            result.Add(d);
        }

        // Detect unused line suppressions
        foreach (var (line, codes) in _lineSuppressions)
        {
            if (codes == null)
            {
                // Blanket suppression — check if any diagnostic was suppressed on this line
                if (!usedLineSuppressions.ContainsKey(line))
                {
                    var span = GetLineSpan(line);
                    result.Add(DiagnosticDescriptors.SA0003.CreateDiagnostic(span, "all codes"));
                }
            }
            else
            {
                foreach (string code in codes)
                {
                    if (!usedLineSuppressions.TryGetValue(line, out var usedCodes) || !usedCodes.Contains(code))
                    {
                        var span = GetLineSpan(line);
                        result.Add(DiagnosticDescriptors.SA0003.CreateDiagnostic(span, code));
                    }
                }
            }
        }

        // Detect unused range suppressions
        for (int i = 0; i < _rangeSuppressions.Count; i++)
        {
            if (!usedRangeIndices.Contains(i))
            {
                var (startLine, _, codes) = _rangeSuppressions[i];
                string label = codes == null ? "all codes" : string.Join(", ", codes);
                var span = GetLineSpan(startLine - 1);
                result.Add(DiagnosticDescriptors.SA0003.CreateDiagnostic(span, label));
            }
        }

        // Add directive diagnostics (these are not suppressible)
        result.AddRange(_directiveDiagnostics);
        return result;
    }

    private bool IsSuppressedWithTracking(string? code, int line, Dictionary<int, HashSet<string>> usedLineSuppressions, HashSet<int> usedRangeIndices)
    {
        bool suppressed = false;

        // Check line-level suppressions
        if (_lineSuppressions.TryGetValue(line, out var lineCodes))
        {
            if (lineCodes == null || (code != null && lineCodes.Contains(code)))
            {
                suppressed = true;
                if (!usedLineSuppressions.TryGetValue(line, out var usedCodes))
                {
                    usedCodes = new HashSet<string>();
                    usedLineSuppressions[line] = usedCodes;
                }
                if (code != null) usedCodes.Add(code);
            }
        }

        // Check range suppressions
        for (int i = 0; i < _rangeSuppressions.Count; i++)
        {
            var (startLine, endLine, rangeCodes) = _rangeSuppressions[i];
            if (line < startLine) continue;
            if (endLine != null && line > endLine) continue;

            if (rangeCodes == null || (code != null && rangeCodes.Contains(code)))
            {
                suppressed = true;
                usedRangeIndices.Add(i);
            }
        }

        return suppressed;
    }

    private static SourceSpan GetLineSpan(int line)
    {
        return new SourceSpan("", line, 1, line, 1);
    }
}
