# Stash.Stdlib — Candidate Findings (UNVERIFIED)

> **Status:** Candidate scope for `stdlib-omission-hardening`. NOT yet specced.
> **Created:** 2026-05-31
> **Provenance:** Two `explore` subagents reading the code (2026-05-31). This is
> locally-plausible, globally-unverified output — the exact failure shape this
> initiative distrusts. **Every item below is a hypothesis. Phase 1 of the `/spec`
> reproduces each claim against the code before any of it is acted on.** Items that
> don't reproduce are struck, not implemented.
> Parent: `MILESTONE.md`.

## 0. Scope-sizer — VERIFY THIS FIRST

**Hypothesis (HIGH confidence):** Built-in *registration* is **already Construct**. A
`[StashNamespace]` source generator (`Stash.Stdlib.Generators/StashNamespaceGenerator.cs`,
`CodeEmitter.cs`) scans `[StashFn]` methods and emits both the per-namespace `Define()`
and `GeneratedStdlibRegistry.g.cs` — there is no hand-maintained registration list to
forget. Aggregated lazily in `StdlibDefinitions.cs` (`GeneratedStdlibRegistry.All()`).

**Why it matters:** if true, Stdlib's real surface is small — the silent-skip *edges* of
the generator (§1), a few nullable-metadata fields (§2), and one non-exhaustive switch (§3).
Confirm before sizing the spec; it could turn a "full spec" into a 2–3 phase job.
**Verify by:** read the three generator/registry files; add a throwaway `[StashFn]` and a
throwaway `[StashNamespace]` and observe what is/ isn't auto-wired.

## 1. Generator silent-skip edges — Instruct → Construct (diagnostics)

| Hypothesis | Confidence | Verify by | Construct fix |
| - | - | - | - |
| `[StashFn]` on a method **outside** any `[StashNamespace]` class is silently ignored — never registered, never warned. | MED | Place a `[StashFn]` on a non-namespace class; build. | Generator diagnostic `STSGxxx`: `[StashFn]` outside `[StashNamespace]`. |
| A method that *should* be a builtin but is missing `[StashFn]` is silently unregistered; call fails at runtime ("field not found"), no compile error. | MED | Drop `[StashFn]` from an existing fn; build + call. | Heuristic diagnostic ("builtin-shaped method without `[StashFn]`"). **Caveat:** heuristic is fuzzy — may be noise; decide if worth it during spec. |

## 2. Optional metadata that should be mandatory — Instruct → Construct (required fields)

`BuiltInParam.Type` (`Models/BuiltInParam.cs`), `NamespaceMember.ReturnType`
(`Models/NamespaceMember.cs`), and `BuiltInField.Type` (`Models/BuiltInField.cs`) are
`string? = null`. A new entry can be added with the type blank → silently incomplete
LSP signature/hover and docs, nothing complains.

- **Confidence:** MED. **Verify by:** confirm these fields are *intended* mandatory (vs. legitimately untyped/union). If mandatory → make the record param required and/or emit a generator diagnostic on omission.

## 3. Non-exhaustive switch — future-omission guard (Construct)

`NamespaceMemberPayload.Invoke()` (`Models/NamespaceMemberPayload.cs:~47-61`) handles
`Stability.Live` and `Stability.Cached`, **defaulting unknown values to the Cached path**.
A future `Stability` variant would silently behave as Cached.

- **Confidence:** MED (behavioral claim — reproduce). This is a *future*-omission guard, not a present bug — rank accordingly. **Construct fix:** exhaustive switch with `_ => throw`.

## 4. Cross-list / consistency gaps — Detect gaps

| Hypothesis | Confidence | Note |
| - | - | - |
| DataMembers lack the runtime-implementation consistency test that Functions/Constants have; a `Member()` registered but missing from the `Members` list (or `Members` passed null) could make a data member silently callable in analysis (SA0846 not firing). | LOW-MED | Behavioral — **must reproduce**; this is the riskiest claim and the least verified. |
| Duplicate-name dedup is per-kind (`seenFnNames`), so a Function vs Constant vs Struct name collision in one namespace isn't caught. | LOW | Rare; low blast radius. |
| `StdlibRegistry._ufcsTypeToNamespace` hard-codes `{string→str, array→arr, …}` with no validation that the target namespaces exist. | MED | Construct fix: static-ctor validation that every UFCS target resolves. |

## 5. Bounded-domain literals (language/stdlib domain)

The repo's no-magic-strings rule is enforced only for the *auth* domain
(`NoMagicAuthStringsMetaTests`). Closed sets in the stdlib domain — namespace names,
type-kind names, stability/deprecation states — may appear as inline literals with **no
guard**. **Confidence:** needs a scan. Likely a small named-constants/enum pass.

## Recommendations to push BACK on (record justification, don't force Construct)

- **Method-body `throw`-scanning generator diagnostic** (proposed to enforce `<exception>`
  metadata at build time): requires a Roslyn body walk — heavy and fragile. The existing
  `Wave1ThrowsCoverageTests` (Detect, with allow-list + fail-path self-test) is likely the
  *correct* level. Record the justification rather than forcing Construct.
- **Replacing `CompletionSurfaceSnapshotTests` / `StandardLibraryReferenceTests` with
  computed assertions:** loses the deliberate "conscious re-baseline" property that makes a
  snapshot valuable. Both explorers agreed — **keep as Detect.**
