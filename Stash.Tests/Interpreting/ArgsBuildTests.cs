using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class ArgsBuildTests
{
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

    private static object? RunWithArgs(string source, string[] scriptArgs)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.ScriptArgs = scriptArgs;
        return vm.Execute(chunk);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // =========================================================================
    // Category 1: Basic Flags
    // =========================================================================

    [Fact]
    public void Build_FlagTrue_EmitsLongFlag()
    {
        var source = """
            let built = args.build(
                { flags: { verbose: { description: "Verbose" } } },
                { verbose: true }
            );
            let result = built[0];
            """;
        Assert.Equal("--verbose", Run(source));
    }

    [Fact]
    public void Build_FlagTrue_PreferShortForm()
    {
        var source = """
            let built = args.build(
                { flags: { verbose: { short: "v" } } },
                { verbose: true }
            );
            let result = built[0];
            """;
        Assert.Equal("-v", Run(source));
    }

    [Fact]
    public void Build_FlagFalse_Skipped()
    {
        var source = """
            let built = args.build(
                { flags: { verbose: { short: "v" } } },
                { verbose: false }
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    [Fact]
    public void Build_FlagMissing_Skipped()
    {
        var source = """
            let built = args.build(
                { flags: { verbose: { short: "v" } } },
                {}
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    [Fact]
    public void Build_MultipleFlags_AllEmitted()
    {
        var source = """
            let built = args.build(
                { flags: { verbose: { short: "v" }, debug: { short: "d" } } },
                { verbose: true, debug: true }
            );
            let result = len(built);
            """;
        Assert.Equal(2L, Run(source));
    }

    // =========================================================================
    // Category 2: Basic Options
    // =========================================================================

    [Fact]
    public void Build_OptionString_EmitsValue()
    {
        var source = """
            let built = args.build(
                { options: { name: { description: "Name" } } },
                { name: "alice" }
            );
            let result = built[0];
            """;
        Assert.Equal("--name", Run(source));
    }

    [Fact]
    public void Build_OptionString_PreferShortForm()
    {
        var source = """
            let built = args.build(
                { options: { name: { short: "n" } } },
                { name: "alice" }
            );
            let result = built[0];
            """;
        Assert.Equal("-n", Run(source));
    }

    [Fact]
    public void Build_OptionInt_EmitsStringValue()
    {
        var source = """
            let built = args.build(
                { options: { port: { short: "p", type: "int" } } },
                { port: 8080 }
            );
            let result = built[1];
            """;
        Assert.Equal("8080", Run(source));
    }

    [Fact]
    public void Build_OptionFloat_EmitsStringValue()
    {
        var source = """
            let built = args.build(
                { options: { ratio: {} } },
                { ratio: 3.14 }
            );
            let result = built[1];
            """;
        Assert.Equal("3.14", Run(source));
    }

    [Fact]
    public void Build_OptionBool_EmitsStringValue()
    {
        var source = """
            let built = args.build(
                { options: { enabled: { type: "bool" } } },
                { enabled: true }
            );
            let result = built[1];
            """;
        Assert.Equal("true", Run(source));
    }

    [Fact]
    public void Build_OptionNull_Skipped()
    {
        var source = """
            let built = args.build(
                { options: { port: { short: "p" } } },
                { port: null }
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    [Fact]
    public void Build_OptionMissing_Skipped()
    {
        var source = """
            let built = args.build(
                { options: { port: { short: "p" } } },
                {}
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    // =========================================================================
    // Category 3: Compound Types (list, map, csv)
    // =========================================================================

    [Fact]
    public void Build_ListOption_RepeatedFlags()
    {
        var source = """
            let built = args.build(
                { options: { ports: { short: "p", type: "list" } } },
                { ports: [8080, 9090] }
            );
            let result = len(built);
            """;
        Assert.Equal(4L, Run(source));
    }

    [Fact]
    public void Build_ListOption_SingleElement()
    {
        var source = """
            let built = args.build(
                { options: { ports: { short: "p", type: "list" } } },
                { ports: [8080] }
            );
            let result = len(built);
            """;
        Assert.Equal(2L, Run(source));
    }

    [Fact]
    public void Build_ListOption_EmptyArray()
    {
        var source = """
            let built = args.build(
                { options: { ports: { short: "p", type: "list" } } },
                { ports: [] }
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    [Fact]
    public void Build_MapOption_RepeatedKeyValue()
    {
        var source = """
            let built = args.build(
                { options: { env: { short: "e", type: "map" } } },
                { env: { A: "1", B: "2" } }
            );
            let result = len(built);
            """;
        Assert.Equal(4L, Run(source));
    }

    [Fact]
    public void Build_MapOption_EmptyDict()
    {
        var source = """
            let built = args.build(
                { options: { env: { short: "e", type: "map" } } },
                { env: {} }
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    [Fact]
    public void Build_CsvOption_CommaJoined()
    {
        var source = """
            let built = args.build(
                { options: { tags: { type: "csv" } } },
                { tags: ["a", "b", "c"] }
            );
            let result = built[1];
            """;
        Assert.Equal("a,b,c", Run(source));
    }

    [Fact]
    public void Build_CsvOption_SingleElement()
    {
        var source = """
            let built = args.build(
                { options: { tags: { type: "csv" } } },
                { tags: ["only"] }
            );
            let result = built[1];
            """;
        Assert.Equal("only", Run(source));
    }

    // =========================================================================
    // Category 4: Positionals
    // =========================================================================

    [Fact]
    public void Build_Positional_EmitsValue()
    {
        var source = """
            let built = args.build(
                { positionals: [{ name: "target", description: "Target host" }] },
                { target: "example.com" }
            );
            let result = built[0];
            """;
        Assert.Equal("example.com", Run(source));
    }

    [Fact]
    public void Build_MultiplePositionals_InOrder()
    {
        var source = """
            let built = args.build(
                { positionals: [{ name: "src" }, { name: "dst" }] },
                { src: "file.txt", dst: "/tmp" }
            );
            let result = built[0];
            """;
        Assert.Equal("file.txt", Run(source));
    }

    [Fact]
    public void Build_PositionalNull_Skipped()
    {
        var source = """
            let built = args.build(
                { positionals: [{ name: "target" }] },
                { target: null }
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    [Fact]
    public void Build_PositionalInt_EmitsStringValue()
    {
        var source = """
            let built = args.build(
                { positionals: [{ name: "count" }] },
                { count: 5 }
            );
            let result = built[0];
            """;
        Assert.Equal("5", Run(source));
    }

    // =========================================================================
    // Category 5: Commands
    // =========================================================================

    [Fact]
    public void Build_Command_EmitsCommandName()
    {
        var source = """
            let built = args.build(
                { commands: { start: { description: "Start the service" } } },
                { command: "start" }
            );
            let result = built[0];
            """;
        Assert.Equal("start", Run(source));
    }

    [Fact]
    public void Build_CommandNoValue_NoOutput()
    {
        var source = """
            let built = args.build(
                { commands: { start: { description: "Start the service" } } },
                { command: null }
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    [Fact]
    public void Build_CommandWithFlags()
    {
        var source = """
            let built = args.build(
                { commands: { start: { flags: { detach: { short: "d" } } } } },
                { command: "start", start: { detach: true } }
            );
            let result = built[1];
            """;
        Assert.Equal("-d", Run(source));
    }

    [Fact]
    public void Build_CommandWithOptions()
    {
        var source = """
            let built = args.build(
                { commands: { start: { options: { port: { description: "Port" } } } } },
                { command: "start", start: { port: 3000 } }
            );
            let result = len(built);
            """;
        Assert.Equal(3L, Run(source));
    }

    [Fact]
    public void Build_CommandWithPositionals()
    {
        var source = """
            let built = args.build(
                { commands: { deploy: { positionals: [{ name: "env" }] } } },
                { command: "deploy", deploy: { env: "prod" } }
            );
            let result = built[1];
            """;
        Assert.Equal("prod", Run(source));
    }

    // =========================================================================
    // Category 6: Mixed (realistic usage)
    // =========================================================================

    [Fact]
    public void Build_Mixed_FlagsOptionsPositionals()
    {
        var source = """
            let built = args.build(
                {
                    flags:       { verbose: { short: "v" } },
                    options:     { port: { short: "p" } },
                    positionals: [{ name: "target" }]
                },
                { verbose: true, port: 8080, target: "example.com" }
            );
            let result = len(built);
            """;
        Assert.Equal(4L, Run(source));
    }

    [Fact]
    public void Build_Mixed_TopLevelAndCommand()
    {
        var source = """
            let spec = {
                flags: { verbose: { short: "v", description: "Verbose" } },
                options: { config: { short: "c", description: "Config" } },
                commands: {
                    start: {
                        description: "Start",
                        flags: { detach: { short: "d", description: "Detach" } },
                        options: { port: { short: "p", type: "int", description: "Port" } },
                        positionals: [{ name: "service", type: "string", description: "Service" }]
                    }
                }
            };
            let built = args.build(spec, {
                verbose: true, config: "/tmp/cfg",
                command: "start",
                start: { detach: true, port: 3000, service: "web" }
            });
            let result = len(built);
            """;
        Assert.Equal(8L, Run(source));

        // Verify command appears between top-level and subcommand args
        var cmdSource = """
            let spec = {
                flags: { verbose: { short: "v", description: "Verbose" } },
                options: { config: { short: "c", description: "Config" } },
                commands: {
                    start: {
                        description: "Start",
                        flags: { detach: { short: "d", description: "Detach" } },
                        options: { port: { short: "p", type: "int", description: "Port" } },
                        positionals: [{ name: "service", type: "string", description: "Service" }]
                    }
                }
            };
            let built = args.build(spec, {
                verbose: true, config: "/tmp/cfg",
                command: "start",
                start: { detach: true, port: 3000, service: "web" }
            });
            let result = built[3];
            """;
        // Index 3 should be "start" (after -v, -c, /tmp/cfg)
        Assert.Equal("start", Run(cmdSource));

        // Verify last token is the positional
        var lastSource = """
            let spec = {
                flags: { verbose: { short: "v", description: "Verbose" } },
                options: { config: { short: "c", description: "Config" } },
                commands: {
                    start: {
                        description: "Start",
                        flags: { detach: { short: "d", description: "Detach" } },
                        options: { port: { short: "p", type: "int", description: "Port" } },
                        positionals: [{ name: "service", type: "string", description: "Service" }]
                    }
                }
            };
            let built = args.build(spec, {
                verbose: true, config: "/tmp/cfg",
                command: "start",
                start: { detach: true, port: 3000, service: "web" }
            });
            let result = built[7];
            """;
        Assert.Equal("web", Run(lastSource));
    }

    [Fact]
    public void Build_EmptySpec_EmptyResult()
    {
        var source = """
            let built = args.build({}, {});
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    [Fact]
    public void Build_EmptyValues_EmptyResult()
    {
        var source = """
            let built = args.build(
                {
                    flags:       { verbose: { short: "v" } },
                    options:     { port: { short: "p" } },
                    positionals: [{ name: "target" }]
                },
                {}
            );
            let result = len(built);
            """;
        Assert.Equal(0L, Run(source));
    }

    // =========================================================================
    // Category: Flag Property Override
    // =========================================================================

    [Fact]
    public void Build_FlagProperty_UsedForFlags()
    {
        var source = """
            let built = args.build(
                { flags: { detach: { flag: "-d" } } },
                { detach: true }
            );
            let result = built[0];
            """;
        Assert.Equal("-d", Run(source));
    }

    [Fact]
    public void Build_FlagProperty_UsedForOptions()
    {
        var source = """
            let built = args.build(
                { options: { env: { flag: "-e", type: "map" } } },
                { env: { A: "1" } }
            );
            let result = built[0];
            """;
        Assert.Equal("-e", Run(source));
    }

    [Fact]
    public void Build_FlagProperty_LongFormOverride()
    {
        var source = """
            let built = args.build(
                { options: { read_only: { flag: "--read-only" } } },
                { read_only: "/data" }
            );
            let result = built[0];
            """;
        Assert.Equal("--read-only", Run(source));
    }

    [Fact]
    public void Build_FlagProperty_TakesPriorityOverShort()
    {
        var source = """
            let built = args.build(
                { flags: { verbose: { flag: "--verbose", short: "v" } } },
                { verbose: true }
            );
            let result = built[0];
            """;
        Assert.Equal("--verbose", Run(source));
    }

    [Fact]
    public void Build_FlagProperty_ListType()
    {
        var source = """
            let built = args.build(
                { options: { ports: { flag: "-p", type: "list" } } },
                { ports: [8080, 9090] }
            );
            let result = built[0];
            """;
        Assert.Equal("-p", Run(source));

        var source2 = """
            let built = args.build(
                { options: { ports: { flag: "-p", type: "list" } } },
                { ports: [8080, 9090] }
            );
            let result = built[2];
            """;
        Assert.Equal("-p", Run(source2));
    }

    [Fact]
    public void Build_FlagProperty_CsvType()
    {
        var source = """
            let built = args.build(
                { options: { tags: { flag: "--tag", type: "csv" } } },
                { tags: ["a", "b"] }
            );
            let result = built[0];
            """;
        Assert.Equal("--tag", Run(source));
    }

    // =========================================================================
    // Category 7: Error Cases
    // =========================================================================

    [Fact]
    public void Build_NonDictSpec_Throws()
    {
        var source = """
            args.build("not a dict", {});
            let result = 0;
            """;
        RunExpectingError(source);
    }

    [Fact]
    public void Build_NonDictValues_Throws()
    {
        var source = """
            args.build({}, "not a dict");
            let result = 0;
            """;
        RunExpectingError(source);
    }

    [Fact]
    public void Build_ListTypeNotArray_Throws()
    {
        var source = """
            args.build(
                { options: { ports: { type: "list" } } },
                { ports: "not an array" }
            );
            let result = 0;
            """;
        RunExpectingError(source);
    }

    [Fact]
    public void Build_MapTypeNotDict_Throws()
    {
        var source = """
            args.build(
                { options: { env: { type: "map" } } },
                { env: "not a dict" }
            );
            let result = 0;
            """;
        RunExpectingError(source);
    }

    [Fact]
    public void Build_CsvTypeNotArray_Throws()
    {
        var source = """
            args.build(
                { options: { tags: { type: "csv" } } },
                { tags: 42 }
            );
            let result = 0;
            """;
        RunExpectingError(source);
    }

    // =========================================================================
    // Category 8: Roundtrip (parse → build → parse)
    // =========================================================================

    [Fact]
    public void Build_Roundtrip_ParseThenBuild()
    {
        var source = """
            let spec = {
                flags:   { verbose: { short: "v" } },
                options: { port: { short: "p", type: "int" } }
            };
            let parsed = args.parse(spec);
            let built  = args.build(spec, parsed);
            let result = built[0];
            """;
        Assert.Equal("-v", RunWithArgs(source, ["-v", "-p", "8080"]));
    }
}
