namespace Stash.Stdlib.BuiltIns;

using System.Text;

/// <summary>
/// Shared text encodings for standard-library file writes.
/// </summary>
internal static class StashEncodings
{
    /// <summary>
    /// UTF-8 <b>without</b> a byte-order mark. This is the single source of truth for
    /// text file output across the stdlib.
    /// <para>
    /// <c>System.Text.Encoding.UTF8</c> (and the parameterless <c>File.WriteAllText</c>
    /// overload, which defaults to it) emits a BOM (<c>EF BB BF</c>). On the leading
    /// bytes of a plain-text file that corrupts strict parsers, churns diffs, and
    /// surprises Unix tooling. Every fs / env / config / process / csv write path
    /// routes through this constant instead so the behaviour is uniform and named once.
    /// </para>
    /// </summary>
    internal static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
}
