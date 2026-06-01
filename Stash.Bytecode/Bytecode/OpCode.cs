namespace Stash.Bytecode;

/// <summary>
/// Opcodes for the register-based bytecode VM.
/// Each instruction is a fixed 32-bit word.
/// Formats: ABC [op:8][A:8][B:8][C:8], ABx [op:8][A:8][Bx:16],
///          AsBx [op:8][A:8][sBx:16], Ax [op:8][Ax:24]
/// <para>
/// Every enum member is decorated with an <see cref="OpCodeAttribute"/> that supplies
/// the metadata its consumers (Disassembler, OpCodeInfo, CfgOpcodeInfo, OpcodeOperands,
/// BytecodeVerifier) need. See <see cref="OpCodeMetadata"/> for the central lookup
/// and the startup assertion that guarantees every enum member declares its metadata.
/// </para>
/// </summary>
public enum OpCode : byte
{
    // === Loads & Constants ===
    /// <summary>ABx: R(A) = K(Bx) — load constant from pool.</summary>
    [OpCode(Mnemonic = "load.k", Format = OpCodeFormat.ABx, Operands = "R(A), K(Bx)", Summary = "Load constant from pool", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx)]
    LoadK = 0,

    /// <summary>ABC: R(A) = null.</summary>
    [OpCode(Mnemonic = "load.null", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Load null", Writes = OperandRole.RegA)]
    LoadNull = 1,

    /// <summary>ABC: R(A) = (B != 0); if C != 0, skip next instruction.</summary>
    [OpCode(Mnemonic = "load.bool", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Load bool", Writes = OperandRole.RegA)]
    LoadBool = 2,

    /// <summary>ABC: R(A) = R(B) — copy register.</summary>
    [OpCode(Mnemonic = "move", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Copy register", Writes = OperandRole.RegA, Reads = OperandRole.RegB)]
    Move = 3,

    // === Global Variables ===
    /// <summary>ABx: R(A) = Globals[Bx].</summary>
    [OpCode(Mnemonic = "get.global", Format = OpCodeFormat.ABx, Operands = OperandTemplate.Bespoke, Summary = "Read global", Writes = OperandRole.RegA, Reads = OperandRole.GlobalBx)]
    GetGlobal = 4,

    /// <summary>ABx: Globals[Bx] = R(A).</summary>
    [OpCode(Mnemonic = "set.global", Format = OpCodeFormat.ABx, Operands = OperandTemplate.Bespoke, Summary = "Write global", Writes = OperandRole.GlobalBx, Reads = OperandRole.RegA)]
    SetGlobal = 5,

    /// <summary>ABx: Globals[Bx] = R(A), mark as const.</summary>
    [OpCode(Mnemonic = "init.const.global", Format = OpCodeFormat.ABx, Operands = OperandTemplate.Bespoke, Summary = "Initialize const global", Writes = OperandRole.GlobalBx, Reads = OperandRole.RegA)]
    InitConstGlobal = 6,

    // === Upvalues ===
    /// <summary>ABC: R(A) = Upvalues[B].</summary>
    [OpCode(Mnemonic = "get.upval", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Read upvalue", Writes = OperandRole.RegA, Reads = OperandRole.UpvalB)]
    GetUpval = 7,

    /// <summary>ABC: Upvalues[B] = R(A).</summary>
    [OpCode(Mnemonic = "set.upval", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Write upvalue", Writes = OperandRole.UpvalB, Reads = OperandRole.RegA)]
    SetUpval = 8,

    /// <summary>ABC: Close upvalue for register A.</summary>
    [OpCode(Mnemonic = "close.upval", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Close upvalue", Writes = OperandRole.None, Reads = OperandRole.None)]
    CloseUpval = 9,

    // === Arithmetic ===
    /// <summary>ABC: R(A) = R(B) + R(C).</summary>
    [OpCode(Mnemonic = "add", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Add", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Add = 10,

    /// <summary>ABC: R(A) = R(B) - R(C).</summary>
    [OpCode(Mnemonic = "sub", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Subtract", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Sub = 11,

    /// <summary>ABC: R(A) = R(B) * R(C).</summary>
    [OpCode(Mnemonic = "mul", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Multiply", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Mul = 12,

    /// <summary>ABC: R(A) = R(B) / R(C).</summary>
    [OpCode(Mnemonic = "div", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Divide", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Div = 13,

    /// <summary>ABC: R(A) = R(B) % R(C).</summary>
    [OpCode(Mnemonic = "mod", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Modulo", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Mod = 14,

    /// <summary>ABC: R(A) = R(B) ** R(C).</summary>
    [OpCode(Mnemonic = "pow", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Power", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Pow = 15,

    /// <summary>ABC: R(A) = -R(B).</summary>
    [OpCode(Mnemonic = "neg", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Negate", Writes = OperandRole.RegA, Reads = OperandRole.RegB)]
    Neg = 16,

    /// <summary>AsBx: R(A) = R(A) + sBx — add signed immediate.</summary>
    [OpCode(Mnemonic = "addi", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Add signed immediate", Writes = OperandRole.RegA, Reads = OperandRole.RegA)]
    AddI = 17,

    // === Bitwise ===
    /// <summary>ABC: R(A) = R(B) &amp; R(C).</summary>
    [OpCode(Mnemonic = "band", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Bitwise AND", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    BAnd = 18,

    /// <summary>ABC: R(A) = R(B) | R(C).</summary>
    [OpCode(Mnemonic = "bor", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Bitwise OR", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    BOr = 19,

    /// <summary>ABC: R(A) = R(B) ^ R(C).</summary>
    [OpCode(Mnemonic = "bxor", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Bitwise XOR", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    BXor = 20,

    /// <summary>ABC: R(A) = ~R(B).</summary>
    [OpCode(Mnemonic = "bnot", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Bitwise NOT", Writes = OperandRole.RegA, Reads = OperandRole.RegB)]
    BNot = 21,

    /// <summary>ABC: R(A) = R(B) &lt;&lt; R(C).</summary>
    [OpCode(Mnemonic = "shl", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Shift left", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Shl = 22,

    /// <summary>ABC: R(A) = R(B) &gt;&gt; R(C).</summary>
    [OpCode(Mnemonic = "shr", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Shift right", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Shr = 23,

    // === Comparison (produce bool in R(A)) ===
    /// <summary>ABC: R(A) = (R(B) == R(C)).</summary>
    [OpCode(Mnemonic = "eq", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Equal", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Eq = 24,

    /// <summary>ABC: R(A) = (R(B) != R(C)).</summary>
    [OpCode(Mnemonic = "ne", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Not equal", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Ne = 25,

    /// <summary>ABC: R(A) = (R(B) &lt; R(C)).</summary>
    [OpCode(Mnemonic = "lt", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Less than", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Lt = 26,

    /// <summary>ABC: R(A) = (R(B) &lt;= R(C)).</summary>
    [OpCode(Mnemonic = "le", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Less or equal", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Le = 27,

    /// <summary>ABC: R(A) = (R(B) &gt; R(C)).</summary>
    [OpCode(Mnemonic = "gt", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Greater than", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Gt = 28,

    /// <summary>ABC: R(A) = (R(B) &gt;= R(C)).</summary>
    [OpCode(Mnemonic = "ge", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Greater or equal", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    Ge = 29,

    // === Logic ===
    /// <summary>ABC: R(A) = !IsTruthy(R(B)).</summary>
    [OpCode(Mnemonic = "not", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Logical NOT", Writes = OperandRole.RegA, Reads = OperandRole.RegB)]
    Not = 30,

    /// <summary>ABC: if IsTruthy(R(B)) == C then R(A) = R(B) else skip next. For &amp;&amp;/||.</summary>
    [OpCode(Mnemonic = "test.set", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Test and set for short-circuit", Writes = OperandRole.RegA, Reads = OperandRole.RegB, IsBranching = true)]
    TestSet = 31,

    /// <summary>ABC: if IsTruthy(R(A)) != C then skip next instruction.</summary>
    [OpCode(Mnemonic = "test", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Test truthiness and skip", Writes = OperandRole.None, Reads = OperandRole.RegA, IsBranching = true)]
    Test = 32,

    // === Control Flow ===
    /// <summary>AsBx: IP += sBx — unconditional jump.</summary>
    [OpCode(Mnemonic = "jmp", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Unconditional jump", Writes = OperandRole.None, Reads = OperandRole.None, IsBranching = true, IsTerminator = true)]
    Jmp = 33,

    /// <summary>AsBx: if !IsTruthy(R(A)) then IP += sBx.</summary>
    [OpCode(Mnemonic = "jmp.false", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Jump if falsy", Writes = OperandRole.None, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    JmpFalse = 34,

    /// <summary>AsBx: if IsTruthy(R(A)) then IP += sBx.</summary>
    [OpCode(Mnemonic = "jmp.true", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Jump if truthy", Writes = OperandRole.None, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    JmpTrue = 35,

    /// <summary>AsBx: IP += sBx — backward jump with cancellation check.</summary>
    [OpCode(Mnemonic = "loop", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Loop back with cancellation", Writes = OperandRole.None, Reads = OperandRole.None, IsBranching = true, IsTerminator = true)]
    Loop = 36,

    /// <summary>ABC: Call R(A) with C args starting at R(A+1); result in R(A).</summary>
    [OpCode(Mnemonic = "call", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Call function", Writes = OperandRole.RegA, Reads = OperandRole.RegA)]
    Call = 37,

    /// <summary>ABC: Return R(A). B=0 means return null.</summary>
    [OpCode(Mnemonic = "return", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Return", Writes = OperandRole.None, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    Return = 38,

    // === Iteration ===
    /// <summary>AsBx: Numeric for init: R(A) -= R(A+2); IP += sBx.</summary>
    [OpCode(Mnemonic = "for.prep", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "For-loop prepare", Writes = OperandRole.RegA, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    ForPrep = 39,

    /// <summary>AsBx: R(A) += R(A+2); if R(A) &lt;= R(A+1) then { IP += sBx; R(A+3) = R(A) }.</summary>
    [OpCode(Mnemonic = "for.loop", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "For-loop step", Writes = OperandRole.RegA, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    ForLoop = 40,

    /// <summary>ABC: Create iterator from R(A), store state in R(A)..R(A+2). B != 0 marks indexed iteration.</summary>
    [OpCode(Mnemonic = "iter.prep", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Iterator prepare", Writes = OperandRole.RegA, Reads = OperandRole.RegA)]
    IterPrep = 41,

    /// <summary>AsBx: Advance iterator; if exhausted, continue; else set values and IP += sBx.</summary>
    [OpCode(Mnemonic = "iter.loop", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Iterator step", Writes = OperandRole.RegA, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    IterLoop = 42,

    // === Tables & Fields ===
    /// <summary>ABC: R(A) = R(B)[R(C)] — array index or dict key lookup.</summary>
    [OpCode(Mnemonic = "get.table", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Table read", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    GetTable = 43,

    /// <summary>ABC: R(A)[R(B)] = R(C) — array/dict element store.</summary>
    [OpCode(Mnemonic = "set.table", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Table write", Writes = OperandRole.None, Reads = OperandRole.RegA | OperandRole.RegB | OperandRole.RegC)]
    SetTable = 44,

    /// <summary>ABC: R(A) = R(B).K(C) — field access by constant key.</summary>
    [OpCode(Mnemonic = "get.field", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Field read", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    GetField = 45,

    /// <summary>ABC: R(A).K(B) = R(C) — field store by constant key.</summary>
    [OpCode(Mnemonic = "set.field", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Field write", Writes = OperandRole.None, Reads = OperandRole.RegA | OperandRole.RegC)]
    SetField = 46,

    /// <summary>ABC: R(A+1) = R(B); R(A) = R(B)[K(C)] — method lookup + self.</summary>
    [OpCode(Mnemonic = "self", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Self/method lookup", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    Self = 47,

    // === Collections ===
    /// <summary>ABC: R(A) = new array with B elements from R(A+1)..R(A+B).</summary>
    [OpCode(Mnemonic = "new.array", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "New array", Writes = OperandRole.RegA)]
    NewArray = 48,

    /// <summary>ABC: R(A) = new dict with B key-value pairs from R(A+1)..R(A+2*B).</summary>
    [OpCode(Mnemonic = "new.dict", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "New dict", Writes = OperandRole.RegA)]
    NewDict = 49,

    /// <summary>ABC: R(A) = range(R(B), R(C)).</summary>
    [OpCode(Mnemonic = "new.range", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "New range", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    NewRange = 50,

    /// <summary>ABC: Expand R(B) into sequential registers starting at R(A).</summary>
    [OpCode(Mnemonic = "spread", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Spread into registers", Writes = OperandRole.RegA, Reads = OperandRole.RegB)]
    Spread = 51,

    // === Closures & Types ===
    /// <summary>ABx: R(A) = new closure from Prototype[Bx], followed by upvalue descriptors.</summary>
    [OpCode(Mnemonic = "closure", Format = OpCodeFormat.ABx, Operands = "R(A), K(Bx)", Summary = "Create closure", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx, CompanionWords = CompanionWordKind.UpvalueDescriptors)]
    Closure = 52,

    /// <summary>ABC: R(A) = new instance of struct K(B) with C field values from R(A+1).</summary>
    [OpCode(Mnemonic = "new.struct", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Instantiate struct", Writes = OperandRole.RegA)]
    NewStruct = 53,

    /// <summary>ABC: R(A) = typeof(R(B)) as string.</summary>
    [OpCode(Mnemonic = "typeof", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Typeof", Writes = OperandRole.RegA, Reads = OperandRole.RegB)]
    TypeOf = 54,

    /// <summary>ABC: R(A) = (R(B) is type K(C)).</summary>
    [OpCode(Mnemonic = "is", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), KN(C)", Summary = "Type check", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    Is = 55,

    // === Error Handling ===
    /// <summary>AsBx: Push exception handler; catch at IP + sBx; error value → R(A).</summary>
    [OpCode(Mnemonic = "try.begin", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Push exception handler", Writes = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    TryBegin = 56,

    /// <summary>Ax: Pop exception handler (no operands needed, Ax unused).</summary>
    [OpCode(Mnemonic = "try.end", Format = OpCodeFormat.Ax, Operands = OperandTemplate.Empty, Summary = "Pop exception handler", Writes = OperandRole.None, IsTerminator = true)]
    TryEnd = 57,

    /// <summary>ABC: Throw R(A) as error.</summary>
    [OpCode(Mnemonic = "throw", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Throw error", Writes = OperandRole.None, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    Throw = 58,

    /// <summary>ABC: R(A) = try evaluate R(B); null on error.</summary>
    [OpCode(Mnemonic = "try.expr", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Try expression", Writes = OperandRole.RegA, Reads = OperandRole.RegB)]
    TryExpr = 59,

    // === Type Declarations ===
    /// <summary>ABx: R(A) = declare struct with metadata K(Bx), methods from following registers.</summary>
    [OpCode(Mnemonic = "struct.decl", Format = OpCodeFormat.ABx, Operands = "R(A), KN(Bx)", Summary = "Declare struct", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx)]
    StructDecl = 60,

    /// <summary>ABx: R(A) = declare enum with metadata K(Bx).</summary>
    [OpCode(Mnemonic = "enum.decl", Format = OpCodeFormat.ABx, Operands = "R(A), KN(Bx)", Summary = "Declare enum", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx)]
    EnumDecl = 61,

    /// <summary>ABx: R(A) = declare interface with metadata K(Bx).</summary>
    [OpCode(Mnemonic = "iface.decl", Format = OpCodeFormat.ABx, Operands = "R(A), KN(Bx)", Summary = "Declare interface", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx)]
    IfaceDecl = 62,

    /// <summary>ABx: Extend type with metadata K(Bx), methods from registers.</summary>
    [OpCode(Mnemonic = "extend", Format = OpCodeFormat.ABx, Operands = "R(A), KN(Bx)", Summary = "Extend type", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx)]
    Extend = 63,

    // === Shell ===
    /// <summary>ABC: R(A) = execute command with B parts from R(A+1)..R(A+B).</summary>
    [OpCode(Mnemonic = "command", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Execute command", Writes = OperandRole.RegA)]
    Command = 64,

    /// <summary>ABC + B companion words: execute streaming pipe chain. A=dest, B=stageCount, C=partsBase. Each companion word: bits15-8=partCount, bits7-0=flags (bit0=isStrict).</summary>
    [OpCode(Mnemonic = "pipe.chain", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Pipe chain", Writes = OperandRole.RegA, CompanionWords = CompanionWordKind.PipeStages)]
    PipeChain = 65,

    /// <summary>ABC: Redirect R(A) stream (B flags) to file R(C).</summary>
    [OpCode(Mnemonic = "redirect", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Redirect stream", Writes = OperandRole.None, Reads = OperandRole.RegA | OperandRole.RegC)]
    Redirect = 66,

    // === Modules ===
    /// <summary>ABx: R(A) = import module with metadata K(Bx).</summary>
    [OpCode(Mnemonic = "import", Format = OpCodeFormat.ABx, Operands = "R(A), KN(Bx)", Summary = "Import module", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx)]
    Import = 67,

    /// <summary>ABx: R(A) = import module as alias, metadata K(Bx).</summary>
    [OpCode(Mnemonic = "import.as", Format = OpCodeFormat.ABx, Operands = "R(A), KN(Bx)", Summary = "Import module as alias", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx)]
    ImportAs = 68,

    // === Strings ===
    /// <summary>ABC: R(A) = interpolate B parts from R(A+1)..R(A+B).</summary>
    [OpCode(Mnemonic = "interpolate", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "String interpolation", Writes = OperandRole.RegA)]
    Interpolate = 69,

    // === Misc ===
    /// <summary>ABC: R(A) = R(B) in R(C) — containment check.</summary>
    [OpCode(Mnemonic = "in", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), R(C)", Summary = "Containment check", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.RegC)]
    In = 70,

    /// <summary>ABx: Switch on R(A) with jump table K(Bx).</summary>
    [OpCode(Mnemonic = "switch", Format = OpCodeFormat.ABx, Operands = "R(A), KN(Bx)", Summary = "Switch with jump table", Writes = OperandRole.None, Reads = OperandRole.RegA | OperandRole.ConstBx, IsBranching = true)]
    Switch = 71,

    /// <summary>ABx: Destructure R(A) per metadata K(Bx) into registers.</summary>
    [OpCode(Mnemonic = "destructure", Format = OpCodeFormat.ABx, Operands = "R(A), KN(Bx)", Summary = "Destructure", Writes = OperandRole.RegA, Reads = OperandRole.RegA | OperandRole.ConstBx)]
    Destructure = 72,

    /// <summary>ABC: R(A) = begin elevation from R(B).</summary>
    [OpCode(Mnemonic = "elevate.begin", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Begin elevation", Writes = OperandRole.RegA, Reads = OperandRole.RegA | OperandRole.RegB)]
    ElevateBegin = 73,

    /// <summary>Ax: End elevation.</summary>
    [OpCode(Mnemonic = "elevate.end", Format = OpCodeFormat.Ax, Operands = OperandTemplate.Empty, Summary = "End elevation", Writes = OperandRole.None)]
    ElevateEnd = 74,

    /// <summary>ABx: Retry block with metadata K(Bx), body/until/onRetry from registers.</summary>
    [OpCode(Mnemonic = "retry", Format = OpCodeFormat.ABx, Operands = OperandTemplate.Bespoke, Summary = "Retry block", Writes = OperandRole.RegA, Reads = OperandRole.ConstBx)]
    Retry = 75,

    /// <summary>ABx: Timeout block. R(A)=duration, body closure at R(A+1). Returns result in R(A).</summary>
    [OpCode(Mnemonic = "timeout", Format = OpCodeFormat.ABx, Operands = OperandTemplate.Bespoke, Summary = "Timeout block", Writes = OperandRole.RegA, Reads = OperandRole.RegA)]
    Timeout = 76,

    /// <summary>ABC: R(A) = await R(B).</summary>
    [OpCode(Mnemonic = "await", Format = OpCodeFormat.ABC, Operands = "R(A), R(B)", Summary = "Await async value", Writes = OperandRole.RegA, Reads = OperandRole.RegB)]
    Await = 77,

    /// <summary>ABC: Call R(A) with spread arguments.</summary>
    [OpCode(Mnemonic = "call.spread", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Call with spread arguments", Writes = OperandRole.RegA, Reads = OperandRole.RegA)]
    CallSpread = 78,

    /// <summary>ABC: Check that R(A) is numeric, throw if not.</summary>
    [OpCode(Mnemonic = "check.numeric", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Numeric type check", Writes = OperandRole.None, Reads = OperandRole.RegA)]
    CheckNumeric = 79,

    /// <summary>ABC+companion: R(A) = R(B).K(C) with inline cache; companion word = IC slot index.</summary>
    [OpCode(Mnemonic = "get.field.ic", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Field read with inline cache", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC, CompanionWords = CompanionWordKind.OneIC)]
    GetFieldIC = 80,

    /// <summary>ABC+companion: Fused GetField+Call for namespace built-ins; R(A) = R(B).K[ic.ConstantIndex](R(A+1)..R(A+C)); companion = IC slot.</summary>
    [OpCode(Mnemonic = "call.builtin", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Built-in namespace call with inline cache", Writes = OperandRole.RegA, Reads = OperandRole.RegB, CompanionWords = CompanionWordKind.OneIC)]
    CallBuiltIn = 81,

    // === Specialized Iteration (compile-time) ===
    /// <summary>AsBx: Integer-specialized ForPrep. R(A) -= R(A+2); IP += sBx. Skips type checks when counter/step are compile-time integers.</summary>
    [OpCode(Mnemonic = "for.prepII", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Integer for-prep", Writes = OperandRole.RegA, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    ForPrepII = 82,

    /// <summary>AsBx: Integer-specialized ForLoop. Guard-free: R(A) += R(A+2); if in-bounds: IP += sBx; R(A+3) = R(A).</summary>
    [OpCode(Mnemonic = "for.loopII", Format = OpCodeFormat.AsBx, Operands = OperandTemplate.Bespoke, Summary = "Integer for-loop", Writes = OperandRole.RegA, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    ForLoopII = 83,

    // === Constant Fusion ===
    /// <summary>ABC: R(A) = R(B) + K(C) — add constant from pool.</summary>
    [OpCode(Mnemonic = "addk", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), K(C)", Summary = "Add fused-K constant", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    AddK = 84,

    /// <summary>ABC: R(A) = R(B) - K(C) — subtract constant from pool.</summary>
    [OpCode(Mnemonic = "subk", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), K(C)", Summary = "Subtract fused-K constant", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    SubK = 85,

    /// <summary>ABC: R(A) = (R(B) == K(C)) — equality with constant from pool.</summary>
    [OpCode(Mnemonic = "eq.k", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), K(C)", Summary = "Equal fused-K", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    EqK = 86,

    /// <summary>ABC: R(A) = (R(B) != K(C)) — inequality with constant from pool.</summary>
    [OpCode(Mnemonic = "ne.k", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), K(C)", Summary = "Not equal fused-K", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    NeK = 87,

    /// <summary>ABC: R(A) = (R(B) &lt; K(C)) — less-than with constant from pool.</summary>
    [OpCode(Mnemonic = "lt.k", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), K(C)", Summary = "Less than fused-K", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    LtK = 88,

    /// <summary>ABC: R(A) = (R(B) &lt;= K(C)) — less-or-equal with constant from pool.</summary>
    [OpCode(Mnemonic = "le.k", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), K(C)", Summary = "Less or equal fused-K", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    LeK = 89,

    /// <summary>ABC: R(A) = (R(B) &gt; K(C)) — greater-than with constant from pool.</summary>
    [OpCode(Mnemonic = "gt.k", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), K(C)", Summary = "Greater than fused-K", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    GtK = 90,

    /// <summary>ABC: R(A) = (R(B) &gt;= K(C)) — greater-or-equal with constant from pool.</summary>
    [OpCode(Mnemonic = "ge.k", Format = OpCodeFormat.ABC, Operands = "R(A), R(B), K(C)", Summary = "Greater or equal fused-K", Writes = OperandRole.RegA, Reads = OperandRole.RegB | OperandRole.ConstC)]
    GeK = 91,

    // === Defer ===
    /// <summary>A: Push deferred closure R(A) onto the current frame's defer stack (LIFO).</summary>
    [OpCode(Mnemonic = "defer", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Push deferred closure", Writes = OperandRole.None, Reads = OperandRole.RegA)]
    Defer = 93,

    // === Exception Type Matching ===
    /// <summary>ABx: Check if caught error in R(A) matches type names K(Bx); on match, skip the following jump.</summary>
    [OpCode(Mnemonic = "catch.match", Format = OpCodeFormat.ABx, Operands = OperandTemplate.Bespoke, Summary = "Catch type match", Writes = OperandRole.None, Reads = OperandRole.RegA | OperandRole.ConstBx, IsBranching = true)]
    CatchMatch = 94,

    /// <summary>A: Re-throw the original RuntimeError that was caught into R(A)'s handler register.</summary>
    [OpCode(Mnemonic = "rethrow", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Re-throw caught error", Writes = OperandRole.None, Reads = OperandRole.RegA, IsBranching = true, IsTerminator = true)]
    Rethrow = 95,

    // === File-Based Mutual Exclusion (Lock) ===
    /// <summary>ABC: Acquire exclusive file lock. A=errReg (scratch), B=pathReg, C=constIdx for LockMetadata. R(B+1)=waitOption, R(B+2)=staleOption.</summary>
    [OpCode(Mnemonic = "lock.begin", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Acquire file lock", Writes = OperandRole.RegA)]
    LockBegin = 96,

    /// <summary>Ax: Release the top lock from VMContext.ActiveLocks. No operands (A=0).</summary>
    [OpCode(Mnemonic = "lock.end", Format = OpCodeFormat.Ax, Operands = OperandTemplate.Empty, Summary = "Release file lock", Writes = OperandRole.None)]
    LockEnd = 97,

    // === Global Bindings ===
    /// <summary>Ax: Remove the global binding at slot Ax from the globals dictionary.</summary>
    [OpCode(Mnemonic = "unset.global", Format = OpCodeFormat.Ax, Operands = OperandTemplate.Bespoke, Summary = "Remove global binding", Writes = OperandRole.GlobalBx)]
    UnsetGlobal = 98,

    // === Iterator Cleanup ===
    /// <summary>A: Dispose iterator at R(A) if IDisposable; clear R(A) to null. Used at for-in loop exits.</summary>
    [OpCode(Mnemonic = "iter.close", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Dispose iterator", Writes = OperandRole.None)]
    IterClose = 99,

    // === Streaming Pipe Chains ===
    /// <summary>
    /// ABC + B companion words (one per stage): A=destReg, B=stageCount, C=partsBase.
    /// Each companion word: bits 15-8 = partCount, bits 7-0 = flags (bit 0x01 = strict on the last stage).
    /// Spawns all stages with intermediate stages captured-piped, exposes the last stage's stdout
    /// via a multi-stage <see cref="Stash.Runtime.Types.StashStreamingProcess"/> handle.
    /// </summary>
    [OpCode(Mnemonic = "stream.pipe", Format = OpCodeFormat.ABC, Operands = OperandTemplate.Bespoke, Summary = "Streaming pipe chain", Writes = OperandRole.RegA, CompanionWords = CompanionWordKind.PipeStages)]
    StreamingPipeline = 100,

    // === Readonly ===
    /// <summary>ABC: DeepFreeze R(A) in place — no-op for primitives; freezes dicts, arrays, structs transitively.</summary>
    [OpCode(Mnemonic = "freeze", Format = OpCodeFormat.ABC, Operands = "R(A)", Summary = "Deep-freeze value in place", Writes = OperandRole.None, Reads = OperandRole.RegA)]
    Freeze = 101,
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

/// <summary>
/// Backwards-compatible facade over <see cref="OpCodeMetadata"/>.
/// Kept so external consumers of <c>OpCodeInfo.GetFormat</c> compile unchanged;
/// all data now comes from the central <see cref="OpCodeMetadata"/> table.
/// </summary>
public static class OpCodeInfo
{
    /// <summary>Returns the instruction format for a given opcode.</summary>
    public static OpCodeFormat GetFormat(OpCode op) => OpCodeMetadata.GetFormat(op);
}
