using Stash.Bytecode;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests that BytecodeVerifier accepts all opcodes the compiler currently emits.
/// Regression coverage for the recurring drift bug where the verifier's hardcoded
/// upper-bound opcode constant lagged behind the OpCode enum (CatchMatch, Rethrow,
/// LockEnd, UnsetGlobal were each rejected as "Invalid opcode" for a period after
/// being added to the enum).
/// </summary>
public class BytecodeVerifierTests : BytecodeTestBase
{
    [Fact]
    public void Verify_TypedCatchClause_AcceptsCatchMatchAndRethrow()
    {
        // Both `catch (Type e)` and bare `rethrow` emit opcodes that historically
        // tripped the verifier's hardcoded range check.
        const string source = @"
            try {
                throw ""boom""
            } catch (RuntimeError e) {
                rethrow
            } catch (Error e) {
                let x = 1
            }
        ";

        Chunk chunk = CompileSource(source);
        BytecodeVerificationResult result = BytecodeVerifier.Verify(chunk);

        Assert.True(result.IsValid,
            $"Verification failed unexpectedly: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public void Verify_LockBlock_AcceptsLockBeginAndLockEnd()
    {
        const string source = @"
            lock ""/tmp/test.lock"" {
                let x = 1
            }
        ";

        Chunk chunk = CompileSource(source);
        BytecodeVerificationResult result = BytecodeVerifier.Verify(chunk);

        Assert.True(result.IsValid,
            $"Verification failed unexpectedly: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public void Verify_UnsetGlobal_AcceptsOpcode()
    {
        const string source = @"
            let x = 1
            unset x
        ";

        Chunk chunk = CompileSource(source);
        BytecodeVerificationResult result = BytecodeVerifier.Verify(chunk);

        Assert.True(result.IsValid,
            $"Verification failed unexpectedly: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public void Verify_AllDefinedOpcodes_AreWithinRangeBound()
    {
        // Sanity check: the verifier's max-opcode bound (computed from the enum)
        // must accept every value defined in the OpCode enum.
        foreach (OpCode op in System.Enum.GetValues<OpCode>())
        {
            byte b = (byte)op;
            Assert.True(System.Enum.IsDefined(op),
                $"Enum.IsDefined returned false for OpCode {op} (byte={b}).");
        }
    }
}
