# readonly-modifier — Review (Pass 2)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `6c84c290..270a12ea` on branch `feature/readonly-modifier`
**Brief:** ./brief.md
**Generated:** 2026-06-01 18:24 UTC

**Summary of findings:** 0 open — all findings resolved.

Pass-1 status (verified, not re-filed):
- **F01** (stale bytecode-instruction reference doc) — fixed at `b8c6242`. Verified: the reference shows `Opcode count | 101` and includes a `Freeze` row.
- **F02** (deep-freeze through stdlib-produced arrays) — fixed at `45a99c0` **for the specific call sites the original finding enumerated** (`arr.slice`, `arr.map`, `arr.flatMap`, `arr.concat`, `arr.zip`, `arr.chunk`, etc. + `dict.keys`/`values`/`pairs`). The CLI repros from the original review all throw `ReadOnlyError` now. However, the same bug class persists in other namespaces — see **F08** below.
- **F03** (typed-array freeze + write-guard) — fixed at `0b353ae` (+ followup `05fe9f4` covering `RemoveLast`/`arr.pop`). Verified: template-method refactor is correct on all five subclasses; every public mutator (`Set`/`Add`/`Insert`/`RemoveAt`/`Clear`/`RemoveLast`) is guarded on the abstract base before delegating to `*Core`; `StashByteArray.GetBackingArray` throws when frozen, closing the `buf.copy`/`buf.fill`/`buf.writeUintX` bypass; `DeepFreezeObject` has a `case StashTypedArray` branch.
- **F04** (`arr.shuffle` runtime + analyzer parity) — fixed at `8650d20`. Verified: shuffle has the `IsArrayFrozen` guard; `ReadOnlyMutationRule.KnownInPlaceMutators` now lists `arr.shuffle`.
- **F05** (brief's `a is D` claim) — fixed at `4caf1ac`. Doc-only.
- **F06** (`StashFrozenArray` retirement) — fixed at `96de0c8`. Verified: `grep -rn "StashFrozenArray" --include="*.cs"` returns zero matches; the file is deleted; the four early-paths in `VirtualMachine.Collections.cs` / `SvArgs.cs` / `GlobalBuiltIns.cs` and the dual-check in `IsArrayFrozen` are gone.
- **F07** (semantic-token modifier asymmetry, tree-sitter for-init, const-local `IsLocalReadonly`) — fixed at `90e64c7`. Verified: `VisitVarDeclStmt` now emits `ModifierReadonly` when `stmt.IsReadonly`; `_for_var_init` is a separate rule without `optional('readonly')`; `VisitConstDeclStmt` passes `isReadonly: stmt.IsReadonly` to `DeclareLocal`.

`final_verify` will still gate on the suite, but the single new finding below is not currently surfaced by any test in the tree — the suite is green for code that the finding proves is silently broken at runtime. That is "absence of coverage", not "absence of bug".

---

## F08 — [CRITICAL] Deep-freeze still silently bypassed for `json.parse`, `str.split`, and every other stdlib namespace that returns a bare `List<StashValue>` (F02 fix was scoped only to `arr.*` + `dict.keys/values/pairs`)

**Status:** fixed
**Fixed in:** 027f915
**Files:** `Stash.Core/Runtime/RuntimeValues.cs:379-388` (`DeepFreezeObject` bare-list safety net — admits the hole in its own comment), `Stash.Bytecode/VM/VirtualMachine.Collections.cs:188-189` and `:86-90` (write-paths gate only on `is StashArray && IsFrozen`), `Stash.Stdlib.Generators/TypeMarshaller.cs:106-108` (the choke point for auto-marshalled producers), `Stash.Stdlib/BuiltIns/StrBuiltIns.cs:254`, `:297`, `:449`, `:556`, `:667`, `:680`, `:694`, `Stash.Stdlib/BuiltIns/JsonBuiltIns.cs:91-100` (`ConvertArray`), plus equivalents in `XmlBuiltIns.cs`, `TomlBuiltIns.cs`, `SysBuiltIns.cs`, `ArchiveBuiltIns.cs`, `CompleteBuiltIns.cs`, `AliasBuiltIns.cs`, `CliBuiltIns.*.cs`, `ProcessBuiltIns.cs`, `NetSocketImpl.cs`, `SshBuiltIns.cs`, `SftpBuiltIns.cs`, `PkgBuiltIns.cs`, `TaskBuiltIns.cs`, `GlobalBuiltIns.cs:181`, `Stash.Tpl/TemplateFilters.cs:227`, `Stash.Tpl/TemplateRenderer.cs:283`, `:291`, `:299`
**Phase:** P3
**Commit:** `45a99c0` (F02 fix — partial); regression class still open

### Observation

F02 was filed against the brief's headline "deep, transitive" claim, with the example
`readonly const D = { items: arr.slice([1,2,3], 0, 2) }; D.items[0] = 99;` silently succeeding.
The fix at `45a99c0` migrated **20 producer sites in `ArrBuiltIns.cs`** + **`StashDictionary.Keys`/`Values`/`Pairs`** from `new List<StashValue>(…)` to `new StashArray(…)`, added a `case StashTypedArray` to `DeepFreezeObject`, and added a `case List<StashValue>` "defense-in-depth safety net" that *recurses into* a bare list but **cannot freeze the list itself**.

The fix is correct for the call sites it covered. It is **not complete** as the bug-class fix the original finding aimed at. Bare-`List<StashValue>` producers in **every other stdlib namespace** still exist, and `DeepFreezeObject`'s own comment at `RuntimeValues.cs:379-382` openly admits the hole:

```csharp
// Defense-in-depth: a bare List<StashValue> (not a StashArray subclass) can slip
// through if any producer is not yet migrated to StashArray.  The list has no IsFrozen
// bit so it cannot be frozen itself, but we can still recurse into its elements …
```

The fast-path write guards in `VirtualMachine.Collections.cs` (`ExecuteSetTable` at line 188, `SetIndexValue` at line 89) only flag mutation when the target is `is StashArray && IsFrozen`. A bare `List<StashValue>` reaching a write path is *unconditionally writable*. So the silent escape persists.

Two CLI repros confirm this on the current HEAD (`270a12ea`):

```stash
# Repro 1 — json.parse returns a bare List for JSON arrays
readonly const D = json.parse("[1, 2, 3]");
D[0] = 99;          # SILENTLY SUCCEEDS — outputs [99, 2, 3]
                    # (nested JSON objects ARE frozen — those come back as StashDictionary)

# Repro 2 — str.split (and ~6 other str.* producers) returns a bare List
readonly const D = { lines: str.split("a,b,c", ",") };
D.lines[0] = "X";   # SILENTLY SUCCEEDS — outputs [X, b, c]
arr.push(D.lines, "d");  # ALSO SILENTLY SUCCEEDS — outputs [X, b, c, d]
```

The producer surface I confirmed by grep over `new List<StashValue>` (excluding internal temporaries already cleared in `ArrBuiltIns.Sort`'s `tempList` and the `ExecutePar*` input lists) covers at least these namespaces still returning bare lists:

- `str` — `split`, `chars`, `matchAll`, `captureAll`, `lines`, `words`, `shellSplit` (auto-marshalled `[StashFn] List<StashValue>` returns; the generator marshals them via `StashValue.FromObj(bareList)`)
- `json` — `parse` (hand-built `ConvertArray` returns bare `List<StashValue>` for JSON arrays; outer dict result is fine because `ConvertObject` uses `StashDictionary`)
- `xml`, `toml` — `XmlBuiltIns.cs:190,252,265,273`, `TomlBuiltIns.cs:143,157`
- `sys`, `complete`, `alias`, `archive`, `pkg`, `process`, `ssh`, `sftp`, `task`, `net.socket` — assorted producers (see the file:line list in the **Files** section)
- `GlobalBuiltIns.cs:181`
- `Stash.Tpl/TemplateFilters.cs:227` (the `split` filter) and `Stash.Tpl/TemplateRenderer.cs:283,291,299` — outside stdlib but on the same value graph

I CLI-verified the two reproductions above; the broader namespace list is identified by the same shape (`new List<StashValue>` reaching a `StashValue.FromObj` boundary in a `[StashFn]` method). Each one needs verification when migrated, but the shape is uniform.

Dicts are clean (I checked): no stdlib producer returns a bare `Dictionary<string, StashValue>` as a top-level value — dict producers use `StashDictionary`. Bare `Dictionary<string, StashValue>` only appears as the *field-storage* parameter to `new StashInstance(...)`, and `StashInstance` is covered by `DeepFreezeObject`. So the finding is scoped to **bare-list carriers** only.

### Why this matters

This is the *exact bug class* F02 was filed against — and the exact failure mode the brief calls "the bug class immutability exists to prevent":

> Two properties keep this safe: it fails **loud** (`ReadOnlyError` at the offending write — never silent data skew) …

`readonly const D = json.parse(...)` followed by `D[0] = 99` is a silent data-skew bug today. It violates:

- **Goal 3** — "All write paths on every collection / object type honour the frozen flag and raise `ReadOnlyError` on attempted mutation."
- **Acceptance Criterion 1** — "deep-freeze through nested array" (the bare-list nest *is* the array).
- **The headline footgun example** — by accident, the *retroactive deep-freeze* property the brief promotes as the genuine novelty is what makes this dangerous: the user writes `readonly` to claim a guarantee that doesn't hold once the value graph passes through any non-`arr.*` stdlib producer.

The downstream embedding phase (hermetic VM, "share-when-frozen, deep-copy otherwise") also relies on `IsFrozen` being a reliable predicate. Today it is not, for any graph that touches `json.parse` / `str.split` / etc. — which is most real-world configuration loading.

This finding clears the "do not re-file pass-1 items" rule explicitly: F02's specific repros (arr.slice, arr.map, dict.pairs) are verified fixed. The fresh repros (json.parse, str.split) hit code F02's resolution never touched. The `DeepFreezeObject` safety net comment is the author's own admission that the hole exists; the F02 commit closed `.kanban/0-backlog/bugs/DeepFreeze-skips-stdlib-produced-bare-lists.md` as resolved despite the safety net being a documented half-measure.

The test suite is green only because no test exercises a `readonly` initializer wrapping a non-`arr` producer's result — the gap is in coverage, not behavior.

### Suggested fix

Two complementary changes, with the **first closing the largest blast radius in one place**:

**1. Migrate the auto-marshal boundary (the choke point) — covers all `[StashFn]` methods that declare `List<StashValue>` as their return type.**

In `Stash.Stdlib.Generators/TypeMarshaller.cs:106-108`:

```csharp
case "System.Collections.Generic.List<Stash.Runtime.StashValue>":
    stashLabel = "array";
    return "global::Stash.Runtime.StashValue.FromObj({BODY})";
```

Change the emitted body to wrap in `StashArray`:

```csharp
return "global::Stash.Runtime.StashValue.FromObj(new global::Stash.Runtime.Types.StashArray({BODY}))";
```

This auto-fixes every auto-marshalled bare-list producer at once: `str.split`, `str.chars`, `str.lines`, `str.words`, `str.shellSplit`, `str.matchAll`, `str.captureAll`, plus any others whose return type is `List<StashValue>`. The `StashArray(IEnumerable<StashValue>)` ctor copies into the wrapper; existing test expectations are unaffected because `StashArray : List<StashValue>` (identity preserved as far as the Stash side can observe).

**2. Migrate the remaining hand-wrapped producers (`Raw = true` + free-standing `StashValue.FromObj(new List<StashValue>(…))` sites).**

These are the namespaces that hand-build the result and don't go through the marshaller boundary:

- `JsonBuiltIns.ConvertArray` (line 91-100) — return a `StashArray` instead of a bare `List`.
- `Xml`, `Toml`, `Sys`, `Complete`, `Alias`, `Archive`, `Pkg`, `Process`, `Ssh`, `Sftp`, `Task`, `NetSocketImpl`, `Cli.*`, `Global` — each `new List<StashValue>` site that escapes via `StashValue.FromObj(...)` (or via a struct field that flows out) becomes `new StashArray(...)`.
- `Stash.Tpl` — `TemplateFilters.cs:227`, `TemplateRenderer.cs:283,291,299`. Same migration.

A grep template to enumerate the call sites for the resolver:

```bash
grep -rnE "StashValue\.FromObj\(\s*new List<StashValue>|return\s+new List<StashValue>|StashValue\.FromObject\(new List<StashValue>" \
  Stash.Stdlib Stash.Tpl Stash.Core Stash.Bytecode --include="*.cs"
```

A few hits in `ArrBuiltIns.cs` (`Sort`'s `tempList`, the three `ExecutePar*` argument lists) are intentionally local temporaries — they never leave the function. Leave those alone.

**3. Drop the misleading "safety net" or upgrade it.**

`DeepFreezeObject`'s `case List<StashValue> list` (line 383-388) recursively walks the bare list but cannot freeze it. After step 1+2, this case should be unreachable from any well-typed producer; keep it as a defensive no-op or **strengthen it by upgrading on the fly**: take the bare list, copy into a `StashArray`, freeze it, and update the parent's reference to the new wrapper. The latter is intrusive (requires walking parents); the former is fine if the producers are migrated.

**4. Test coverage to lock the bug-class shut.**

In `Stash.Tests/Bytecode/ReadonlyTests.cs`, add one assertion per producer family:

```csharp
[Theory]
[InlineData("readonly const D = json.parse(\"[1,2,3]\"); D[0] = 99;")]
[InlineData("readonly const D = { lines: str.split(\"a,b,c\", \",\") }; D.lines[0] = \"X\";")]
[InlineData("readonly const D = { lines: str.lines(\"a\\nb\\nc\") }; D.lines[0] = \"X\";")]
[InlineData("readonly const D = { chars: str.chars(\"abc\") }; D.chars[0] = \"X\";")]
[InlineData("readonly const D = { matches: str.matchAll(\"aaa\", \"a\") }; arr.push(D.matches, \"x\");")]
[InlineData("readonly const D = { nodes: xml.parseFragment(\"<a/>\") }; arr.push(D.nodes, \"x\");")]
public void Readonly_StdlibProducerArray_MutationThrowsReadOnlyError(string src)
    => Assert.Throws<ReadOnlyError>(() => Run(src));
```

…plus one negative case that proves the safety-net case is unreachable in normal flow (asserts that no shipped stdlib producer returns a bare `List<StashValue>`, via a reflection / source-scan meta-test).

### Verify

```bash
# After the marshaller fix, regenerate the source-generated assemblies and verify the choke point.
dotnet build Stash.Stdlib

# CLI repros — these are currently silent successes; after the fix they must throw ReadOnlyError:
echo 'readonly const D = json.parse("[1, 2, 3]"); D[0] = 99;' | \
  dotnet run --project Stash.Cli/ -c Release -- /dev/stdin
echo 'readonly const D = { lines: str.split("a,b,c", ",") }; D.lines[0] = "X";' | \
  dotnet run --project Stash.Cli/ -c Release -- /dev/stdin

# Targeted test runs.
dotnet test --filter "FullyQualifiedName~FreezeTests|FullyQualifiedName~ReadonlyTests"
dotnet test --filter "FullyQualifiedName~StrBuiltInsTests|FullyQualifiedName~JsonBuiltInsTests|FullyQualifiedName~XmlBuiltInsTests"

# Broad regression check — the StashArray subclass migration should not break any existing producer behavior.
dotnet test
```

---

## Out-of-scope (noted, not findings)

- The F02 commit message's perf observation (`bench_data_transform` +22 ms / +6%) is within
  run-to-run noise per the brief's perf gate; I did not re-run because the suite-side baseline
  was provided green.
- Pre-existing analyzer warnings (VSTHRD / CS0419) noted in the task brief are unrelated to this
  feature.
- The asymmetry where `VisitConstDeclStmt` unconditionally emits `ModifierReadonly` while
  `VisitVarDeclStmt` only conditionally does is intentional (`const` is binding-fixed, so the
  modifier semantics differ); not a finding.
