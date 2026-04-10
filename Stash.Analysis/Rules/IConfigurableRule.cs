namespace Stash.Analysis.Rules;

using System.Collections.Generic;

/// <summary>
/// Marker interface for rules that accept configuration options from <c>.stashcheck</c> files.
/// </summary>
public interface IConfigurableRule
{
    /// <summary>
    /// Applies options from the project config. Called before analysis begins.
    /// </summary>
    void Configure(IReadOnlyDictionary<string, string> options);
}
