namespace Stash.Bytecode;

/// <summary>
/// Opcodes for the register-based bytecode VM.
/// Each instruction is a fixed 32-bit word.
/// Formats: ABC [op:8][A:8][B:8][C:8], ABx [op:8][A:8][Bx:16],
///          AsBx [op:8][A:8][sBx:16], Ax [op:8][Ax:24]
/// </summary>
public enum OpCode : byte
{
    // === Loads & Constants ===
    /// <summary>ABx: R(A) = K(Bx) — load constant from pool.</summary>
    LoadK,
    /// <summary>ABC: R(A) = null.</summary>
    LoadNull,
    /// <summary>ABC: R(A) = (B != 0); if C != 0, skip next instruction.</summary>
    LoadBool,
    /// <summary>ABC: R(A) = R(B) — copy register.</summary>
    Move,

    // === Global Variables ===
    /// <summary>ABx: R(A) = Globals[Bx].</summary>
    GetGlobal,
    /// <summary>ABx: Globals[Bx] = R(A).</summary>
    SetGlobal,
    /// <summary>ABx: Globals[Bx] = R(A), mark as const.</summary>
    InitConstGlobal,

    // === Upvalues ===
    /// <summary>ABC: R(A) = Upvalues[B].</summary>
    GetUpval,
    /// <summary>ABC: Upvalues[B] = R(A).</summary>
    SetUpval,
    /// <summary>ABC: Close upvalue for register A.</summary>
    CloseUpval,

    // === Arithmetic ===
    /// <summary>ABC: R(A) = R(B) + R(C).</summary>
    Add,
    /// <summary>ABC: R(A) = R(B) - R(C).</summary>
    Sub,
    /// <summary>ABC: R(A) = R(B) * R(C).</summary>
    Mul,
    /// <summary>ABC: R(A) = R(B) / R(C).</summary>
    Div,
    /// <summary>ABC: R(A) = R(B) % R(C).</summary>
    Mod,
    /// <summary>ABC: R(A) = R(B) ** R(C).</summary>
    Pow,
    /// <summary>ABC: R(A) = -R(B).</summary>
    Neg,
    /// <summary>AsBx: R(A) = R(A) + sBx — add signed immediate.</summary>
    AddI,

    // === Bitwise ===
    /// <summary>ABC: R(A) = R(B) &amp; R(C).</summary>
    BAnd,
    /// <summary>ABC: R(A) = R(B) | R(C).</summary>
    BOr,
    /// <summary>ABC: R(A) = R(B) ^ R(C).</summary>
    BXor,
    /// <summary>ABC: R(A) = ~R(B).</summary>
    BNot,
    /// <summary>ABC: R(A) = R(B) &lt;&lt; R(C).</summary>
    Shl,
    /// <summary>ABC: R(A) = R(B) &gt;&gt; R(C).</summary>
    Shr,

    // === Comparison (produce bool in R(A)) ===
    /// <summary>ABC: R(A) = (R(B) == R(C)).</summary>
    Eq,
    /// <summary>ABC: R(A) = (R(B) != R(C)).</summary>
    Ne,
    /// <summary>ABC: R(A) = (R(B) &lt; R(C)).</summary>
    Lt,
    /// <summary>ABC: R(A) = (R(B) &lt;= R(C)).</summary>
    Le,
    /// <summary>ABC: R(A) = (R(B) &gt; R(C)).</summary>
    Gt,
    /// <summary>ABC: R(A) = (R(B) &gt;= R(C)).</summary>
    Ge,

    // === Logic ===
    /// <summary>ABC: R(A) = !IsTruthy(R(B)).</summary>
    Not,
    /// <summary>ABC: if IsTruthy(R(B)) == C then R(A) = R(B) else skip next. For &amp;&amp;/||.</summary>
    TestSet,
    /// <summary>ABC: if IsTruthy(R(A)) != C then skip next instruction.</summary>
    Test,

    // === Control Flow ===
    /// <summary>AsBx: IP += sBx — unconditional jump.</summary>
    Jmp,
    /// <summary>AsBx: if !IsTruthy(R(A)) then IP += sBx.</summary>
    JmpFalse,
    /// <summary>AsBx: if IsTruthy(R(A)) then IP += sBx.</summary>
    JmpTrue,
    /// <summary>AsBx: IP += sBx — backward jump with cancellation check.</summary>
    Loop,
    /// <summary>ABC: Call R(A) with C args starting at R(A+1); result in R(A).</summary>
    Call,
    /// <summary>ABC: Return R(A). B=0 means return null.</summary>
    Return,

    // === Iteration ===
    /// <summary>AsBx: Numeric for init: R(A) -= R(A+2); IP += sBx.</summary>
    ForPrep,
    /// <summary>AsBx: R(A) += R(A+2); if R(A) &lt;= R(A+1) then { IP += sBx; R(A+3) = R(A) }.</summary>
    ForLoop,
    /// <summary>ABC: Create iterator from R(A), store state in R(A)..R(A+2).</summary>
    IterPrep,
    /// <summary>AsBx: Advance iterator; if exhausted, continue; else set values and IP += sBx.</summary>
    IterLoop,

    // === Tables & Fields ===
    /// <summary>ABC: R(A) = R(B)[R(C)] — array index or dict key lookup.</summary>
    GetTable,
    /// <summary>ABC: R(A)[R(B)] = R(C) — array/dict element store.</summary>
    SetTable,
    /// <summary>ABC: R(A) = R(B).K(C) — field access by constant key.</summary>
    GetField,
    /// <summary>ABC: R(A).K(B) = R(C) — field store by constant key.</summary>
    SetField,
    /// <summary>ABC: R(A+1) = R(B); R(A) = R(B)[K(C)] — method lookup + self.</summary>
    Self,

    // === Collections ===
    /// <summary>ABC: R(A) = new array with B elements from R(A+1)..R(A+B).</summary>
    NewArray,
    /// <summary>ABC: R(A) = new dict with B key-value pairs from R(A+1)..R(A+2*B).</summary>
    NewDict,
    /// <summary>ABC: R(A) = range(R(B), R(C)).</summary>
    NewRange,
    /// <summary>ABC: Expand R(B) into sequential registers starting at R(A).</summary>
    Spread,

    // === Closures & Types ===
    /// <summary>ABx: R(A) = new closure from Prototype[Bx], followed by upvalue descriptors.</summary>
    Closure,
    /// <summary>ABC: R(A) = new instance of struct K(B) with C field values from R(A+1).</summary>
    NewStruct,
    /// <summary>ABC: R(A) = typeof(R(B)) as string.</summary>
    TypeOf,
    /// <summary>ABC: R(A) = (R(B) is type K(C)).</summary>
    Is,

    // === Error Handling ===
    /// <summary>ABx: Push exception handler; catch at IP + Bx; error value → R(A).</summary>
    TryBegin,
    /// <summary>Ax: Pop exception handler (no operands needed, Ax unused).</summary>
    TryEnd,
    /// <summary>ABC: Throw R(A) as error.</summary>
    Throw,
    /// <summary>ABC: R(A) = try evaluate R(B); null on error.</summary>
    TryExpr,

    // === Type Declarations ===
    /// <summary>ABx: R(A) = declare struct with metadata K(Bx), methods from following registers.</summary>
    StructDecl,
    /// <summary>ABx: R(A) = declare enum with metadata K(Bx).</summary>
    EnumDecl,
    /// <summary>ABx: R(A) = declare interface with metadata K(Bx).</summary>
    IfaceDecl,
    /// <summary>ABx: Extend type with metadata K(Bx), methods from registers.</summary>
    Extend,

    // === Shell ===
    /// <summary>ABC: R(A) = execute command with B parts from R(A+1)..R(A+B).</summary>
    Command,
    /// <summary>ABC: R(A) = pipe(R(B), R(C)).</summary>
    Pipe,
    /// <summary>ABC: Redirect R(A) stream (B flags) to file R(C).</summary>
    Redirect,

    // === Modules ===
    /// <summary>ABx: R(A) = import module with metadata K(Bx).</summary>
    Import,
    /// <summary>ABx: R(A) = import module as alias, metadata K(Bx).</summary>
    ImportAs,

    // === Strings ===
    /// <summary>ABC: R(A) = interpolate B parts from R(A+1)..R(A+B).</summary>
    Interpolate,

    // === Misc ===
    /// <summary>ABC: R(A) = R(B) in R(C) — containment check.</summary>
    In,
    /// <summary>ABx: Switch on R(A) with jump table K(Bx).</summary>
    Switch,
    /// <summary>ABx: Destructure R(A) per metadata K(Bx) into registers.</summary>
    Destructure,
    /// <summary>ABC: R(A) = begin elevation from R(B).</summary>
    ElevateBegin,
    /// <summary>Ax: End elevation.</summary>
    ElevateEnd,
    /// <summary>ABx: Retry block with metadata K(Bx), body/until/onRetry from registers.</summary>
    Retry,
    /// <summary>ABx: Timeout block. R(A)=duration, body closure at R(A+1). Returns result in R(A).</summary>
    Timeout,
    /// <summary>ABC: R(A) = await R(B).</summary>
    Await,
    /// <summary>ABC: Call R(A) with spread arguments.</summary>
    CallSpread,
    /// <summary>ABC: Check that R(A) is numeric, throw if not.</summary>
    CheckNumeric,
    /// <summary>ABC+companion: R(A) = R(B).K(C) with inline cache; companion word = IC slot index.</summary>
    GetFieldIC,                 // 79
    /// <summary>ABC+companion: Fused GetField+Call for namespace built-ins; R(A) = R(B).K[ic.ConstantIndex](R(A+1)..R(A+C)); companion = IC slot.</summary>
    CallBuiltIn,                // 80

    // === Specialized Iteration (compile-time) ===
    /// <summary>AsBx: Integer-specialized ForPrep. R(A) -= R(A+2); IP += sBx. Skips type checks when counter/step are compile-time integers.</summary>
    ForPrepII,                  // 81
    /// <summary>AsBx: Integer-specialized ForLoop. Guard-free: R(A) += R(A+2); if in-bounds: IP += sBx; R(A+3) = R(A).</summary>
    ForLoopII,                  // 82

    // === Constant Fusion ===
    /// <summary>ABC: R(A) = R(B) + K(C) — add constant from pool.</summary>
    AddK,                       // 83
    /// <summary>ABC: R(A) = R(B) - K(C) — subtract constant from pool.</summary>
    SubK,                       // 84
    /// <summary>ABC: R(A) = (R(B) == K(C)) — equality with constant from pool.</summary>
    EqK,                        // 85
    /// <summary>ABC: R(A) = (R(B) != K(C)) — inequality with constant from pool.</summary>
    NeK,                        // 86
    /// <summary>ABC: R(A) = (R(B) &lt; K(C)) — less-than with constant from pool.</summary>
    LtK,                        // 87
    /// <summary>ABC: R(A) = (R(B) &lt;= K(C)) — less-or-equal with constant from pool.</summary>
    LeK,                        // 88
    /// <summary>ABC: R(A) = (R(B) &gt; K(C)) — greater-than with constant from pool.</summary>
    GtK,                        // 89
    /// <summary>ABC: R(A) = (R(B) &gt;= K(C)) — greater-or-equal with constant from pool.</summary>
    GeK,                        // 90

    // === Typed Arrays ===
    /// <summary>ABx: R(A) = TypedArray(elementType=K(Bx), elements=R(A)).</summary>
    TypedWrap,                  // 91
}

/// <summary>Instruction format types for the 32-bit encoding.</summary>
public enum OpCodeFormat
{
    /// <summary>[op:8][A:8][B:8][C:8] — three registers.</summary>
    ABC,
    /// <summary>[op:8][A:8][Bx:16] — register + unsigned 16-bit.</summary>
    ABx,
    /// <summary>[op:8][A:8][sBx:16] — register + signed 16-bit (bias-encoded).</summary>
    AsBx,
    /// <summary>[op:8][Ax:24] — 24-bit payload.</summary>
    Ax,
}

public static class OpCodeInfo
{
    /// <summary>Returns the instruction format for a given opcode.</summary>
    public static OpCodeFormat GetFormat(OpCode op) => op switch
    {
        // ABx format: register + 16-bit constant/slot index
        OpCode.LoadK or OpCode.GetGlobal or OpCode.SetGlobal or OpCode.InitConstGlobal
        or OpCode.AddI    // AsBx but same extraction format
        or OpCode.Jmp or OpCode.JmpFalse or OpCode.JmpTrue or OpCode.Loop
        or OpCode.ForPrep or OpCode.ForLoop or OpCode.ForPrepII or OpCode.ForLoopII or OpCode.IterLoop
        or OpCode.Closure or OpCode.TryBegin
        or OpCode.StructDecl or OpCode.EnumDecl or OpCode.IfaceDecl or OpCode.Extend
        or OpCode.Import or OpCode.ImportAs
        or OpCode.Switch or OpCode.Destructure or OpCode.Retry or OpCode.Timeout
        or OpCode.TypedWrap
            => OpCodeFormat.ABx,

        // Ax format: 24-bit payload
        OpCode.TryEnd or OpCode.ElevateEnd
            => OpCodeFormat.Ax,

        // Everything else is ABC
        _ => OpCodeFormat.ABC,
    };
}
