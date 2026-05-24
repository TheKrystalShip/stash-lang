namespace Stash.Lsp.Completion.Providers;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Provides type-name completion candidates for <c>extend</c> declarations.
/// Emits built-in extendable types first, then user-defined struct names from the current
/// analysis result, with deduplication that prevents built-in names from appearing twice
/// if a user defines a struct with the same name.
/// </summary>
/// <remarks>
/// <para>
/// Applies exclusively to <see cref="CompletionMode.AfterExtend"/> mode.
/// </para>
/// <para>
/// Dedup behaviour mirrors <c>BuildExtendTypeCompletionList</c>: the built-in type names
/// are seeded into a <see cref="HashSet{T}"/> before user structs are enumerated, so
/// built-ins always win on collision.
/// </para>
/// <para>
/// The extendable built-in types are hardcoded here to match the runtime whitelist in
/// <c>Stash.Bytecode/Compilation/Compiler.Declarations.cs</c> (the <c>isBuiltIn</c> check).
/// Only these five types are accepted by the runtime's <c>extend</c> execution path; all
/// other primitive types (including <c>byte</c>, <c>duration</c>, <c>range</c>, etc.)
/// produce a <c>RuntimeError: Cannot extend '...': not a known type.</c> error.
/// If the runtime list changes, update <see cref="BuiltInExtendableTypes"/> to match.
/// </para>
/// </remarks>
public sealed class ExtendTypeCompletionProvider : ICompletionProvider
{
    /// <summary>
    /// The canonical set of built-in types that the Stash runtime accepts as
    /// <c>extend</c> targets. Matches the <c>isBuiltIn</c> check in
    /// <c>Stash.Bytecode/Compilation/Compiler.Declarations.cs</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> BuiltInExtendableTypes =
        ["array", "dict", "float", "int", "string"];

    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) => ctx.Mode == CompletionMode.AfterExtend;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        // Built-in extendable types — always emitted first
        foreach (var typeName in BuiltInExtendableTypes)
        {
            yield return new CompletionCandidate(
                Label: typeName,
                Kind: LspCompletionItemKind.TypeParameter,
                Detail: "built-in type",
                SourcePriority: 10,
                SourceTag: nameof(ExtendTypeCompletionProvider));
        }

        // User-defined struct names from analysis (dedup against built-in names)
        if (ctx.Analysis == null)
        {
            yield break;
        }

        var seen = new HashSet<string>(BuiltInExtendableTypes, StringComparer.Ordinal);
        foreach (var sym in ctx.Analysis.Symbols.All.Where(s => s.Kind == StashSymbolKind.Struct))
        {
            if (!seen.Add(sym.Name))
            {
                continue;
            }

            yield return new CompletionCandidate(
                Label: sym.Name,
                Kind: LspCompletionItemKind.Struct,
                Detail: sym.Detail,
                SourcePriority: 10,
                SourceTag: nameof(ExtendTypeCompletionProvider));
        }
    }
}
