namespace Stash.Common;

public record DiagnosticError(
    SourceSpan Span,
    string Message
);
