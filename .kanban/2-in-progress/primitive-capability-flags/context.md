# Context — primitive-capability-flags

Files the implementer must read before touching code. Not exhaustive — the brief
points at additional code as needed.

## Primary integration point

- **`Stash.Core/Common/PrimitiveTypes.cs`** — the registry to extend. Note the
  two private arrays (`_languagePrimitives`, `_runtimePrimitives`), the `Read<T>()`
  helper feeding the runtime row from `IVMPrimitiveType` static-abstract members,
  and the public `Names` (FrozenSet) / `Descriptions` (FrozenDictionary) surface
  that must be preserved.

## Consumers to migrate

- **`Stash.Bytecode/Compilation/Compiler.Declarations.cs:298`** — the literal
  switch `typeName is "string" or "array" or "dict" or "int" or "float"` inside
  `VisitExtendStmt`. This is the runtime authority for which primitives accept
  `extend`. Replace with `PrimitiveTypes.ExtendableNames.Contains(typeName)`.
- **`Stash.Lsp/Completion/Providers/ExtendTypeCompletionProvider.cs:41`** — the
  `BuiltInExtendableTypes` static field. Public API stays; initialization
  switches from a literal to a projection of `PrimitiveTypes.ExtendableNames`
  with deterministic alphabetical ordering.

## Existing test consumers (must keep passing without modification)

- **`Stash.Tests/Lsp/Completion/ContextModeProvidersTests.cs`** — the
  `ExtendableBuiltInTypes()` helper at line 358 reads
  `ExtendTypeCompletionProvider.BuiltInExtendableTypes`. Public API preservation
  guarantees this keeps working.
- **`Stash.Tests/Lsp/CompletionSurfaceSnapshotTests.cs`** —
  `Snapshot_AfterExtend` locks in the post-`extend` completion set. No
  re-baselining expected; if the set changes order, fix the projection.
- **`Stash.Tests/Core/IVMPrimitiveTypeInvariantTests.cs`** — template for the
  new `PrimitiveCapabilityInvariantTests`. Read its data-driven pattern
  (reflection over `IVMPrimitiveType` implementers, parameterized assertions
  with diagnostic messages naming the offending type) and mirror the shape.
- Bytecode `extend` tests — search `Stash.Tests/Bytecode/` and
  `Stash.Tests/Interpreting/` for existing `extend` coverage. The runtime
  message used by the negative path is `Cannot extend '<name>': not a known type.`
  per `ExtendTypeCompletionProvider.cs:30` documentation; confirm with a
  pre-change run before authoring the invariant test.

## Culture context (read these once)

- **`Stash.Core/CLAUDE.md`** — section "Never add hardcoded `if (value is X)` type
  switches in the VM dispatch." Explains the protocol-first preference for
  runtime types and why this feature does not violate it (the integration point
  is in `Stash.Common`'s registry layer, not in VM dispatch; the affected
  primitives include tagged-union variants that have no backing class).
- **`Stash.Lsp/CLAUDE.md`** — section "Symbol filtering invariant". Background
  on why the LSP and runtime carry independent views of the same data and the
  filtering they impose.
- **`.kanban/2-in-progress/lsp-completion-providers/review.md`** — F01 entry.
  Original observation, conservative fix (commit `4bcfb3a`), and the explicit
  call-out that the architectural fix lives in a follow-up feature (this one).

## Sequencing

This feature is **blocked** until `lsp-completion-providers` is promoted to
`.kanban/4-done/`. Do not start P1 while the predecessor feature still has
`2-in-progress/lsp-completion-providers/checkpoint.yaml`. Re-check by listing
`.kanban/2-in-progress/` before invoking `/next-phase`.
