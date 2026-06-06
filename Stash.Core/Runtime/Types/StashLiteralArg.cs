namespace Stash.Runtime.Types;

using System;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a single literal token in a command expression (<c>$(cmd arg)</c>),
/// carrying both the resolved text and a flag indicating whether shell expansion
/// (tilde and glob) should be applied when building the final argv.
/// </summary>
/// <remarks>
/// <para>
/// This type is intentionally not user-constructible from Stash code. It is only
/// produced by the bytecode compiler during command lowering (Phase B), and is
/// stored in the constant pool as tag 18.
/// </para>
/// <para>
/// When <see cref="ShouldExpand"/> is <see langword="true"/>, the token came from
/// unquoted literal source text and is eligible for tilde (<c>~</c>) and glob
/// expansion at call time. When <see langword="false"/>, the token was enclosed in
/// source-level quotes and must be passed through verbatim as a single argv element.
/// </para>
/// </remarks>
public sealed class StashLiteralArg : IVMTyped, IVMStringifiable, IVMTruthiness
{
    /// <summary>Initialises a new literal argument token.</summary>
    /// <param name="text">The resolved text of the argument.</param>
    /// <param name="shouldExpand">
    /// <see langword="true"/> when the token came from unquoted source text
    /// (eligible for tilde and glob expansion); <see langword="false"/> when
    /// source-quoted (verbatim).
    /// </param>
    public StashLiteralArg(string text, bool shouldExpand)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        ShouldExpand = shouldExpand;
    }

    /// <summary>The resolved text of the argument token.</summary>
    public string Text { get; }

    /// <summary>
    /// When <see langword="true"/>, the token came from unquoted source literal text
    /// and should have tilde and glob expansion applied. When <see langword="false"/>,
    /// the token was source-quoted and should be passed verbatim.
    /// </summary>
    public bool ShouldExpand { get; }

    // IVMTyped
    public string VMTypeName => "LiteralArg";

    // IVMStringifiable
    public string VMToString() => Text;

    // IVMTruthiness
    /// <summary>
    /// Internal-only invariant: <see cref="StashLiteralArg"/> is never reachable as a
    /// user-visible Stash value — it is consumed by the VM command-dispatch path before
    /// any user-facing truthiness check can observe it. This truthiness rule is therefore
    /// not part of the §Values and Types spec surface.
    /// </summary>
    public bool VMIsFalsy => string.IsNullOrEmpty(Text);

    public override bool Equals(object? obj) =>
        obj is StashLiteralArg other &&
        string.Equals(Text, other.Text, StringComparison.Ordinal) &&
        ShouldExpand == other.ShouldExpand;

    public override int GetHashCode() => HashCode.Combine(Text, ShouldExpand);

    public override string ToString() => Text;
}
