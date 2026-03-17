namespace Stash.Interpreting.Templating;

using System;

/// <summary>
/// Exception thrown when a template cannot be parsed or rendered.
/// Carries line and column information relative to the template source.
/// </summary>
public class TemplateException : Exception
{
    public int Line { get; }
    public int Column { get; }

    public TemplateException(string message, int line = 0, int column = 0)
        : base(line > 0 ? $"Template error at line {line}, column {column}: {message}" : $"Template error: {message}")
    {
        Line = line;
        Column = column;
    }

    public TemplateException(string message, Exception innerException, int line = 0, int column = 0)
        : base(line > 0 ? $"Template error at line {line}, column {column}: {message}" : $"Template error: {message}", innerException)
    {
        Line = line;
        Column = column;
    }
}
