using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Runtime;

namespace Stash.Tests.Interpreting;

public class SftpBuiltInsTests
{
    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    private static RuntimeError RunCapturingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        return Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    // sftp.connect
    [Fact]
    public void Connect_NonDictThrows()
    {
        RunExpectingError("sftp.connect(\"not a dict\");");
    }

    [Fact]
    public void Connect_MissingHostThrows()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"username\", \"user\"); dict.set(opts, \"password\", \"pass\"); sftp.connect(opts);");
        Assert.Contains("host", ex.Message);
    }

    [Fact]
    public void Connect_MissingUsernameThrows()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"host\", \"example.com\"); dict.set(opts, \"password\", \"pass\"); sftp.connect(opts);");
        Assert.Contains("username", ex.Message);
    }

    [Fact]
    public void Connect_NoAuthMethodThrows()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"host\", \"example.com\"); dict.set(opts, \"username\", \"user\"); sftp.connect(opts);");
        Assert.Contains("password", ex.Message);
    }

    [Fact]
    public void Connect_InvalidHost_ThrowsConnectionError()
    {
        var ex = RunCapturingError("let opts = dict.new(); dict.set(opts, \"host\", \"invalid.test.localhost.invalid\"); dict.set(opts, \"username\", \"user\"); dict.set(opts, \"password\", \"pass\"); sftp.connect(opts);");
        Assert.Contains("sftp.connect:", ex.Message);
    }

    // sftp.upload
    [Fact]
    public void Upload_NonConnectionThrows()
    {
        RunExpectingError("sftp.upload(\"not a conn\", \"/local\", \"/remote\");");
    }

    [Fact]
    public void Upload_NonStringLocalPathThrows()
    {
        RunExpectingError("sftp.upload(\"not a conn\", 123, \"/remote\");");
    }

    [Fact]
    public void Upload_NonStringRemotePathThrows()
    {
        RunExpectingError("sftp.upload(\"not a conn\", \"/local\", 123);");
    }

    // sftp.download
    [Fact]
    public void Download_NonConnectionThrows()
    {
        RunExpectingError("sftp.download(\"not a conn\", \"/remote\", \"/local\");");
    }

    // sftp.readFile
    [Fact]
    public void ReadFile_NonConnectionThrows()
    {
        RunExpectingError("sftp.readFile(\"not a conn\", \"/remote\");");
    }

    [Fact]
    public void ReadFile_NonStringPathThrows()
    {
        RunExpectingError("sftp.readFile(\"not a conn\", 123);");
    }

    // sftp.writeFile
    [Fact]
    public void WriteFile_NonConnectionThrows()
    {
        RunExpectingError("sftp.writeFile(\"not a conn\", \"/remote\", \"content\");");
    }

    [Fact]
    public void WriteFile_NonStringPathThrows()
    {
        RunExpectingError("sftp.writeFile(\"not a conn\", 123, \"content\");");
    }

    [Fact]
    public void WriteFile_NonStringContentThrows()
    {
        RunExpectingError("sftp.writeFile(\"not a conn\", \"/remote\", 123);");
    }

    // sftp.list
    [Fact]
    public void List_NonConnectionThrows()
    {
        RunExpectingError("sftp.list(\"not a conn\", \"/path\");");
    }

    // sftp.delete
    [Fact]
    public void Delete_NonConnectionThrows()
    {
        RunExpectingError("sftp.delete(\"not a conn\", \"/path\");");
    }

    // sftp.mkdir
    [Fact]
    public void Mkdir_NonConnectionThrows()
    {
        RunExpectingError("sftp.mkdir(\"not a conn\", \"/path\");");
    }

    // sftp.rmdir
    [Fact]
    public void Rmdir_NonConnectionThrows()
    {
        RunExpectingError("sftp.rmdir(\"not a conn\", \"/path\");");
    }

    // sftp.exists
    [Fact]
    public void Exists_NonConnectionThrows()
    {
        RunExpectingError("sftp.exists(\"not a conn\", \"/path\");");
    }

    // sftp.stat
    [Fact]
    public void Stat_NonConnectionThrows()
    {
        RunExpectingError("sftp.stat(\"not a conn\", \"/path\");");
    }

    // sftp.chmod
    [Fact]
    public void Chmod_NonConnectionThrows()
    {
        RunExpectingError("sftp.chmod(\"not a conn\", \"/path\", 755);");
    }

    [Fact]
    public void Chmod_NonStringPathThrows()
    {
        RunExpectingError("sftp.chmod(\"not a conn\", 123, 755);");
    }

    [Fact]
    public void Chmod_NonIntModeThrows()
    {
        RunExpectingError("sftp.chmod(\"not a conn\", \"/path\", \"755\");");
    }

    // sftp.rename
    [Fact]
    public void Rename_NonConnectionThrows()
    {
        RunExpectingError("sftp.rename(\"not a conn\", \"/old\", \"/new\");");
    }

    // sftp.close
    [Fact]
    public void Close_NonConnectionThrows()
    {
        RunExpectingError("sftp.close(\"not a conn\");");
    }

    // sftp.isConnected
    [Fact]
    public void IsConnected_NonConnectionThrows()
    {
        RunExpectingError("sftp.isConnected(\"not a conn\");");
    }
}
