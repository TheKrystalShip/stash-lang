namespace Stash.Lsp.Completion.Providers.Dot;

using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Stdlib;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Strategy 3 — Dot-access on a variable/parameter/struct instance or a struct type, user enum,
/// or built-in struct field access.
/// Also handles the qualified namespace-enum prefix (e.g., <c>task.Status.</c>) as a sub-branch.
/// </summary>
/// <remarks>
/// <para>
/// The strategy performs the following resolution in order:
/// </para>
/// <list type="number">
///   <item>
///     If the prefix resolves to a variable/parameter/loop-var, use the narrowed type hint
///     or declared type hint to find a user-defined struct definition, then emit its
///     fields and methods.
///   </item>
///   <item>
///     Fallback: check built-in structs from <see cref="StdlibRegistry.Structs"/> using
///     the resolved struct name.
///   </item>
///   <item>
///     If the prefix is a user-defined struct type, emit its fields and methods directly.
///   </item>
///   <item>
///     If the prefix is a user-defined enum, emit its members.
///   </item>
///   <item>
///     If none of the above yielded results and the prefix contains a dot (e.g.,
///     <c>task.Status</c>), attempt to resolve a built-in namespace's nested enum
///     (e.g., <c>task.Status.</c> → members of the <c>Status</c> enum in namespace
///     <c>task</c>).
///   </item>
/// </list>
/// <para>
/// <strong>Gating semantics:</strong> This strategy runs whenever strategies 1 and 2 did
/// not short-circuit. Its candidates accumulate alongside the UfcsDotStrategy (strategy 4),
/// which is invoked immediately after — the two run in parallel and both contribute to the
/// same candidate list.
/// </para>
/// <para>
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>120</c> (strategy 3 of 6).
/// </para>
/// </remarks>
public sealed class StructOrUserEnumDotStrategy : IDotStrategy
{
    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Apply(
        CompletionContext ctx,
        string prefix,
        DotResolutionContext resolution)
    {
        if (ctx.Analysis is null) yield break;

        var prefixDef = resolution.PrefixDef;
        var structName = resolution.StructName;
        var allSymbols = ctx.Analysis.Symbols.All;

        // ── Instance or variable resolution (prefixDef is a variable/param/loop-var) ──
        if (prefixDef == null || prefixDef.Kind != StashSymbolKind.Struct)
        {
            // Check if the resolved structName matches a user-defined struct
            var structDef = allSymbols.FirstOrDefault(s => s.Name == structName && s.Kind == StashSymbolKind.Struct);
            if (structDef != null)
            {
                foreach (var sym in allSymbols.Where(s =>
                    s.ParentName == structName &&
                    (s.Kind == StashSymbolKind.Field || s.Kind == StashSymbolKind.Method)))
                {
                    yield return new CompletionCandidate(
                        Label: sym.Name,
                        Kind: sym.Kind == StashSymbolKind.Method
                            ? LspCompletionItemKind.Method
                            : LspCompletionItemKind.Field,
                        Detail: sym.Detail,
                        SourcePriority: 120,
                        SourceTag: nameof(StructOrUserEnumDotStrategy),
                        Accessibility: SymbolAccessibility.BareIdentifier);
                }

                // Struct instance found — enum/built-in-struct checks below are not needed.
                yield break;
            }

            // Fallback: check built-in structs (e.g., a variable typed as a stdlib struct)
            var builtInStruct = StdlibRegistry.Structs.FirstOrDefault(s => s.Name == structName);
            if (builtInStruct != null)
            {
                foreach (var field in builtInStruct.Fields)
                {
                    var fieldDetail = field.Type != null
                        ? $"field of {structName}: {field.Type}"
                        : $"field of {structName}";

                    yield return new CompletionCandidate(
                        Label: field.Name,
                        Kind: LspCompletionItemKind.Field,
                        Detail: fieldDetail,
                        SourcePriority: 120,
                        SourceTag: nameof(StructOrUserEnumDotStrategy),
                        Accessibility: SymbolAccessibility.BareIdentifier);
                }
            }
        }

        // ── Struct type dot-access (prefixDef.Kind == Struct) ──────────────────
        if (prefixDef != null && prefixDef.Kind == StashSymbolKind.Struct)
        {
            foreach (var sym in allSymbols.Where(s =>
                s.ParentName == prefix &&
                (s.Kind == StashSymbolKind.Field || s.Kind == StashSymbolKind.Method)))
            {
                yield return new CompletionCandidate(
                    Label: sym.Name,
                    Kind: sym.Kind == StashSymbolKind.Method
                        ? LspCompletionItemKind.Method
                        : LspCompletionItemKind.Field,
                    Detail: sym.Detail,
                    SourcePriority: 120,
                    SourceTag: nameof(StructOrUserEnumDotStrategy),
                    Accessibility: SymbolAccessibility.BareIdentifier);
            }
        }

        // ── User enum members (e.g., Color.Red, Color.Green) ───────────────────
        var enumDef = allSymbols.FirstOrDefault(s => s.Name == prefix && s.Kind == StashSymbolKind.Enum);
        if (enumDef != null)
        {
            foreach (var sym in allSymbols.Where(s =>
                s.ParentName == prefix && s.Kind == StashSymbolKind.EnumMember))
            {
                yield return new CompletionCandidate(
                    Label: sym.Name,
                    Kind: LspCompletionItemKind.EnumMember,
                    Detail: sym.Detail,
                    SourcePriority: 120,
                    SourceTag: nameof(StructOrUserEnumDotStrategy),
                    Accessibility: SymbolAccessibility.BareIdentifier);
            }
        }

        // ── Built-in namespace nested-enum sub-branch (e.g., task.Status.) ─────
        // Handles dot-access on a dotted prefix that refers to a namespace's nested enum:
        // e.g., prefix = "task.Status" → ns = "task", enum = "Status". The dotted-prefix
        // check itself acts as the gate (only triggered when the prefix is qualified).
        if (prefix.Contains('.'))
        {
            int dotIndex = prefix.LastIndexOf('.');
            string nsName = prefix[..dotIndex];
            string enumName = prefix[(dotIndex + 1)..];
            if (StdlibRegistry.IsBuiltInNamespace(nsName))
            {
                var nsEnumDef = allSymbols.FirstOrDefault(s =>
                    s.Name == enumName &&
                    s.Kind == StashSymbolKind.Enum &&
                    s.ParentName == nsName);
                if (nsEnumDef != null)
                {
                    foreach (var sym in allSymbols.Where(s =>
                        s.ParentName == enumName && s.Kind == StashSymbolKind.EnumMember))
                    {
                        yield return new CompletionCandidate(
                            Label: sym.Name,
                            Kind: LspCompletionItemKind.EnumMember,
                            Detail: sym.Detail,
                            SourcePriority: 120,
                            SourceTag: nameof(StructOrUserEnumDotStrategy),
                            Accessibility: SymbolAccessibility.BareIdentifier);
                    }
                }
            }
        }
    }
}
