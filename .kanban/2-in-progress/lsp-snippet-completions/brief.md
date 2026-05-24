# RFC: LSP Snippet Completions

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-05-24
> **Slug:** lsp-snippet-completions

## Summary

Move Stash editor snippets from the VS Code extension (`.vscode/extensions/stash-lang/snippets/stash.json`,
declared via `package.json#contributes.snippets`) into the **LSP server** as a first-class
`ICompletionProvider`. After this feature, the LSP is the single source of truth for snippet
completions: any LSP-aware client (VS Code, Neovim, Helix, Zed) gets the same snippet set with no
client-side configuration.

The provider is built around a small `ISnippetRegistry` abstraction so that the v1 **bundled**
source can be joined later by **project-local** (`<project>/stash-snippets.json`) and
**user-global** (`~/.stash/snippets.json`) sources without rewriting the provider. Snippets are
validated aggressively at load time — bad definitions never silently disappear, and validation
failure never prevents the LSP from starting.

## Motivation

- **Editor lock-in.** Snippets only fire in VS Code today. Neovim and Helix users get nothing,
  even though they use the same LSP server.
- **Drift risk.** The extension's snippet JSON is hand-maintained and unverified. Several entries
  reference syntax constructs without any guarantee they still parse as valid Stash. As the
  language evolves (recent example: `try` expression form), snippets silently rot.
- **No context awareness.** VS Code fires every snippet in every position. A `return` snippet at
  the top of a file is noise; a `struct` snippet inside a function body is misleading. The LSP
  already has the `ScopeTree` to make these decisions; the extension does not.
- **One source of truth.** Future work — per-project shared snippets, per-user snippets — needs a
  server-side composition point. Putting that point in the VS Code extension would force every
  editor to reimplement it.

## Goals

- Ship a `SnippetCompletionProvider` (an `ICompletionProvider`) that emits snippet completions in
  Default mode through the existing dispatcher.
- One concrete source in v1: a JSON file embedded in the LSP binary. Same on-disk shape as VS
  Code's snippet JSON to keep migration trivial.
- `ISnippetRegistry` abstraction supports adding project-local and user-global sources later
  without changing the provider, with the precedence **user > project > bundled** wired in from
  v1 even though only `bundled` is registered.
- Aggressive load-time validation. Each snippet body must lex and parse as Stash; prefix shape,
  tabstop placeholder syntax, and uniqueness are checked.
- Validation failures surface loudly: `window/showMessage` (Error) + a structured `ILogger` entry,
  carrying the offending snippet name / source file / reason. The rest of the LSP keeps working.
- Context-awareness via a small scope vocabulary derived from `ScopeTree.FindScopeAt`. Snippets
  declare which scopes they may fire in; the provider gates on cursor scope.
- Delete the VS Code extension's `contributes.snippets` entry and `snippets/stash.json` in the
  same PR.

## Non-Goals

- **No new sources in v1.** Project-local and user-global registries are designed but not
  implemented — only `BundledSnippetRegistry` is wired in.
- **No file-watching.** Users restart the LSP to pick up new snippets. The registry interface is
  reload-friendly so this can be added later without provider churn.
- **No semantic validation.** No identifier resolution, no type checking, no static analyzer
  pass over snippet bodies. Validation bar is "the body lexes and parses as Stash."
- **No client-side rendering work.** Snippet rendering and tabstop navigation are LSP-client
  concerns. We just emit `CompletionItem.InsertTextFormat = Snippet`.
- **No change to `ICompletionProvider`.** Snippets fit the existing
  `Provide(CompletionContext) -> IEnumerable<CompletionCandidate>` shape. The only surface change
  is two optional fields on `CompletionCandidate`.
- **Not migrating the `.tpl` snippet file** (`snippets/stash-tpl.json`). The LSP today only
  serves `stash` language files. Template-language snippets stay in the VS Code extension and are
  called out under Open Questions.

## Design

### Surface

#### `CompletionCandidate` extension (additive, default-preserving)

```csharp
public sealed record CompletionCandidate(
    string Label,
    LspCompletionItemKind Kind,
    string? Detail = null,
    string? Documentation = null,
    int SourcePriority = 100,
    string SourceTag = "",
    SymbolAccessibility? Accessibility = null,
    // NEW (both default to current behavior):
    string? InsertText = null,
    InsertTextFormat InsertTextFormat = InsertTextFormat.PlainText);
```

When `InsertText is null`, the sink falls back to the current behavior of inserting `Label`
verbatim. When `InsertText` is non-null, the sink writes it into `CompletionItem.InsertText` and
copies `InsertTextFormat` into `CompletionItem.InsertTextFormat`. Existing providers don't pass
these arguments and continue to work unchanged.

#### `Snippet` record

```csharp
public sealed record Snippet(
    string Id,                  // unique key for diagnostics: "<source>:<prefix>:<scope>"
    string Prefix,              // completion trigger, e.g. "for"
    string DisplayName,         // human label, e.g. "For-In Loop"
    string Body,                // joined with "\n" if originally an array
    string? Description,
    SnippetScope Scope,         // one of the scope vocabulary values
    SnippetSourceKind Source);  // Bundled | Project | User
```

#### Scope vocabulary

Derived from `ScopeKind` (`Global | Function | Block | Loop`); no new analysis pass.

```csharp
public enum SnippetScope
{
    Any,        // fires in every Default-mode position
    TopLevel,   // ScopeKind.Global only
    FnBody,     // inside a ScopeKind.Function (transitively, via Parent walk)
    LoopBody,   // inside a ScopeKind.Loop
}
```

Deliberately **omitted** from v1: `struct-body`, `enum-body`, `interface-body`. Stash struct /
enum / interface declarations do not currently open a `Scope` of their own — gating on them
would require a new analysis pass, which the user requirements forbid. Recorded as a future
extension (Open Questions).

#### `SnippetContext` helper

```csharp
internal static class SnippetContext
{
    // Classifies the cursor's current scope into the snippet vocabulary.
    // Walks Scope.Parent until a Function/Loop ancestor is found, else returns TopLevel.
    public static SnippetScope Classify(ScopeTree tree, int line, int column);

    // Returns true iff the snippet may fire at the cursor scope.
    public static bool Matches(SnippetScope snippetScope, SnippetScope cursorScope);
}
```

Matching rule: `Any` matches every cursor scope; everything else matches by equality. (No
hierarchy: a `FnBody` snippet does not fire in a nested loop unless it also declares
`LoopBody` — but multi-scope is out of scope for v1; if it's needed, the JSON schema's `scope`
field becomes an array. See Open Questions.)

#### `ISnippetRegistry`

```csharp
public interface ISnippetRegistry
{
    // Source kind for precedence ordering. Higher precedence wins on duplicates.
    SnippetSourceKind Kind { get; }

    // Snapshot of currently-loaded snippets. Callers must not assume identity stability across
    // reload boundaries.
    IReadOnlyList<Snippet> Snapshot();

    // Triggered by future file-watcher integration; v1 calls once at startup.
    void Reload();
}

public sealed class BundledSnippetRegistry : ISnippetRegistry { /* embedded resource */ }
// Deferred (interface present, not registered in DI in v1):
public sealed class ProjectSnippetRegistry : ISnippetRegistry { /* per-workspace */ }
public sealed class UserSnippetRegistry    : ISnippetRegistry { /* ~/.stash/snippets.json */ }
```

A single `CompositeSnippetRegistry` (or equivalent composition site inside
`SnippetCompletionProvider`) merges registered registries with precedence
`User > Project > Bundled`. Conflict semantics are pinned in the Decision Log (Q4): collisions
are resolved per-`Id` where Id includes `(source, prefix, scope)` — different sources contributing
the *same `(prefix, scope)` pair* collapse to the higher-precedence one; same-source duplicates
fail validation.

#### `SnippetCompletionProvider`

```csharp
public sealed class SnippetCompletionProvider : ICompletionProvider
{
    public CompletionMode Mode => CompletionMode.Default;

    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        var cursorScope = SnippetContext.Classify(ctx.Analysis.ScopeTree, ctx.Line, ctx.Column);
        foreach (var snippet in _composite.Snapshot())
        {
            if (!SnippetContext.Matches(snippet.Scope, cursorScope)) continue;
            yield return new CompletionCandidate(
                Label: snippet.Prefix,
                Kind: LspCompletionItemKind.Snippet,
                Detail: snippet.DisplayName,
                Documentation: snippet.Description,
                SourcePriority: SnippetPriority,         // pinned below scoped symbols
                SourceTag: nameof(SnippetCompletionProvider),
                InsertText: snippet.Body,
                InsertTextFormat: InsertTextFormat.Snippet);
        }
    }
}
```

`SnippetPriority` is greater than `ScopedSymbolCompletionProvider`'s priority — when a snippet
prefix collides with an in-scope symbol of the same name, the symbol wins the sink's dedup race.
This preserves the current behavior of the VS Code extension (snippets never shadow user code).

#### `SnippetValidator`

```csharp
public static class SnippetValidator
{
    public sealed record Result(IReadOnlyList<Snippet> Valid, IReadOnlyList<SnippetLoadError> Errors);

    public static Result Validate(IEnumerable<RawSnippet> raw, SnippetSourceKind source);
}

public sealed record SnippetLoadError(string SnippetIdOrName, string SourceLocation, string Reason);
```

Validation contract (every check is mandatory; any failure rejects the snippet **and** records a
`SnippetLoadError`):

1. **Prefix shape.** Non-empty, matches `[A-Za-z_][A-Za-z0-9_.]*` (allows `test.it`, `test.beforeAll`).
2. **Body presence.** Non-empty after array-join.
3. **Lexes.** `Stash.Core.Lexer.Tokenize(strippedBody)` returns without throwing.
   Bodies are pre-processed by **stripping LSP tabstop / placeholder tokens**
   (`$N`, `${N}`, `${N:default}`) before lexing — the placeholder's *default text* (if present) is
   substituted in, otherwise a synthetic identifier `__snip_N` is substituted. This way placeholders
   that are meant to become identifiers don't break tokenization.
4. **Parses.** `Stash.Core.Parsing.Parser.Parse(tokens)` returns a non-empty statement list and
   produces no parse errors. (Decision Log Q1.)
5. **Tabstop syntax well-formed.** Tabstop scan accepts `$0`, `$N`, `${N}`, `${N:default}`,
   `${N|opt1,opt2|}` for `N >= 0`. Lone `$` not followed by a valid tabstop is allowed only when
   followed by `"` or `(` — the Stash interpolation prefix — to avoid false rejection of
   `\$"…"` bodies in the seed list.
6. **Scope value.** Resolves to a known `SnippetScope` value (case-insensitive); absent field
   defaults to `Any`.
7. **Per-source uniqueness.** `(prefix, scope)` is unique within the same source. Duplicates
   within one source are an error for **all** duplicates of that key (no silent first-wins).

`SnippetLoadError`s are surfaced as described under "Failure surfacing" below.

#### Snippet JSON schema (annotated)

The bundled file mirrors the existing VS Code snippet shape, with one **optional extension**:
`scope`. Example:

```json
{
  "For-In Loop": {
    "prefix": "for",
    "body": [
      "for (let ${1:item} in ${2:collection}) {",
      "\t$0",
      "}"
    ],
    "description": "For-in loop",
    "scope": "fn-body"
  }
}
```

Fields:

| Field         | Required | Type                | Notes                                              |
| ------------- | -------- | ------------------- | -------------------------------------------------- |
| (object key)  | yes      | string              | `DisplayName`                                      |
| `prefix`      | yes      | string              | Trigger; validated per the prefix-shape rule.      |
| `body`        | yes      | string \| string[]  | Array entries joined with `"\n"`.                  |
| `description` | no       | string              | Used as `CompletionItem.Documentation`.            |
| `scope`       | no       | string              | `any` (default), `top-level`, `fn-body`, `loop-body`. |

#### Failure surfacing

The DI registration site (`StashLanguageServer.cs`) constructs `BundledSnippetRegistry` eagerly.
The registry's constructor invokes the validator and stores **both** the valid snippets and the
errors. If `errors.Count > 0`:

- For each error, log one entry via `Microsoft.Extensions.Logging.ILogger.LogError` with a
  structured payload `(SnippetId, SourceLocation, Reason)`.
- Publish a single OmniSharp notification via the injected `ILanguageServerFacade.Window.ShowMessage(...)`
  with severity `MessageType.Error` and a message like
  `"Stash snippets: 3 invalid in 'bundled' source. See LSP log."` (severity is `Error` per
  user requirement #4).
- The registry still returns the **valid** subset from `Snapshot()`. The provider continues
  serving the good snippets.
- Critically: **no exception escapes the registry constructor**. The LSP starts even if every
  bundled snippet is invalid. Verified by a regression test that injects a corrupted bundle.

### Semantics

- **Mode.** Snippets fire only in `CompletionMode.Default`. Dot mode (member access), import-path
  mode, and `is`-type mode never see snippet candidates. The dispatcher enforces this by virtue
  of the provider's `Mode` property.
- **Dedup vs scoped symbols.** Snippet `SourcePriority` is numerically greater than
  `ScopedSymbolCompletionProvider`'s — so a user variable named `for` wins the sink's first-add
  race and the snippet is silently dropped. (Decision Log Q9.)
- **Cursor scope.** A snippet with `scope: fn-body` fires when the cursor is inside any
  transitive `ScopeKind.Function` ancestor. A snippet with `scope: top-level` fires only when
  the immediate enclosing scope is `Global` (cursor above all function/loop bodies).
- **Reload semantics.** v1 only ever calls `Reload()` once (at construction). The method exists
  on the interface so file-watching can be added later without rewriting the provider.

### Implementation Path

`CompletionCandidate` gains two optional fields (additive, no provider edits) ->
`CompletionItemSink.Add` populates `CompletionItem.InsertText` / `InsertTextFormat` when present ->
`SnippetValidator` + `BundledSnippetRegistry` load and validate an embedded JSON resource at
construction time, surfacing errors via `window/showMessage` and `ILogger` without throwing ->
`SnippetCompletionProvider` (registered for `CompletionMode.Default`) gates each snippet on
`SnippetContext.Classify` and emits a `CompletionCandidate` with `Kind = Snippet` and
`InsertTextFormat = Snippet` -> snippet JSON migrated from `.vscode/extensions/stash-lang/snippets/stash.json`
into `Stash.Lsp/Completion/Snippets/bundled.json` (embedded resource); the extension's
`contributes.snippets` entry is removed and the JSON file deleted in the same PR.

## Acceptance Criteria

- A user typing `fo` in a function body in any LSP-aware editor sees `for` (For-In Loop) and
  `fori` (C-style for loop) as completion items with `InsertTextFormat.Snippet`, and accepting one
  inserts a body with tabstops.
- A user typing `fo` at top-level (outside any function) **does not** see the `for` snippet
  (gated to `fn-body`); other top-level snippets like `fn`, `struct`, `import` still appear.
- An in-scope user variable named `for` shadows the snippet (variable appears, snippet does not),
  because the symbol provider's priority is lower-numbered than the snippet provider's.
- Corrupting `bundled.json` (deleting a required field, breaking JSON syntax, or inserting a
  snippet whose body is `let x =` — unparseable) causes:
  - LSP still starts and serves hover/definition/etc. on `.stash` files.
  - VS Code displays an Error-severity notification naming the offending snippet(s) and source.
  - Other (valid) snippets continue to appear in completion.
- After migration, `.vscode/extensions/stash-lang/package.json` contains no `contributes.snippets`
  entry and `.vscode/extensions/stash-lang/snippets/stash.json` is deleted; the snippets still
  appear in VS Code completion via the LSP.

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Each phase has a concrete `done_when`
list there. Phase IDs are `P1`-`P5`.

## Open Questions

- **Stash Template (`.tpl`) snippets.** `snippets/stash-tpl.json` is for the template language,
  which the LSP does not serve. Leave in the extension for v1; revisit when the LSP grows a
  template document selector.
- **Multi-scope snippets.** If a snippet should fire in *both* `fn-body` and `loop-body`, do we
  need an array form (`"scope": ["fn-body", "loop-body"]`)? Defer to a follow-up; the JSON field
  name `scope` is reserved so a future array form is backward-compatible (string vs array
  discriminated by token kind at load time).
- **Struct/enum/interface body context.** Requires extending `SymbolCollector` to open a scope
  for these declarations. Tracked as a backlog item, not in this feature.
- **Telemetry/metrics.** Should snippet expansions be counted (e.g. `SnippetsExpanded` counter)?
  Out of scope unless LSP gains a metrics surface.

## Decision Log

| Date       | Decision                                                                                                         | Rationale                                                                                                                                          |
| ---------- | ---------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-05-24 | **Q1: Validation bar = lex + parse (no semantic resolution).**                                                  | Catches syntactic rot (the real motivation) without rejecting placeholder identifiers. Semantic checks would reject legitimate `${1:myStruct}` references that don't resolve. |
| 2026-05-24 | **Q2: Source format = JSON, identical to VS Code snippet schema + optional `scope` field.**                     | Zero migration friction; reuses System.Text.Json; users editing project/user snippets can reuse mental model from VS Code. No new parser.        |
| 2026-05-24 | **Q3: Unique snippet Id = `"<source>:<prefix>:<scope>"`.**                                                       | Display name may be missing or duplicated; prefix may collide across scopes by design (e.g. `return` in `fn-body` vs nothing top-level). Composite key uniquely names a snippet for error messages and dedup. |
| 2026-05-24 | **Q4: Cross-source precedence is per `(prefix, scope)` pair, full override (not merge).**                       | Predictable: a user who overrides `for` in `fn-body` doesn't have to also restate other scopes. Same `(prefix, scope)` in a higher-precedence source fully replaces the lower-precedence definition; different scopes coexist. |
| 2026-05-24 | **Q5: File-watching deferred. Registry is reload-friendly (`Reload()` method on interface).**                   | V1 has no non-bundled sources; bundled changes require a new LSP build anyway. The method exists so the future project/user implementations can be hooked into the document-sync handler without interface churn. |
| 2026-05-24 | **Q6: Context detection lives in a static `SnippetContext` helper that calls `ScopeTree.FindScopeAt`.**          | Trivial wrapper; no new analysis pass per user requirement #5. Walking `Scope.Parent` for the nearest `Function`/`Loop` ancestor is O(scope-depth) and runs once per completion request.                                  |
| 2026-05-24 | **Q7: `CompletionCandidate` extension = two optional positional fields (`InsertText`, `InsertTextFormat`).**     | Minimal change; record positional ergonomics preserved; all existing call sites continue to compile. A nested `InsertionTemplate` record adds an allocation per candidate with no semantic gain.                          |
| 2026-05-24 | **Q8: Sink writes `InsertText` straight through; when null, leaves the field unset (client uses `Label`).**      | Matches the LSP spec's default behavior. Keeps non-snippet providers' on-the-wire output bit-identical to today.                                                                                                          |
| 2026-05-24 | **Q9: Snippet priority strictly greater than `ScopedSymbolCompletionProvider.SourcePriority`.**                  | Symbols win dedup race with snippets sharing a prefix - preserves VS Code precedent that user code shadows snippets, avoids accidental refactor pain.                                                                     |
| 2026-05-24 | **Q10: Migration sequenced as two commits in one PR. LSP-side first (snippets visible in both LSP and extension), then extension cleanup.** | Eliminates any window where snippets are missing on both sides; bisect-friendly.                                                                                                                                          |
| 2026-05-24 | **Q11: v1 scope vocabulary = `Any \| TopLevel \| FnBody \| LoopBody`.**                                          | These are the only scopes derivable from existing `ScopeKind` (`Global / Function / Block / Loop`). Struct/enum/interface bodies are not separate scopes today; gating on them would require a new analysis pass (forbidden). |

## Risks

| Risk                                                                       | Likelihood | Mitigation                                                                                                                    |
| -------------------------------------------------------------------------- | ---------- | ----------------------------------------------------------------------------------------------------------------------------- |
| Validation regression at startup blocks LSP from starting.                 | Low        | Failure semantics (#4): registry never throws; errors surface via `window/showMessage` + `ILogger`. Regression test in P3 injects an invalid bundle and asserts the LSP serves other completion modes normally. |
| Existing seed snippets fail the new parse validator.                       | Medium     | P5 dry-runs the validator on the migrated bundle; any failures are fixed before deletion of the extension JSON. Specifically, the `cmd` and `str` snippets use `\$` escapes that the lexer must accept post-strip — covered by unit test. |
| Snippet candidate leaks into Dot mode and shows up after `.`.              | Low        | Provider's `Mode => Default`. Sink already gates per-mode. Snapshot test in P4 asserts no snippet labels appear in Dot-mode output. |
| Snippets shadow scoped variables and confuse users.                        | Low        | Decision Q9: priority pinned strictly below ScopedSymbol. Unit test in P2 asserts variable wins when names collide.           |
| VS Code users see duplicate snippets during the migration window.         | Medium     | Q10: both commits land in one PR; migration is atomic from the user's perspective. CI verifies extension `package.json` no longer contains `contributes.snippets`. |
