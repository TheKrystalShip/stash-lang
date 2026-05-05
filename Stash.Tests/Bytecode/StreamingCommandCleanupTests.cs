using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

public class StreamingCommandCleanupTests : BytecodeTestBase
{
    // Mirror of the helper in StreamingCommandTests — inject stdlib globals.
    protected new static object? Execute(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        Chunk chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return Normalize(vm.Execute(chunk));
    }

    // ── Cleanup contract ──────────────────────────────────────────────

    [Fact]
    public void Cleanup_Break_KillsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        // `yes` runs forever; break must kill it via IterClose.
        object? result = Execute(@"
            let s = $<(yes hi);
            let n = 0;
            for (let line in s) {
                n = n + 1;
                if (n >= 3) { break; }
            }
            return n;
        ");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Cleanup_Throw_KillsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            fn run() {
                let s = $<(yes hi);
                let n = 0;
                for (let line in s) {
                    n = n + 1;
                    if (n >= 3) { throw ""stop""; }
                }
                return n;
            }
            try { run(); } catch (e) { }
            return ""done"";
        ");
        Assert.Equal("done", result);
    }

    [Fact]
    public void Cleanup_Return_KillsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            fn run() {
                let s = $<(yes hi);
                let n = 0;
                for (let line in s) {
                    n = n + 1;
                    if (n >= 3) { return n; }
                }
                return -1;
            }
            return run();
        ");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Cleanup_OuterCatch_KillsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        // Streaming process created inside a try block — exception inside the loop
        // should still cleanup the iterator created after the handler's stack level.
        object? result = Execute(@"
            try {
                let s = $<(yes hi);
                let n = 0;
                for (let line in s) {
                    n = n + 1;
                    if (n >= 3) { throw ""stop""; }
                }
            } catch (e) { }
            return ""done"";
        ");
        Assert.Equal("done", result);
    }

    // ── Double consumption ────────────────────────────────────────────

    [Fact]
    public void DoubleConsumption_LinesThenIterate_Throws()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf 'a\nb\n');
            let it = s.lines();
            for (let line in it) { }
            try {
                for (let line in s) { }
                return ""no-throw"";
            } catch (e) {
                return e.type;
            }
        ");
        Assert.Equal("StateError", result);
    }

    [Fact]
    public void DoubleConsumption_DoubleIterate_Throws()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf 'a\nb\n');
            for (let line in s) { }
            try {
                for (let line in s) { }
                return ""no-throw"";
            } catch (e) {
                return e.type;
            }
        ");
        Assert.Equal("StateError", result);
    }

    // ── Dual iteration ────────────────────────────────────────────────

    [Fact]
    public void DualIteration_InterleavesOutAndErr()
    {
        if (OperatingSystem.IsWindows()) return;
        // Distinguish out vs err: count which channel each line came from.
        object? result = Execute(@"
            let s = $<(sh -c 'printf ""a\nb\n""; printf ""x\ny\n"" 1>&2');
            let outCount = 0;
            let errCount = 0;
            for (let outLine, errLine in s) {
                if (outLine != null) { outCount = outCount + 1; }
                if (errLine != null) { errCount = errCount + 1; }
            }
            return [outCount, errCount];
        ");
        Assert.Equal(new List<object?> { 2L, 2L }, result);
    }

    // ── Pipe chains inside the parens ─────────────────────────────────
    // NOTE: The lexer splits `$<(a | b)` into two streaming literals separated
    // by Pipe (matching the existing $<(...) lexer behavior). True in-paren
    // pipe-chain support requires either lexer changes or a structured
    // multi-stage streaming pipeline — both deferred beyond Phase C.

    // ── Kill ──────────────────────────────────────────────────────────

    [Fact]
    public void Kill_Term_PopulatesExitCode()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(yes hi);
            s.kill(Signal.Term);
            s.wait();
            return s.exitCode != null;
        ");
        Assert.Equal(true, result);
    }

    // ── Framing ───────────────────────────────────────────────────────

    [Fact]
    public void JsonFraming_TwoDicts()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf '{""a"":1}\n{""b"":2}\n');
            let count = 0;
            for (let v in s.json()) {
                count = count + 1;
            }
            return count;
        ");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void JsonFraming_MalformedThrowsParseError_AndCleansUp()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf '{""a"":1}\nnotjson\n');
            try {
                for (let v in s.json()) { }
                return ""no-throw"";
            } catch (e) {
                return e.type;
            }
        ");
        Assert.Equal("ParseError", result);
    }

    [Fact]
    public void BytesFraming_FixedSize()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf 'abcdefghij');
            let count = 0;
            for (let chunk in s.bytes(3)) {
                count = count + 1;
            }
            return count;
        ");
        // 'abcdefghij' = 10 bytes / 3 → 4 chunks (3,3,3,1).
        Assert.Equal(4L, result);
    }

    [Fact]
    public void FramedFraming_NullSeparator()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf 'one|two|three|');
            let parts = [];
            for (let p in s.framed(""|"")) {
                arr.push(parts, p);
            }
            return parts;
        ");
        Assert.Equal(new List<object?> { "one", "two", "three" }, result);
    }
}
