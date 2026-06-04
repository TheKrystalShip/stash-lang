namespace Stash.Hosting;

using System.Collections.Generic;
using Stash.Runtime;

/// <summary>
/// The result of executing a Stash script through <see cref="IStashHost.RunAsync(CompiledScript, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed record StashResult(
    bool Success,
    StashValue? Value,
    IReadOnlyList<StashError> Errors);

/// <summary>
/// The typed result of executing a Stash script or calling a Stash function.
/// </summary>
/// <typeparam name="T">The expected CLR type of the return value.</typeparam>
public sealed record StashResult<T>(
    bool Success,
    T? Value,
    IReadOnlyList<StashError> Errors);
