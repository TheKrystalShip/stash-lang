namespace Stash.Tpl;

using System;

/// <summary>
/// Exception thrown when a template cannot be parsed or rendered.
/// Carries line and column information relative to the template source.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TemplateLexer"/>, <see cref="TemplateParser"/>, and
/// <see cref="TemplateRenderer"/> all throw this exception on structural errors
/// (e.g. unterminated blocks, unknown tags, evaluation failures).
/// </para>
/// <para>
/// When <see cref="Line"/> is greater than zero the exception message includes the
/// human-readable location prefix <c>"Template error at line N, column C: …"</c>;
/// otherwise it uses the shorter prefix <c>"Template error: …"</c>.
/// </para>
/// </remarks>
public class TemplateException : Exception
{
    /// <summary>
    /// Gets the 1-based line number in the template source where the error occurred,
    /// or <c>0</c> when no position is available.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the 1-based column number in the template source where the error occurred,
    /// or <c>0</c> when no position is available.
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Initializes a new <see cref="TemplateException"/> with an error message and
    /// an optional source position.
    /// </summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="line">1-based line number, or <c>0</c> if unavailable.</param>
    /// <param name="column">1-based column number, or <c>0</c> if unavailable.</param>
    public TemplateException(string message, int line = 0, int column = 0)
        : base(line > 0 ? $"Template error at line {line}, column {column}: {message}" : $"Template error: {message}")
    {
        Line = line;
        Column = column;
    }

    /// <summary>
    /// Initializes a new <see cref="TemplateException"/> with an error message, an inner
    /// exception, and an optional source position.
    /// </summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="line">1-based line number, or <c>0</c> if unavailable.</param>
    /// <param name="column">1-based column number, or <c>0</c> if unavailable.</param>
    public TemplateException(string message, Exception innerException, int line = 0, int column = 0)
        : base(line > 0 ? $"Template error at line {line}, column {column}: {message}" : $"Template error: {message}", innerException)
    {
        Line = line;
        Column = column;
    }
}
