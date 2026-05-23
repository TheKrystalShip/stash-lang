# Standard Library Reference — Generated Contract and Quality Rewrite

**Status:** Todo — Documentation Architecture
**Created:** 2026-05-14
**Purpose:** Bring `docs/Stash — Standard Library Reference.md` up to the same quality bar as the rewritten language specification while preventing future API drift.

---

## Problem

The standard library reference is valuable but organically grown. It is currently about 5,500 lines and mixes several concerns:

- API inventory, function contracts, examples, deprecation history, moved namespace notes, threading model, shell tooling, and testing infrastructure
- Hand-maintained namespace/function lists that can drift from `StdlibDefinitions`
- Uneven namespace section depth and inconsistent function documentation shape
- Known visible drift such as the duplicate `toml` entry in the table of contents
- Source-of-truth duplication between the Markdown file and generated stdlib metadata

The language specification has been rewritten as a concise normative contract. The stdlib reference should reach the same standard, but the correct approach is different: the stdlib API inventory should be generated from source metadata, while high-level semantics and examples remain curated.

---

## Goal

Turn `docs/Stash — Standard Library Reference.md` into a polished, contract-grade API reference whose function/namespace inventory is generated from the same source-of-truth metadata used by runtime registration, LSP hover, completion, static analysis, deprecation warnings, and throws metadata.

The finished reference should feel closer to a professional standard-library reference than a narrative guide:

- predictable namespace layout
- complete function signatures
- explicit parameter and return contracts
- explicit throws/deprecation/capability metadata
- concise examples
- no stale namespace counts or duplicate TOC entries

---

## Non-Goals

- Do not change stdlib runtime behavior.
- Do not redesign namespace APIs as part of the documentation rewrite.
- Do not move the public reference path; it remains `docs/Stash — Standard Library Reference.md`.
- Do not make the entire document generated. The implementation should generate the mechanical API inventory and keep curated explanatory text where it adds value.
- Do not duplicate TAP, shell-mode, package-registry, or VM protocol documentation inside the stdlib reference. Link to companion docs instead.

---

## Current Source of Truth

The implementation already exposes most of the metadata needed for a generated reference:

- Namespaces: `[StashNamespace]` and `StdlibDefinitions.Namespaces`
- Functions: `[StashFn]`, `NamespaceFunction.Detail`, parameters, return type, documentation
- Throws: `[StashFn(ThrowsTypes = ...)]` and XML `<exception>` docs
- Deprecation: `[StashDeprecated(...)]`
- Capability gates: namespace-level `StashNamespaceAttribute.Capability` and function-level `StashFnAttribute.Capability`
- Types: `[StashStruct]`, `[StashEnum]`, generated `BuiltInStruct`, `BuiltInEnum`, fields, constants
- Registry queries: `StdlibRegistry.NamespaceNames`, `NamespaceFunctions`, `NamespaceConstants`, `Structs`, `Enums`

The generator and registry should be treated as the canonical API inventory. Markdown should not manually maintain namespace counts, function lists, or signatures when metadata can produce them.

---

## Metadata Improvements

DocFX is useful prior art: it extracts C# metadata and XML documentation into a structured model, then renders that model into HTML or other formats. Stash should follow the same discipline, but the generated stdlib reference must document the **Stash-visible API**, not the C# implementation API. The source of truth should therefore be Stash metadata attributes plus XML docs, not raw C# method signatures alone.

Before the full rewrite, improve the C# metadata surface so the generator has enough structured information:

- Ensure every `[StashNamespace]` class has namespace-level XML docs with a short purpose summary.
- Ensure every `[StashFn]` method has XML `<summary>`, `<param>`, `<returns>`, and `<exception>` docs where applicable.
- Use `<remarks>` for semantic notes that belong in the reference, such as ordering, encoding, platform behavior, buffering, cancellation, or path resolution.
- Use `<example>` for compact examples that can be rendered into namespace or function examples.
- Keep `[StashFn(ReturnType = ...)]`, `[StashParam(Type = ...)]`, `[StashDeprecated(...)]`, and `[StashFn(ThrowsTypes = ...)]` as the structured source of truth for the public Stash contract.
- Add metadata only where it removes ambiguity for generation; avoid duplicating information that can already be inferred from attributes or generated models.

Recommended small additions to the metadata model:

- `StashNamespaceAttribute.Category` or equivalent grouping metadata for reference organization, for example `Core`, `Data`, `System`, `Network`, `Shell`, `Tooling`.
- Optional `StashFnAttribute.ExamplesKey` / `StashNamespaceAttribute.ExamplesKey` if curated examples need to live outside XML comments while still being merged deterministically.
- Optional `StashFnAttribute.Since` and `StashDeprecatedAttribute.RemoveIn` if the project wants version-aware docs and cleaner deprecation tables.
- A generated-docs model that normalizes Stash-facing signatures independently of C# method names and C# parameter types.

DocFX itself may still be useful later for a developer-facing C# API website. It should not be the primary stdlib reference generator unless it is fed a Stash-specific metadata model, because default DocFX output would document implementation classes such as `CsvBuiltIns.Parse` rather than public functions such as `csv.parse(text, options?)`.

---

## Proposed Document Shape

Rewrite the reference around a stable template:

1. **Overview**
   - Purpose and audience
   - Relationship to the language spec
   - How to read signatures
   - Type notation
   - Error and throws conventions
   - Capability gates and embedded-mode restrictions
   - Deprecation policy

2. **Global Functions and Types**
   - Generated inventory for global functions
   - Generated built-in error/type tables
   - Curated examples for secrets, error values, and common global helpers

3. **Namespace Reference**
   - One section per namespace, in generated canonical order
   - Each namespace follows the same structure:
     - Purpose
     - Capability requirement
     - Types/constants
     - Function summary table
     - Function details
     - Throws and deprecations
     - Short examples

4. **Appendices**
   - Deprecated members and replacements
   - Capability-gated namespaces/functions
   - Cross-document links for TAP, shell mode, package manager, registry, TPL, LSP/DAP, and VM docs

---

## Generated Reference Design

Add a small documentation generator that reads `StdlibRegistry` / `StdlibDefinitions` metadata and overwrites deterministic Markdown output. Generated API content is canonical: if checked-in Markdown differs from stdlib metadata, the generator replaces the Markdown with the metadata-derived version and git shows the resulting diff.

Minimum generated fragments:

- Namespace index table
- Per-namespace function summary tables
- Function signatures and parameter lists
- Return types
- Throws lists
- Deprecation notices and replacements
- Namespace constants
- Struct and enum fields/members
- Capability requirements

Curated text should live in hand-authored sections keyed by namespace or function. The generator should merge curated prose with generated inventory while treating generated inventory as authoritative. Hand-written text may explain behavior, but it must not override metadata-derived signatures, throws, deprecations, types, constants, capabilities, or namespace membership.

Implementation shape:

- A `dotnet run`-style internal docs command writes the final Markdown in place.
- The command is deterministic: running it twice without stdlib metadata changes produces byte-identical output.
- CI runs the command and then checks that the working tree is clean.
- The tool should avoid a separate "compare Markdown to registry" mode; git is the diff engine.

Preferred default: implement the generator in C# so it can reuse `StdlibDefinitions` directly without brittle parsing.

---

## Staged Implementation Plan

### Stage 1 — Deterministic Overwrite Generator

Build the first version of the generator as an overwrite command. It reads `StdlibRegistry` / `StdlibDefinitions` metadata and writes the canonical generated reference output to `docs/Stash — Standard Library Reference.md` or to clearly marked generated regions inside that file.

It must overwrite canonical generated content for:

- namespaces and TOC entries
- function entries and signatures
- structs, enums, and constants
- throws metadata
- deprecation notices and replacements
- capability metadata

Acceptance:

- The command removes generated drift such as the duplicate `toml` TOC entry by rewriting from metadata.
- Running the command twice with no code changes produces no git diff.
- Running the command after a stdlib metadata change updates the Markdown deterministically.
- The command can be used in CI by running it and checking that git reports no changes.

### Stage 2 — Define the Reference Template

Create the final section template and normalize the Markdown style before doing a full rewrite.

Acceptance:

- The top matter matches the new language specification style and companion links.
- The "how to read this reference" section defines signature notation, optional parameters, nullable types, arrays, `Future<T>`, errors, capabilities, and deprecations.
- A single namespace is rewritten as the gold-standard example section.

Recommended pilot namespace: `csv` or `archive`, because they are concrete, bounded, and include options structs plus file I/O errors.

### Stage 3 — Complete Generated API Inventory

Expand deterministic generation for API inventory tables and details from metadata.

Generated output must include:

- namespace names and capability gates
- functions with signatures
- parameters and return type
- throws metadata
- deprecation metadata
- constants
- structs and enums

Acceptance:

- Generated output is stable across repeated runs.
- Generated output requires no hardcoded namespace list.
- Existing source-generator snapshot tests remain green.

### Stage 4 — Rewrite the Reference

Rewrite `docs/Stash — Standard Library Reference.md` using the final structure.

Keep curated examples and semantics, but remove duplicated implementation history and long-form material better owned by companion docs:

- Move testing framework detail to `docs/TAP — Testing Infrastructure.md`
- Link shell alias/completion/prompt behavior to `docs/Shell — Interactive Shell Mode.md`
- Link package/registry behavior to `docs/PKG — Package Manager CLI.md` and `docs/Registry — Package Registry.md`
- Keep `tpl` as a namespace reference and link deeper templating semantics to `docs/TPL — Templating Engine.md`

Acceptance:

- No duplicate TOC entries.
- Every generated namespace appears exactly once.
- Every generated function is represented exactly once.
- The reference remains concise enough to scan.
- Link validation passes.

### Stage 5 — CI Guardrail

Wire the overwrite generator into tests or CI.

Acceptance:

- Adding a new `[StashFn]` without running the generator causes CI to fail because generated Markdown changes.
- Renaming/removing a namespace or function produces a useful failure message.
- Deprecation and throws metadata drift is corrected by generation and caught by the clean-worktree check.

---

## Reference Section Template

Each namespace section should use this shape:

````markdown
## `namespace` — Human Name

Brief purpose statement.

**Capability:** `Network` / `FileSystem` / `Process` / `Environment` / none
**Throws:** generated summary, with function-specific exceptions in detail rows

### Types and Constants

Generated tables for structs, enums, and constants.

### Functions

Generated function summary table:

| Function | Returns | Throws | Description |
| -------- | ------- | ------ | ----------- |

### Function Details

#### `namespace.function(param: type, optional?: type = default) -> return`

Short contract:
- Parameters
- Return value
- Errors
- Deprecation note, if applicable

```stash
// one compact example
```
````

For very small namespaces, the function details may be omitted if the summary table and examples fully specify the contract.

---

## Generation and CI Details

The generator should write canonical Markdown from metadata rather than relying on fragile namespace-count strings or hand-maintained comparison logic.

Generation rules:

- Generated namespace headings come from non-global `StdlibDefinitions` namespaces.
- The global namespace is rendered in "Global Functions and Types".
- Function detail headings and summary rows come from generated qualified signatures.
- Deprecation rows include the replacement from `DeprecationInfo`.
- Throws docs include each generated `ThrowsEntry.ErrorType`.
- The generated TOC cannot contain duplicate namespace entries because it is derived from the metadata set.

CI should run the generator, then fail if `git diff --exit-code` reports changes. This keeps the tool simple: the generator always writes canonical output, and git reports whether the checked-in document was stale.

---

## Acceptance Criteria

- [ ] `docs/Stash — Standard Library Reference.md` has the same professional quality bar as the language spec.
- [ ] Generated metadata, not hand-maintained lists, controls namespace/function inventory.
- [ ] All namespaces in `StdlibDefinitions.Namespaces` are documented exactly once, excluding the global namespace which is documented in "Global Functions and Types".
- [ ] All functions include signatures, return types, throws metadata, and deprecation status where available.
- [ ] The duplicate `toml` TOC entry is removed.
- [ ] Companion links use the current docs layout.
- [ ] TAP, shell, package, registry, TPL, LSP/DAP, and VM details are linked instead of duplicated.
- [ ] The generator runs in tests or CI and stale docs fail via a clean-worktree check.

---

## Risks and Notes

- The current Markdown is large enough that a one-shot rewrite is likely to lose useful examples or subtle behavior notes. Prefer namespace-by-namespace migration after the template is locked.
- Generated documentation must not become unreadable. Keep generated tables mechanical and use curated prose for behavior, gotchas, and examples.
- Function-level metadata may not yet contain enough prose for a complete public reference. Where XML docs are sparse, improve source XML docs instead of only patching Markdown.
- Some namespaces are better treated as companion-doc entry points (`test`, `assert`, `tpl`, `pkg`, shell helpers). The stdlib reference should still list their API surface but defer deep workflow explanation to their dedicated docs.
