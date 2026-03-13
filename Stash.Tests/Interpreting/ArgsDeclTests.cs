using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;

namespace Stash.Tests.Interpreting;

public class ArgsDeclTests
{
    private static object? RunWithArgs(string source, string[] scriptArgs)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.SetScriptArgs(scriptArgs);
        interpreter.Interpret(statements);
        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private static void RunWithArgsExpectingError(string source, string[] scriptArgs)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.SetScriptArgs(scriptArgs);
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    // =========================================================================
    // Category 1: Basic Flags
    // =========================================================================

    [Fact]
    public void Flag_LongForm_SetsTrue()
    {
        var source = """
            let tree = ArgTree { flags: [ArgDef { name: "verbose", description: "Verbose mode" }] };
            let args = parseArgs(tree);
            let result = args.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--verbose"]));
    }

    [Fact]
    public void Flag_Absent_DefaultsFalse()
    {
        var source = """
            let tree = ArgTree { flags: [ArgDef { name: "verbose", description: "Verbose mode" }] };
            let args = parseArgs(tree);
            let result = args.verbose;
            """;
        Assert.Equal(false, RunWithArgs(source, []));
    }

    [Fact]
    public void Flag_ShortForm_SetsTrue()
    {
        var source = """
            let tree = ArgTree { flags: [ArgDef { name: "verbose", short: "v", description: "Verbose mode" }] };
            let args = parseArgs(tree);
            let result = args.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["-v"]));
    }

    [Fact]
    public void Flag_MultipleFlags_AllSet()
    {
        var source = """
            let tree = ArgTree {
                flags: [
                    ArgDef { name: "verbose", short: "v", description: "Verbose" },
                    ArgDef { name: "debug",   short: "d", description: "Debug"   }
                ]
            };
            let args = parseArgs(tree);
            let result = args.debug;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--verbose", "--debug"]));
    }

    [Fact]
    public void Flag_CustomName_NotHelpOrVersion()
    {
        var source = """
            let tree = ArgTree { flags: [ArgDef { name: "quiet", short: "q", description: "Suppress output" }] };
            let args = parseArgs(tree);
            let result = args.quiet;
            """;
        Assert.Equal(true, RunWithArgs(source, ["-q"]));
    }

    [Fact]
    public void Flag_NoShortName_OnlyLongForm()
    {
        var source = """
            let tree = ArgTree { flags: [ArgDef { name: "dryrun", description: "Dry run mode" }] };
            let args = parseArgs(tree);
            let result = args.dryrun;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--dryrun"]));
    }

    // =========================================================================
    // Category 2: Options
    // =========================================================================

    [Fact]
    public void Option_LongForm_SetsValue()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "port", type: "int", description: "Port number" }] };
            let args = parseArgs(tree);
            let result = args.port;
            """;
        Assert.Equal(8080L, RunWithArgs(source, ["--port", "8080"]));
    }

    [Fact]
    public void Option_ShortForm_SetsValue()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "port", short: "p", type: "int", description: "Port number" }] };
            let args = parseArgs(tree);
            let result = args.port;
            """;
        Assert.Equal(9000L, RunWithArgs(source, ["-p", "9000"]));
    }

    [Fact]
    public void Option_EqualsSyntax_SetsValue()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "host", description: "Hostname" }] };
            let args = parseArgs(tree);
            let result = args.host;
            """;
        Assert.Equal("localhost", RunWithArgs(source, ["--host=localhost"]));
    }

    [Fact]
    public void Option_DefaultValue_UsedWhenAbsent()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "port", type: "int", default: 8080, description: "Port" }] };
            let args = parseArgs(tree);
            let result = args.port;
            """;
        Assert.Equal(8080L, RunWithArgs(source, []));
    }

    [Fact]
    public void Option_NoDefault_ReturnsNullWhenAbsent()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "host", description: "Hostname" }] };
            let args = parseArgs(tree);
            let result = args.host;
            """;
        Assert.Null(RunWithArgs(source, []));
    }

    [Fact]
    public void Option_TypeInt_CoercesToLong()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "count", type: "int", description: "Count" }] };
            let args = parseArgs(tree);
            let result = args.count;
            """;
        var result = RunWithArgs(source, ["--count", "42"]);
        Assert.IsType<long>(result);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Option_TypeFloat_CoercesToDouble()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "ratio", type: "float", description: "Ratio" }] };
            let args = parseArgs(tree);
            let result = args.ratio;
            """;
        var result = RunWithArgs(source, ["--ratio", "3.14"]);
        Assert.IsType<double>(result);
        Assert.Equal(3.14, (double)result!, 10);
    }

    [Fact]
    public void Option_TypeBool_CoercesToBool()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "enabled", type: "bool", description: "Enabled" }] };
            let args = parseArgs(tree);
            let result = args.enabled;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--enabled", "true"]));
        Assert.Equal(false, RunWithArgs(source, ["--enabled", "false"]));
        Assert.Equal(true, RunWithArgs(source, ["--enabled", "1"]));
        Assert.Equal(false, RunWithArgs(source, ["--enabled", "0"]));
        Assert.Equal(true, RunWithArgs(source, ["--enabled", "yes"]));
        Assert.Equal(false, RunWithArgs(source, ["--enabled", "no"]));
    }

    [Fact]
    public void Option_TypeStringOrNull_KeepsAsString()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "name", type: "string", description: "Name" }] };
            let args = parseArgs(tree);
            let result = args.name;
            """;
        var result = RunWithArgs(source, ["--name", "alice"]);
        Assert.IsType<string>(result);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void Option_Required_Missing_ThrowsRuntimeError()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "host", required: true, description: "Host" }] };
            let args = parseArgs(tree);
            let result = args.host;
            """;
        RunWithArgsExpectingError(source, []);
    }

    // =========================================================================
    // Category 3: Positionals
    // =========================================================================

    [Fact]
    public void Positional_Single_CapturedCorrectly()
    {
        var source = """
            let tree = ArgTree { positionals: [ArgDef { name: "target", type: "string", description: "Target" }] };
            let args = parseArgs(tree);
            let result = args.target;
            """;
        Assert.Equal("example.com", RunWithArgs(source, ["example.com"]));
    }

    [Fact]
    public void Positional_Multiple_CapturedInOrder()
    {
        var source = """
            let tree = ArgTree {
                positionals: [
                    ArgDef { name: "src", type: "string", description: "Source"      },
                    ArgDef { name: "dst", type: "string", description: "Destination" }
                ]
            };
            let args = parseArgs(tree);
            let result = args.dst;
            """;
        Assert.Equal("/tmp/out", RunWithArgs(source, ["/home/user/file", "/tmp/out"]));
    }

    [Fact]
    public void Positional_TypeInt_Coerces()
    {
        var source = """
            let tree = ArgTree { positionals: [ArgDef { name: "count", type: "int", description: "Count" }] };
            let args = parseArgs(tree);
            let result = args.count;
            """;
        Assert.Equal(5L, RunWithArgs(source, ["5"]));
    }

    [Fact]
    public void Positional_Required_Missing_ThrowsRuntimeError()
    {
        var source = """
            let tree = ArgTree { positionals: [ArgDef { name: "target", required: true, description: "Target" }] };
            let args = parseArgs(tree);
            let result = args.target;
            """;
        RunWithArgsExpectingError(source, []);
    }

    [Fact]
    public void Positional_WithDefault_UsedWhenAbsent()
    {
        var source = """
            let tree = ArgTree { positionals: [ArgDef { name: "host", default: "localhost", description: "Host" }] };
            let args = parseArgs(tree);
            let result = args.host;
            """;
        Assert.Equal("localhost", RunWithArgs(source, []));
    }

    [Fact]
    public void Positional_NoType_KeepsAsString()
    {
        var source = """
            let tree = ArgTree { positionals: [ArgDef { name: "filename", description: "File" }] };
            let args = parseArgs(tree);
            let result = args.filename;
            """;
        var result = RunWithArgs(source, ["report.txt"]);
        Assert.IsType<string>(result);
        Assert.Equal("report.txt", result);
    }

    // =========================================================================
    // Category 4: Subcommands
    // =========================================================================

    [Fact]
    public void Command_Name_CapturedInArgsCommand()
    {
        var source = """
            let tree = ArgTree {
                commands: [
                    ArgDef { name: "start", description: "Start service" },
                    ArgDef { name: "stop",  description: "Stop service"  }
                ]
            };
            let args = parseArgs(tree);
            let result = args.command;
            """;
        Assert.Equal("start", RunWithArgs(source, ["start"]));
    }

    [Fact]
    public void Command_NoneProvided_ArgsCommandIsNull()
    {
        var source = """
            let tree = ArgTree {
                commands: [ArgDef { name: "start", description: "Start service" }]
            };
            let args = parseArgs(tree);
            let result = args.command;
            """;
        Assert.Null(RunWithArgs(source, []));
    }

    [Fact]
    public void Command_LevelFlag_Works()
    {
        var source = """
            let subtree = ArgTree { flags: [ArgDef { name: "detach", short: "d", description: "Run in background" }] };
            let tree = ArgTree {
                commands: [ArgDef { name: "start", description: "Start service", args: subtree }]
            };
            let args = parseArgs(tree);
            let result = args.start.detach;
            """;
        Assert.Equal(true, RunWithArgs(source, ["start", "--detach"]));
    }

    [Fact]
    public void Command_LevelOption_Works()
    {
        var source = """
            let subtree = ArgTree { options: [ArgDef { name: "delay", type: "int", default: 0, description: "Delay seconds" }] };
            let tree = ArgTree {
                commands: [ArgDef { name: "start", description: "Start service", args: subtree }]
            };
            let args = parseArgs(tree);
            let result = args.start.delay;
            """;
        Assert.Equal(10L, RunWithArgs(source, ["start", "--delay", "10"]));
    }

    [Fact]
    public void Command_LevelPositional_Works()
    {
        var source = """
            let subtree = ArgTree { positionals: [ArgDef { name: "env", type: "string", description: "Environment" }] };
            let tree = ArgTree {
                commands: [ArgDef { name: "deploy", description: "Deploy app", args: subtree }]
            };
            let args = parseArgs(tree);
            let result = args.deploy.env;
            """;
        Assert.Equal("production", RunWithArgs(source, ["deploy", "production"]));
    }

    [Fact]
    public void Command_LevelOption_EqualsSyntax()
    {
        var source = """
            let subtree = ArgTree { options: [ArgDef { name: "port", type: "int", default: 8080, description: "Port" }] };
            let tree = ArgTree {
                commands: [ArgDef { name: "start", description: "Start service", args: subtree }]
            };
            let args = parseArgs(tree);
            let result = args.start.port;
            """;
        Assert.Equal(3000L, RunWithArgs(source, ["start", "--port=3000"]));
    }

    [Fact]
    public void Command_LevelRequiredOption_Missing_ThrowsRuntimeError()
    {
        var source = """
            let subtree = ArgTree { options: [ArgDef { name: "env", required: true, description: "Environment" }] };
            let tree = ArgTree {
                commands: [ArgDef { name: "deploy", description: "Deploy", args: subtree }]
            };
            let args = parseArgs(tree);
            let result = args.command;
            """;
        RunWithArgsExpectingError(source, ["deploy"]);
    }

    [Fact]
    public void Command_MultipleDefinedCorrectOneActivated()
    {
        var source = """
            let tree = ArgTree {
                commands: [
                    ArgDef { name: "build",  description: "Build"  },
                    ArgDef { name: "deploy", description: "Deploy" },
                    ArgDef { name: "test",   description: "Test"   }
                ]
            };
            let args = parseArgs(tree);
            let result = args.command;
            """;
        Assert.Equal("deploy", RunWithArgs(source, ["deploy"]));
    }

    // =========================================================================
    // Category 5: Mixed Arguments
    // =========================================================================

    [Fact]
    public void Mixed_FlagsOptionsPositional_Together()
    {
        var source = """
            let tree = ArgTree {
                flags:      [ArgDef { name: "verbose", short: "v", description: "Verbose" }],
                options:    [ArgDef { name: "port",    short: "p", type: "int", default: 80, description: "Port" }],
                positionals:[ArgDef { name: "host",    type: "string", description: "Host" }]
            };
            let args = parseArgs(tree);
            let result = args.port;
            """;
        Assert.Equal(9090L, RunWithArgs(source, ["-v", "--port", "9090", "myhost"]));
    }

    [Fact]
    public void Mixed_TopLevelFlagWithSubcommand()
    {
        var source = """
            let tree = ArgTree {
                flags:    [ArgDef { name: "verbose", short: "v", description: "Verbose" }],
                commands: [ArgDef { name: "start",                description: "Start"   }]
            };
            let args = parseArgs(tree);
            let result = args.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--verbose", "start"]));
    }

    [Fact]
    public void Mixed_OptionBeforeAndAfterSubcommand()
    {
        var source = """
            let subtree = ArgTree { options: [ArgDef { name: "workers", type: "int", default: 1, description: "Workers" }] };
            let tree = ArgTree {
                options:  [ArgDef { name: "config", description: "Config file" }],
                commands: [ArgDef { name: "run",    description: "Run", args: subtree }]
            };
            let args = parseArgs(tree);
            let result = args.run.workers;
            """;
        Assert.Equal(4L, RunWithArgs(source, ["run", "--workers", "4"]));
    }

    [Fact]
    public void Mixed_MultipleOptionsWithDifferentTypes()
    {
        var source = """
            let tree = ArgTree {
                options: [
                    ArgDef { name: "count", type: "int",    description: "Count" },
                    ArgDef { name: "ratio", type: "float",  description: "Ratio" },
                    ArgDef { name: "label", type: "string", description: "Label" }
                ]
            };
            let args = parseArgs(tree);
            let result = args.label;
            """;
        Assert.Equal("test", RunWithArgs(source, ["--count", "3", "--ratio", "0.5", "--label", "test"]));
    }

    [Fact]
    public void Mixed_EmptyArgsBlock_NoArgsPassed()
    {
        var source = """
            let tree = ArgTree { name: "mytool", description: "A tool" };
            let args = parseArgs(tree);
            let result = 42;
            """;
        Assert.Equal(42L, RunWithArgs(source, []));
    }

    // =========================================================================
    // Category 6: Error Cases
    // =========================================================================

    [Fact]
    public void Error_UnknownLongFlag_ThrowsRuntimeError()
    {
        var source = """
            let tree = ArgTree { flags: [ArgDef { name: "verbose", description: "Verbose" }] };
            let args = parseArgs(tree);
            let result = args.verbose;
            """;
        RunWithArgsExpectingError(source, ["--unknown"]);
    }

    [Fact]
    public void Error_UnknownShortFlag_ThrowsRuntimeError()
    {
        var source = """
            let tree = ArgTree { flags: [ArgDef { name: "verbose", short: "v", description: "Verbose" }] };
            let args = parseArgs(tree);
            let result = args.verbose;
            """;
        RunWithArgsExpectingError(source, ["-z"]);
    }

    [Fact]
    public void Error_OptionMissingValue_ThrowsRuntimeError()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "port", type: "int", description: "Port" }] };
            let args = parseArgs(tree);
            let result = args.port;
            """;
        RunWithArgsExpectingError(source, ["--port"]);
    }

    [Fact]
    public void Error_InvalidIntCoercion_ThrowsRuntimeError()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "port", type: "int", description: "Port" }] };
            let args = parseArgs(tree);
            let result = args.port;
            """;
        RunWithArgsExpectingError(source, ["--port", "abc"]);
    }

    [Fact]
    public void Error_InvalidBoolValue_ThrowsRuntimeError()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "enabled", type: "bool", description: "Enabled" }] };
            let args = parseArgs(tree);
            let result = args.enabled;
            """;
        RunWithArgsExpectingError(source, ["--enabled", "maybe"]);
    }

    [Fact]
    public void Error_InvalidFloatValue_ThrowsRuntimeError()
    {
        var source = """
            let tree = ArgTree { options: [ArgDef { name: "ratio", type: "float", description: "Ratio" }] };
            let args = parseArgs(tree);
            let result = args.ratio;
            """;
        RunWithArgsExpectingError(source, ["--ratio", "not-a-float"]);
    }

    // =========================================================================
    // Category 7: Metadata
    // =========================================================================

    [Fact]
    public void Metadata_NameParses()
    {
        var source = """
            let tree = ArgTree { name: "mytool", flags: [ArgDef { name: "verbose", description: "Verbose" }] };
            let args = parseArgs(tree);
            let result = args.verbose;
            """;
        Assert.Equal(false, RunWithArgs(source, []));
    }

    [Fact]
    public void Metadata_VersionParses()
    {
        var source = """
            let tree = ArgTree { name: "mytool", version: "2.0.0", description: "A tool" };
            let args = parseArgs(tree);
            let result = 1;
            """;
        Assert.Equal(1L, RunWithArgs(source, []));
    }

    [Fact]
    public void Metadata_DescriptionParses()
    {
        var source = """
            let tree = ArgTree { description: "This is a description", flags: [ArgDef { name: "quiet", description: "Quiet mode" }] };
            let args = parseArgs(tree);
            let result = args.quiet;
            """;
        Assert.Equal(false, RunWithArgs(source, []));
    }

    // =========================================================================
    // Category 8: Contextual Keywords (args/flag/option/command are regular
    //             identifiers — the args { } syntax has been removed)
    // =========================================================================

    [Fact]
    public void ContextualKeyword_Args_UsableAsVariable()
    {
        var source = """
            let args = 42;
            let result = args;
            """;
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.SetScriptArgs([]);
        interpreter.Interpret(statements);
        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        Assert.Equal(42L, interpreter.Interpret(resultParser.Parse()));
    }

    [Fact]
    public void ContextualKeyword_Flag_UsableAsVariable()
    {
        var source = """
            let flag = "my-flag";
            let result = flag;
            """;
        Assert.Equal("my-flag", RunWithArgs(source, []));
    }

    [Fact]
    public void ContextualKeyword_Option_UsableAsVariable()
    {
        var source = """
            let option = true;
            let result = option;
            """;
        Assert.Equal(true, RunWithArgs(source, []));
    }

    [Fact]
    public void ContextualKeyword_Command_UsableAsVariable()
    {
        var source = """
            let command = 99;
            let result = command;
            """;
        Assert.Equal(99L, RunWithArgs(source, []));
    }
}
