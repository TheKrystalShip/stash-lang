namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "Parsing failed (JSON, INI, TOML, CSV, number conversion, etc.).")]
public sealed class ParseError : RuntimeError
{
    public ParseError(string message, SourceSpan? span = null) : base(message, span) {}
}
