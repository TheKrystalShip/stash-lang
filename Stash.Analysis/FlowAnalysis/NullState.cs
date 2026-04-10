namespace Stash.Analysis.FlowAnalysis;

/// <summary>
/// The possible null states of a variable at a program point.
/// </summary>
public enum NullState
{
    /// <summary>Not yet analyzed or not trackable.</summary>
    Unknown,
    /// <summary>Definitely null at this point.</summary>
    Null,
    /// <summary>Definitely not null at this point.</summary>
    NonNull,
    /// <summary>May or may not be null (join of Null and NonNull paths).</summary>
    MaybeNull
}
