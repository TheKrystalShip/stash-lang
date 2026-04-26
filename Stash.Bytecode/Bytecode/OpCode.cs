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
    LoadK = 0,
    /// <summary>ABC: R(A) = null.</summary>
    LoadNull = 1,
    /// <summary>ABC: R(A) = (B != 0); if C != 0, skip next instruction.</summary>
    LoadBool = 2,
    /// <summary>ABC: R(A) = R(B) — copy register.</summary>
    Move = 3,

    // === Global Variables ===
    /// <summary>ABx: R(A) = Globals[Bx].</summary>
    GetGlobal = 4,
    /// <summary>ABx: Globals[Bx] = R(A).</summary>
    SetGlobal = 5,
    /// <summary>ABx: Globals[Bx] = R(A), mark as const.</summary>
    InitConstGlobal = 6,

    // === Upvalues ===
    /// <summary>ABC: R(A) = Upvalues[B].</summary>
    GetUpval = 7,
    /// <summary>ABC: Upvalues[B] = R(A).</summary>
    SetUpval = 8,
    /// <summary>ABC: Close upvalue for register A.</summary>
    CloseUpval = 9,

    // === Arithmetic ===
    /// <summary>ABC: R(A) = R(B) + R(C).</summary>
    Add = 10,
    /// <summary>ABC: R(A) = R(B) - R(C).</summary>
    Sub = 11,
    /// <summary>ABC: R(A) = R(B) * R(C).</summary>
    Mul = 12,
    /// <summary>ABC: R(A) = R(B) / R(C).</summary>
    Div = 13,
    /// <summary>ABC: R(A) = R(B) % R(C).</summary>
    Mod = 14,
    /// <summary>ABC: R(A) = R(B) ** R(C).</summary>
    Pow = 15,
    /// <summary>ABC: R(A) = -R(B).</summary>
    Neg = 16,
    /// <summary>AsBx: R(A) = R(A) + sBx — add signed immediate.</summary>
    AddI = 17,

    // === Bitwise ===
    /// <summary>ABC: R(A) = R(B) &amp; R(C).</summary>
    BAnd = 18,
    /// <summary>ABC: R(A) = R(B) | R(C).</summary>
    BOr = 19,
    /// <summary>ABC: R(A) = R(B) ^ R(C).</summary>
    BXor = 20,
    /// <summary>ABC: R(A) = ~R(B).</summary>
    BNot = 21,
    /// <summary>ABC: R(A) = R(B) &lt;&lt; R(C).</summary>
    Shl = 22,
    /// <summary>ABC: R(A) = R(B) &gt;&gt; R(C).</summary>
    Shr = 23,

    // === Comparison (produce bool in R(A)) ===
    /// <summary>ABC: R(A) = (R(B) == R(C)).</summary>
    Eq = 24,
    /// <summary>ABC: R(A) = (R(B) != R(C)).</summary>
    Ne = 25,
    /// <summary>ABC: R(A) = (R(B) &lt; R(C)).</summary>
    Lt = 26,
    /// <summary>ABC: R(A) = (R(B) &lt;= R(C)).</summary>
    Le = 27,
    /// <summary>ABC: R(A) = (R(B) &gt; R(C)).</summary>
    Gt = 28,
    /// <summary>ABC: R(A) = (R(B) &gt;= R(C)).</summary>
    Ge = 29,

    // === Logic ===
    /// <summary>ABC: R(A) = !IsTruthy(R(B)).</summary>
    Not = 30,
    /// <summary>ABC: if IsTruthy(R(B)) == C then R(A) = R(B) else skip next. For &amp;&amp;/||.</summary>
    TestSet = 31,
    /// <summary>ABC: if IsTruthy(R(A)) != C then skip next instruction.</summary>
    Test = 32,

    // === Control Flow ===
    /// <summary>AsBx: IP += sBx — unconditional jump.</summary>
    Jmp = 33,
    /// <summary>AsBx: if !IsTruthy(R(A)) then IP += sBx.</summary>
    JmpFalse = 34,
    /// <summary>AsBx: if IsTruthy(R(A)) then IP += sBx.</summary>
    JmpTrue = 35,
    /// <summary>AsBx: IP += sBx — backward jump with cancellation check.</summary>
    Loop = 36,
    /// <summary>ABC: Call R(A) with C args starting at R(A+1); result in R(A).</summary>
    Call = 37,
    /// <summary>ABC: Return R(A). B=0 means return null.</summary>
    Return = 38,

    // === Iteration ===
    /// <summary>AsBx: Numeric for init: R(A) -= R(A+2); IP += sBx.</summary>
    ForPrep = 39,
    /// <summary>AsBx: R(A) += R(A+2); if R(A) &lt;= R(A+1) then { IP += sBx; R(A+3) = R(A) }.</summary>
    ForLoop = 40,
    /// <summary>ABC: Create iterator from R(A), store state in R(A)..R(A+2).</summary>
    IterPrep = 41,
    /// <summary>AsBx: Advance iterator; if exhausted, continue; else set values and IP += sBx.</summary>
    IterLoop = 42,

    // === Tables & Fields ===
    /// <summary>ABC: R(A) = R(B)[R(C)] — array index or dict key lookup.</summary>
    GetTable = 43,
    /// <summary>ABC: R(A)[R(B)] = R(C) — array/dict element store.</summary>
    SetTable = 44,
    /// <summary>ABC: R(A) = R(B).K(C) — field access by constant key.</summary>
    GetField = 45,
    /// <summary>ABC: R(A).K(B) = R(C) — field store by constant key.</summary>
    SetField = 46,
    /// <summary>ABC: R(A+1) = R(B); R(A) = R(B)[K(C)] — method lookup + self.</summary>
    Self = 47,

    // === Collections ===
    /// <summary>ABC: R(A) = new array with B elements from R(A+1)..R(A+B).</summary>
    NewArray = 48,
    /// <summary>ABC: R(A) = new dict with B key-value pairs from R(A+1)..R(A+2*B).</summary>
    NewDict = 49,
    /// <summary>ABC: R(A) = range(R(B), R(C)).</summary>
    NewRange = 50,
    /// <summary>ABC: Expand R(B) into sequential registers starting at R(A).</summary>
    Spread = 51,

    // === Closures & Types ===
    /// <summary>ABx: R(A) = new closure from Prototype[Bx], followed by upvalue descriptors.</summary>
    Closure = 52,
    /// <summary>ABC: R(A) = new instance of struct K(B) with C field values from R(A+1).</summary>
    NewStruct = 53,
    /// <summary>ABC: R(A) = typeof(R(B)) as string.</summary>
    TypeOf = 54,
    /// <summary>ABC: R(A) = (R(B) is type K(C)).</summary>
    Is = 55,

    // === Error Handling ===
    /// <summary>ABx: Push exception handler; catch at IP + Bx; error value → R(A).</summary>
    TryBegin = 56,
    /// <summary>Ax: Pop exception handler (no operands needed, Ax unused).</summary>
    TryEnd = 57,
    /// <summary>ABC: Throw R(A) as error.</summary>
    Throw = 58,
    /// <summary>ABC: R(A) = try evaluate R(B); null on error.</summary>
    TryExpr = 59,

    // === Type Declarations ===
    /// <summary>ABx: R(A) = declare struct with metadata K(Bx), methods from following registers.</summary>
    StructDecl = 60,
    /// <summary>ABx: R(A) = declare enum with metadata K(Bx).</summary>
    EnumDecl = 61,
    /// <summary>ABx: R(A) = declare interface with metadata K(Bx).</summary>
    IfaceDecl = 62,
    /// <summary>ABx: Extend type with metadata K(Bx), methods from registers.</summary>
    Extend = 63,

    // === Shell ===
    /// <summary>ABC: R(A) = execute command with B parts from R(A+1)..R(A+B).</summary>
    Command = 64,
    /// <summary>ABC + B companion words: execute streaming pipe chain. A=dest, B=stageCount, C=partsBase. Each companion word: bits15-8=partCount, bits7-0=flags (bit0=isStrict).</summary>
    PipeChain = 65,
    /// <summary>ABC: Redirect R(A) stream (B flags) to file R(C).</summary>
    Redirect = 66,

    // === Modules ===
    /// <summary>ABx: R(A) = import module with metadata K(Bx).</summary>
    Import = 67,
    /// <summary>ABx: R(A) = import module as alias, metadata K(Bx).</summary>
    ImportAs = 68,

    // === Strings ===
    /// <summary>ABC: R(A) = interpolate B parts from R(A+1)..R(A+B).</summary>
    Interpolate = 69,

    // === Misc ===
    /// <summary>ABC: R(A) = R(B) in R(C) — containment check.</summary>
    In = 70,
    /// <summary>ABx: Switch on R(A) with jump table K(Bx).</summary>
    Switch = 71,
    /// <summary>ABx: Destructure R(A) per metadata K(Bx) into registers.</summary>
    Destructure = 72,
    /// <summary>ABC: R(A) = begin elevation from R(B).</summary>
    ElevateBegin = 73,
    /// <summary>Ax: End elevation.</summary>
    ElevateEnd = 74,
    /// <summary>ABx: Retry block with metadata K(Bx), body/until/onRetry from registers.</summary>
    Retry = 75,
    /// <summary>ABx: Timeout block. R(A)=duration, body closure at R(A+1). Returns result in R(A).</summary>
    Timeout = 76,
    /// <summary>ABC: R(A) = await R(B).</summary>
    Await = 77,
    /// <summary>ABC: Call R(A) with spread arguments.</summary>
    CallSpread = 78,
    /// <summary>ABC: Check that R(A) is numeric, throw if not.</summary>
    CheckNumeric = 79,
    /// <summary>ABC+companion: R(A) = R(B).K(C) with inline cache; companion word = IC slot index.</summary>
    GetFieldIC = 80,
    /// <summary>ABC+companion: Fused GetField+Call for namespace built-ins; R(A) = R(B).K[ic.ConstantIndex](R(A+1)..R(A+C)); companion = IC slot.</summary>
    CallBuiltIn = 81,

    // === Specialized Iteration (compile-time) ===
    /// <summary>AsBx: Integer-specialized ForPrep. R(A) -= R(A+2); IP += sBx. Skips type checks when counter/step are compile-time integers.</summary>
    ForPrepII = 82,
    /// <summary>AsBx: Integer-specialized ForLoop. Guard-free: R(A) += R(A+2); if in-bounds: IP += sBx; R(A+3) = R(A).</summary>
    ForLoopII = 83,

    // === Constant Fusion ===
    /// <summary>ABC: R(A) = R(B) + K(C) — add constant from pool.</summary>
    AddK = 84,
    /// <summary>ABC: R(A) = R(B) - K(C) — subtract constant from pool.</summary>
    SubK = 85,
    /// <summary>ABC: R(A) = (R(B) == K(C)) — equality with constant from pool.</summary>
    EqK = 86,
    /// <summary>ABC: R(A) = (R(B) != K(C)) — inequality with constant from pool.</summary>
    NeK = 87,
    /// <summary>ABC: R(A) = (R(B) &lt; K(C)) — less-than with constant from pool.</summary>
    LtK = 88,
    /// <summary>ABC: R(A) = (R(B) &lt;= K(C)) — less-or-equal with constant from pool.</summary>
    LeK = 89,
    /// <summary>ABC: R(A) = (R(B) &gt; K(C)) — greater-than with constant from pool.</summary>
    GtK = 90,
    /// <summary>ABC: R(A) = (R(B) &gt;= K(C)) — greater-or-equal with constant from pool.</summary>
    GeK = 91,

    // === Typed Arrays ===
    /// <summary>ABx: R(A) = TypedArray(elementType=K(Bx), elements=R(A)).</summary>
    TypedWrap = 92,

    // === Defer ===
    /// <summary>A: Push deferred closure R(A) onto the current frame's defer stack (LIFO).</summary>
    Defer = 93,

    // === Exception Type Matching ===
    /// <summary>ABC: Check if caught error in R(A) matches type names K(B); if no match, jump by signed C offset to next clause.</summary>
    CatchMatch = 94,
    /// <summary>A: Re-throw the original RuntimeError that was caught into R(A)'s handler register.</summary>
    Rethrow = 95,
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
