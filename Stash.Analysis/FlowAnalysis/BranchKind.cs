namespace Stash.Analysis.FlowAnalysis;

/// <summary>
/// Describes how control leaves a <see cref="BasicBlock"/>.
/// </summary>
public enum BranchKind
{
    /// <summary>Falls through to the next block or unconditionally jumps to one successor.</summary>
    Unconditional,

    /// <summary>Branches to one of two successors based on a condition expression.</summary>
    Conditional,

    /// <summary>The block ends with a <c>return</c> statement.</summary>
    Return,

    /// <summary>The block ends with a <c>throw</c> statement or a <c>process.exit()</c> call.</summary>
    Throw,

    /// <summary>The block ends with a <c>break</c> statement.</summary>
    Break,

    /// <summary>The block ends with a <c>continue</c> statement.</summary>
    Continue,
}
