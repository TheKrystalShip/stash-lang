namespace Stash.Analysis.Rules;

using System.Collections.Generic;

/// <summary>
/// Central registry of all <see cref="IAnalysisRule"/> implementations.
/// </summary>
/// <remarks>
/// Add a new entry here whenever a new rule is introduced. The list order determines the
/// order in which per-node rules are dispatched by <see cref="SemanticValidator"/>. Post-walk
/// rules are always run after the full AST walk, in registration order.
/// </remarks>
public static class RuleRegistry
{
    /// <summary>
    /// Returns a new list containing one instance of every registered analysis rule.
    /// </summary>
    public static IReadOnlyList<IAnalysisRule> GetAllRules()
    {
        return
        [
            // Control Flow (SA01xx)
            new BreakOutsideLoopRule(),
            new ContinueOutsideLoopRule(),
            new ReturnOutsideFunctionRule(),
            new UnreachableCodeRule(),
            new EmptyBlockRule(),
            new UnreachableBranchRule(),
            new CyclomaticComplexityRule(),

            // Declarations (SA02xx)
            new UnusedDeclarationRule(),
            new UndefinedIdentifierRule(),
            new ConstantReassignmentRule(),
            new LetCouldBeConstRule(),
            new UnusedParameterRule(),
            new ShadowVariableRule(),
            new DeadStoreRule(),
            new DefiniteAssignmentRule(),
            new NamingConventionRule(),
            new FunctionInLoopRule(),
            new ShadowsBuiltinNamespaceRule(),

            // Type Safety (SA03xx)
            new VariableTypeMismatchRule(),
            new ConstantTypeMismatchRule(),
            new UnknownTypeRule(),
            new FieldTypeMismatchRule(),
            new AssignmentTypeMismatchRule(),
            new PossibleNullAccessRule(),
            new NullFlowRule(),
            new ExhaustiveMatchRule(),
            new InvalidRegexPatternRule(),

            // Functions & Calls (SA04xx)
            new UserFunctionArityRule(),
            new BuiltInFunctionArityRule(),
            new ArgumentTypeMismatchRule(),
            new MissingReturnRule(),
            new TooManyParametersRule(),
            new AsyncCallNotAwaitedRule(),
            new AsyncFunctionWithoutAwaitRule(),

            // Spread / Rest (SA05xx)
            new SpreadDiagnosticsRule(),

            // Commands (SA07xx)
            new NestedElevateRule(),
            new RetryValidationRule(),

            // Imports (SA08xx)
            new UnusedImportRule(),
            new ImportOrderingRule(),

            // Aliases (SA085x)
            new Aliases.AliasDefineRule(),

            // Deprecations (SA083x)
            new Deprecations.DeprecatedBuiltInMemberRule(),

            // Style (SA09xx)
            new NoUnnecessaryElseRule(),
            new FunctionBodyTooLongRule(),

            // Complexity (SA10xx)
            new MaxDepthRule(),

            // Best Practices (SA11xx)
            new NoSelfAssignRule(),
            new NoDuplicateCaseRule(),
            new NoLoneBlocksRule(),
            new NoSelfCompareRule(),
            new NoConstantConditionRule(),
            new NoUnreachableLoopRule(),
            new AssignmentInConditionRule(),
            new MagicNumberRule(),

            // Performance (SA12xx)
            new NoAccumulatingSpreadRule(),
            new StringConcatInLoopRule(),
            new RepeatedCallInLoopConditionRule(),

            // Security (SA13xx)
            new NoHardcodedCredentialsRule(),
            new NoUnsafeCommandInterpolationRule(),
            new CatastrophicBacktrackingTaintRule(),

            // Suggestions (SA14xx)
            new UseOptionalChainingRule(),
            new UseNullCoalescingRule(),
            new PreferStringInterpolationRule(),
        ];
    }
}
