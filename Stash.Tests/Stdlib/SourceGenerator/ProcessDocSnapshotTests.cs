namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Linq;
using Stash.Stdlib;
using Stash.Stdlib.Registration;
using Xunit;

public class ProcessDocSnapshotTests
{
    private static readonly NamespaceDefinition Ns =
        StdlibDefinitions.Namespaces.First(n => n.Name == "process");

    [Fact]
    public void Process_HasExpectedFunctionCount()
    {
        Assert.Equal(30, Ns.Functions.Count);
    }

    [Fact]
    public void Exec_Documentation_MatchesSnapshot()
    {
        var fn = Ns.Functions.First(f => f.Name == "exec");
        Assert.Equal(
            "Runs a program with an explicit argv array. Unlike `$(…)`, no shell tokenisation or glob expansion is applied to the args — they are passed verbatim.\n@param program The executable name or path\n@param args Array of argument strings\n@param opts Optional ExecOptions controlling mode, strict, redirect, cwd, env\n@return A CommandResult (stdout, stderr, exitCode) in Capture/Passthrough mode, or a StreamingProcess in Stream mode",
            fn.Documentation);
    }

    [Fact]
    public void Spawn_Documentation_MatchesSnapshot()
    {
        var fn = Ns.Functions.First(f => f.Name == "spawn");
        Assert.Equal(
            "Spawns a child process with redirected stdio. Returns a Process handle. Use process.wait() to collect output and the exit code.\n@param command The command and arguments to spawn\n@return A Process handle",
            fn.Documentation);
    }

    [Fact]
    public void HistoryList_Documentation_MatchesSnapshot()
    {
        var fn = Ns.Functions.First(f => f.Name == "historyList");
        Assert.Equal(
            "Returns the REPL command history as an array of strings, oldest-first. Each entry is one logical command line (multi-line entries contain embedded newlines). Returns an empty array when persistence is disabled or in non-interactive script mode.\n@return Array of history entries",
            fn.Documentation);
    }

    [Fact]
    public void Exit_Documentation_AndDeprecation_MatchSnapshot()
    {
        var fn = Ns.Functions.First(f => f.Name == "exit");
        Assert.Equal(
            "Exits the current process with the given integer exit code (default 0). Runs all pending defer blocks before terminating. Cannot be caught by try/catch.\n@param code (optional) The exit code. Defaults to 0\n@return Does not return — exits the process",
            fn.Documentation);
        Assert.Equal("env.exit", fn.Deprecation?.ReplacementQualifiedName);
    }

    [Fact]
    public void SIGTERM_Constant_MatchesSnapshot()
    {
        var c = Ns.Constants.First(c => c.Name == "SIGTERM");
        Assert.Equal("15", c.Value);
        Assert.Equal("int", c.Type);
        Assert.Equal("Signal.Term", c.Deprecation?.ReplacementQualifiedName);
    }

    [Fact]
    public void CommandResult_Struct_HasExpectedFields()
    {
        var s = Ns.Structs.First(s => s.Name == "CommandResult");
        Assert.Equal(3, s.Fields.Length);
        Assert.Equal("stdout", s.Fields[0].Name);
        Assert.Equal("string", s.Fields[0].Type);
        Assert.Equal("stderr", s.Fields[1].Name);
        Assert.Equal("string", s.Fields[1].Type);
        Assert.Equal("exitCode", s.Fields[2].Name);
        Assert.Equal("int", s.Fields[2].Type);
    }
}
