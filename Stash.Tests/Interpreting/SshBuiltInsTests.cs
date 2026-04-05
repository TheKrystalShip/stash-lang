using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;

namespace Stash.Tests.Interpreting;

public class SshBuiltInsTests
{
    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    private static RuntimeError RunCapturingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // ssh.connect
    [Fact]
    public void Connect_NonDictThrows()
    {
        RunExpectingError("ssh.connect(\"not a dict\");");
    }

    [Fact]
    public void Connect_MissingHostThrows()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"username\", \"user\"); dict.set(opts, \"password\", \"pass\"); ssh.connect(opts);");
        Assert.Contains("host", ex.Message);
    }

    [Fact]
    public void Connect_MissingUsernameThrows()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"host\", \"example.com\"); dict.set(opts, \"password\", \"pass\"); ssh.connect(opts);");
        Assert.Contains("username", ex.Message);
    }

    [Fact]
    public void Connect_NoAuthMethodThrows()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"host\", \"example.com\"); dict.set(opts, \"username\", \"user\"); ssh.connect(opts);");
        Assert.Contains("password", ex.Message);
    }

    [Fact]
    public void Connect_InvalidHost_ThrowsConnectionError()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"host\", \"invalid.test.localhost.invalid\"); dict.set(opts, \"username\", \"user\"); dict.set(opts, \"password\", \"pass\"); ssh.connect(opts);");
        Assert.Contains("ssh.connect:", ex.Message);
    }

    [Fact]
    public void Connect_InvalidKeyPath_ThrowsError()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"host\", \"example.com\"); dict.set(opts, \"username\", \"user\"); dict.set(opts, \"privateKey\", \"/nonexistent/key\"); ssh.connect(opts);");
        Assert.Contains("ssh.connect:", ex.Message);
    }

    // ssh.exec
    [Fact]
    public void Exec_NonConnectionThrows()
    {
        RunExpectingError("ssh.exec(\"not a connection\", \"ls\");");
    }

    [Fact]
    public void Exec_NonStringCommandThrows()
    {
        RunExpectingError("ssh.exec(\"not a connection\", 123);");
    }

    // ssh.execAll
    [Fact]
    public void ExecAll_NonConnectionThrows()
    {
        RunExpectingError("ssh.execAll(\"not a connection\", [\"ls\"]);");
    }

    [Fact]
    public void ExecAll_NonArrayThrows()
    {
        RunExpectingError("ssh.execAll(\"not a connection\", \"ls\");");
    }

    // ssh.shell
    [Fact]
    public void Shell_NonConnectionThrows()
    {
        RunExpectingError("ssh.shell(\"not a connection\", [\"ls\"]);");
    }

    [Fact]
    public void Shell_NonArrayThrows()
    {
        RunExpectingError("ssh.shell(\"not a connection\", \"ls\");");
    }

    // ssh.close
    [Fact]
    public void Close_NonConnectionThrows()
    {
        RunExpectingError("ssh.close(\"not a connection\");");
    }

    // ssh.isConnected
    [Fact]
    public void IsConnected_NonConnectionThrows()
    {
        RunExpectingError("ssh.isConnected(\"not a connection\");");
    }

    // ssh.tunnel
    [Fact]
    public void Tunnel_NonConnectionThrows()
    {
        RunExpectingError("ssh.tunnel(\"not a connection\", dict.new());");
    }

    [Fact]
    public void Tunnel_NonDictOptionsThrows()
    {
        RunExpectingError("ssh.tunnel(\"not a connection\", \"options\");");
    }

    // ssh.closeTunnel
    [Fact]
    public void CloseTunnel_NonTunnelThrows()
    {
        RunExpectingError("ssh.closeTunnel(\"not a tunnel\");");
    }
}
