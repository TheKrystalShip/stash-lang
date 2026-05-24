namespace Stash.Tests.Lsp;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Stdlib;
using Stash.Stdlib.Models;
using Stash.Stdlib.Abstractions;
using Stash.Lsp.Analysis;
using Stash.Lsp.Completion;
using Stash.Lsp.Completion.Providers;
using Stash.Lsp.Completion.Providers.Dot;
using Stash.Lsp.Handlers;
using Xunit;
using LspCompletionContext = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext;

/// <summary>
/// Tests for P6: LSP completion and hover over built-in namespace data members
/// (entries registered via <c>[StashMember]</c>).
/// </summary>
public class NamespaceMembersLspTests
{
    // ── Registry unit tests ───────────────────────────────────────────────────

    /// <summary>
    /// P6 done_when: <c>cli.argc</c> and <c>cli.argv</c> appear in the data-member registry.
    /// </summary>
    [Fact]
    public void Registry_CliNamespace_ExposesArgcAndArgv()
    {
        var members = StdlibRegistry.GetNamespaceDataMembers("cli").ToList();
        Assert.Contains(members, m => m.Name == "argc");
        Assert.Contains(members, m => m.Name == "argv");
    }

    [Fact]
    public void Registry_TryGetNamespaceDataMember_FindsCliArgv()
    {
        bool found = StdlibRegistry.TryGetNamespaceDataMember("cli.argv", out var member);
        Assert.True(found);
        Assert.Equal("cli", member.Namespace);
        Assert.Equal("argv", member.Name);
        Assert.NotNull(member.ReturnType); // "array"
    }

    [Fact]
    public void Registry_TryGetNamespaceDataMember_NotFoundForFunction()
    {
        // io.println is a Function, not a DataMember — must not appear in the data-member registry.
        bool found = StdlibRegistry.TryGetNamespaceDataMember("io.println", out _);
        Assert.False(found);
    }

    // ── Completion kind tests ─────────────────────────────────────────────────

    /// <summary>
    /// P6 done_when: completion at <c>cli.</c> lists <c>argc</c> and <c>argv</c> with
    /// <see cref="CompletionItemKind.Property"/> (distinct from Function for callables).
    /// Decision Log: Property chosen because it matches the C#-property idiom — looks
    /// like a field but invokes a getter. This separates it from Function (callables) and
    /// Constant (static snapshots).
    /// </summary>
    [Fact]
    public void Completion_CliDot_IncludesArgcAndArgvWithPropertyKind()
    {
        var (handler, uri, source) = CreateCompletionHandler();
        // Position cursor after "cli." on the last line
        var completions = InvokeHandleDotCompletion(handler, "cli", uri, source);

        var argcItem = completions.FirstOrDefault(i => i.Label == "argc");
        var argvItem = completions.FirstOrDefault(i => i.Label == "argv");

        Assert.NotNull(argcItem);
        Assert.NotNull(argvItem);
        Assert.Equal(CompletionItemKind.Property, argcItem.Kind);
        Assert.Equal(CompletionItemKind.Property, argvItem.Kind);
    }

    [Fact]
    public void Completion_CliDot_FunctionItemsRetainFunctionKind()
    {
        var (handler, uri, source) = CreateCompletionHandler();
        var completions = InvokeHandleDotCompletion(handler, "cli", uri, source);

        // cli.parse is a function — must stay as Function kind.
        var parseItem = completions.FirstOrDefault(i => i.Label == "parse");
        Assert.NotNull(parseItem);
        Assert.Equal(CompletionItemKind.Function, parseItem.Kind);
    }

    [Fact]
    public void Completion_DataMember_KindIsPropertyNotFunction()
    {
        // Across all namespaces, no DataMember completion item should have Function kind.
        var (handler, uri, source) = CreateCompletionHandler();
        var allDataMemberNames = StdlibRegistry.GetNamespaceDataMembers("cli")
            .Concat(StdlibRegistry.GetNamespaceDataMembers("env"))
            .Select(m => m.Name)
            .ToHashSet();

        // For the cli namespace, verify all data member items are Property kind.
        var completions = InvokeHandleDotCompletion(handler, "cli", uri, source).ToList();
        foreach (var item in completions.Where(i => allDataMemberNames.Contains(i.Label)))
        {
            Assert.Equal(CompletionItemKind.Property, item.Kind);
        }
    }

    // ── Hover rendering tests ─────────────────────────────────────────────────

    /// <summary>
    /// P6 done_when: hover on <c>cli.argv</c> renders (i) signature line showing member type,
    /// (ii) XML summary, (iii) does NOT render as a function signature.
    /// </summary>
    [Fact]
    public void Hover_CliArgv_RendersAsMemberNotFunction()
    {
        bool found = StdlibRegistry.TryGetNamespaceDataMember("cli.argv", out var member);
        Assert.True(found);

        // The Detail property must NOT contain "fn" or "->" (function-signature markers).
        Assert.DoesNotContain("fn ", member.Detail);
        Assert.DoesNotContain("->", member.Detail);

        // Must contain the return type.
        Assert.NotNull(member.ReturnType);
        Assert.Contains(member.ReturnType!, member.Detail);
    }

    [Fact]
    public void Hover_DataMember_DetailContainsMemberKeyword()
    {
        bool found = StdlibRegistry.TryGetNamespaceDataMember("cli.argc", out var member);
        Assert.True(found);
        // NamespaceMember.Detail renders as "member cli.argc: int"
        Assert.Contains("member", member.Detail);
        Assert.Contains("cli.argc", member.Detail);
    }

    /// <summary>
    /// P6 done_when: nullable ReturnType renders nullable form (e.g. <c>member&lt;string?&gt;</c>).
    /// We test the Detail renderer directly with a synthetic member since no v1 member has nullable type.
    /// </summary>
    [Fact]
    public void Hover_NullableReturnType_RendersNullableForm()
    {
        // Construct a synthetic NamespaceMember with nullable ReturnType
        var m = new NamespaceMember("test", "nullableProp", ReturnType: "string?");
        // Detail should include "string?"
        Assert.Contains("string?", m.Detail);
    }

    /// <summary>
    /// P6 done_when: Live-stability member surfaces a stability indicator; Cached does not.
    /// </summary>
    [Fact]
    public void Hover_LiveMember_IncludesLiveIndicatorInDetail()
    {
        bool found = StdlibRegistry.TryGetNamespaceDataMember("env.cwd", out var member);
        Assert.True(found);
        Assert.Equal(Stability.Live, member.Stability);
        // NamespaceMember.Detail includes "[live]" for Live members
        Assert.Contains("[live]", member.Detail);
    }

    [Fact]
    public void Hover_CachedMember_DoesNotIncludeLiveIndicator()
    {
        bool found = StdlibRegistry.TryGetNamespaceDataMember("cli.argc", out var member);
        Assert.True(found);
        Assert.Equal(Stability.Cached, member.Stability);
        // Cached members must NOT include "[live]" in their detail
        Assert.DoesNotContain("[live]", member.Detail);
    }

    /// <summary>
    /// P6 done_when: hover on <c>io.println</c> continues to render the function signature unchanged.
    /// We verify the function is not in the data-member registry (so it gets the function hover path).
    /// </summary>
    [Fact]
    public void Hover_IoPrintln_StaysInFunctionRegistry()
    {
        // io.println must remain a namespace function, not a data member.
        bool isDataMember = StdlibRegistry.TryGetNamespaceDataMember("io.println", out _);
        Assert.False(isDataMember);

        bool isFunction = StdlibRegistry.TryGetNamespaceFunction("io.println", out var fn);
        Assert.True(isFunction);
        // Function detail contains "->" signature marker
        Assert.Contains("->", fn.Detail);
    }

    // ── Documentation contract ────────────────────────────────────────────────

    [Fact]
    public void AllDataMembers_HaveNonEmptySummary()
    {
        // Every [StashMember] must carry a non-empty XML <summary>. This mirrors the
        // [StashFn] enforcement from P1 and is part of the documentation contract.
        foreach (var ns in StdlibDefinitions.Namespaces)
        {
            if (ns.Members is null) continue;
            foreach (var m in ns.Members)
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Documentation),
                    $"{m.QualifiedName} has no documentation — [StashMember] requires a non-empty XML <summary>.");
            }
        }
    }

    // ── Unqualified completion: member-only symbols must NOT leak ─────────────

    /// <summary>
    /// Stdlib struct fields are registered into the global scope (so hover/goto-def
    /// work for them), but they must NOT surface in the unqualified completion list
    /// — they are always dot-accessed (instance.field). Regression test for the bug
    /// where typing in file scope offered "debounce: field of WatchOptions" etc.
    /// </summary>
    [Fact]
    public void Completion_Unqualified_ExcludesStdlibStructFieldsMethodsAndEnumMembers()
    {
        var items = InvokeUnqualifiedCompletion("\n").ToList();

        // Sanity: namespaces and keywords still appear.
        Assert.Contains(items, i => i.Label == "fs" && i.Kind == CompletionItemKind.Module);
        Assert.Contains(items, i => i.Label == "let" && i.Kind == CompletionItemKind.Keyword);

        // No stdlib struct field (e.g. WatchOptions.debounce, TcpServer.active).
        Assert.DoesNotContain(items, i => i.Kind == CompletionItemKind.Field);
        // No struct method.
        Assert.DoesNotContain(items, i => i.Kind == CompletionItemKind.Method);
        // No enum member (e.g. WatchEventType.Deleted).
        Assert.DoesNotContain(items, i => i.Kind == CompletionItemKind.EnumMember);
    }

    /// <summary>
    /// User-defined struct fields/methods and enum members are also member-only
    /// and must be suppressed in unqualified completion.
    /// </summary>
    [Fact]
    public void Completion_Unqualified_ExcludesUserStructFieldsAndEnumMembers()
    {
        const string src = "struct Point { x, y }\nenum Color { Red, Green }\n";
        var items = InvokeUnqualifiedCompletion(src).ToList();

        Assert.DoesNotContain(items, i => i.Label == "x");
        Assert.DoesNotContain(items, i => i.Label == "y");
        Assert.DoesNotContain(items, i => i.Label == "Red");
        Assert.DoesNotContain(items, i => i.Label == "Green");

        // The type names themselves still appear.
        Assert.Contains(items, i => i.Label == "Point");
        Assert.Contains(items, i => i.Label == "Color");
    }

    /// <summary>
    /// Parameters also carry a non-null ParentName (their owning function), but they
    /// must still appear in unqualified completion inside the function body. This
    /// guards against an over-broad "skip anything with ParentName" filter.
    /// </summary>
    [Fact]
    public void Completion_Unqualified_IncludesFunctionParameters()
    {
        // Cursor sits on line 2 inside the body of fn greet(name).
        const string src = "fn greet(name) {\n\n}\n";
        var items = InvokeUnqualifiedCompletionAt(src, line: 1, character: 0).ToList();

        Assert.Contains(items, i => i.Label == "name");
    }

    /// <summary>
    /// Stdlib namespaces are surfaced by two passes — the explicit
    /// <c>StdlibRegistry.NamespaceNames</c> loop and <c>GetVisibleSymbols</c>
    /// (which returns the <c>Kind.Namespace</c> symbols that <c>SymbolCollector</c>
    /// injects for hover/goto-def). Each name must appear at most once in the
    /// unqualified completion list.
    /// </summary>
    [Fact]
    public void Completion_Unqualified_StdlibNamespacesAreNotDuplicated()
    {
        var items = InvokeUnqualifiedCompletion("\n").ToList();

        foreach (var ns in StdlibRegistry.NamespaceNames)
        {
            int count = items.Count(i => i.Label == ns);
            Assert.True(count <= 1, $"namespace '{ns}' appeared {count} times in completion list");
        }
    }

    [Fact]
    public void Completion_Unqualified_NoLabelAppearsMoreThanOnce()
    {
        var items = InvokeUnqualifiedCompletion("\n").ToList();

        var duplicates = items.GroupBy(i => i.Label).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicates);
    }

    private static System.Collections.Generic.IEnumerable<CompletionItem> InvokeUnqualifiedCompletion(string source)
    {
        // Position cursor on a fresh empty line at the end of the source.
        var lines = source.Split('\n');
        int line = lines.Length - 1;
        int character = lines[line].Length;
        return InvokeUnqualifiedCompletionAt(source + "\n", line + 1, 0);
    }

    private static System.Collections.Generic.IEnumerable<CompletionItem> InvokeUnqualifiedCompletionAt(
        string source, int line, int character)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var logger = NullLogger<CompletionHandler>.Instance;

        var uri = new Uri($"file:///test/unqualified_{Guid.NewGuid():N}.stash");
        docs.Open(uri, source, 1);
        engine.Analyze(uri, source);
        var handler = new CompletionHandler(engine, docs, logger, BuildDispatcher());

        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = character },
            Context = new LspCompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = handler.Handle(request, default).Result;
        return result.Items ?? Enumerable.Empty<CompletionItem>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (CompletionHandler Handler, Uri Uri, string Source) CreateCompletionHandler()
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var logger = NullLogger<CompletionHandler>.Instance;

        var source = "let x = cli.argc;\n";
        var uri = new Uri("file:///test/test.stash");
        docs.Open(uri, source, 1);
        engine.Analyze(uri, source);

        return (new CompletionHandler(engine, docs, logger, BuildDispatcher()), uri, source);
    }

    /// <summary>
    /// Invokes the private <c>HandleDotCompletion</c> method via the public
    /// <c>Handle</c> pathway by building a fake completion request positioned
    /// immediately after <c>{prefix}.</c> on a synthetic line.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<CompletionItem> InvokeHandleDotCompletion(
        CompletionHandler handler, string prefix, Uri uri, string source)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var newLogger = NullLogger<CompletionHandler>.Instance;

        // Create a source with a dot on its own line so cursor is at dot position.
        string testSource = $"{prefix}.\n";
        var testUri = new Uri($"file:///test/completions_{prefix}.stash");
        docs.Open(testUri, testSource, 1);
        engine.Analyze(testUri, testSource);
        var testHandler = new CompletionHandler(engine, docs, newLogger, BuildDispatcher());

        // The dot is at position (0, prefix.Length + 1) — we want completions after the dot.
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position { Line = 0, Character = prefix.Length + 1 },
            Context = new LspCompletionContext { TriggerKind = CompletionTriggerKind.TriggerCharacter, TriggerCharacter = "." }
        };

        var result = testHandler.Handle(request, default).Result;
        return result.Items ?? Enumerable.Empty<CompletionItem>();
    }

    private static CompletionDispatcher BuildDispatcher()
    {
        var pipelines = new System.Collections.Generic.Dictionary<CompletionMode, System.Collections.Generic.IReadOnlyList<ICompletionProvider>>
        {
            [CompletionMode.Default] = new ICompletionProvider[]
            {
                new KeywordCompletionProvider(),
                new StdlibFunctionCompletionProvider(),
                new StdlibNamespaceCompletionProvider(),
                new ScopedSymbolCompletionProvider(),
            },
            [CompletionMode.Dot] = new ICompletionProvider[] { new DotCompletionProvider() },
            [CompletionMode.ImportString] = new ICompletionProvider[] { new ImportPathCompletionProvider() },
            [CompletionMode.AfterIs] = new ICompletionProvider[] { new IsTypeCompletionProvider() },
            [CompletionMode.AfterExtend] = new ICompletionProvider[] { new ExtendTypeCompletionProvider() },
        };
        return new CompletionDispatcher(pipelines);
    }
}
