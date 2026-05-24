namespace Stash.Lsp.Completion.Providers.Dot;

using Stash.Analysis;

/// <summary>
/// Holds shared prefix-resolution data that <see cref="DotCompletionProvider"/> computes
/// exactly once per request and distributes to all <see cref="IDotStrategy"/> implementations.
/// </summary>
/// <remarks>
/// <para>
/// Several dot strategies need the same information about the receiver — the symbol
/// visible at the cursor position, its resolved struct/type name, and the UFCS namespace
/// for that type. Computing this once in the provider avoids redundant scope tree walks.
/// </para>
/// <para>
/// Strategies 1 (<c>BuiltInNamespaceDotStrategy</c>) and 2 (<c>ImportAliasDotStrategy</c>)
/// do not consume this context because they resolve the prefix against global registries,
/// not the cursor-local symbol table. They receive it for signature uniformity only.
/// </para>
/// </remarks>
/// <param name="PrefixDef">
/// The <see cref="SymbolInfo"/> for the identifier before the dot at the cursor position,
/// or <see langword="null"/> when no visible symbol matches the prefix.
/// </param>
/// <param name="StructName">
/// The resolved struct/type name for the prefix. For variables/parameters/loop vars this
/// is the narrowed type hint or declared type hint; for all other symbols it equals the
/// prefix itself. Used by StructOrUserEnum and UFCS strategies.
/// </param>
public sealed record DotResolutionContext(
    SymbolInfo? PrefixDef,
    string StructName);
