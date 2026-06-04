namespace Stash.Hosting;

/// <summary>
/// A single frame in the Stash call stack, captured at the moment of an error.
/// </summary>
/// <param name="File">The source file name, or <c>null</c> for anonymous/inline scripts.</param>
/// <param name="Line">1-based line number of the call site.</param>
/// <param name="Column">1-based column number of the call site.</param>
/// <param name="FunctionName">The function name, or <c>&lt;main&gt;</c> for top-level code.</param>
public sealed record StackFrameInfo(string? File, int Line, int Column, string? FunctionName);
