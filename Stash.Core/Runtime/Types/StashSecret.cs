namespace Stash.Runtime.Types;

using System;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a secret value that auto-redacts when stringified.
/// Wraps an inner <see cref="StashValue"/> and returns "******" from <see cref="ToString"/>.
/// Use <see cref="Reveal"/> to access the underlying value.
/// </summary>
public sealed class StashSecret : IEquatable<StashSecret>,
    IVMTyped, IVMTruthiness, IVMStringifiable, IVMArithmetic
{
    public const string RedactedText = "******";

    public StashValue InnerValue { get; }

    public StashSecret(StashValue value)
    {
        // If wrapping another secret, unwrap to avoid nesting
        if (value.IsObj && value.AsObj is StashSecret inner)
            InnerValue = inner.InnerValue;
        else
            InnerValue = value;
    }

    /// <summary>
    /// Returns the real underlying value (the "escape hatch").
    /// </summary>
    public StashValue Reveal() => InnerValue;

    public override string ToString() => RedactedText;

    public bool Equals(StashSecret? other) => other is not null && InnerValue.Equals(other.InnerValue);
    public override bool Equals(object? obj) => obj is StashSecret other && Equals(other);
    public override int GetHashCode() => InnerValue.GetHashCode();

    // --- VM Protocol Implementations ---

    public string VMTypeName => "secret";
    public bool VMIsFalsy => false;
    public string VMToString() => RedactedText;

    public bool VMTryArithmetic(ArithmeticOp op, StashValue other, bool isLeftOperand,
                                out StashValue result, SourceSpan? span)
    {
        result = StashValue.Null;
        if (op == ArithmeticOp.Add)
        {
            // Taint propagation: any add involving a secret produces a secret
            StashValue realSelf = InnerValue;
            StashValue realOther = other.IsObj && other.AsObj is StashSecret otherSec ? otherSec.InnerValue : other;
            string selfStr = RuntimeValues.Stringify(realSelf.ToObject());
            string otherStr = RuntimeValues.Stringify(realOther.ToObject());
            string concat = isLeftOperand ? string.Concat(selfStr, otherStr) : string.Concat(otherStr, selfStr);
            result = StashValue.FromObj(new StashSecret(StashValue.FromObj(concat)));
            return true;
        }
        return false;
    }
}
