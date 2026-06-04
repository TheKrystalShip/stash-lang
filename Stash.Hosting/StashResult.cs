namespace Stash.Hosting;

using System.Collections.Generic;
using Stash.Runtime;

/// <summary>
/// The result of executing a Stash script through <see cref="IStashHost.RunAsync(CompiledScript, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// P1 placeholder: <see cref="Errors"/> carries string messages.
/// The full structured <c>StashError</c> shape with <c>Kind</c>, <c>Span</c>, and
/// <c>CallStack</c> lands in P2.
/// </remarks>
public sealed record StashResult(
    bool Success,
    StashValue? Value,
    IReadOnlyList<string> Errors);

/// <summary>
/// The typed result of executing a Stash script through <see cref="IStashHost.RunAsync{T}"/>.
/// </summary>
/// <typeparam name="T">The expected CLR type of the script's return value.</typeparam>
/// <remarks>
/// P1 placeholder: <see cref="Errors"/> carries string messages.
/// The full structured <c>StashError</c> shape with <c>Kind</c>, <c>Span</c>, and
/// <c>CallStack</c> lands in P2.
/// </remarks>
public sealed record StashResult<T>(
    bool Success,
    T? Value,
    IReadOnlyList<string> Errors);
