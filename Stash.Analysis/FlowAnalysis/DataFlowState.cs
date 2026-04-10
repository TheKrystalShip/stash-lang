namespace Stash.Analysis.FlowAnalysis;

using System.Collections.Generic;

/// <summary>
/// Tracks the abstract state of variables at a program point.
/// Each variable maps to a <see cref="NullState"/> value.
/// </summary>
public sealed class DataFlowState
{
    private readonly Dictionary<string, NullState> _variableStates = new();

    public NullState GetState(string name)
    {
        return _variableStates.TryGetValue(name, out var state) ? state : NullState.Unknown;
    }

    public void SetState(string name, NullState state)
    {
        _variableStates[name] = state;
    }

    /// <summary>
    /// Creates a shallow copy of this state.
    /// </summary>
    public DataFlowState Clone()
    {
        var clone = new DataFlowState();
        foreach (var kv in _variableStates)
            clone._variableStates[kv.Key] = kv.Value;
        return clone;
    }

    /// <summary>
    /// Merges another state into this one. At join points, states are widened:
    /// if one path says NonNull and another says Null, the result is MaybeNull.
    /// Returns true if this state changed.
    /// </summary>
    public bool MergeFrom(DataFlowState other)
    {
        bool changed = false;
        foreach (var kv in other._variableStates)
        {
            if (!_variableStates.TryGetValue(kv.Key, out var existing))
            {
                _variableStates[kv.Key] = kv.Value;
                changed = true;
            }
            else if (existing != kv.Value)
            {
                var merged = MergeNullStates(existing, kv.Value);
                if (merged != existing)
                {
                    _variableStates[kv.Key] = merged;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private static NullState MergeNullStates(NullState a, NullState b)
    {
        if (a == b) return a;
        if (a == NullState.Unknown) return b;
        if (b == NullState.Unknown) return a;
        return NullState.MaybeNull; // Null + NonNull = MaybeNull
    }

    public IReadOnlyDictionary<string, NullState> AllStates => _variableStates;
}
