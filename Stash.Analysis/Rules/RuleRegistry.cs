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

            // Declarations (SA02xx)
            new UnusedDeclarationRule(),
            new UndefinedIdentifierRule(),
            new ConstantReassignmentRule(),
            new LetCouldBeConstRule(),
            new UnusedParameterRule(),
            new ShadowVariableRule(),

            // Type Safety (SA03xx)
            new VariableTypeMismatchRule(),
            new ConstantTypeMismatchRule(),
            new UnknownTypeRule(),
            new FieldTypeMismatchRule(),
            new AssignmentTypeMismatchRule(),

            // Functions & Calls (SA04xx)
            new UserFunctionArityRule(),
            new BuiltInFunctionArityRule(),
            new ArgumentTypeMismatchRule(),

            // Spread / Rest (SA05xx)
            new SpreadDiagnosticsRule(),

            // Commands (SA07xx)
            new NestedElevateRule(),
            new RetryValidationRule(),

            // Imports (SA08xx)
            new UnusedImportRule(),
        ];
    }
}
