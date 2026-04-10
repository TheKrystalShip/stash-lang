namespace Stash.Analysis.Formatting;

using System.Collections.Generic;

/// <summary>
/// Wadler-Lindig pretty-printing intermediate representation.
/// Construct documents via the static factory methods; use a <c>DocPrinter</c> to render.
/// </summary>
public abstract class Doc
{
    // Private constructor prevents external subclassing while allowing nested sealed subclasses.
    private Doc() { }

    // -------------------------------------------------------------------------
    // Singletons
    // -------------------------------------------------------------------------

    /// <summary>The empty document — produces no output.</summary>
    public static readonly Doc Empty = new ConcatDoc(System.Array.Empty<Doc>());

    /// <summary>A space in flat mode; a line break in break mode.</summary>
    public static readonly Doc Line = new LineDoc();

    /// <summary>Nothing in flat mode; a line break in break mode.</summary>
    public static readonly Doc SoftLine = new SoftLineDoc();

    /// <summary>Always a line break, regardless of mode.</summary>
    public static readonly Doc HardLine = new HardLineDoc();

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>Creates a document that renders as the literal <paramref name="value"/>.</summary>
    public static Doc Text(string value) => new TextDoc(value);

    /// <summary>Increases the indentation level for <paramref name="contents"/>.</summary>
    public static Doc Indent(Doc contents) => new IndentDoc(contents);

    /// <summary>Decreases the indentation level for <paramref name="contents"/>.</summary>
    public static Doc Dedent(Doc contents) => new DedentDoc(contents);

    /// <summary>
    /// Tries to render <paramref name="contents"/> in flat (single-line) mode;
    /// falls back to break (multi-line) mode if it does not fit the line width.
    /// </summary>
    public static Doc Group(Doc contents) => new GroupDoc(contents);

    /// <summary>Greedily packs <paramref name="parts"/> onto lines.</summary>
    public static Doc Fill(params Doc[] parts) => new FillDoc(parts);

    /// <summary>
    /// Chooses between <paramref name="breakContents"/> and <paramref name="flatContents"/>
    /// based on whether the enclosing group is in break or flat mode.
    /// </summary>
    public static Doc IfBreak(Doc breakContents, Doc flatContents) =>
        new IfBreakDoc(breakContents, flatContents);

    /// <summary>Defers <paramref name="contents"/> to the end of the current line (trailing-comment support).</summary>
    public static Doc LineSuffix(Doc contents) => new LineSuffixDoc(contents);

    /// <summary>
    /// Concatenates <paramref name="parts"/> into a single document.
    /// Nested <see cref="ConcatDoc"/> instances are flattened and <see cref="Empty"/> nodes are skipped.
    /// Returns <see cref="Empty"/> for zero effective parts and unwraps single-part results.
    /// </summary>
    public static Doc Concat(params Doc[] parts)
    {
        var flat = new List<Doc>(parts.Length);
        Flatten(parts, flat);

        return flat.Count switch
        {
            0 => Empty,
            1 => flat[0],
            _ => new ConcatDoc(flat.ToArray())
        };
    }

    /// <summary>
    /// Joins <paramref name="docs"/> with <paramref name="separator"/> between consecutive items.
    /// </summary>
    public static Doc Join(Doc separator, IReadOnlyList<Doc> docs)
    {
        if (docs.Count == 0) return Empty;
        if (docs.Count == 1) return docs[0];

        var parts = new List<Doc>(docs.Count * 2 - 1);
        for (int i = 0; i < docs.Count; i++)
        {
            if (i > 0) parts.Add(separator);
            parts.Add(docs[i]);
        }

        return new ConcatDoc(parts.ToArray());
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private static void Flatten(Doc[] parts, List<Doc> output)
    {
        foreach (var part in parts)
        {
            if (part is ConcatDoc concat && concat.Parts.Length == 0)
            {
                // This is Empty — skip it
                continue;
            }
            if (part is ConcatDoc nested)
            {
                Flatten(nested.Parts, output);
            }
            else
            {
                output.Add(part);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Node types
    // -------------------------------------------------------------------------

    /// <summary>A literal text node.</summary>
    public sealed class TextDoc : Doc
    {
        public string Value { get; }
        public TextDoc(string value) => Value = value;
    }

    /// <summary>A space in flat mode; a line break in break mode.</summary>
    public sealed class LineDoc : Doc
    {
        internal LineDoc() { }
    }

    /// <summary>Nothing in flat mode; a line break in break mode.</summary>
    public sealed class SoftLineDoc : Doc
    {
        internal SoftLineDoc() { }
    }

    /// <summary>Always a line break.</summary>
    public sealed class HardLineDoc : Doc
    {
        internal HardLineDoc() { }
    }

    /// <summary>Increases the indentation level for its contents.</summary>
    public sealed class IndentDoc : Doc
    {
        public Doc Contents { get; }
        public IndentDoc(Doc contents) => Contents = contents;
    }

    /// <summary>Decreases the indentation level for its contents.</summary>
    public sealed class DedentDoc : Doc
    {
        public Doc Contents { get; }
        public DedentDoc(Doc contents) => Contents = contents;
    }

    /// <summary>Tries flat mode; switches to break mode if the contents exceed the line width.</summary>
    public sealed class GroupDoc : Doc
    {
        public Doc Contents { get; }
        public GroupDoc(Doc contents) => Contents = contents;
    }

    /// <summary>Greedily packs parts onto lines.</summary>
    public sealed class FillDoc : Doc
    {
        public Doc[] Parts { get; }
        public FillDoc(Doc[] parts) => Parts = parts;
    }

    /// <summary>Selects one of two documents based on the enclosing group's break/flat state.</summary>
    public sealed class IfBreakDoc : Doc
    {
        public Doc BreakContents { get; }
        public Doc FlatContents { get; }
        public IfBreakDoc(Doc breakContents, Doc flatContents)
        {
            BreakContents = breakContents;
            FlatContents = flatContents;
        }
    }

    /// <summary>Defers rendering its contents to the end of the current line.</summary>
    public sealed class LineSuffixDoc : Doc
    {
        public Doc Contents { get; }
        public LineSuffixDoc(Doc contents) => Contents = contents;
    }

    /// <summary>Concatenation of multiple documents.</summary>
    public sealed class ConcatDoc : Doc
    {
        public Doc[] Parts { get; }
        public ConcatDoc(Doc[] parts) => Parts = parts;
    }
}
