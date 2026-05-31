# stash-script-readiness — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `33f0e6a..HEAD` on branch `feature/stash-script-readiness` (8 commits, P1–P4)
**Brief:** ../brief.md
**Generated:** 2026-05-31 15:33

**Severity gist:** the implementation lands, docs/LSP plumbing is wired, and the install gate is genuinely proven. But the brief's load-bearing claim — *bash-`[[ ]]`-globstar parity* — has three real divergence classes the fixture never reaches: (a) `*`/`?` don't match `\n` in .NET regex but do in bash, (b) several malformed patterns throw `RuntimeError` instead of falling back to literal-match per the Decision Log, and (c) any `\x` escape *inside* a character class passes through to .NET regex unmangled, allowing `\d`, `\s`, `\w` etc. to silently change semantics. The functional smoke is fine because real plan.yaml paths trigger none of these. The first two are the only ones that meaningfully affect the feature-2 use case (a deleted-file path string with embedded newline is rare but possible; a malformed pattern in a future plan.yaml is more plausible). The class-escape issue is a sharp foot-gun for anyone who composes patterns programmatically.

**Counts:** CRITICAL: 0 · IMPORTANT: 3 · MINOR: 4

---

## F01 — [IMPORTANT] Glob→regex translation misses single-line mode: `*`, `**`, `?` do not match `\n` (bash does)

**Status:** fixed
**Files:** `Stash.Stdlib/BuiltIns/PathGlobImpl.cs:28`, `Stash.Stdlib/BuiltIns/PathGlobImpl.cs:55-71`
**Phase:** P2
**Commit:** 2becf79

### Observation

`PathGlobImpl.Matches` calls `Regex.IsMatch(path, regex)` with default `RegexOptions`. `*` and `**` translate to `.*`, `?` to `.`. In default .NET regex `.` does **not** match `\n`, but in bash `[[ ]]` `*` and `?` match any character including `\n`.

Reproduced against the installed binary:

```
bash:  [[ $'a\nb' == a*b ]]   → true
stash: path.match("a\nb","a*b")  → false
bash:  [[ $'a\nb' == a?b ]]   → true
stash: path.match("a\nb","a?b")  → false
```

### Why this matters

The brief's acceptance criterion is "verdict matches bash `[[ ]]` under globstar". A path string with an embedded newline (a corner case but legal — git can hand them to verify-phase.sh's scope loop via `git diff -z`) silently flips the verdict. This contradicts the brief's "bash decision parity" framing.

### Suggested fix

Compile the regex with `RegexOptions.Singleline` so `.` matches `\n`:

```csharp
return Regex.IsMatch(path, regex, RegexOptions.Singleline | RegexOptions.CultureInvariant);
```

Add a unit test exercising `\n` in path and a fixture row in `gen-bash-parity-fixture.sh` (synthesized B-section), so the parity test catches future regression.

### Verify

```
dotnet test --filter "FullyQualifiedName~PathBuiltInsTests.Match"
~/.local/bin/stash -c 'io.println(path.match("a\nb","a*b"));'   # expect: true
```

---

## F02 — [IMPORTANT] Malformed patterns can throw `RuntimeError` instead of falling back to literal-match (Decision Log violation)

**Status:** fixed
**Files:** `Stash.Stdlib/BuiltIns/PathGlobImpl.cs:25-29`
**Phase:** P2
**Commit:** 2becf79

### Observation

Brief Decision Log: *"Malformed patterns (unclosed `[`, trailing `\`) match as literals, never throw."* The implementation handles unclosed `[` and trailing `\` in `GlobToRegex` itself, but any pattern that translates to a syntactically invalid .NET regex still hands the translated string to `Regex.IsMatch`, which throws `RegexParseException` — caught by `VirtualMachine.Functions.cs:519` and re-thrown as `RuntimeError("Built-in function error: …")`. Repro:

```
~/.local/bin/stash -c 'io.println(path.match("b","[z-a]"));'
→ RuntimeError: Built-in function error: Invalid pattern '^[z-a]$' at offset 5. [x-y] range in reverse order.
```

Bash treats `[z-a]` as a literal-shaped pattern that simply doesn't match `b` (returns `false`).

### Why this matters

- Violates an explicit Decision Log row.
- The error type is `RuntimeError`, but `Wave1ThrowsCoverageTests` allow-lists `match` on the premise it never throws on legal inputs — and "legal" here was defined as anything except extglob. A reverse range is legal bash glob.
- Feature 2 (checkpoint-script rewrite) will iterate `path.match` over every harvested plan.yaml pattern. A single bad pattern aborts the whole verify-phase scope check.

### Suggested fix

Wrap the `Regex.IsMatch` call in a try/catch: on `RegexParseException`, fall back to literal `path == pattern`. This matches the Decision Log and the existing `FindClassEnd`/trailing-`\` literal-fallback policy.

```csharp
try { return Regex.IsMatch(path, regex, RegexOptions.Singleline | RegexOptions.CultureInvariant); }
catch (RegexParseException) { return path == pattern; }
```

Add unit tests covering reverse range `[z-a]`, empty class `[]` (bash treats as literal), and a class with an embedded null member.

### Verify

```
dotnet test --filter "FullyQualifiedName~PathBuiltInsTests.Match_Malformed"
~/.local/bin/stash -c 'io.println(path.match("b","[z-a]"));'   # expect: false
~/.local/bin/stash -c 'io.println(path.match("[z-a]","[z-a]"));' # expect: true
```

---

## F03 — [IMPORTANT] `EscapeClassContents` is a no-op: `\x` inside `[...]` reaches .NET regex unescaped, silently activating `\d`/`\s`/`\w`/etc.

**Status:** fixed
**Files:** `Stash.Stdlib/BuiltIns/PathGlobImpl.cs:164-171`, `Stash.Stdlib/BuiltIns/PathGlobImpl.cs:88-98`
**Phase:** P2
**Commit:** 2becf79

### Observation

`EscapeClassContents` returns its input verbatim with a comment claiming the `[…]` syntax is regex-safe. It is not: .NET regex character classes recognise `\d`, `\s`, `\w`, `\D`, `\S`, `\W`, `\p{…}`, `\\`, `\]` etc. inside `[…]`. Bash glob character classes treat `\x` as literal `\` plus literal `x`. Repro (pattern `[\d]` constructed inside Stash so the lexer is happy):

```
stash:  path.match("d","[\d]") (programmatically) → false  (but should be true: bash treats class as {\, d})
stash:  path.match("1","[\d]")                     → true   (.NET regex matches digit; bash returns false)
bash:   [[ d == [\d] ]] → true   ; [[ 1 == [\d] ]] → false
```

Same trap with `[\s]`, `[\w]`, `[\\]` etc. Also: an unescaped `\` at the end of a class content (e.g. `[a\]`) lets the closing `]` get consumed as `\]` by .NET regex, producing a hung-open class and a `RegexParseException` (F02 path).

### Why this matters

Anyone composing patterns from Stash strings (which is exactly what feature 2's script rewrite will do) can silently get .NET-regex semantics. This is the single class of divergence most likely to surprise a user who reads the brief and assumes bash semantics. The fixture has zero `\` rows so it cannot catch this.

### Suggested fix

Escape backslashes inside the class contents — for each `\` in the source class, emit `\\` to the regex; preserve `^`, `-`, `]` (which `FindClassEnd` has already validated cannot appear ambiguously). A minimal implementation: replace `\\` → `\\\\` (regex literal `\`) in the class content before emission. Add fixture rows in P1's harness covering `\d` / `\\` / `\.` inside classes.

### Verify

```
dotnet test --filter "FullyQualifiedName~PathBuiltInsTests.Match_BackslashInClass"
```

---

## F04 — [MINOR] No structural assertion that `path.match` is reachable as an LSP member completion under `path.`

**Status:** open
**Files:** `Stash.Tests/Lsp/CompletionSurfaceSnapshotTests.cs:96-120`, `plan.yaml:64`
**Phase:** P2
**Commit:** 2becf79

### Observation

`plan.yaml` declared a new snapshot file `Stash.Tests/Lsp/Snapshots/path-member-access.txt` for P2; no such file exists. The orchestrator's claim was that re-baselining was unnecessary because `Snapshot_FsDot_MatchesRegistrySurface` is structural (it asserts the dot-completion items equal `StdlibRegistry.GetNamespaceMembers("fs")`). But that test is hard-coded to the `fs` namespace — there is no equivalent for `path`. The only `path`-related snapshot row is `Module\tpath` in `empty-file.completion.txt`, which proves the namespace surfaces, not that `match` does as a member.

### Why this matters

The brief's Acceptance Criteria explicitly requires *"`path.match` appears in completion at a cursor inside a `path.` member access"*. Today this is *probably* true (the same `BuiltInNamespaceDotStrategy` path serves all namespaces), but there is no test pinning it. The omission-prevention "Detect" guard listed in the Cross-Cutting Concerns table is missing exactly here.

### Suggested fix

Either (a) parameterise `Snapshot_FsDot_MatchesRegistrySurface` to iterate over all stdlib namespaces (cheapest, broadest coverage), or (b) add a one-line fact:

```csharp
[Fact]
public void PathDot_ContainsMatch()
{
    var labels = InvokeDotCompletion("path").Select(i => i.Label).ToHashSet();
    Assert.Contains("match", labels);
}
```

### Verify

```
dotnet test --filter "FullyQualifiedName~CompletionSurfaceSnapshotTests"
```

---

## F05 — [MINOR] Standard Library Reference renders summary as "Returns true iff  matches the glob…" (double space — `<paramref>` tags stripped, not substituted)

**Status:** open
**Files:** `Stash.Stdlib/BuiltIns/PathBuiltIns.cs:101-106`, `docs/Stash — Standard Library Reference.md:6570`, `docs/Stash — Standard Library Reference.md:6711`
**Phase:** P2
**Commit:** 2becf79

### Observation

The XML doc on `PathBuiltIns.Match` uses `<paramref name="path"/>` and `<paramref name="pattern"/>` inside `<summary>`. The doc generator drops the `<paramref>` element entirely (no substitution to backticked param name), producing the literal rendered text:

```
| path.match | bool | — | Returns true iff  matches the glob under bash [[ ]] globstar semantics. |
```

(Two spaces where the param refs were.) Same issue in the function-detail section.

### Why this matters

It's user-facing documentation, regenerated by `dotnet run --project Stash.Docs/`. Other `path.*` summaries use bare prose ("Returns the absolute path for the given path string."); they read cleanly. This is the only `path.*` entry with empty `<paramref>` holes, and the imperfect summary surfaces in autocomplete tooltips too.

### Suggested fix

Rewrite the `<summary>` to inline parameter names without `<paramref>`, matching the prose style of `Abs`, `Dir`, etc.:

```csharp
/// <summary>Returns true iff the path matches the glob pattern under
/// bash [[ ]] globstar semantics. Pure: does not touch the filesystem; the
/// path need not exist on disk.</summary>
```

Then `dotnet run --project Stash.Docs/` and commit the regenerated doc.

### Verify

```
dotnet run --project Stash.Docs/
grep "Returns true iff" "docs/Stash — Standard Library Reference.md"
dotnet test --filter "FullyQualifiedName~StandardLibraryReferenceTests"
```

---

## F06 — [MINOR] Parity fixture's coverage breadth understated: no `\` escape rows, no `]` first-class-member rows, no character-class-with-meta rows

**Status:** fixed
**Files:** `scripts/path-match/gen-bash-parity-fixture.sh:188-242`, `Stash.Tests/Stdlib/Fixtures/path-match-bash-parity.tsv`
**Phase:** P1
**Commit:** 4169d9d

### Observation

The shape-coverage `[Fact]` requires only: >50 rows, `**`, `*` crossing `/`, `?`, character class, literal-only. That floor is met. But the fixture (2811 rows) contains zero rows exercising any of: backslash escape (`\*`, `\?`, `\[`, `\\`), `]` as the first character class member, character class containing a regex-meta character (`[*]`, `[.]`, `[\d]`), or any path with whitespace/control characters. Result: F01/F02/F03 above pass parity even though the implementation diverges from bash on each.

### Why this matters

The brief's headline acceptance criterion is bash-decision parity, and the test's authority over that claim depends on the fixture's edge-case reach. The harness's two strategies (transform real patterns + synthesized B-section) both target the *common* cases that occur in real plan.yaml entries — which is exactly where the implementation is sound. The risky cases come in via Strategy B and require explicit enumeration.

### Suggested fix

Extend Strategy B in `gen-bash-parity-fixture.sh` with explicit synthesized rows for each of the missing constructs:

- Backslash escape outside class: `record_pair "a\\*" "a*"; record_pair "a\\*" "ab"`.
- Closing `]` first member: `record_pair "[]abc]" "]"; record_pair "[]abc]" "a"; record_pair "[!]abc]" "x"`.
- Class with regex-meta members: `record_pair "[\\\\d]" "d"; record_pair "[\\\\d]" "1"; record_pair "[.]" "."; record_pair "[.]" "x"`.
- `\n` in path (Strategy B path strings can include `$'\n'`).

Then `bash scripts/path-match/gen-bash-parity-fixture.sh` and commit the regenerated TSV; the new rows will fail until F01/F02/F03 are fixed.

### Verify

```
bash scripts/path-match/gen-bash-parity-fixture.sh --check
dotnet test --filter "FullyQualifiedName~PathMatchBashParityTests"
```

---

## F07 — [MINOR] `EscapeClassContents` claim ("the only truly special chars are ] ^ - \\") is also incomplete for `[:posix:]` classes and `[=eq=]` collations

**Status:** open
**Files:** `Stash.Stdlib/BuiltIns/PathGlobImpl.cs:165-170`
**Phase:** P2
**Commit:** 2becf79

### Observation

POSIX bracket-expression collations (`[[:alpha:]]`, `[[:digit:]]`) and equivalence classes (`[[=a=]]`) are supported by bash but not by .NET regex (which interprets `[:digit:]` as the class `{:, d, i, g, t}`). Bash plan.yaml patterns historically don't use these, but the comment in `EscapeClassContents` claims completeness it doesn't have.

### Why this matters

Low-frequency in real corpora — zero hits in any in-repo plan.yaml. Listed as MINOR because the realistic blast radius is small and the right resolution is documentation, not code: explicitly call out the unsupported constructs in the `Matches` XML doc and add a fixture row that asserts current behaviour (literal match), so a future maintainer doesn't add half-support.

### Suggested fix

Add an `<exception>` or `<remarks>` block to `PathBuiltIns.Match` listing POSIX class / collation as unsupported, document that they will be treated as literal class-member characters, and add a parity row to lock the divergence in place.

### Verify

```
grep -A2 "POSIX" Stash.Stdlib/BuiltIns/PathBuiltIns.cs
dotnet test --filter "FullyQualifiedName~PathMatchBashParityTests"
```
