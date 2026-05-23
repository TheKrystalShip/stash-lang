namespace Stash.Lsp.Completion;

using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Owns the per-mode provider pipeline and orchestrates a single completion request
/// through the applicable providers into a <see cref="CompletionItemSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="CompletionMode"/> maps to an ordered list of
/// <see cref="ICompletionProvider"/> instances. Providers are invoked in list order;
/// within a mode, earlier providers have higher priority (their labels win the
/// first-wins dedup in the sink). Providers that return <see langword="false"/> from
/// <see cref="ICompletionProvider.AppliesTo"/> are skipped cheaply.
/// </para>
/// <para>
/// The dispatcher is stateless with respect to requests — it holds only the pipeline
/// table and is safe under OmniSharp's request-parallel dispatch. The
/// <see cref="CompletionItemSink"/> is created fresh for each call to <see cref="Run"/>.
/// </para>
/// <para>
/// During phases 1–4, provider pipelines are populated incrementally as providers are
/// ported. An empty pipeline for a mode is valid and results in an empty
/// <see cref="CompletionList"/> for that mode.
/// </para>
/// </remarks>
public sealed class CompletionDispatcher
{
    private readonly IReadOnlyDictionary<CompletionMode, IReadOnlyList<ICompletionProvider>> _pipelines;

    /// <summary>
    /// Initialises the dispatcher with the given per-mode provider pipelines.
    /// </summary>
    /// <param name="pipelines">
    /// A mapping from each <see cref="CompletionMode"/> to an ordered list of providers.
    /// Modes not present in the dictionary yield an empty <see cref="CompletionList"/>.
    /// </param>
    public CompletionDispatcher(IReadOnlyDictionary<CompletionMode, IReadOnlyList<ICompletionProvider>> pipelines)
    {
        _pipelines = pipelines;
    }

    /// <summary>
    /// Runs the provider pipeline for <paramref name="ctx"/>.<see cref="CompletionContext.Mode"/>
    /// and returns the deduplicated, ordered <see cref="CompletionList"/>.
    /// </summary>
    /// <param name="ctx">The completion context for the current request.</param>
    /// <returns>A <see cref="CompletionList"/> with all accepted candidates in priority order.</returns>
    public CompletionList Run(CompletionContext ctx)
    {
        var sink = new CompletionItemSink(ctx.Mode);

        if (!_pipelines.TryGetValue(ctx.Mode, out var pipeline))
        {
            return sink.Materialize();
        }

        foreach (var provider in pipeline)
        {
            if (!provider.AppliesTo(ctx)) continue;
            foreach (var candidate in provider.Provide(ctx))
            {
                sink.Add(candidate);
            }
        }

        return sink.Materialize();
    }
}
