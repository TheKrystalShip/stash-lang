namespace Stash.Runtime.Protocols;

using Stash.Common;

/// <summary>
/// Supports arithmetic operators (+, -, *, /, %, **).
/// The VM calls the LEFT operand's protocol first. If it returns false,
/// the VM calls the RIGHT operand's protocol (reverse dispatch).
/// </summary>
public interface IVMArithmetic
{
    /// <summary>
    /// Try to perform the arithmetic operation. Returns false if this type
    /// cannot handle the given operation with the given other operand.
    /// When isLeftOperand is false, this is a reverse dispatch (this type is the right operand).
    /// </summary>
    bool VMTryArithmetic(ArithmeticOp op, StashValue other, bool isLeftOperand,
                         out StashValue result, SourceSpan? span);
}

/// <summary>
/// Arithmetic operations dispatched via the protocol.
/// </summary>
public enum ArithmeticOp : byte
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Power,
    Negate
}
