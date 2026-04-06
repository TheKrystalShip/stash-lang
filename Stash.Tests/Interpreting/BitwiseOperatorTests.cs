using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class BitwiseOperatorTests
{
    private static object? Eval(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var expr = parser.Parse();
        var chunk = Compiler.CompileExpression(expr);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return vm.Execute(chunk);
    }

    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return vm.Execute(chunk);
    }

    private static List<Token> Scan(string source) => new Lexer(source).ScanTokens();

    private static Expr ParseExpr(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    // ── Lexer: Token scanning ────────────────────────────────────────

    [Fact]
    public void Scan_Ampersand_ProducesAmpersandToken()
    {
        var tokens = Scan("&");
        Assert.Equal(TokenType.Ampersand, tokens[0].Type);
    }

    [Fact]
    public void Scan_Caret_ProducesCaretToken()
    {
        var tokens = Scan("^");
        Assert.Equal(TokenType.Caret, tokens[0].Type);
    }

    [Fact]
    public void Scan_Tilde_ProducesTildeToken()
    {
        var tokens = Scan("~");
        Assert.Equal(TokenType.Tilde, tokens[0].Type);
    }

    [Fact]
    public void Scan_LessLess_ProducesLessLessToken()
    {
        var tokens = Scan("<<");
        Assert.Equal(TokenType.LessLess, tokens[0].Type);
    }

    [Fact]
    public void Scan_AmpersandEqual_ProducesAmpersandEqualToken()
    {
        var tokens = Scan("&=");
        Assert.Equal(TokenType.AmpersandEqual, tokens[0].Type);
    }

    [Fact]
    public void Scan_PipeEqual_ProducesPipeEqualToken()
    {
        var tokens = Scan("|=");
        Assert.Equal(TokenType.PipeEqual, tokens[0].Type);
    }

    [Fact]
    public void Scan_CaretEqual_ProducesCaretEqualToken()
    {
        var tokens = Scan("^=");
        Assert.Equal(TokenType.CaretEqual, tokens[0].Type);
    }

    [Fact]
    public void Scan_LessLessEqual_ProducesLessLessEqualToken()
    {
        var tokens = Scan("<<=");
        Assert.Equal(TokenType.LessLessEqual, tokens[0].Type);
    }

    [Fact]
    public void Scan_GreaterGreaterEqual_ProducesGreaterGreaterEqualToken()
    {
        var tokens = Scan(">>=");
        Assert.Equal(TokenType.GreaterGreaterEqual, tokens[0].Type);
    }

    [Fact]
    public void Scan_AmpersandAmpersand_StillWorks()
    {
        var tokens = Scan("&&");
        Assert.Equal(TokenType.AmpersandAmpersand, tokens[0].Type);
    }

    [Fact]
    public void Scan_PipePipe_StillWorks()
    {
        var tokens = Scan("||");
        Assert.Equal(TokenType.PipePipe, tokens[0].Type);
    }

    [Fact]
    public void Scan_LessEqual_StillWorks()
    {
        var tokens = Scan("<=");
        Assert.Equal(TokenType.LessEqual, tokens[0].Type);
    }

    [Fact]
    public void Scan_GreaterGreater_StillWorks()
    {
        var tokens = Scan(">>");
        Assert.Equal(TokenType.GreaterGreater, tokens[0].Type);
    }

    // ── Parser: AST structure ────────────────────────────────────────

    [Fact]
    public void Parse_BitwiseAnd_ReturnsBinaryExprWithAmpersand()
    {
        var result = ParseExpr("5 & 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Ampersand, binary.Operator.Type);
    }

    [Fact]
    public void Parse_BitwiseOr_ReturnsBinaryExprWithPipe()
    {
        var result = ParseExpr("5 | 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Pipe, binary.Operator.Type);
    }

    [Fact]
    public void Parse_BitwiseXor_ReturnsBinaryExprWithCaret()
    {
        var result = ParseExpr("5 ^ 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Caret, binary.Operator.Type);
    }

    [Fact]
    public void Parse_BitwiseNot_ReturnsUnaryExprWithTilde()
    {
        var result = ParseExpr("~5");
        var unary = Assert.IsType<UnaryExpr>(result);
        Assert.Equal(TokenType.Tilde, unary.Operator.Type);
    }

    [Fact]
    public void Parse_LeftShift_ReturnsBinaryExprWithLessLess()
    {
        var result = ParseExpr("1 << 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.LessLess, binary.Operator.Type);
    }

    [Fact]
    public void Parse_RightShift_ReturnsBinaryExprWithGreaterGreater()
    {
        var result = ParseExpr("8 >> 2");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.GreaterGreater, binary.Operator.Type);
    }

    // ── Interpreter: Bitwise AND ─────────────────────────────────────

    [Fact]
    public void BitwiseAnd_BasicOperation()
    {
        Assert.Equal(1L, Eval("5 & 3"));
    }

    [Fact]
    public void BitwiseAnd_ZeroMask()
    {
        Assert.Equal(0L, Eval("0xFF & 0"));
    }

    [Fact]
    public void BitwiseAnd_IdentityMask()
    {
        Assert.Equal(0xFFL, Eval("0xFF & 0xFF"));
    }

    [Fact]
    public void BitwiseAnd_HexLiterals()
    {
        Assert.Equal(0x0FL, Eval("0xFF & 0x0F"));
    }

    [Fact]
    public void BitwiseAnd_NegativeNumbers()
    {
        // -1 in two's complement is all ones
        Assert.Equal(42L, Eval("-1 & 42"));
    }

    // ── Interpreter: Bitwise OR ──────────────────────────────────────

    [Fact]
    public void BitwiseOr_BasicOperation()
    {
        Assert.Equal(7L, Eval("5 | 3"));
    }

    [Fact]
    public void BitwiseOr_ZeroIdentity()
    {
        Assert.Equal(42L, Eval("42 | 0"));
    }

    [Fact]
    public void BitwiseOr_CombineFlags()
    {
        // 1 | 2 | 4 = 7 (binary: 001 | 010 | 100 = 111)
        Assert.Equal(7L, Eval("1 | 2 | 4"));
    }

    [Fact]
    public void BitwiseOr_HexLiterals()
    {
        Assert.Equal(0xFFL, Eval("0xF0 | 0x0F"));
    }

    // ── Interpreter: Bitwise XOR ─────────────────────────────────────

    [Fact]
    public void BitwiseXor_BasicOperation()
    {
        Assert.Equal(6L, Eval("5 ^ 3"));
    }

    [Fact]
    public void BitwiseXor_SelfCancels()
    {
        Assert.Equal(0L, Eval("42 ^ 42"));
    }

    [Fact]
    public void BitwiseXor_ZeroIdentity()
    {
        Assert.Equal(42L, Eval("42 ^ 0"));
    }

    [Fact]
    public void BitwiseXor_Toggle()
    {
        // XOR with all ones flips all bits (for lower 8 bits)
        Assert.Equal(0xF0L, Eval("0x0F ^ 0xFF"));
    }

    // ── Interpreter: Bitwise NOT ─────────────────────────────────────

    [Fact]
    public void BitwiseNot_Zero()
    {
        Assert.Equal(-1L, Eval("~0"));
    }

    [Fact]
    public void BitwiseNot_AllOnes()
    {
        Assert.Equal(0L, Eval("~(-1)"));
    }

    [Fact]
    public void BitwiseNot_PositiveNumber()
    {
        // ~5 = -6 in two's complement (long)
        Assert.Equal(-6L, Eval("~5"));
    }

    [Fact]
    public void BitwiseNot_DoubleNot_Identity()
    {
        Assert.Equal(42L, Eval("~~42"));
    }

    // ── Interpreter: Left Shift ──────────────────────────────────────

    [Fact]
    public void LeftShift_BasicOperation()
    {
        Assert.Equal(8L, Eval("1 << 3"));
    }

    [Fact]
    public void LeftShift_ZeroShift()
    {
        Assert.Equal(42L, Eval("42 << 0"));
    }

    [Fact]
    public void LeftShift_MultiplyByPowerOfTwo()
    {
        // 5 << 4 = 5 * 16 = 80
        Assert.Equal(80L, Eval("5 << 4"));
    }

    [Fact]
    public void LeftShift_LargeShift()
    {
        Assert.Equal(1024L, Eval("1 << 10"));
    }

    // ── Interpreter: Right Shift ─────────────────────────────────────

    [Fact]
    public void RightShift_BasicOperation()
    {
        Assert.Equal(2L, Eval("8 >> 2"));
    }

    [Fact]
    public void RightShift_ZeroShift()
    {
        Assert.Equal(42L, Eval("42 >> 0"));
    }

    [Fact]
    public void RightShift_DivideByPowerOfTwo()
    {
        // 80 >> 4 = 80 / 16 = 5
        Assert.Equal(5L, Eval("80 >> 4"));
    }

    [Fact]
    public void RightShift_NegativePreservesSign()
    {
        // Arithmetic right shift preserves sign bit
        Assert.True((long)Eval("-8 >> 1")! < 0);
        Assert.Equal(-4L, Eval("-8 >> 1"));
    }

    // ── Interpreter: Precedence ──────────────────────────────────────

    [Fact]
    public void Precedence_BitwiseAndBeforeOr()
    {
        // 6 | 5 & 3 should be 6 | (5 & 3) = 6 | 1 = 7
        // NOT (6 | 5) & 3 = 7 & 3 = 3
        Assert.Equal(7L, Eval("6 | 5 & 3"));
    }

    [Fact]
    public void Precedence_BitwiseXorBetweenAndAndOr()
    {
        // 7 | 5 ^ 3 & 1 should be 7 | (5 ^ (3 & 1)) = 7 | (5 ^ 1) = 7 | 4 = 7
        Assert.Equal(7L, Eval("7 | 5 ^ 3 & 1"));
    }

    [Fact]
    public void Precedence_ShiftBeforeComparison()
    {
        // 1 << 3 > 4 should be (1 << 3) > 4 = 8 > 4 = true
        Assert.Equal(true, Eval("1 << 3 > 4"));
    }

    [Fact]
    public void Precedence_ShiftBeforeBitwiseAnd()
    {
        // 0xFF & 1 << 4 should be 0xFF & (1 << 4) = 0xFF & 16 = 16
        Assert.Equal(16L, Eval("0xFF & 1 << 4"));
    }

    [Fact]
    public void Precedence_ArithmeticBeforeShift()
    {
        // 2 + 1 << 3 should be (2 + 1) << 3 = 3 << 3 = 24
        Assert.Equal(24L, Eval("2 + 1 << 3"));
    }

    [Fact]
    public void Eval_Precedence_BitwiseOrAfterLogicalAnd()
    {
        // && binds tighter than | in Stash:
        // 0 && 5 | 2  →  (0 && 5) | 2  →  0 | 2  →  2
        // If | bound tighter: 0 && (5 | 2) = 0 && 7 = 0
        Assert.Equal(2L, Eval("0 && 5 | 2"));
    }

    [Fact]
    public void Precedence_BitwiseWithParens()
    {
        // Parentheses override precedence
        Assert.Equal(3L, Eval("(6 | 5) & 3"));
    }

    [Fact]
    public void Precedence_NotWithBitwiseAnd()
    {
        // ~0 & 0xFF should be (~0) & 0xFF = -1 & 255 = 255
        Assert.Equal(255L, Eval("~0 & 0xFF"));
    }

    // ── Interpreter: Combined with hex/octal/binary literals ─────────

    [Fact]
    public void BitwiseAnd_OctalLiterals()
    {
        // 0o755 & 0o444 = permissions check (readable bits)
        Assert.Equal(Convert.ToInt64("444", 8), Eval("0o755 & 0o444"));
    }

    [Fact]
    public void BitwiseOr_BinaryLiterals()
    {
        // 0b1010 | 0b0101 = 0b1111 = 15
        Assert.Equal(15L, Eval("0b1010 | 0b0101"));
    }

    [Fact]
    public void LeftShift_BinaryLiteral()
    {
        // 0b1 << 7 = 128
        Assert.Equal(128L, Eval("0b1 << 7"));
    }

    [Fact]
    public void BitwiseOperations_MaskAndExtract()
    {
        // Extract bits 4-7 from 0xABCD: (0xABCD >> 4) & 0xF = 0xC = 12
        Assert.Equal(12L, Eval("(0xABCD >> 4) & 0xF"));
    }

    // ── Interpreter: Type errors ─────────────────────────────────────

    [Fact]
    public void BitwiseAnd_NonInteger_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Eval("5 & 3.0"));
    }

    [Fact]
    public void BitwiseOr_NonInteger_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Eval("5 | 3.0"));
    }

    [Fact]
    public void BitwiseXor_NonInteger_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Eval("5 ^ 3.0"));
    }

    [Fact]
    public void BitwiseNot_NonInteger_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Eval("~3.14"));
    }

    [Fact]
    public void LeftShift_NonInteger_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Eval("1.5 << 3"));
    }

    [Fact]
    public void RightShift_NonInteger_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Eval("8 >> 2.0"));
    }

    [Fact]
    public void BitwiseAnd_String_ThrowsError()
    {
        var source = "\"hello\" & 5";
        Assert.Throws<RuntimeError>(() => Eval(source));
    }

    [Fact]
    public void BitwiseOr_String_ThrowsError()
    {
        var source = "\"hello\" | 5";
        Assert.Throws<RuntimeError>(() => Eval(source));
    }

    [Fact]
    public void Eval_LeftShift_NegativeCount_ThrowsRuntimeError()
    {
        var ex = Assert.Throws<RuntimeError>(() => Eval("1 << -1"));
        Assert.Contains("0..63", ex.Message);
    }

    [Fact]
    public void Eval_LeftShift_CountTooLarge_ThrowsRuntimeError()
    {
        var ex = Assert.Throws<RuntimeError>(() => Eval("1 << 64"));
        Assert.Contains("0..63", ex.Message);
    }

    [Fact]
    public void Eval_RightShift_NegativeCount_ThrowsRuntimeError()
    {
        var ex = Assert.Throws<RuntimeError>(() => Eval("1 >> -1"));
        Assert.Contains("0..63", ex.Message);
    }

    [Fact]
    public void Eval_RightShift_CountTooLarge_ThrowsRuntimeError()
    {
        var ex = Assert.Throws<RuntimeError>(() => Eval("1 >> 64"));
        Assert.Contains("0..63", ex.Message);
    }

    [Fact]
    public void Eval_LeftShift_BoundaryCount_Zero_Succeeds()
    {
        Assert.Equal(5L, Eval("5 << 0"));
    }

    [Fact]
    public void Eval_LeftShift_BoundaryCount_SixtyThree_Succeeds()
    {
        Assert.Equal(long.MinValue, Eval("1 << 63"));
    }

    // ── Interpreter: Compound assignment ─────────────────────────────

    [Fact]
    public void CompoundAssignment_BitwiseAndEqual()
    {
        Assert.Equal(1L, Run("let result = 5; result &= 3;"));
    }

    [Fact]
    public void CompoundAssignment_BitwiseOrEqual()
    {
        Assert.Equal(7L, Run("let result = 5; result |= 2;"));
    }

    [Fact]
    public void CompoundAssignment_BitwiseXorEqual()
    {
        Assert.Equal(6L, Run("let result = 5; result ^= 3;"));
    }

    [Fact]
    public void CompoundAssignment_LeftShiftEqual()
    {
        Assert.Equal(20L, Run("let result = 5; result <<= 2;"));
    }

    [Fact]
    public void CompoundAssignment_RightShiftEqual()
    {
        Assert.Equal(5L, Run("let result = 20; result >>= 2;"));
    }

    // ── Interpreter: Practical use cases ─────────────────────────────

    [Fact]
    public void Practical_PermissionFlags()
    {
        // Simulate Unix-style permissions
        var result = Run(@"
            let READ = 4;
            let WRITE = 2;
            let EXEC = 1;
            let perms = READ | WRITE | EXEC;
            let result = perms & READ;
        ");
        Assert.Equal(4L, result);
    }

    [Fact]
    public void Practical_CheckFlagSet()
    {
        var result = Run(@"
            let FLAGS = 0b1010;
            let result = (FLAGS & 0b1000) != 0;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Practical_CheckFlagNotSet()
    {
        var result = Run(@"
            let FLAGS = 0b1010;
            let result = (FLAGS & 0b0100) != 0;
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Practical_ClearBit()
    {
        // Clear bit 1: flags & ~(1 << 1)
        var result = Run(@"
            let flags = 0b1111;
            let result = flags & ~(1 << 1);
        ");
        Assert.Equal(0b1101L, result);
    }

    [Fact]
    public void Practical_SetBit()
    {
        // Set bit 2: flags | (1 << 2)
        var result = Run(@"
            let flags = 0b1001;
            let result = flags | (1 << 2);
        ");
        Assert.Equal(0b1101L, result);
    }

    [Fact]
    public void Practical_ToggleBit()
    {
        // Toggle bit 0: flags ^ (1 << 0)
        var result = Run(@"
            let flags = 0b1010;
            let result = flags ^ (1 << 0);
        ");
        Assert.Equal(0b1011L, result);
    }

    [Fact]
    public void Practical_ExtractByteFromInt()
    {
        // Extract second byte from 0xAABBCCDD
        var result = Run(@"
            let value = 0xAABBCCDD;
            let result = (value >> 8) & 0xFF;
        ");
        Assert.Equal(0xCCL, result);
    }

    [Fact]
    public void Practical_IpAddressPacking()
    {
        // Pack 192.168.1.1 into a single integer
        var result = Run(@"
            let ip = (192 << 24) | (168 << 16) | (1 << 8) | 1;
            let result = ip;
        ");
        Assert.Equal((192L << 24) | (168L << 16) | (1L << 8) | 1L, result);
    }

    [Fact]
    public void Practical_SubnetMask()
    {
        // Create a /24 subnet mask: ~((1 << 8) - 1) & 0xFFFFFFFF
        var result = Run(@"
            let mask = ~((1 << 8) - 1) & 0xFFFFFFFF;
            let result = mask;
        ");
        Assert.Equal(0xFFFFFF00L, result);
    }

    // ── Chaining and complex expressions ─────────────────────────────

    [Fact]
    public void Chaining_MultipleBitwiseOr()
    {
        Assert.Equal(15L, Eval("1 | 2 | 4 | 8"));
    }

    [Fact]
    public void Chaining_MultipleBitwiseAnd()
    {
        Assert.Equal(0L, Eval("0xFF & 0x0F & 0xF0"));
    }

    [Fact]
    public void Chaining_MultipleXor()
    {
        // XOR is associative: a ^ b ^ c
        Assert.Equal(6L, Eval("5 ^ 3 ^ 0"));
    }

    [Fact]
    public void Complex_ShiftAndMask()
    {
        // (value >> 4) & 0xF — extract high nibble of a byte
        Assert.Equal(0xAL, Eval("(0xAB >> 4) & 0xF"));
    }
}
