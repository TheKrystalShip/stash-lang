# lsp-snippet-completions — Review

> Produced by `/feature-review`. One finding per H2 section.
> `/resolve lsp-snippet-completions Fxx` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** `afd2a10..2f71240` on `main` (P1–P5 + 0edffd6 fix)
**Brief:** ./brief.md
**Generated:** 2026-05-25

Summary: **3 IMPORTANT, 2 MINOR, 0 CRITICAL** — feature is functionally sound and tests are green. Findings are about contract drift between brief/plan and the as-shipped bundled set: scope-tagging asymmetry that violates a stated AC, an undocumented prefix rename that breaks every published prefix, and a brittle substitution choice in `StripTabstops`. Nothing here breaks the LSP or fails the existing test surface.

---

## F01 — [IMPORTANT] `fore` / `fori` scope tag violates P4 `done_when` and AC

**Status:** fixed
**Fixed in:** 63ef055
**Files:** `Stash.Lsp/Completion/Snippets/bundled.json:141-158`, `Stash.Tests/Lsp/Completion/SnippetCompletionProviderTests.cs:256-265`
**Phase:** P4 / P5
**Commit:** `5013c86`, `bb77cdc`

### Observation

`plan.yaml` P4 `done_when` explicitly enumerates statement snippets that must declare `fn-body`:

> "statement snippets that require a function body (return, while, for, fori, forr, try, pln, aeq, atrue) declare fn-body"

The shipped `bundled.json` tags `whl, forr, swtch, tryc, retn, pln, aeq, atrue` as `fn-body` but leaves `fore` (the renamed `for`) and `fori` as **no scope → `Any`**. The asymmetry is then locked in by a self-authored test `BundledRegistry_StatementSnippets_HaveScopeAny` (line 257) which asserts `fore` and `fori` *must* stay `Any`.

The brief's Acceptance Criteria #1 / #2 are explicit:

> "A user typing `fo` at top-level (outside any function) **does not** see the `for` snippet (gated to `fn-body`)"

With `fore` scope=`Any`, this AC is violated. The bundled-snippets snapshot (`default-with-snippets.completion.txt`) confirms `fore` and `fori` appear at top-level alongside the declaration snippets.

### Why this matters

This is the headline UX claim of the feature ("context-aware snippets via scope vocabulary"). Two of the brief's named acceptance examples are wrong against shipped behavior. The plan was not amended; the implementer instead added a test pinning the deviation, which makes the regression invisible to future readers of the spec.

There is no observed loss of language correctness — `for`/`fori` at top-level still produces a valid Stash program — but the contextual gating the feature was sold on doesn't fire for the two most-typed loop snippets.

### Suggested fix

Pick one of two paths and apply consistently:

- (preferred, matches brief) Add `"scope": "fn-body"` to the `For-In Loop` (`fore`) and `C-Style For Loop` (`fori`) entries in `bundled.json`. Update `BundledRegistry_StatementSnippets_HaveScopeAny` to drop `fore` / `fori` from the assertion list and rename it `BundledRegistry_StatementSnippets_ScopedToFnBody` (or split into two tests, one for each scope). Regenerate the snapshot fixture (`STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~CompletionSurfaceSnapshotTests`).
- (alternate) If the user explicitly wants `fore`/`fori` available at top-level, amend `brief.md` AC #1/#2 and `plan.yaml` P4 `done_when` to remove `for`/`fori` from the fn-body list and record the rationale in the Decision Log. Update the snapshot accordingly.

### Verify

```
dotnet test --filter "FullyQualifiedName~SnippetCompletionProviderTests|FullyQualifiedName~CompletionSurfaceSnapshotTests"
```

After fix, top-level completion at `line 0, col 0` of an empty doc must not contain `fore` or `fori`; inside `fn foo() { … }` they must appear.

---

## F02 — [IMPORTANT] 18 snippet prefixes renamed without spec amendment; brief still references the old names

**Status:** fixed
**Fixed in:** 63ef055
**Files:** `Stash.Lsp/Completion/Snippets/bundled.json` (all renamed entries), `.kanban/2-in-progress/lsp-snippet-completions/brief.md:319-322` (AC), commit message `0edffd6`
**Phase:** P2-fix / P5
**Commit:** `0edffd6`, `bb77cdc`

### Observation

The migration renamed 18 of the 39 originally-shipped VS Code prefixes:

```
fn → fnd          let → letv         const → cst
for → fore        if → ifc           struct → strc
enum → enmd       interface → intfc  while → whl
try → tryc        switch → swtch     import → imp
return → (new: retn)                 str → istr
constt → cstt     structt → strct    interfacet → intfct
elevate → elv     elevatewith → elvw
```

The commit message `0edffd6` documents the rationale (keyword shadowing by `KeywordCompletionProvider` at priority 10), and `SnippetCompletionProvider.cs` carries a `<remarks>` block explaining the choice. However:

1. `brief.md` Decision Log Q2 says **"identical to VS Code snippet schema"** and **"Zero migration friction"** — the renames break user muscle memory wholesale.
2. `brief.md` Acceptance Criteria still names the original prefixes — e.g. "A user typing `fo`… sees `for` (For-In Loop)". The shipped label is `fore`, not `for`. The AC reads as not-met against the shipped artifact.
3. `plan.yaml` P5 `done_when` says **"1:1 migration of every entry from .vscode/extensions/stash-lang/snippets/stash.json that passes the validator"**. 18 of 39 entries are not 1:1.
4. The deletion in commit `2f71240` removes the only document that pinned the old prefixes; no migration note for end users (e.g. a CHANGELOG / extension `README.md` snippet section) was added.

### Why this matters

The brief is currently the contract a reader inspects to understand the feature. Reading it against `main` produces three concrete mismatches (Q2 "zero friction", AC examples, P5 done_when). Every Stash extension user has lost the `fn`, `let`, `if`, `for`, `struct`, `import`, `while`, `try`, `return`, `switch`, `interface`, `enum`, `const`, `elevate` snippet prefixes they had yesterday with no documented migration path.

Note: the keyword-collision the rename addresses is real (`SnippetCompletionProvider.cs:27-32` documents it) — the fix itself is sensible. The finding is about *unrecorded* deviation, not the technical choice.

### Suggested fix

- Amend `brief.md` Decision Log with a new "Q12 — Keyword-prefix collision: 18 snippet prefixes renamed to non-keyword forms" entry. Update the Q2 "zero migration friction" sentence (or add a caveat). Update Acceptance Criteria #1/#2 to use the new prefixes (`fore`, `fori`).
- Amend `plan.yaml` P5 `done_when` to read "1:1 migration of every entry … *with prefix renames per Decision Log Q12*".
- Add a short migration note to `.vscode/extensions/stash-lang/README.md` (or the extension's CHANGELOG) listing the old→new prefix table so users who upgrade see why `fn`<Tab> stopped expanding.

### Verify

Read brief.md and plan.yaml after the edits; cross-reference the prefix list in `bundled.json` against the new tables. No code/test changes required.

---

## F03 — [IMPORTANT] `$0` → `null;` substitution is fragile in non-block-body positions

**Status:** open
**Files:** `Stash.Lsp/Completion/Snippets/SnippetValidator.cs:225-240, 258-263`
**Phase:** P2-fix
**Commit:** `0edffd6`

### Observation

`StripTabstops` substitutes `$0` (and `${0}`) with the literal text `null;`. This was chosen so $0 in statement position parses cleanly. The brief's specified substitution (line 230) was `__snip_0`; the change to `null;` is per commit `0edffd6` rationale "loud failure for $0 in non-statement positions".

The substitution is correct for every body currently in `bundled.json` (verified by simulating StripTabstops on every entry — no double-semicolons, all bodies still parse). But the encoding silently fails for plausible future snippets:

- `let x = $0;` → `let x = null;;` — double semicolon, parse error (`stash -c 'let x = 1;;'` returns `Error at ';': Expected expression.`).
- `[$0]` → `[null;]` — parse error.
- `foo($0)` → `foo(null;)` — parse error.
- `"$0"` is left as-is because lone `$` followed by `"` is treated as Stash interpolation prefix (line 216) — but a literal `$0` *inside* a `"…"` string in a snippet body would be incorrectly passed-through; not a regression today (no such bundled entry) but a subtle pitfall.

Conversely `$0` *outside* a statement position in a real expansion (e.g. `let x = $0` in a future user snippet) would pass validation (the `null` keyword is a valid expression) yet expand to broken text at the editor (`let x = ` with cursor — fine for editor, but the validator's loud-failure promise was meant to catch this at load time).

In short: the "fail loudly" guarantee from the commit message is only partial. It catches some misuses (`[$0]`) and silently endorses others (`let x = $0`).

### Why this matters

Maintainability risk for the snippet authoring workflow. A user editing future project/user snippets gets uneven validator feedback for the same `$0` placeholder depending on syntactic context, with no documentation of the rule. The brief's substitution (`__snip_0`) was uniform; the new substitution is context-sensitive without being principled.

This is the kind of foot-gun that becomes a bug report 6 months from now when someone adds a `let ${1:name} = ${2:value}$0;` (no semicolon between value and `$0` because they want the cursor right before the `;`) and gets `let name = value null;;` → parse fail → load error → confusion.

### Suggested fix

Two options, in increasing rigour:

- (minimal) Document the substitution behavior in `StripTabstops` xmldoc and add a one-line note to `brief.md` Decision Log: "Q13 — `$0`/`${0}` substitute to `null;` (block-statement context only). Snippets that place `$0` in expression position fail validation; this is by design." Add 2 unit tests asserting `[$0]` and `let x = $0;` reject loudly with a useful error message.
- (preferred) Revert the substitution to `__snip_0` per the brief (uniform, passes lex/parse as an identifier in every context), and instead add a dedicated `$0`-placement validator pass that asserts `$0` (and `${0}`) appears only at a position where deletion-after-strip leaves a valid Stash body — i.e. validate that `body.Replace($0, "")` parses. This gives the "loud failure" guarantee without the `;;` pitfall.

### Verify

After the fix, add tests:

```csharp
[Fact] public void Validate_DollarZeroInArrayLiteral_RejectsLoudly() { … "[$0]" … }
[Fact] public void Validate_DollarZeroAfterEquals_AcceptsOrRejectsConsistently() { … "let x = $0;" … }
```

Then run:

```
dotnet test --filter "FullyQualifiedName~SnippetValidatorTests"
```

---

## F04 — [MINOR] `SnippetCompletionProvider` priority 1000 contradicts brief Q9 phrasing

**Status:** open
**Files:** `Stash.Lsp/Completion/Providers/SnippetCompletionProvider.cs:41`, `brief.md` Decision Log Q9
**Phase:** P2-fix
**Commit:** `0edffd6`

### Observation

`SourcePriority = 1000` (line 41) is well below every other Default-mode provider (keywords=10, stdlib=20/30, scoped symbols=40). The brief Q9 says only "strictly greater than `ScopedSymbolCompletionProvider`'s priority" — i.e. just slightly greater than 40 was the design. The commit `0edffd6` rationale states this was a deliberate user-policy change ("snippets should not get in the way of normal typing"), but Decision Log Q9 was not amended.

A consequence the brief does not anticipate: snippet candidates always render *after* every other completion category in clients that sort by `sortText` derived from server priority. Users who *want* a snippet (e.g. type `fnd<Tab>`) still get it because the prefix is unique, but for keyword-shadowed cases (`if<Tab>` → `ife`/`ifc` exist) the snippet appears at the bottom of the list. That is presumably the intent but should be recorded.

### Why this matters

Decision Log drift. Anyone reading the brief alone gets one design (snippet just below scoped symbols); the code carries another (snippet far below everything). Easy to fix.

### Suggested fix

Amend `brief.md` Decision Log Q9 to read: "Priority pinned to **`1000`** (significantly below every other Default-mode provider) per user policy added during P2 review: snippets should be available, not pushy." Cite commit `0edffd6`.

### Verify

No test change required; documentation only.

---

## F05 — [MINOR] LOAD-ERRORS CONTRACT in plan vs implemented "single summary popup" semantics

**Status:** open
**Files:** `Stash.Lsp/Completion/Snippets/SnippetDiagnosticsReporter.cs:81-95`, `plan.yaml` P3 notes block
**Phase:** P3
**Commit:** `c3f6db8`

### Observation

`plan.yaml` P3 has two contradictory contracts:

- `done_when`: "On startup, `ILanguageServerFacade.Window.ShowMessage` is invoked **exactly once** with `MessageType.Error` and a message naming the count and source."
- The notes block (added by commit `0edffd6`) says: "The reporter MUST surface every LoadErrors entry via **both window/showMessage AND ILogger.LogError exactly once per LSP startup** so users see every invalid snippet (name + reason + source) and can fix them."

The implementation follows the `done_when` (one summary popup, N log entries). Test `Report_MultipleErrors_FiresOneLogPerErrorAndExactlyOneWindowMessage` (line 58) locks in that interpretation. So a user without log access only sees "3 invalid snippets in bundled snippets — see log for details." with no per-snippet detail in the popup.

This works in practice (the rationale comment in the test, "to avoid spamming users with N popups", is sound) but the plan's notes block is internally contradictory.

### Why this matters

Future maintainer reading the plan sees two contradictory rules. The "exactly once" rule wins per the tests, but the LOAD-ERRORS CONTRACT prose suggests the user should see per-snippet detail in the popup. Resolve the ambiguity.

### Suggested fix

Edit `plan.yaml` P3 notes to read: "Reporter emits **exactly one** `showMessage` (summary with count + source) **plus exactly one** `LogError` per LoadErrors entry. The per-error detail (id + reason + source) lives in the log, not the popup, to avoid spamming users with N popups."

### Verify

No code or test change required; plan-prose edit only.

---

## Out-of-scope observations (no findings)

- `JsonDocument.Parse` is `using`-disposed in `BundledSnippetRegistry.LoadRawEntries`, but `RawSnippet.Body` (a `JsonElement`) is re-parsed via `JsonSerializer.Deserialize<RawSnippet>(property.Value.GetRawText(), …)` which creates an independent `JsonDocument`, so the early dispose is safe. Verified.
- `SnippetContext.Classify` correctly converts 0-based LSP coords to 1-based analyzer coords (`line + 1, column + 1`), matching `docs/stash-analysis`'s coordinate convention pinned in commit `0605fec`.
- Malformed-bundle path traced: `JsonDocument.Parse` throws → `Reload` catch produces a synthetic `SnippetLoadError("bundled", …)` → startup reporter reads it → ShowError fires. LSP-stays-up guarantee holds.
- `BundledRegistry_NeverThrows_OnConstruction` + `BundledRegistry_ExposesLoadErrors_AsTheSingleSurfaceForFailures` together prove the production bundle is clean (0 LoadErrors).
- No prefix collisions in the shipped bundled set — each of the 40 entries has a unique `(prefix, scope)` pair (verified by re-running the validator's Rule 7 pre-scan mentally; would fail load otherwise).
- Multi-scope limitation (a `fn-body` snippet not firing inside a `loop-body` inside `fn`) is documented in brief Open Questions ("Multi-scope snippets") as deferred. `retn`/`pln`/`aeq`/`atrue` will not fire inside a nested loop body inside a function — this matches v1's pinned Matches semantics.
- Baseline `dotnet test` reported 43 failures, all within the documented flaky-class envelope from `.claude/repo.md` (DiffPackageTests, RegistryTests, NetBuiltInsTests, parallel-execution DAP). 376/377 LSP tests pass (1 pre-existing skip).
