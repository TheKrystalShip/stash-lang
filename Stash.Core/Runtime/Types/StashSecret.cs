namespace Stash.Runtime.Types;

using System;
using Stash.Runtime;

/// <summary>
/// Represents a secret value that auto-redacts when stringified.
/// Wraps an inner <see cref="StashValue"/> and returns "******" from <see cref="ToString"/>.
/// Use <see cref="Reveal"/> to access the underlying value.
/// </summary>
public sealed class StashSecret : IEquatable<StashSecret>
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
}
