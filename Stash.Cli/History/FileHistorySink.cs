namespace Stash.Cli.History;

using System.Collections.Generic;

/// <summary>
/// History sink backed by <see cref="HistoryFileWriter"/> for persistent file storage.
/// </summary>
internal sealed class FileHistorySink : IHistorySink
{
    private readonly HistoryFileWriter _writer;
    private readonly IReadOnlyList<string> _initial;

    public FileHistorySink(HistoryFileWriter writer, IReadOnlyList<string> initial)
    {
        _writer = writer;
        _initial = initial;
    }

    public IReadOnlyList<string> Initial => _initial;
    public void Append(string entry) => _writer.Append(entry);
}
