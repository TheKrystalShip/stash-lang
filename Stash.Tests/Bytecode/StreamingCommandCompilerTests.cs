using Stash.Bytecode;

namespace Stash.Tests.Bytecode;

public class StreamingCommandCompilerTests : BytecodeTestBase
{
    [Fact]
    public void Compile_StreamingCommand_DoesNotThrow()
    {
        // Phase B: streaming commands now compile.
        Chunk chunk = CompileSource("let s = $<(echo hi);");
        Assert.NotNull(chunk);
    }

    [Fact]
    public void Compile_StrictStreamingCommand_DoesNotThrow()
    {
        Chunk chunk = CompileSource("let s = $!<(echo hi);");
        Assert.NotNull(chunk);
    }

    [Fact]
    public void Compile_StreamingCommandInPipeChain_ThrowsCompileError()
    {
        var ex = Assert.Throws<CompileError>(() => CompileSource("let r = $<(cat) | $(grep x);"));
        Assert.Contains("streaming", ex.Message);
        Assert.Contains("pipe chain", ex.Message);
    }
}
