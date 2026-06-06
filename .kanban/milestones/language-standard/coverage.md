# Language Standard — Spec Coverage Map

> **Living artifact.** This is the single source of truth for *what is sealed, what is not, and
> what must be fixed* in `docs/Stash — Language Specification.md`. It resolves the milestone
> charter's open question ("should there be a single roll-up coverage report?") — yes, this file.
>
> **Derived** 2026-06-06 by a 15-agent grammar-diffed audit (workflow `language-standard-coverage-map`,
> run `wf_4db398de-d48`): 1 inventory agent → 13 parallel per-section auditors → 1 omission-oracle +
> adversarial-skeptic synthesizer. See [Provenance](#provenance). Re-run the workflow and refresh this
> file when the spec moves materially; update per-section rows as each seal unit lands.

## How to read this

Each of the spec's 13 top-level `##` sections is one **row**. The milestone seals them one unit at a
time (a child `/spec language-standard-<area>` feature). A row is "done" when its status reads
**sealed**: full observable behavior + negative space written, every normative clause backed by a
`Category=Conformance` test, and any code that contradicted the now-written law fixed (seal-first-then-bend).

**The grammar is the completeness oracle.** Appendix A's EBNF (100 productions) was diffed against the
prose: a production or AST node with no semantics clause is a provable gap, not a judgment call. The
audit confirmed **every grammar production and keyword is owned by some section** — there are no
structural orphans *except* the three named [grammar holes](#structural--grammar-holes) below.

## Scoreboard

| | Count | Sections |
| --- | --- | --- |
| 🟢 **sealed** | **0** | — (the expected baseline; nothing meets the bar yet) |
| 🟡 **partial** | **6** | Bindings & Scope · Functions/Closures/Async · Source Files & Modules · Function References · Namespace Members · Runtime Behavior |
| 🔴 **unsealed** | **7** | Lexical Structure · Values & Types · Expressions · Statements & Control Flow · Aggregate Types & Members · Errors & Cleanup · Shell Integration |

**Inventory oracle:** 100 grammar productions · 39 keywords · 55 operators · 64 AST nodes.

**Seal-status discriminator.** "Zero `Category=Conformance` tests" is *uniform* across all 13 sections
(none exist yet) — so it is a **milestone-wide precondition, not a per-section maturity signal**. The
operative axis is therefore the prose itself:

- 🔴 **unsealed** — carries a **false/contradicted clause** OR a whole owned construct with **no
  semantics clause**.
- 🟡 **partial** — prose covers the happy path but has gaps: missing negative space, unnamed error
  types, under-specified constructs. No outright-false clause.
- 🟢 **sealed** — full behavior + negative space + conformance tests + no code contradicting the law.

## Coverage matrix (recommended seal order)

| # | Spec § | Status | Conformance dir | Highest-value open items (evidence) | Existing tests |
| - | ------ | ------ | --------------- | ----------------------------------- | -------------- |
| 1 | **Lexical Structure** (L84) | 🔴 unsealed | — | False clause: L198 "any other backslash escape is a lex error" vs `Lexer.cs:812` silently preserves unknown escapes in Windows-path heuristic. Multi-line string dangling normative ref. | Lexing/ (3) |
| 2 | **Values & Types** (L570) | 🔴 unsealed | — | **Contradiction: L629 lists empty array FALSEY; `Truthiness_EmptyArrayIsTruthy` (InterpreterTests.cs:4204) asserts truthy.** Equality/coercion punt to "the implementation" (L642, L650). Cross-type primitive `==` (1==1.0) unspecified. `range` missing from type table. Secret redaction string/edges unspec. NaN/-0.0/null equality edges. | Interpreting/ (7) |
| 3 | **Bindings & Scope** (L666) | 🟡 partial | — | Assign-to-`const` throws **unregistered** `RuntimeError` base, error type unspec (L689). Destructure-mismatch / `unset` failure error types unnamed. Same-scope redeclare unspec. (`readonly` sub-section is the spec's best-sealed prose.) | Readonly*, Scope* (11) |
| 4 | **Expressions** (L942) | 🔴 unsealed | — | Relational `< > <= >=` have **no semantics clause** (only a precedence row). Dict-spread `{...x}` implemented, unspecified. Arithmetic edges (div/mod by zero, overflow) unspec. Every error is bare "a runtime error", no named type. Range inclusivity unstated. | Interpreting/ (7) |
| 5 | **Statements & Control Flow** (L1208) | 🔴 unsealed | — | **Contradiction: `elevate` spec'd as "elevated privileges" (L1338) vs impl command-prefixing reality.** `lockOptions` (wait/stale) has no semantics. for-in iterable set + non-iterable error type unspec. switch-*statement* no-match unspec (asymmetric with switch-expr). | Interpreting/ (7) |
| 6 | **Functions, Closures, Async** (L1353) | 🟡 partial | `Conformance/Async/` *(populated — pattern-setter; 8 test classes, 105+ tests)* | §Async sealed; Functions/Closures/Lambdas/Methods half pending. Open items: `-> returnHint` has **zero prose**. **Loop-variable closure capture unspec** (impl shares the binding — prints 2,2 not 0,1). Arity-mismatch error unspec. Type-hint runtime-enforcement negative space. Future unit: `language-standard-functions`. **D5 note (P6):** D5 cross-task handle enforcement narrowed to process handles only; socket task-affinity is a documented impl limitation (not enforced at runtime — spec prose updated to reflect gap; architect-ratification pending). | **Conformance/Async/ (8 classes)** + **Interpreting/Async/ (16)** + (8) |
| 7 | **Aggregate Types & Members** (L1820) | 🔴 unsealed | — | **Two FALSE clauses: L1846 "missing required fields produce a runtime error" (VM `TypeOps.cs:347` does not check); L1867 declares interfaces "structural" but impl is nominal.** Struct instance identity/mutability/self unspec. Enum ordinal model unspec. | Interpreting/ (7) |
| 8 | **Errors & Cleanup** (L2248) | 🔴 unsealed | — | `retry`/`timeout` have keywords + AST nodes + ~90 tests but **no Appendix A grammar production**. `RetryExhaustedError`/`TimeoutError` tested but unspecified. Generalized error-type catalogue (see cross-cutting #2) belongs here. | Interpreting/ (5) |
| 9 | **Source Files & Modules** (L281) | 🟡 partial | — | Module caching / single-evaluation semantics unspec. Import-of-non-exported-name runtime contract is only an SA hint (SA0809), not normative. Every module failure is bare "a runtime error" (cycle/non-string-path/not-found), no named types. | Bytecode/Import* (10) |
| 10 | **Shell Integration** (L2114) | 🔴 unsealed | — | **Contradiction: L2124 "`$(...)` captures stdout as a string" vs impl returns `CommandResult(Stdout,Stderr,ExitCode)` (ProcessBuiltIns.cs:68).** Passthrough/streaming result shapes unspec. Pipe exit-code propagation unspec. Program-not-found error type unspec. | Interpreting/ (10) |
| 11 | **Function References** (L1917) | 🟡 partial | — | Fn-reference identity/equality unspec (`io.println == io.println` is `true`, unstated). DataMember call on a *dynamic* receiver leaks internal C# type name. (Happy path well-tested.) | FunctionReference* (4) |
| 12 | **Namespace Members** (L1962) | 🟡 partial | — | v1 member-set table already stale (`log.level`). Cached-getter-throws-on-first-access negative space unspec. Capability-denial vs getter-execution ordering unspec. | Interpreting/ (10) |
| 13 | **Runtime Behavior** (L2349) | 🟡 partial | — | Embedded-Mode restricted-side-effect contract fully open (which effects? what error?). "documented host diagnostic" escape hatch weakens the runtime-error guarantee. (Per-VM cwd/env overlay sub-section is well-specified.) | Stdlib/Env* (7) |

## Cross-cutting workstreams (owned by NO single section — highest leverage)

These are the **omission class** — concerns that span sections, so no per-section auditor owns them.
They are the async-gap failure mode generalized, and they unblock multiple rows at once. Do them
*before or alongside* the per-section seal passes.

1. **Stand up the `Conformance/` suite.** ✅ **Complete** (`language-standard-async`, 2026-06-06).
   `Stash.Tests/Conformance/Async/` established (8 test classes, 105+ tests), clause-citing convention
   documented, and `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` is the durable guard (reflection
   scan with fail-path self-test + scanned-count floor) every later unit inherits. The `[Trait("Category",
   "Conformance")]` trait now has non-zero bindings reachable via `dotnet test --filter "Category=Conformance"`.
2. **Author the error-type taxonomy (the single biggest gap).** Nearly every section describes failures
   only as "produces a runtime error" with **no named registered `[StashError]` type** — Bindings,
   Expressions, Statements, Functions, Modules, Shell, Aggregate, Errors. Worse, some throw the
   **unregistered `RuntimeError` base** (const-assignment). No section owns the catalogue; this is the
   async-gap class *at the error layer*. Likely home: **§Errors & Cleanup** absorbs the generalized map,
   each consuming section cites it.
3. **Pin the truthiness / equality / coercion substrate (§Values & Types).** Defined once, cited
   everywhere (every `if`/`while`/ternary/`switch` condition, struct/array/fn-ref equality). The
   empty-array contradiction and unspecified cross-type `==` propagate into every consumer — these
   sections **cannot be sealed independently** of Values & Types. Seal the substrate first (order #2).
4. **Specify closure / loop-variable capture once.** Straddles §Bindings (L780) and §Functions (L1414).
   Impl empirically **shares** the loop binding (a closure made in a loop observes the final value).
   Unspecified in *both* — a reviewer must spec it once, not double-spec or leave it in the seam.
5. **Resolve the `namedArgument` grammar/example/binding contradiction.** Appendix A defines
   `namedArgument = identifier ":" expression` (L2577) and §Calls *uses* it (`deploy("prod", retries: 3)`,
   L1051), but the parser rejects named args on user functions (**parse error**, verified on the built
   CLI). The grammar production, the worked example, and the parameter-binding semantics all reference a
   feature that does not exist for general calls. Spans Expressions + Functions + Appendix A; owned by none.

## Live spec-vs-impl contradictions (seal-first decisions)

Each is a clause where the spec and the implementation **disagree**. Per the milestone DoD, each needs
an explicit ruling in its seal unit: **fix the code to honor the law, or correct the law on purpose.**
The first two are confirmed by direct spot-check; the rest are audit-identified (confirm in the unit).

| § | Spec says | Impl does | Status |
| - | --------- | --------- | ------ |
| Values & Types | L629: empty array is **falsey** | `[] ? …` is **truthy** (InterpreterTests.cs:4204) | ✅ confirmed |
| Shell Integration | L2124: `$(...)` captures stdout **as a string** | returns `CommandResult(Stdout,Stderr,ExitCode)` (ProcessBuiltIns.cs:68) | ✅ confirmed |
| Lexical Structure | L198: any other backslash escape is a **lex error** | `Lexer.cs:812` silently preserves unknown escapes (Windows-path heuristic) | audit-identified |
| Aggregate Types | L1846: missing required fields **produce a runtime error** | `TypeOps.cs:347` does **not** check | audit-identified |
| Aggregate Types | L1867: interfaces are **structural** | conformance is **nominal** | audit-identified |
| Statements | L1338: `elevate` runs with **elevated privileges** | command-prefixing reality (`VirtualMachine` elevate impl) | audit-identified |

## Structural / grammar holes

The omission oracle's three true structural findings (the rest of the 64 AST nodes are covered-by-proxy
through their grammar productions):

- **`namedArgumentList` — dangling grammar reference.** `lockOptions = "(" namedArgumentList? ")"`
  (Appendix A L2522) references a production that **is never defined anywhere** in Appendix A. (Same
  family as the `namedArgument` cross-cutting contradiction.)
- **`retry` / `timeout` — missing grammar productions.** Both have keywords, AST nodes
  (`RetryExpr`/`TimeoutExpr`/`OnRetryNode`), prose (§2302, §2321) and tests, but `unary` (L2548) lists
  only `! - ~ try await`; they appear only in Appendix B's reserved list. Prose-vs-grammar binding is
  unbacked.
- **`@throws` (`ThrowsEntry`) — semantic negative space owned by no section.** Doc-comments are spec'd
  as "must not affect program behavior" (L107), yet `@throws` **drives tooling** (the
  Wave1/Wave2 throws-coverage gate). What `@throws` asserts and whether it is enforced is unspecified.

**Two inventory artifacts to NOT spec** (flagged so a later pass doesn't chase them):
- Bare `<(` / `>(` in the operators list are **substring artifacts** of the `$<(` / `$>(` command-literal
  tokens — no `TokenType`, no production, not real operators.
- `export` is a soft keyword **missing from the inventory keyword list** (an inventory gap, not an
  orphan) — it is owned by §Source Files & Modules.

## Recommended sealing order & rationale

Foundations first — later sections speak the vocabulary earlier ones define (truthiness, type names,
error types). The order below is the verifier's, dependency-ranked:

1. Lexical Structure → 2. **Values & Types** → 3. Bindings & Scope → 4. Expressions →
5. Statements & Control Flow → 6. Functions, Closures, Async → 7. Aggregate Types →
8. Errors & Cleanup → 9. Source Files & Modules → 10. Shell Integration →
11. Function References → 12. Namespace Members → 13. Runtime Behavior.

*(Function References + Namespace Members are adjacent because they share the Function/DataMember/Constant
declaration-kind contract.)*

**But two adjustments to a naive top-down march:**

- **Async ships first as the pattern-setter** (out of strict order — historical, and it is already
  in-flight). Finishing §Async stands up `Conformance/Async/` (cross-cutting move #1) and exercises the
  error-type discipline (D7) on one section before generalizing.
- **The cross-cutting workstreams precede or parallel the per-section march.** In particular the
  error-type taxonomy (#2) and the truthiness/equality substrate (#3) unblock many rows; sequence them
  early so per-section units can *cite* them rather than re-derive.

**Suggested first units:** `language-standard-async` (seal §Async + establish `Conformance/`) → then
either `language-standard-errors` (the cross-cutting error-type taxonomy) or `language-standard-values`
(the truthiness/equality substrate, order #2).

## Provenance

- **Produced by** workflow `language-standard-coverage-map`, run `wf_4db398de-d48`, 2026-06-06 — 15
  agents, ~19 min. Inventory agent extracted Appendix A + `TokenType.cs` + the AST dir; 13 auditors read
  one section each (prose + grammar-diff + code + test grep); the synthesizer ran the orphan check and an
  adversarial seal-status review (one downgrade: Values & Types partial→unsealed).
- **To refresh:** re-invoke the workflow (script persisted under the session's `workflows/scripts/`),
  then reconcile this file's rows. Update individual rows in place as each seal unit lands its prose +
  conformance tests + code fixes.
