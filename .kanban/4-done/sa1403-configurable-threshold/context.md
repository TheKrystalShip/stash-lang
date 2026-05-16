# SA1403 Configurable String Concatenation Threshold — Context

> Consolidated explorer findings produced by the architect.
> Purpose: give each implementer turn the exact code-pointers it needs without re-exploring.

## Key file paths

| Concern | File | Notes |
| --- | --- | --- |
| The rule itself (today) | `Stash.Analysis/Rules/Suggestions/PreferStringInterpolationRule.cs` | 48 lines. Implements `IAnalysisRule`, subscribes to `BinaryExpr`. **Rewrite target.** |
| Diagnostic descriptor for SA1403 | `Stash.Analysis/Models/DiagnosticDescriptors.cs:202` | `MessageFormat` is plain (no placeholders) today. Registered at line 317 in `BuildCodeLookup`. |
| Configurable rule marker interface | `Stash.Analysis/Rules/IConfigurableRule.cs` | `Configure(IReadOnlyDictionary<string,string> options)`. |
| Prior art: configurable rule with `Threshold` | `Stash.Analysis/Rules/Style/FunctionBodyTooLongRule.cs` | Uses `int.TryParse(val, out int v) && v > 0` pattern. Public `DefaultThreshold` const, public `Threshold` property with private setter. Mirror exactly. |
| Other configurable rules | `Stash.Analysis/Rules/` (search `IConfigurableRule`) | `CyclomaticComplexityRule` (maxComplexity), `MaxDepthRule` (maxDepth), `TooManyParametersRule` (maxParams), `FunctionBodyTooLongRule` (max_function_lines). Inconsistent key naming — we use `threshold` per Decision Log §5. |
| `.stashcheck` parser | `Stash.Analysis/Models/ProjectConfig.cs` | `options.<CODE>.<key>` → `RuleOptions[code][key]` is **already wired** (lines 268–283 of ParseContent). No parser change needed. |
| Rule options applied to rules | `Stash.Analysis/Engines/AnalysisEngine.cs:179-186` | Loop after rule filtering: `if (rule is IConfigurableRule c && projectConfig.RuleOptions.TryGetValue(rule.Descriptor.Code, out var opts)) c.Configure(opts);` |
| Rule registration | `Stash.Analysis/Rules/RuleRegistry.cs:118` | `new PreferStringInterpolationRule()`. No change unless ctor signature changes (it should not). |
| RuleContext (rule input) | `Stash.Analysis/Rules/RuleContext.cs` | **Phase 1A must inspect this** to confirm whether a rule can see its parent AST node. Today's rule only reads `context.Expression`, `context.LoopDepth`. |
| Visitor / rule dispatch | `Stash.Analysis/Engines/SemanticValidator.cs` | Walks the AST and calls each subscribed rule's `Analyze`. Phase 1A reads this to see how `RuleContext` is constructed per node — that is where any new parent-tracking would be populated. |
| Tests for SA1403 | `Stash.Tests/Analysis/StaticAnalysisEnhancementsTests.cs:480-510` | Three existing tests: `SA1403_StringLiteralPlusVar_ReportsInfo`, `SA1403_TwoLiterals_NoInfo`, `SA1403_StringConcatInLoop_NoInfo`. First test must be renamed/inverted (see spec §6). |
| Tests for `IConfigurableRule.Configure` | `Stash.Tests/Analysis/Phase6RuleOptionsTests.cs:144+` | Pattern: instantiate rule, call `Configure(dict)`, assert `rule.Threshold == expected`. Mirror for `PreferStringInterpolationRule`. |
| Stash.Check docs | `Stash.Check/README.md:136` | Has a `options.<CODE>.<key>` line in the table; add a concrete SA1403 example nearby. |

## Key types

- `PreferStringInterpolationRule` — `Stash.Analysis/Rules/Suggestions/PreferStringInterpolationRule.cs:16`
- `IConfigurableRule` — `Stash.Analysis/Rules/IConfigurableRule.cs:8` — single method `Configure`.
- `IAnalysisRule` — sibling file; requires `Descriptor`, `SubscribedNodeTypes`, `Analyze(RuleContext)`.
- `RuleContext` — `Stash.Analysis/Rules/RuleContext.cs` — known fields used today: `Expression`, `Statement`, `LoopDepth`. **Parent visibility is the open question Phase 1A resolves.**
- `BinaryExpr` — `Stash.Parsing.AST.BinaryExpr` — has `Left`, `Right`, `Operator` (a `Token`), `Span`.
- `LiteralExpr` — `Stash.Parsing.AST.LiteralExpr` — has `Value` (object). String literals: `Value is string`.
- `StringInterpolationExpr` — separate AST node; **not** a `LiteralExpr`. Important for Decision Log §3.
- `TokenType.Plus` — operator discriminant the rule already filters on.
- `DiagnosticDescriptor` — `Stash.Analysis/Models/DiagnosticDescriptor.cs` — record with `CreateDiagnostic(Span, params object[] args)` factory.
- `DiagnosticDescriptors.SA1403` — `Stash.Analysis/Models/DiagnosticDescriptors.cs:202` — update `MessageFormat` here.
- `ProjectConfig.RuleOptions` — `Stash.Analysis/Models/ProjectConfig.cs:39` — `IReadOnlyDictionary<string, Dictionary<string,string>>`. Key is rule code, inner key is option name.

## Conventions discovered

- **Single source of truth for diagnostics.** Per `Stash.Analysis/CLAUDE.md`: never construct a `SemanticDiagnostic` by hand; always emit via `DiagnosticDescriptors.<CODE>.CreateDiagnostic(span, args...)`. Changing the message text means editing exactly one line in `DiagnosticDescriptors.cs`.
- **Configurable-rule shape:** public `const int DefaultThreshold = N`, public `Threshold` with private setter initialized to `DefaultThreshold`, `Configure()` uses `int.TryParse` + positivity guard, falls back silently. Match `FunctionBodyTooLongRule` byte-for-byte where reasonable.
- **`.stashcheck` option key shape:** `options.<CODE>.<key> = <value>`. Codes are full 6-char SA-codes; keys are camelCase or snake_case (existing keys are inconsistent: `max_function_lines`, `maxComplexity`, `maxDepth`, `maxParams`). New rules should prefer the prior-art name for that rule family — `threshold` matches `FunctionBodyTooLongRule`'s public surface.
- **Default-disabled rules** require `enable=<code>` in `.stashcheck`. SA1403 is **not** default-disabled (see `ProjectConfig.DefaultDisabledCodes` — only SA0164 and SA0169). No change here.
- **Loop-aware rules read `RuleContext.LoopDepth`.** This is how the existing rule defers to SA1202 inside loops. Preserve this guard verbatim.
- **Test base class:** `Stash.Tests/Analysis/AnalysisTestBase` (used by `Phase6RuleOptionsTests`). Provides helpers like `Analyze(source)` returning diagnostics. Look at neighboring tests for the exact API surface (e.g. how to inject a `ProjectConfig` override — `AnalysisEngine.Analyze` takes a `configOverride` parameter).

## Prior art / similar features

- `.kanban/4-done/Static Analysis — v1.0 Enhancements.md` — original SA1403 introduction. Useful for the rule's original intent.
- `.kanban/4-done/Diagnostic Codes & Suppression — Infrastructure Spec.md` — the `.stashcheck` and suppression-comment infrastructure.
- `.kanban/4-done/Static Analysis Engine — Long-Term Improvement Roadmap.md` — strategic context for the rule system.
- `FunctionBodyTooLongRule` (cited above) — closest implementation analogue. SA0902. Same `IConfigurableRule` + `Threshold` shape we are adopting.
- `Phase6RuleOptionsTests` — the canonical test-shape for verifying `Configure()` behavior on configurable rules.

## Gotchas surfaced during exploration

- **Constant folding may pre-collapse all-literal chains.** Even though spec §3.2 implements the `HasNonLiteral` guard, a future folder running before analysis would mean we never see those chains. That is acceptable — the guard makes the rule robust regardless of fold order. Do not assume folding happens; do not assume it doesn't.
- **`RuleContext.Parent` may not exist.** If Phase 1A finds no parent access, the rule must either:
  1. Add a minimal `Parent` (or `ParentBinaryOperator`) field to `RuleContext`, populated by `SemanticValidator` as it walks (cheap, isolated), OR
  2. Use a thread-local "I'm inside a `+` chain root" sentinel during the walk — *not recommended*, fragile.
  Approach (1) is the documented path. The field should be nullable / optional so existing rules continue to compile unchanged.
- **Message text changes ripple through `RuleDocGenerator`.** `Stash.Check/RuleDocGenerator.cs` reads `Descriptor.MessageFormat`. Regenerated docs will show the new format. Any snapshot test of generated rule docs (search for `RuleDocGenerator` test references) must be updated.
- **`AnalysisEngine.Analyze`'s `configOverride` parameter** is the test seam for injecting a `ProjectConfig` with rule options. Use it in the new `ConfigureThresholdX_...` tests rather than touching the filesystem.
- **The diagnostic span** should be the **root chain `BinaryExpr.Span`**, not any individual `+` token's span. This is what the current rule does (`bin.Span`); preserve.
- **`bin.Operator.Type != TokenType.Plus`** is the existing filter — preserve. Stash uses `+` for both numeric add and string concat; the literal-string check is what disambiguates.
- **AST grouping nodes.** Whether `(a + b) + c` produces a Group/Paren node between the outer and inner `BinaryExpr` is unconfirmed. Phase 1A must answer. If a Group node exists, the simplest safe behavior is to treat it as a leaf (terminating the chain there); spec §3.2 permits either choice as long as it's documented.
- **Do not regenerate `docs/Stash — Standard Library Reference.md`.** This feature touches no stdlib metadata. The doc-regeneration step in `.claude/language-changes.md` does not apply.
