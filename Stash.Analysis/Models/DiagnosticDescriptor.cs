namespace Stash.Analysis;

using Stash.Common;

/// <summary>
/// Describes a known diagnostic produced by the Stash static analysis engine.
/// Each descriptor has a unique code, default severity, message template, and metadata.
/// </summary>
public sealed class DiagnosticDescriptor
{
    public string Code { get; }
    public string Title { get; }
    public DiagnosticLevel DefaultLevel { get; }
    public string Category { get; }
    public string MessageFormat { get; }

    public DiagnosticDescriptor(string code, string title, DiagnosticLevel defaultLevel, string category, string messageFormat)
    {
        Code = code;
        Title = title;
        DefaultLevel = defaultLevel;
        Category = category;
        MessageFormat = messageFormat;
    }

    /// <summary>
    /// Formats the message template with the given arguments.
    /// </summary>
    public string FormatMessage(params object[] args)
    {
        return args.Length == 0 ? MessageFormat : string.Format(MessageFormat, args);
    }

    /// <summary>
    /// Creates a <see cref="SemanticDiagnostic"/> from this descriptor with the default severity.
    /// </summary>
    public SemanticDiagnostic CreateDiagnostic(SourceSpan span, params object[] args)
    {
        return new SemanticDiagnostic(Code, FormatMessage(args), DefaultLevel, span);
    }

    /// <summary>
    /// Creates a <see cref="SemanticDiagnostic"/> marked as unnecessary (faded) from this descriptor.
    /// </summary>
    public SemanticDiagnostic CreateUnnecessaryDiagnostic(SourceSpan span, params object[] args)
    {
        return new SemanticDiagnostic(Code, FormatMessage(args), DefaultLevel, span, isUnnecessary: true);
    }
}
