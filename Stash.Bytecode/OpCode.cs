namespace Stash.Bytecode;

/// <summary>
/// Single-byte instruction opcodes for the Stash bytecode virtual machine.
/// Each opcode may be followed by zero, one, or two operand bytes as described
/// by <see cref="OpCodeInfo.OperandSize"/>.
/// </summary>
public enum OpCode : byte
{
    // -------------------------------------------------------------------------
    // Constants & Literals
    // -------------------------------------------------------------------------

    /// <summary>Load constant from pool (u16 operand).</summary>
    Const,          // 0
    /// <summary>Push null.</summary>
    Null,           // 1
    /// <summary>Push true.</summary>
    True,           // 2
    /// <summary>Push false.</summary>
    False,          // 3

    // -------------------------------------------------------------------------
    // Stack Manipulation
    // -------------------------------------------------------------------------

    /// <summary>Discard top of stack.</summary>
    Pop,            // 4
    /// <summary>Duplicate top of stack.</summary>
    Dup,            // 5

    // -------------------------------------------------------------------------
    // Variable Access
    // -------------------------------------------------------------------------

    /// <summary>Push local variable by slot index (u8 operand).</summary>
    LoadLocal,      // 6
    /// <summary>Pop and store to local slot (u8 operand).</summary>
    StoreLocal,     // 7
    /// <summary>Push global by name (u16 constant pool index).</summary>
    LoadGlobal,     // 8
    /// <summary>Pop and store to global by name (u16 constant pool index).</summary>
    StoreGlobal,    // 9
    /// <summary>Push captured upvalue (u8 operand).</summary>
    LoadUpvalue,    // 10
    /// <summary>Pop and store to upvalue (u8 operand).</summary>
    StoreUpvalue,   // 11

    // -------------------------------------------------------------------------
    // Arithmetic
    // -------------------------------------------------------------------------

    /// <summary>Pop 2, push sum (long/double/string/duration/bytesize).</summary>
    Add,            // 12
    /// <summary>Pop 2, push difference.</summary>
    Subtract,       // 13
    /// <summary>Pop 2, push product.</summary>
    Multiply,       // 14
    /// <summary>Pop 2, push quotient.</summary>
    Divide,         // 15
    /// <summary>Pop 2, push remainder.</summary>
    Modulo,         // 16
    /// <summary>Pop 2, push exponentiation.</summary>
    Power,          // 17
    /// <summary>Pop 1, push negation.</summary>
    Negate,         // 18

    // -------------------------------------------------------------------------
    // Bitwise
    // -------------------------------------------------------------------------

    /// <summary>Pop 2, push bitwise AND.</summary>
    BitAnd,         // 19
    /// <summary>Pop 2, push bitwise OR.</summary>
    BitOr,          // 20
    /// <summary>Pop 2, push bitwise XOR.</summary>
    BitXor,         // 21
    /// <summary>Pop 1, push bitwise NOT.</summary>
    BitNot,         // 22
    /// <summary>Pop 2, push left shift.</summary>
    ShiftLeft,      // 23
    /// <summary>Pop 2, push right shift.</summary>
    ShiftRight,     // 24

    // -------------------------------------------------------------------------
    // Comparison
    // -------------------------------------------------------------------------

    /// <summary>Pop 2, push equality result.</summary>
    Equal,          // 25
    /// <summary>Pop 2, push inequality result.</summary>
    NotEqual,       // 26
    /// <summary>Pop 2, push less-than result.</summary>
    LessThan,       // 27
    /// <summary>Pop 2, push less-or-equal result.</summary>
    LessEqual,      // 28
    /// <summary>Pop 2, push greater-than result.</summary>
    GreaterThan,    // 29
    /// <summary>Pop 2, push greater-or-equal result.</summary>
    GreaterEqual,   // 30

    // -------------------------------------------------------------------------
    // Logic
    // -------------------------------------------------------------------------

    /// <summary>Pop 1, push logical not.</summary>
    Not,            // 31
    /// <summary>Short-circuit AND: if falsy, jump ahead (i16 operand).</summary>
    And,            // 32
    /// <summary>Short-circuit OR: if truthy, jump ahead (i16 operand).</summary>
    Or,             // 33
    /// <summary>If not null, jump ahead (i16 operand).</summary>
    NullCoalesce,   // 34

    // -------------------------------------------------------------------------
    // Control Flow
    // -------------------------------------------------------------------------

    /// <summary>Unconditional jump (i16 signed offset).</summary>
    Jump,           // 35
    /// <summary>Jump if top is truthy, pop condition (i16 operand).</summary>
    JumpTrue,       // 36
    /// <summary>Jump if top is falsy, pop condition (i16 operand).</summary>
    JumpFalse,      // 37
    /// <summary>Backward jump (u16 operand — cancellation check point).</summary>
    Loop,           // 38

    // -------------------------------------------------------------------------
    // Functions
    // -------------------------------------------------------------------------

    /// <summary>Call function with N args on stack (u8 operand).</summary>
    Call,           // 39
    /// <summary>Return top of stack from current frame.</summary>
    Return,         // 40
    /// <summary>Create closure (u16 constant pool index + upvalue descriptors).</summary>
    Closure,        // 41

    // -------------------------------------------------------------------------
    // Collections
    // -------------------------------------------------------------------------

    /// <summary>Build array from N stack values (u16 operand).</summary>
    Array,          // 42
    /// <summary>Build dictionary from N key/value pairs (u16 operand).</summary>
    Dict,           // 43
    /// <summary>Pop 2-3 values, push range (start..end[..step]).</summary>
    Range,          // 44
    /// <summary>Spread iterable onto stack.</summary>
    Spread,         // 45

    // -------------------------------------------------------------------------
    // Object Access
    // -------------------------------------------------------------------------

    /// <summary>Pop object, push field value (u16 name from constant pool).</summary>
    GetField,       // 46
    /// <summary>Pop value + object, set field (u16 name from constant pool).</summary>
    SetField,       // 47
    /// <summary>Pop index + object, push element.</summary>
    GetIndex,       // 48
    /// <summary>Pop value + index + object, set element.</summary>
    SetIndex,       // 49

    // -------------------------------------------------------------------------
    // Type Operations
    // -------------------------------------------------------------------------

    /// <summary>Declare struct type (u16 operand).</summary>
    StructDecl,     // 50
    /// <summary>Instantiate struct with N fields (u16 operand).</summary>
    StructInit,     // 51
    /// <summary>Declare enum type (u16 operand).</summary>
    EnumDecl,       // 52
    /// <summary>Declare interface (u16 operand).</summary>
    InterfaceDecl,  // 53
    /// <summary>Register extension methods (u16 operand).</summary>
    Extend,         // 54
    /// <summary>Type check (u16 type_name constant pool index).</summary>
    Is,             // 55

    // -------------------------------------------------------------------------
    // Strings
    // -------------------------------------------------------------------------

    /// <summary>Build interpolated string from N parts (u16 operand).</summary>
    Interpolate,    // 56

    // -------------------------------------------------------------------------
    // Special
    // -------------------------------------------------------------------------

    /// <summary>Execute shell command (u16 operand).</summary>
    Command,        // 57
    /// <summary>Pipe two command results.</summary>
    Pipe,           // 58
    /// <summary>Redirect command output (u8 operand).</summary>
    Redirect,       // 59
    /// <summary>Import module (u16 operand).</summary>
    Import,         // 60
    /// <summary>Import module with alias (u16 operand).</summary>
    ImportAs,       // 61
    /// <summary>Destructure array/dict into locals (u8 operand).</summary>
    Destructure,    // 62

    // -------------------------------------------------------------------------
    // Error Handling
    // -------------------------------------------------------------------------

    /// <summary>Pop value, throw as error.</summary>
    Throw,          // 63
    /// <summary>Push exception handler (u16 jump offset to catch).</summary>
    TryBegin,       // 64
    /// <summary>Pop exception handler.</summary>
    TryEnd,         // 65
    /// <summary>Pop expr, push result or null on error.</summary>
    TryExpr,        // 66

    // -------------------------------------------------------------------------
    // Async
    // -------------------------------------------------------------------------

    /// <summary>Pop future, push resolved value.</summary>
    Await,          // 67

    // -------------------------------------------------------------------------
    // Misc
    // -------------------------------------------------------------------------

    /// <summary>Pre-increment variable.</summary>
    PreIncrement,   // 68
    /// <summary>Pre-decrement variable.</summary>
    PreDecrement,   // 69
    /// <summary>Post-increment variable.</summary>
    PostIncrement,  // 70
    /// <summary>Post-decrement variable.</summary>
    PostDecrement,  // 71
    /// <summary>Switch expression dispatch (u16 operand).</summary>
    Switch,         // 72
    /// <summary>Enter elevated context.</summary>
    ElevateBegin,   // 73
    /// <summary>Exit elevated context.</summary>
    ElevateEnd,     // 74
    /// <summary>Retry block (u16 operand).</summary>
    Retry,          // 75
    /// <summary>Create iterator from iterable.</summary>
    Iterator,       // 76
    /// <summary>Advance iterator, push value or jump if done (i16 operand).</summary>
    Iterate,        // 77
}

/// <summary>
/// Helper utilities for <see cref="OpCode"/> metadata.
/// </summary>
public static class OpCodeInfo
{
    /// <summary>
    /// Returns the number of operand bytes that follow the given opcode in the instruction stream.
    /// </summary>
    /// <param name="opCode">The opcode to query.</param>
    /// <returns>0, 1, or 2.</returns>
    public static int OperandSize(OpCode opCode) => opCode switch
    {
        // 0-byte operands
        OpCode.Null          => 0,
        OpCode.True          => 0,
        OpCode.False         => 0,
        OpCode.Pop           => 0,
        OpCode.Dup           => 0,
        OpCode.Add           => 0,
        OpCode.Subtract      => 0,
        OpCode.Multiply      => 0,
        OpCode.Divide        => 0,
        OpCode.Modulo        => 0,
        OpCode.Power         => 0,
        OpCode.Negate        => 0,
        OpCode.BitAnd        => 0,
        OpCode.BitOr         => 0,
        OpCode.BitXor        => 0,
        OpCode.BitNot        => 0,
        OpCode.ShiftLeft     => 0,
        OpCode.ShiftRight    => 0,
        OpCode.Equal         => 0,
        OpCode.NotEqual      => 0,
        OpCode.LessThan      => 0,
        OpCode.LessEqual     => 0,
        OpCode.GreaterThan   => 0,
        OpCode.GreaterEqual  => 0,
        OpCode.Not           => 0,
        OpCode.Return        => 0,
        OpCode.Range         => 0,
        OpCode.Spread        => 0,
        OpCode.GetIndex      => 0,
        OpCode.SetIndex      => 0,
        OpCode.Pipe          => 0,
        OpCode.Throw         => 0,
        OpCode.TryEnd        => 0,
        OpCode.TryExpr       => 0,
        OpCode.Await         => 0,
        OpCode.PreIncrement  => 0,
        OpCode.PreDecrement  => 0,
        OpCode.PostIncrement => 0,
        OpCode.PostDecrement => 0,
        OpCode.ElevateBegin  => 0,
        OpCode.ElevateEnd    => 0,
        OpCode.Iterator      => 0,

        // 1-byte (u8) operands
        OpCode.LoadLocal     => 1,
        OpCode.StoreLocal    => 1,
        OpCode.LoadUpvalue   => 1,
        OpCode.StoreUpvalue  => 1,
        OpCode.Call          => 1,
        OpCode.Redirect      => 1,
        OpCode.Destructure   => 1,

        // 2-byte (u16/i16) operands
        OpCode.Const         => 2,
        OpCode.LoadGlobal    => 2,
        OpCode.StoreGlobal   => 2,
        OpCode.And           => 2,
        OpCode.Or            => 2,
        OpCode.NullCoalesce  => 2,
        OpCode.Jump          => 2,
        OpCode.JumpTrue      => 2,
        OpCode.JumpFalse     => 2,
        OpCode.Loop          => 2,
        OpCode.Closure       => 2,
        OpCode.Array         => 2,
        OpCode.Dict          => 2,
        OpCode.GetField      => 2,
        OpCode.SetField      => 2,
        OpCode.StructDecl    => 2,
        OpCode.StructInit    => 2,
        OpCode.EnumDecl      => 2,
        OpCode.InterfaceDecl => 2,
        OpCode.Extend        => 2,
        OpCode.Is            => 2,
        OpCode.Interpolate   => 2,
        OpCode.Command       => 2,
        OpCode.Import        => 2,
        OpCode.ImportAs      => 2,
        OpCode.TryBegin      => 2,
        OpCode.Switch        => 2,
        OpCode.Retry         => 2,
        OpCode.Iterate       => 2,

        _ => throw new System.ArgumentOutOfRangeException(nameof(opCode), opCode, "Unknown opcode."),
    };
}
