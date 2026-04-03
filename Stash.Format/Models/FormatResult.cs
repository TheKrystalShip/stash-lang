namespace Stash.Format;

using System.Collections.Generic;
using System.Linq;

internal sealed class FormatResult
{
    public IReadOnlyList<FileFormatResult> Files { get; }
    public int TotalFiles => Files.Count;
    public int ChangedFiles => Files.Count(f => f.Changed);
    public int ErrorFiles => Files.Count(f => f.Error != null);

    public FormatResult(IReadOnlyList<FileFormatResult> files)
    {
        Files = files;
    }
}

internal sealed record FileFormatResult(
    string FilePath,
    string? Original = null,
    string? Formatted = null,
    bool Changed = false,
    string? Error = null);
