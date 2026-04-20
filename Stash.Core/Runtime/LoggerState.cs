namespace Stash.Runtime;

using System;
using System.IO;
using System.Text;

/// <summary>
/// Mutable per-execution logger configuration owned by the interpreter context.
/// </summary>
public sealed class LoggerState : IDisposable
{
    public int Level { get; set; } = 1;      // 0=DEBUG,1=INFO,2=WARN,3=ERROR
    public string Format { get; set; } = "text";
    public string Output { get; set; } = "stderr";
    public TextWriter? FileWriter { get; private set; }

    public void SetFileOutput(string path)
    {
        FileWriter?.Dispose();
        FileWriter = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
        Output = path;
    }

    public void ClearFileOutput()
    {
        FileWriter?.Dispose();
        FileWriter = null;
    }

    public void Dispose()
    {
        FileWriter?.Dispose();
        FileWriter = null;
    }
}
