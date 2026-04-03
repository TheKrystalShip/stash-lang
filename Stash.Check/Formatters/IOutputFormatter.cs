namespace Stash.Check;

using System.IO;

internal interface IOutputFormatter
{
    string Format { get; }
    void Write(CheckResult result, Stream output);
}
