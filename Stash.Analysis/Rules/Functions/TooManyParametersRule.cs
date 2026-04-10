namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0405 — Emits an information diagnostic when a function declaration has more parameters
/// than the configured threshold (default: 5).
/// </summary>
/// <remarks>
/// Rest parameters are counted as one parameter. Lambda parameters are not checked because
/// lambdas are commonly used as callbacks and often inherit callback signatures.
/// </remarks>
public sealed class TooManyParametersRule : IAnalysisRule, IConfigurableRule
{
    /// <summary>Default parameter count threshold.</summary>
    public const int DefaultThreshold = 5;

    /// <summary>Configurable threshold; defaults to <see cref="DefaultThreshold"/>.</summary>
    public int Threshold { get; private set; } = DefaultThreshold;

    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0405;

    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("maxParams", out string? val) && int.TryParse(val, out int v) && v > 0)
            Threshold = v;
    }

    /// <summary>Subscribed to FnDeclStmt — analyzed once per function declaration.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(FnDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not FnDeclStmt fn)
        {
            return;
        }

        int paramCount = fn.Parameters.Count;
        if (paramCount > Threshold)
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0405.CreateDiagnostic(fn.Name.Span, fn.Name.Lexeme, paramCount, Threshold));
        }
    }
}
