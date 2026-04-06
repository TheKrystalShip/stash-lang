using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class DictArgParseTests : StashTestBase
{
    private static void RunWithArgsExpectingError(string source, string[] scriptArgs)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.ScriptArgs = scriptArgs;
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // =========================================================================
    // Category 1: Basic Flags (dict format)
    // =========================================================================

    [Fact]
    public void DictFlag_LongForm_SetsTrue()
    {
        var source = """
            let args = args.parse({
                flags: {
                    verbose: { description: "Verbose" }
                }
            });
            let result = args.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--verbose"]));
    }

    [Fact]
    public void DictFlag_Absent_DefaultsFalse()
    {
        var source = """
            let args = args.parse({
                flags: {
                    verbose: { description: "Verbose" }
                }
            });
            let result = args.verbose;
            """;
        Assert.Equal(false, RunWithArgs(source, []));
    }

    [Fact]
    public void DictFlag_ShortForm_SetsTrue()
    {
        var source = """
            let args = args.parse({
                flags: {
                    verbose: { short: "v", description: "Verbose" }
                }
            });
            let result = args.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["-v"]));
    }

    [Fact]
    public void DictFlag_MultipleFlags_AllSet()
    {
        var source = """
            let args = args.parse({
                flags: {
                    verbose: { short: "v", description: "Verbose" },
                    debug:   { short: "d", description: "Debug"   }
                }
            });
            let result = args.debug;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--verbose", "--debug"]));
    }

    [Fact]
    public void DictFlag_NoShortName_OnlyLongForm()
    {
        var source = """
            let args = args.parse({
                flags: {
                    dryrun: { description: "Dry run" }
                }
            });
            let result = args.dryrun;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--dryrun"]));
    }

    // =========================================================================
    // Category 2: Options (dict format)
    // =========================================================================

    [Fact]
    public void DictOption_LongForm_SetsValue()
    {
        var source = """
            let args = args.parse({
                options: {
                    port: { type: "int", description: "Port" }
                }
            });
            let result = args.port;
            """;
        Assert.Equal(8080L, RunWithArgs(source, ["--port", "8080"]));
    }

    [Fact]
    public void DictOption_ShortForm_SetsValue()
    {
        var source = """
            let args = args.parse({
                options: {
                    port: { short: "p", type: "int", description: "Port" }
                }
            });
            let result = args.port;
            """;
        Assert.Equal(9000L, RunWithArgs(source, ["-p", "9000"]));
    }

    [Fact]
    public void DictOption_EqualsSyntax()
    {
        var source = """
            let args = args.parse({
                options: {
                    host: { description: "Host" }
                }
            });
            let result = args.host;
            """;
        Assert.Equal("localhost", RunWithArgs(source, ["--host=localhost"]));
    }

    [Fact]
    public void DictOption_DefaultValue()
    {
        var source = """
            let args = args.parse({
                options: {
                    port: { type: "int", default: 8080, description: "Port" }
                }
            });
            let result = args.port;
            """;
        Assert.Equal(8080L, RunWithArgs(source, []));
    }

    [Fact]
    public void DictOption_NoDefault_ReturnsNull()
    {
        var source = """
            let args = args.parse({
                options: {
                    host: { description: "Host" }
                }
            });
            let result = args.host;
            """;
        Assert.Null(RunWithArgs(source, []));
    }

    [Fact]
    public void DictOption_TypeInt_Coerces()
    {
        var source = """
            let args = args.parse({
                options: {
                    count: { type: "int", description: "Count" }
                }
            });
            let result = args.count;
            """;
        var result = RunWithArgs(source, ["--count", "42"]);
        Assert.IsType<long>(result);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void DictOption_TypeFloat_Coerces()
    {
        var source = """
            let args = args.parse({
                options: {
                    ratio: { type: "float", description: "Ratio" }
                }
            });
            let result = args.ratio;
            """;
        var result = RunWithArgs(source, ["--ratio", "3.14"]);
        Assert.IsType<double>(result);
        Assert.Equal(3.14, (double)result!, 10);
    }

    [Fact]
    public void DictOption_TypeBool_Coerces()
    {
        var source = """
            let args = args.parse({
                options: {
                    enabled: { type: "bool", description: "Enabled" }
                }
            });
            let result = args.enabled;
            """;
        Assert.Equal(true,  RunWithArgs(source, ["--enabled", "true"]));
        Assert.Equal(false, RunWithArgs(source, ["--enabled", "false"]));
        Assert.Equal(true,  RunWithArgs(source, ["--enabled", "1"]));
        Assert.Equal(false, RunWithArgs(source, ["--enabled", "0"]));
        Assert.Equal(true,  RunWithArgs(source, ["--enabled", "yes"]));
        Assert.Equal(false, RunWithArgs(source, ["--enabled", "no"]));
    }

    [Fact]
    public void DictOption_TypeString_Keeps()
    {
        var source = """
            let args = args.parse({
                options: {
                    name: { type: "string", description: "Name" }
                }
            });
            let result = args.name;
            """;
        var result = RunWithArgs(source, ["--name", "alice"]);
        Assert.IsType<string>(result);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void DictOption_Required_Missing_Throws()
    {
        var source = """
            let args = args.parse({
                options: {
                    host: { required: true, description: "Host" }
                }
            });
            let result = args.host;
            """;
        RunWithArgsExpectingError(source, []);
    }

    // =========================================================================
    // Category 3: Positionals (dict format)
    // =========================================================================

    [Fact]
    public void DictPositional_Single_Captured()
    {
        var source = """
            let args = args.parse({
                positionals: [{ name: "target", type: "string", description: "Target" }]
            });
            let result = args.target;
            """;
        Assert.Equal("example.com", RunWithArgs(source, ["example.com"]));
    }

    [Fact]
    public void DictPositional_Multiple_InOrder()
    {
        var source = """
            let args = args.parse({
                positionals: [
                    { name: "src", type: "string", description: "Source"      },
                    { name: "dst", type: "string", description: "Destination" }
                ]
            });
            let result = args.dst;
            """;
        Assert.Equal("/tmp/out", RunWithArgs(source, ["/home/user/file", "/tmp/out"]));
    }

    [Fact]
    public void DictPositional_TypeCoercion()
    {
        var source = """
            let args = args.parse({
                positionals: [{ name: "count", type: "int", description: "Count" }]
            });
            let result = args.count;
            """;
        Assert.Equal(5L, RunWithArgs(source, ["5"]));
    }

    [Fact]
    public void DictPositional_Required_Missing_Throws()
    {
        var source = """
            let args = args.parse({
                positionals: [{ name: "target", required: true, description: "Target" }]
            });
            let result = args.target;
            """;
        RunWithArgsExpectingError(source, []);
    }

    [Fact]
    public void DictPositional_WithDefault()
    {
        var source = """
            let args = args.parse({
                positionals: [{ name: "host", default: "localhost", description: "Host" }]
            });
            let result = args.host;
            """;
        Assert.Equal("localhost", RunWithArgs(source, []));
    }

    // =========================================================================
    // Category 4: Commands (dict format)
    // =========================================================================

    [Fact]
    public void DictCommand_Name_Captured()
    {
        var source = """
            let args = args.parse({
                commands: {
                    start: { description: "Start" },
                    stop:  { description: "Stop"  }
                }
            });
            let result = args.command;
            """;
        Assert.Equal("start", RunWithArgs(source, ["start"]));
    }

    [Fact]
    public void DictCommand_NoneProvided_Null()
    {
        var source = """
            let args = args.parse({
                commands: {
                    start: { description: "Start" }
                }
            });
            let result = args.command;
            """;
        Assert.Null(RunWithArgs(source, []));
    }

    [Fact]
    public void DictCommand_LevelFlag_Works()
    {
        var source = """
            let args = args.parse({
                commands: {
                    start: { description: "Start", flags: { detach: { short: "d", description: "Detach" } } }
                }
            });
            let result = args.start.detach;
            """;
        Assert.Equal(true, RunWithArgs(source, ["start", "--detach"]));
    }

    [Fact]
    public void DictCommand_LevelOption_Works()
    {
        var source = """
            let args = args.parse({
                commands: {
                    start: { description: "Start", options: { delay: { type: "int", default: 0, description: "Delay" } } }
                }
            });
            let result = args.start.delay;
            """;
        Assert.Equal(10L, RunWithArgs(source, ["start", "--delay", "10"]));
    }

    [Fact]
    public void DictCommand_LevelPositional_Works()
    {
        var source = """
            let args = args.parse({
                commands: {
                    deploy: { description: "Deploy", positionals: [{ name: "env", type: "string", description: "Environment" }] }
                }
            });
            let result = args.deploy.env;
            """;
        Assert.Equal("production", RunWithArgs(source, ["deploy", "production"]));
    }

    [Fact]
    public void DictCommand_LevelOption_EqualsSyntax()
    {
        var source = """
            let args = args.parse({
                commands: {
                    start: { description: "Start", options: { port: { type: "int", default: 8080, description: "Port" } } }
                }
            });
            let result = args.start.port;
            """;
        Assert.Equal(3000L, RunWithArgs(source, ["start", "--port=3000"]));
    }

    [Fact]
    public void DictCommand_LevelRequiredOption_Missing_Throws()
    {
        var source = """
            let args = args.parse({
                commands: {
                    deploy: { description: "Deploy", options: { env: { required: true, description: "Environment" } } }
                }
            });
            let result = args.command;
            """;
        RunWithArgsExpectingError(source, ["deploy"]);
    }

    // =========================================================================
    // Category 5: Mixed Arguments (dict format)
    // =========================================================================

    [Fact]
    public void DictMixed_FlagsOptionsPositional()
    {
        var source = """
            let args = args.parse({
                flags:      { verbose: { short: "v", description: "Verbose" } },
                options:    { port:    { short: "p", type: "int", default: 80, description: "Port" } },
                positionals: [{ name: "host", type: "string", description: "Host" }]
            });
            let result = args.port;
            """;
        Assert.Equal(9090L, RunWithArgs(source, ["-v", "--port", "9090", "myhost"]));
    }

    [Fact]
    public void DictMixed_TopLevelFlagWithSubcommand()
    {
        var source = """
            let args = args.parse({
                flags:    { verbose: { short: "v", description: "Verbose" } },
                commands: { start:   { description: "Start" }                }
            });
            let result = args.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--verbose", "start"]));
    }

    [Fact]
    public void DictMixed_EmptySpec_NoArgs()
    {
        var source = """
            let args = args.parse({ name: "mytool", description: "A tool" });
            let result = 42;
            """;
        Assert.Equal(42L, RunWithArgs(source, []));
    }

    // =========================================================================
    // Category 6: Error Cases (dict format)
    // =========================================================================

    [Fact]
    public void DictError_UnknownLongFlag_Throws()
    {
        var source = """
            let args = args.parse({
                flags: {
                    verbose: { description: "Verbose" }
                }
            });
            let result = args.verbose;
            """;
        RunWithArgsExpectingError(source, ["--unknown"]);
    }

    [Fact]
    public void DictError_UnknownShortFlag_Throws()
    {
        var source = """
            let args = args.parse({
                flags: {
                    verbose: { short: "v", description: "Verbose" }
                }
            });
            let result = args.verbose;
            """;
        RunWithArgsExpectingError(source, ["-z"]);
    }

    [Fact]
    public void DictError_OptionMissingValue_Throws()
    {
        var source = """
            let args = args.parse({
                options: {
                    port: { type: "int", description: "Port" }
                }
            });
            let result = args.port;
            """;
        RunWithArgsExpectingError(source, ["--port"]);
    }

    [Fact]
    public void DictError_InvalidIntCoercion_Throws()
    {
        var source = """
            let args = args.parse({
                options: {
                    port: { type: "int", description: "Port" }
                }
            });
            let result = args.port;
            """;
        RunWithArgsExpectingError(source, ["--port", "abc"]);
    }

    // =========================================================================
    // Category 7: Metadata (dict format)
    // =========================================================================

    [Fact]
    public void DictMetadata_NameParses()
    {
        var source = """
            let args = args.parse({
                name: "mytool",
                flags: {
                    verbose: { description: "Verbose" }
                }
            });
            let result = args.verbose;
            """;
        Assert.Equal(false, RunWithArgs(source, []));
    }

    [Fact]
    public void DictMetadata_VersionParses()
    {
        var source = """
            let args = args.parse({ name: "mytool", version: "2.0.0", description: "A tool" });
            let result = 1;
            """;
        Assert.Equal(1L, RunWithArgs(source, []));
    }

    // =========================================================================
    // Category 8: Inline Dict Spec (no intermediate variable)
    // =========================================================================

    [Fact]
    public void DictInlineSpec_DirectCallNoVariable()
    {
        var source = """
            let args = args.parse({ flags: { verbose: { short: "v", description: "Verbose" } } });
            let result = args.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["-v"]));
    }

    // =========================================================================
    // Category 9: Complex Real-World Spec
    // =========================================================================

    [Fact]
    public void DictComplex_FullServiceCtlSpec()
    {
        var complexSpec = """
            let args = args.parse({
                name: "svc",
                version: "1.0.0",
                description: "Service controller",
                flags: {
                    verbose: { short: "v", description: "Verbose output" },
                    quiet:   { short: "q", description: "Quiet mode"     }
                },
                options: {
                    config: { short: "c", type: "string", default: "/etc/svc.conf", description: "Config file" }
                },
                commands: {
                    start: {
                        description: "Start a service",
                        flags: { detach: { short: "d", description: "Run in background" } },
                        options: { port: { short: "p", type: "int", default: 8080, description: "Port" } },
                        positionals: [{ name: "service", type: "string", required: true, description: "Service name" }]
                    },
                    stop: {
                        description: "Stop a service",
                        positionals: [{ name: "service", type: "string", required: true, description: "Service name" }]
                    }
                }
            });
            """;

        var scriptArgs = new[] { "-v", "--config", "/tmp/cfg", "start", "-d", "--port", "3000", "web" };

        // Test args.verbose == true
        var verboseSource = complexSpec + "\nlet result = args.verbose;";
        Assert.Equal(true, RunWithArgs(verboseSource, scriptArgs));

        // Test args.config == "/tmp/cfg"
        var configSource = complexSpec + "\nlet result = args.config;";
        Assert.Equal("/tmp/cfg", RunWithArgs(configSource, scriptArgs));

        // Test args.command == "start"
        var commandSource = complexSpec + "\nlet result = args.command;";
        Assert.Equal("start", RunWithArgs(commandSource, scriptArgs));

        // Test args.start.detach == true
        var detachSource = complexSpec + "\nlet result = args.start.detach;";
        Assert.Equal(true, RunWithArgs(detachSource, scriptArgs));

        // Test args.start.port == 3000L
        var portSource = complexSpec + "\nlet result = args.start.port;";
        Assert.Equal(3000L, RunWithArgs(portSource, scriptArgs));

        // Test args.start.service == "web"
        var serviceSource = complexSpec + "\nlet result = args.start.service;";
        Assert.Equal("web", RunWithArgs(serviceSource, scriptArgs));
    }
}
