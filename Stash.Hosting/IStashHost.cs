namespace Stash.Hosting;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;

/// <summary>
/// Async facade for embedding the Stash scripting language in a .NET application.
/// </summary>
/// <remarks>
/// One <see cref="IStashHost"/> instance owns one engine. Constructing two hosts
/// yields two universes that share no observable state. Sequential calls on the
/// same host accumulate global state; the only reset mechanism is dispose-and-create-new
/// (the deliberate v1 lua_State contract).
/// </remarks>
public interface IStashHost : IAsyncDisposable
{
    /// <summary>
    /// Compiles Stash source code into a reusable <see cref="CompiledScript"/>.
    /// The source is lexed, parsed, and semantically resolved once; the compiled
    /// script may be passed to <see cref="RunAsync(CompiledScript, CancellationToken)"/>
    /// multiple times.
    /// </summary>
    /// <param name="source">Stash source code.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A compiled script.</returns>
    /// <exception cref="InvalidOperationException">Thrown when compilation fails (parse/lex errors).</exception>
    Task<CompiledScript> CompileAsync(string source, CancellationToken ct = default);

    /// <summary>
    /// Executes a previously compiled script on this host's engine.
    /// Returns a <see cref="StashResult"/> indicating success or failure.
    /// </summary>
    /// <param name="script">A compiled script produced by <see cref="CompileAsync"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<StashResult> RunAsync(CompiledScript script, CancellationToken ct = default);

    /// <summary>
    /// Executes a previously compiled script and attempts to convert the returned
    /// <see cref="StashValue"/> to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Expected CLR type of the script's return value.</typeparam>
    /// <param name="script">A compiled script produced by <see cref="CompileAsync"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<StashResult<T>> RunAsync<T>(CompiledScript script, CancellationToken ct = default);

    /// <summary>
    /// Calls a named Stash function previously defined by a <see cref="RunAsync"/> call.
    /// Marshals <paramref name="args"/> to Stash values, invokes the function, and
    /// marshals the return value to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Expected CLR type of the function's return value.</typeparam>
    /// <param name="fn">The name of the function to call.</param>
    /// <param name="args">
    /// The argument to pass. An <c>object?[]</c> is treated as multiple positional arguments;
    /// any other single value is wrapped as a single argument; <c>null</c> means no arguments.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The function's return value converted to <typeparamref name="T"/>.</returns>
    /// <exception cref="StashScriptException">
    /// Thrown when the Stash function raises a script-level error.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Propagated when <paramref name="ct"/> is cancelled.
    /// </exception>
    Task<T> CallAsync<T>(string fn, object? args = null, CancellationToken ct = default);

    /// <summary>
    /// Calls a named Stash function previously defined by a <see cref="RunAsync"/> call.
    /// Unlike <see cref="CallAsync{T}"/>, this overload never throws on script-level failure;
    /// errors (including cancellation) are returned as <see cref="StashResult{T}.Errors"/>.
    /// </summary>
    /// <typeparam name="T">Expected CLR type of the function's return value.</typeparam>
    /// <param name="fn">The name of the function to call.</param>
    /// <param name="args">Arguments — same shape as <see cref="CallAsync{T}"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="StashResult{T}"/> with <see cref="StashResult{T}.Success"/> set to
    /// <c>false</c> and <see cref="StashResult{T}.Errors"/> populated on any failure.
    /// Cancellation produces a single error with <see cref="StashError.Kind"/> ==
    /// <see cref="StashError.KindCancelled"/>.
    /// </returns>
    Task<StashResult<T>> TryCallAsync<T>(string fn, object? args = null, CancellationToken ct = default);
}
