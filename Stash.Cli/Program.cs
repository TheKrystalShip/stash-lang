// ============================================================================
// Stash — Phase 5 (Script File Execution + REPL)
//
// Entry point supporting multiple modes:
//
//   stash                        → REPL mode (interactive)
//   stash <script.stash>         → execute script file
//   stash -c '<code>'            → execute code from argument
//   echo '<code>' | stash        → execute code from stdin
//
// Processing pipeline (both modes):
//   1. Lex:       Source text → token list       (Lexer)
//   2. Parse:     Token list  → AST              (Parser)
//   3. Interpret: AST         → execution        (Interpreter)
//
// The REPL also supports two sub-modes:
//   - Statement mode: input containing ';' or '{' is parsed as a program.
//   - Expression mode: input is parsed as a single expression and printed.
//
// Each stage can fail independently. Errors go to stderr, results to stdout.
// The loop exits on "exit" or EOF (Ctrl+D).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Stash.Bytecode;
using Stash.Cli.Completion;
using Stash.Cli.Shell;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Tap;

namespace Stash;

/// <summary>CLI entry point for the Stash language: REPL, file execution, debug mode, and test runner.</summary>
public class Program
{
    private static VirtualMachine? _activeVM;
    private static bool _optimize = true;
    private static bool _disassemble = false;
    private static bool _shellEnabled = false;
    private static bool _shellExplicitlyDisabled = false;
    private static bool _historyDisabled = false;
    private const string Version = "0.5.0";
    /// <summary>Exact stderr message emitted when shell mode is requested on Windows (v1 not yet supported).</summary>
    internal const string WindowsNoShellMessage = "shell mode not yet supported on Windows";
    /// <summary>Parses CLI arguments and dispatches to the appropriate execution mode.</summary>
    /// <param name="args">Command-line arguments passed to the program.</param>
    public static void Main(string[] args)
    {
        // Package manager subcommand
        if (args.Length > 0 && args[0] is "pkg" or "p")
        {
            Stash.Cli.PackageManager.Commands.PackageCommands.Run(args[1..]);
            return;
        }

        // Service manager subcommand
        if (args.Length > 0 && args[0] is "service" or "svc")
        {
            Stash.Cli.ServiceManager.ServiceCommands.Run(args[1..]);
            return;
        }

        // Early exit flags
        if (args.Length > 0 && args[0] is "--help" or "-h")
        {
            PrintHelp();
            return;
        }

        if (args.Length > 0 && args[0] is "--version" or "-v")
        {
            Console.WriteLine($"stash {Version}");
            return;
        }

        string? commandString = null;
        bool debug = false;
        bool disassemble = false;
        bool test = false;
        bool testList = false;
        string? testFilter = null;
        bool optimize = true;
        string? scriptPath = null;
        int scriptArgStart = -1;
        bool compile = false;
        bool strip = false;
        bool verify = false;
        string? outputPath = null;
        bool shellEnabled = false;
        bool shellExplicitlyDisabled = false;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-c" || args[i] == "--command") && scriptPath is null && commandString is null)
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: -c requires a command string argument.");
                    System.Environment.Exit(64);
                    return;
                }
                commandString = args[i + 1];
                i++;
                scriptArgStart = i + 1;
                break;
            }
            else if (args[i] == "--debug" && commandString is null)
            {
                debug = true;
            }
            else if (args[i] == "--test" && commandString is null)
            {
                test = true;
            }
            else if (args[i] == "--test-list" && commandString is null)
            {
                testList = true;
                test = true;  // --test-list implies --test
            }
            else if (args[i].StartsWith("--test-filter=") && commandString is null)
            {
                testFilter = args[i]["--test-filter=".Length..];
            }
            else if (args[i] == "--no-optimize" && commandString is null)
            {
                optimize = false;
            }
            else if (args[i] == "--compile" && commandString is null)
            {
                compile = true;
            }
            else if (args[i] == "--strip" && commandString is null)
            {
                strip = true;
            }
            else if (args[i] == "--verify" && commandString is null)
            {
                verify = true;
            }
            else if ((args[i] == "-o" || args[i] == "--output") && commandString is null)
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: -o requires an output path argument.");
                    System.Environment.Exit(64);
                    return;
                }
                outputPath = args[i + 1];
                i++;
            }
            else if (args[i] == "--disassemble" && commandString is null)
            {
                disassemble = true;
            }
            else if (args[i] == "--shell" && commandString is null)
            {
                shellEnabled = true;
            }
            else if (args[i] == "--no-shell" && commandString is null)
            {
                shellExplicitlyDisabled = true;
            }
            else if (args[i] == "--no-history" && commandString is null)
            {
                _historyDisabled = true;
            }
            else if (args[i] == "--reset-prompt" && commandString is null)
            {
                // Re-extract bootstrap scripts and exit.
                Stash.Cli.Repl.BootstrapExtractor.Extract(
                    Stash.Cli.Repl.BootstrapExtractor.GetTargetDir());
                System.Environment.Exit(0);
                return;
            }
            else if (args[i] == "--" && commandString is null)
            {
                // Everything after -- becomes script args (for stdin piping)
                scriptArgStart = i + 1;
                break;
            }
            else if (args[i].StartsWith('-') && scriptPath is null && commandString is null)
            {
                Console.Error.WriteLine($"Error: Unknown option '{args[i]}'.");
                Console.Error.WriteLine("Run 'stash --help' for usage information.");
                System.Environment.Exit(64);
                return;
            }
            else if (scriptPath is null && commandString is null)
            {
                scriptPath = args[i];
                scriptArgStart = i + 1;
            }
            else
            {
                break; // remaining args belong to the script
            }
        }

        // Collect script arguments (available for all modes)
        string[] scriptArgs = scriptArgStart >= 0 && scriptArgStart < args.Length
            ? args[scriptArgStart..]
            : Array.Empty<string>();
        _optimize = optimize;
        _disassemble = disassemble;

        // Apply STASH_SHELL env var (can be overridden by --no-shell).
        if (!shellExplicitlyDisabled &&
            string.Equals(System.Environment.GetEnvironmentVariable("STASH_SHELL"), "1", StringComparison.Ordinal))
        {
            shellEnabled = true;
        }

        _shellEnabled = shellEnabled;
        _shellExplicitlyDisabled = shellExplicitlyDisabled;

        // Register cleanup handlers for graceful shutdown
        Console.CancelKeyPress += (_, e) =>
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
        };

        // Mode 1: -c command string
        if (commandString is not null)
        {
            if (debug || test)
            {
                Console.Error.WriteLine("Error: --debug and --test flags cannot be used with -c.");
                System.Environment.Exit(64);
                return;
            }
            RunSource(commandString, "<command>", scriptArgs, disassemble);
            return;
        }

        // Mode 2: Piped stdin
        if (Console.IsInputRedirected && scriptPath is null)
        {
            if (debug || test)
            {
                Console.Error.WriteLine("Error: --debug and --test flags cannot be used with stdin input.");
                System.Environment.Exit(64);
                return;
            }
            string source = Console.In.ReadToEnd();
            RunSource(source, "<stdin>", scriptArgs, disassemble);
            return;
        }

        // Mode 3: Flags requiring a script file
        if (debug && scriptPath is null)
        {
            Console.Error.WriteLine("Error: --debug mode requires a script file.");
            System.Environment.Exit(64);
            return;
        }

        if (test && scriptPath is null)
        {
            Console.Error.WriteLine("Error: --test mode requires a script file.");
            System.Environment.Exit(64);
            return;
        }

        if (disassemble && scriptPath is null)
        {
            Console.Error.WriteLine("Error: --disassemble mode requires a script file.");
            System.Environment.Exit(64);
            return;
        }

        if (compile && scriptPath is null)
        {
            Console.Error.WriteLine("Error: --compile requires a script file.");
            System.Environment.Exit(64);
            return;
        }

        if (verify && scriptPath is null)
        {
            Console.Error.WriteLine("Error: --verify requires a file.");
            System.Environment.Exit(64);
            return;
        }

        if (compile && (debug || test || disassemble))
        {
            Console.Error.WriteLine("Error: --compile cannot be combined with --debug, --test, or --disassemble.");
            System.Environment.Exit(64);
            return;
        }

        if (verify && (debug || test || disassemble || compile))
        {
            Console.Error.WriteLine("Error: --verify cannot be combined with other flags.");
            System.Environment.Exit(64);
            return;
        }

        // Mode 4: Script file execution
        if (scriptPath is not null)
        {
            if (verify)
            {
                VerifyBytecodeFile(scriptPath);
                return;
            }

            if (compile)
            {
                CompileToFile(scriptPath, outputPath, strip, optimize);
                return;
            }

            if (disassemble && (debug || test))
            {
                Console.Error.WriteLine("Error: --disassemble cannot be combined with --debug or --test.");
                System.Environment.Exit(64);
                return;
            }

            if (debug && test)
            {
                RunFileWithDebuggerAndTests(scriptPath, scriptArgs, testFilter, testList);
            }
            else if (debug)
            {
                RunFileWithDebugger(scriptPath, scriptArgs);
            }
            else if (test)
            {
                RunFileWithTests(scriptPath, scriptArgs, testFilter, testList);
            }
            else
            {
                RunFile(scriptPath, scriptArgs, disassemble);
            }
            return;
        }

        // Mode 5: Interactive REPL
        RunRepl();
    }

    /// <summary>Executes Stash source code from a string (used by -c and stdin piping).</summary>
    /// <param name="source">The source code to execute.</param>
    /// <param name="sourceName">Diagnostic name for the source (e.g., "&lt;command&gt;" or "&lt;stdin&gt;").</param>
    /// <param name="scriptArgs">Arguments to pass to the script.</param>
    private static void PrintHelp()
    {
        Console.WriteLine($"Stash v{Version} \u2014 A scripting language for system administration");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  stash [options] [script.stash] [args...]");
        Console.WriteLine("  stash -c '<code>' [args...]");
        Console.WriteLine("  stash pkg <command> [options]");
        Console.WriteLine("  stash service <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help                Show this help message");
        Console.WriteLine("  -v, --version             Show version information");
        Console.WriteLine("  -c, --command <code>      Execute code from argument");
        Console.WriteLine("      --test                Run script with TAP test harness");
        Console.WriteLine("      --test-list           List test names without running");
        Console.WriteLine("      --test-filter=<pat>   Filter tests by semicolon-separated names");
        Console.WriteLine("      --debug               Run script with debugger attached");
        Console.WriteLine("      --compile             Compile script to .stashc bytecode");
        Console.WriteLine("      --strip               Strip debug info from compiled bytecode");
        Console.WriteLine("      --verify              Verify bytecode file integrity");
        Console.WriteLine("      --disassemble         Print bytecode disassembly");
        Console.WriteLine("      --no-optimize         Disable bytecode optimizations");
        Console.WriteLine("      --shell               Enable REPL shell mode (experimental)");
        Console.WriteLine("      --no-shell            Disable REPL shell mode (overrides STASH_SHELL=1)");
        Console.WriteLine("      --no-history          Disable persistent REPL history for this session");
        Console.WriteLine("      --reset-prompt        Re-extract prompt bootstrap scripts and exit");
        Console.WriteLine("  -o, --output <path>       Output path for compiled bytecode");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  pkg, p                    Package manager (stash pkg --help)");
        Console.WriteLine("  service, svc              Service manager (stash service --help)");
        Console.WriteLine();
        Console.WriteLine("Modes:");
        Console.WriteLine("  stash                     Start interactive REPL");
        Console.WriteLine("  stash <file.stash>        Execute a Stash script");
        Console.WriteLine("  stash <file.stashc>       Execute compiled bytecode");
        Console.WriteLine("  stash -c '<code>'         Execute code string");
        Console.WriteLine("  echo '<code>' | stash     Execute code from stdin");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  stash script.stash                Run a script");
        Console.WriteLine("  stash -c 'io.println(\"hello\")'    Execute inline code");
        Console.WriteLine("  stash --test tests.stash          Run tests in a file");
        Console.WriteLine("  stash --compile app.stash         Compile to app.stashc");
        Console.WriteLine("  stash pkg install                 Install package dependencies");
        Console.WriteLine();
        Console.WriteLine("Documentation: https://stash-lang.dev/docs");
    }

    private static void RunSource(string source, string sourceName, string[] scriptArgs, bool disassemble = false)
    {
        // Stage 1: Lex
        var lexer = new Lexer(source, sourceName);
        List<Token> tokens = lexer.ScanTokens();

        if (lexer.Errors.Count > 0)
        {
            foreach (string error in lexer.Errors)
            {
                Console.Error.WriteLine($"[lex error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        // Stage 2: Parse
        var parser = new Parser(tokens);
        List<Stmt> statements = parser.ParseProgram();

        if (parser.Errors.Count > 0)
        {
            foreach (string error in parser.Errors)
            {
                Console.Error.WriteLine($"[parse error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        // Stage 3: Execute via bytecode VM
        try
        {
            SemanticResolver.Resolve(statements);
            Chunk chunk = Compiler.Compile(statements);

            if (disassemble)
            {
                PrintDisassembly(chunk, source);
                return;
            }

            var globals = CreateVMGlobals();
            var vm = new VirtualMachine(globals);
            _activeVM = vm;
            vm.Output = Console.Out;
            vm.ErrorOutput = Console.Error;
            vm.Input = Console.In;
            vm.ScriptArgs = scriptArgs;
            vm.ModuleLoader = LoadModuleForVM;
            vm.Execute(chunk);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("[runtime error] Script execution was cancelled.");
            System.Environment.Exit(70);
        }
        finally
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
            _activeVM = null;
        }
    }

    /// <summary>Executes a Stash script file.</summary>
    /// <param name="path">Path to the script file.</param>
    /// <param name="scriptArgs">Arguments to pass to the script.</param>
    private static void RunFile(string path, string[] scriptArgs, bool disassemble = false)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            System.Environment.Exit(66);
            return;
        }

        // Check if file is pre-compiled bytecode
        if (BytecodeReader.IsBytecodeFile(path))
        {
            RunBytecodeFile(path, scriptArgs, disassemble);
            return;
        }

        string source = System.IO.File.ReadAllText(path);

        // Stage 1: Lex — pass filename so SourceSpan references the actual file
        var lexer = new Lexer(source, path);
        List<Token> tokens = lexer.ScanTokens();

        if (lexer.Errors.Count > 0)
        {
            foreach (string error in lexer.Errors)
            {
                Console.Error.WriteLine($"[lex error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        // Stage 2: Parse
        var parser = new Parser(tokens);
        List<Stmt> statements = parser.ParseProgram();

        if (parser.Errors.Count > 0)
        {
            foreach (string error in parser.Errors)
            {
                Console.Error.WriteLine($"[parse error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        try
        {
            SemanticResolver.Resolve(statements);
            Chunk chunk = Compiler.Compile(statements);

            if (disassemble)
            {
                PrintDisassembly(chunk, source);
                return;
            }

            var globals = CreateVMGlobals();
            var vm = new VirtualMachine(globals);
            _activeVM = vm;
            vm.CurrentFile = path;
            vm.Output = Console.Out;
            vm.ErrorOutput = Console.Error;
            vm.Input = Console.In;
            vm.ScriptArgs = scriptArgs;
            vm.ModuleLoader = LoadModuleForVM;
            vm.Execute(chunk);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("[runtime error] Script execution was cancelled.");
            System.Environment.Exit(70);
        }
        finally
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
            _activeVM = null;
        }
    }

    /// <summary>Executes a script with the interactive CLI debugger attached.</summary>
    /// <param name="path">Path to the script file.</param>
    /// <param name="scriptArgs">Arguments to pass to the script.</param>
    private static void RunFileWithDebugger(string path, string[] scriptArgs)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            System.Environment.Exit(66);
            return;
        }

        string source = System.IO.File.ReadAllText(path);
        var lexer = new Lexer(source, path);
        List<Token> tokens = lexer.ScanTokens();

        if (lexer.Errors.Count > 0)
        {
            foreach (string error in lexer.Errors)
            {
                Console.Error.WriteLine($"[lex error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        var parser = new Parser(tokens);
        List<Stmt> statements = parser.ParseProgram();

        if (parser.Errors.Count > 0)
        {
            foreach (string error in parser.Errors)
            {
                Console.Error.WriteLine($"[parse error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        Console.Error.WriteLine("Warning: --debug CLI mode is not yet supported with the bytecode VM. Running without debugger.");
        Console.Error.WriteLine("Use the VS Code debugger (DAP) for full debugging support.");

        try
        {
            SemanticResolver.Resolve(statements);
            Chunk chunk = Compiler.Compile(statements);
            var globals = CreateVMGlobals();
            var vm = new VirtualMachine(globals);
            _activeVM = vm;
            vm.CurrentFile = path;
            vm.Output = Console.Out;
            vm.ErrorOutput = Console.Error;
            vm.Input = Console.In;
            vm.ScriptArgs = scriptArgs;
            vm.ModuleLoader = LoadModuleForVM;
            vm.Execute(chunk);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
        }
        finally
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
            _activeVM = null;
        }
        Console.WriteLine("Script execution completed.");
    }

    /// <summary>Executes a script with debugger and test harness.</summary>
    /// <param name="path">Path to the script file.</param>
    /// <param name="scriptArgs">Arguments to pass to the script.</param>
    /// <param name="testFilter">Optional semicolon-separated test name filter.</param>
    /// <param name="testList">When true, lists tests without running them.</param>
    private static void RunFileWithDebuggerAndTests(string path, string[] scriptArgs, string? testFilter = null, bool testList = false)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            System.Environment.Exit(66);
            return;
        }

        string source = System.IO.File.ReadAllText(path);
        var lexer = new Lexer(source, path);
        List<Token> tokens = lexer.ScanTokens();

        if (lexer.Errors.Count > 0)
        {
            foreach (string error in lexer.Errors)
            {
                Console.Error.WriteLine($"[lex error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        var parser = new Parser(tokens);
        List<Stmt> statements = parser.ParseProgram();

        if (parser.Errors.Count > 0)
        {
            foreach (string error in parser.Errors)
            {
                Console.Error.WriteLine($"[parse error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        Console.Error.WriteLine("Warning: --debug CLI mode is not yet supported with the bytecode VM. Running tests without debugger.");

        var reporter = new TapReporter();

        try
        {
            SemanticResolver.Resolve(statements);
            Chunk chunk = Compiler.Compile(statements);
            var globals = CreateVMGlobals();
            var vm = new VirtualMachine(globals);
            _activeVM = vm;
            vm.CurrentFile = path;
            vm.Output = Console.Out;
            vm.ErrorOutput = Console.Error;
            vm.Input = Console.In;
            vm.ScriptArgs = scriptArgs;
            vm.ModuleLoader = LoadModuleForVM;
            vm.TestHarness = reporter;
            if (testFilter is not null)
                vm.TestFilter = testFilter.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (testList)
                vm.DiscoveryMode = true;
            vm.Execute(chunk);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
            return;
        }
        finally
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
            _activeVM = null;
        }

        reporter.OnRunComplete(reporter.PassedCount, reporter.FailedCount, reporter.SkippedCount);
        Console.WriteLine("Script execution completed.");

        if (reporter.FailedCount > 0)
            System.Environment.Exit(1);
    }

    /// <summary>Executes a test script with TAP harness.</summary>
    /// <param name="path">Path to the script file.</param>
    /// <param name="scriptArgs">Arguments to pass to the script.</param>
    /// <param name="testFilter">Optional semicolon-separated test name filter.</param>
    /// <param name="testList">When true, lists tests without running them.</param>
    private static void RunFileWithTests(string path, string[] scriptArgs, string? testFilter = null, bool testList = false)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            System.Environment.Exit(66);
            return;
        }

        string source = System.IO.File.ReadAllText(path);

        // Stage 1: Lex
        var lexer = new Lexer(source, path);
        List<Token> tokens = lexer.ScanTokens();

        if (lexer.Errors.Count > 0)
        {
            foreach (string error in lexer.Errors)
            {
                Console.Error.WriteLine($"[lex error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        // Stage 2: Parse
        var parser = new Parser(tokens);
        List<Stmt> statements = parser.ParseProgram();

        if (parser.Errors.Count > 0)
        {
            foreach (string error in parser.Errors)
            {
                Console.Error.WriteLine($"[parse error] {error}");
            }
            System.Environment.Exit(65);
            return;
        }

        var reporter = new TapReporter();

        try
        {
            SemanticResolver.Resolve(statements);
            Chunk chunk = Compiler.Compile(statements);
            var globals = CreateVMGlobals();
            var vm = new VirtualMachine(globals);
            _activeVM = vm;
            vm.CurrentFile = path;
            vm.Output = Console.Out;
            vm.ErrorOutput = Console.Error;
            vm.Input = Console.In;
            vm.ScriptArgs = scriptArgs;
            vm.ModuleLoader = LoadModuleForVM;
            vm.TestHarness = reporter;
            if (testFilter is not null)
                vm.TestFilter = testFilter.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (testList)
                vm.DiscoveryMode = true;
            vm.Execute(chunk);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
            return;
        }
        finally
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
            _activeVM = null;
        }

        // Emit TAP plan and exit with appropriate code
        reporter.OnRunComplete(reporter.PassedCount, reporter.FailedCount, reporter.SkippedCount);

        if (reporter.FailedCount > 0)
        {
            System.Environment.Exit(1);
        }
    }

    /// <summary>Starts the interactive REPL.</summary>
    private static void RunRepl()
    {
        Console.WriteLine($"Stash v{Version} \u2014 Type statements or expressions, or 'exit' to quit.");

        // ── RC file discovery (§12.1) ────────────────────────────────────────
        string? rcPath = RcFileLoader.FindRcFile();

        // ── Shell mode setup (§12.3 — RC presence implicitly enables shell) ──
        // Priority: --no-shell / STASH_SHELL=0 > --shell / STASH_SHELL=1 > RC presence > default.
        bool stashShellExplicitlyZero = string.Equals(
            System.Environment.GetEnvironmentVariable("STASH_SHELL"), "0", StringComparison.Ordinal);
        bool explicitlyDisabled = _shellExplicitlyDisabled || stashShellExplicitlyZero;

        bool shellModeEnabled;
        if (explicitlyDisabled)
            shellModeEnabled = false;
        else if (_shellEnabled)
            shellModeEnabled = true;
        else if (rcPath != null)
            shellModeEnabled = true; // RC file presence → implicit shell activation
        else
            shellModeEnabled = false;

        // Shell mode is not supported on Windows in v1.
        if (shellModeEnabled && OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(WindowsNoShellMessage);
            shellModeEnabled = false;
        }

        var editor = new LineEditor();
        var globals = CreateVMGlobals();
        var vm = new VirtualMachine(globals);
        var reader = new MultiLineReader(editor,
            firstPromptProvider: () => Stash.Cli.Repl.PromptRenderer.Render(vm),
            continuationPromptProvider: depth => Stash.Cli.Repl.PromptRenderer.Continuation(vm, depth));

        _activeVM = vm;
        vm.Output = Console.Out;
        vm.ErrorOutput = Console.Error;
        vm.Input = Console.In;
        vm.ModuleLoader = LoadModuleForVM;

        // Register the prompt.resetBootstrap() handler. This lets Stash code
        // trigger a re-extract + reload without a layering violation (Stdlib
        // cannot reference Cli directly).
        Stash.Stdlib.BuiltIns.PromptBuiltIns.ResetBootstrapHandler = () =>
        {
            string promptDir = Stash.Cli.Repl.BootstrapExtractor.GetTargetDir();
            Stash.Cli.Repl.BootstrapExtractor.Extract(promptDir);
            Stash.Cli.Repl.BootstrapLoader.Load(promptDir, vm);
        };

        // Register the git status probe handler. Timeout is 150 ms by default
        // and can be overridden via STASH_PROMPT_GIT_TIMEOUT_MS.
        Stash.Stdlib.BuiltIns.PromptBuiltIns.GitProbeHandler = cwd =>
        {
            int timeoutMs = 150;
            string? envVal = Environment.GetEnvironmentVariable("STASH_PROMPT_GIT_TIMEOUT_MS");
            if (envVal != null && int.TryParse(envVal, out int parsed) && parsed > 0)
                timeoutMs = parsed;
            return Stash.Cli.Repl.GitStatusProbe.Probe(cwd, timeoutMs);
        };

        // Register the convention global resolver so prompt.render() (per spec §4.3) can
        // fall back to a top-level `prompt` global defined in the REPL or rc file.
        Stash.Stdlib.BuiltIns.PromptBuiltIns.ConventionFnResolver = name =>
        {
            if (!vm.HasReplGlobal(name)) return null;
            return vm.Globals.TryGetValue(name, out StashValue val) && val.ToObject() is IStashCallable c
                ? c
                : null;
        };

        // Surface the active shell-mode flag so prompt.context() reports the correct
        // mode (per spec \u00a75.1) regardless of how shell mode was activated.
        Stash.Stdlib.BuiltIns.PromptBuiltIns.ShellModeActive = shellModeEnabled;

        // Build shell context once per REPL session.
        // Always constructed for tab-completion; also used for shell execution if enabled.
        var completionPathCache = new PathExecutableCache();
        var completionShellCtx = new ShellContext
        {
            Vm = vm,
            PathCache = completionPathCache,
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new System.Collections.Generic.HashSet<string>(
                StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        var completionClassifier = new ShellLineClassifier(completionShellCtx);

        ShellLineClassifier? classifier = null;
        ShellRunner? shellRunner = null;

        if (shellModeEnabled)
        {
            Console.WriteLine("Shell mode enabled (experimental). Type Stash code or shell commands.");

            // Reuse the already-constructed context, path cache, and classifier.
            classifier = completionClassifier;
            shellRunner = new ShellRunner(completionShellCtx);

            // Wire alias.exec() so template aliases can run through the shell runner
            // from Stash code (e.g. alias.exec("g", ["status"])).
            Stash.Cli.Shell.AliasDispatcher.Wire(shellRunner, vm);

            // Extend the multi-line reader to recognise trailing-pipe continuation.
            reader.IsShellIncomplete = line => classifier.IsShellIncomplete(line);
        }

        // ── Tab completion setup ─────────────────────────────────────────────
        bool completionEnabled = !string.Equals(
            Environment.GetEnvironmentVariable("STASH_NO_COMPLETION"),
            "1", StringComparison.Ordinal);

        var customCompleters = new CustomCompleterRegistry();
        var completionEngine = new CompletionEngine(
            vm, completionPathCache, customCompleters, completionClassifier);
        CompletionWiring.Wire(completionEngine, customCompleters, vm);

        editor.CompletionEngine = completionEngine;
        editor.CompletionEnabled = completionEnabled;

        // ── Prompt bootstrap (shell mode only, before RC) ────────────────────
        // Extract embedded scripts to ~/.config/stash/prompt/ on first run or
        // after a version upgrade, then load them into the VM so that theme.*
        // and starter.* globals are available when ~/.stashrc runs.
        // Skip if STASH_NO_PROMPT_BOOTSTRAP=1 (opt-out / CI environments).
        if (shellModeEnabled &&
            !string.Equals(
                System.Environment.GetEnvironmentVariable("STASH_NO_PROMPT_BOOTSTRAP"),
                "1",
                StringComparison.Ordinal))
        {
            try
            {
                Stash.Cli.Repl.BootstrapExtractor.EnsureExtracted();
                Stash.Cli.Repl.BootstrapLoader.Load(
                    Stash.Cli.Repl.BootstrapExtractor.GetTargetDir(), vm);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"prompt: bootstrap error — {ex.Message}");
                // Renderer falls back to "stash> " automatically.
            }
        }

        // ── Load RC file (§12.2) — before the first REPL prompt ─────────────
        if (rcPath != null)
        {
            RcFileLoader.Load(rcPath, vm, shellModeEnabled);
        }

        // ── Load aliases.stash (§10.3) — after RC file, before first prompt ─
        // Sources the managed aliases file so persisted aliases are available at start-up.
        // Errors are tolerant (warn to stderr); missing file is silently skipped.
        if (shellModeEnabled && shellRunner is not null)
        {
            Stash.Cli.Shell.AliasPersistence.Load(vm, shellRunner);
        }

        // ── History setup (Persistent REPL History spec §9.3) ────────────────
        if (!_historyDisabled)
        {
            int cap = Stash.Cli.History.HistoryFileLoader.GetCap();
            string? historyPath = cap == 0 ? null : Stash.Cli.History.HistoryFileLoader.ResolvePath();
            if (historyPath != null)
            {
                var initial = Stash.Cli.History.HistoryFileLoader.Load(historyPath, cap, Console.Error);
                var writer = new Stash.Cli.History.HistoryFileWriter(historyPath, cap, initial, Console.Error);
                var sink = new Stash.Cli.History.FileHistorySink(writer, initial);
                editor.HistorySink = sink;
                editor.SeedHistory(initial);

                Stash.Stdlib.BuiltIns.ProcessBuiltIns.HistoryListProvider = () => writer.Snapshot();
                Stash.Stdlib.BuiltIns.ProcessBuiltIns.HistoryClearHandler = () => writer.Clear();
                Stash.Stdlib.BuiltIns.ProcessBuiltIns.HistoryAddHandler = entry => writer.Append(entry);
            }
        }

        try
        {
            while (true)
            {
                string? line = reader.ReadLogicalLine();

                // null means EOF (Ctrl+D on Unix, Ctrl+Z+Enter on Windows).
                if (line is null || line == "exit")
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // ── REPL-only desugarings (§8.2): apply before classification/evaluation ──
                line = ReplLinePreprocessor.Apply(line);

                // ── Shell mode routing ────────────────────────────────────
                if (shellModeEnabled && classifier is not null && shellRunner is not null)
                {
                    var mode = classifier.Classify(line);
                    if (mode is LineMode.Shell or LineMode.ShellForced or LineMode.ShellStrict)
                    {
                        // ShellRunner consumes the full line; ShellLineLexer.Parse strips
                        // the leading '\' or '!' and sets IsForced / IsStrict accordingly.
                        Stash.Cli.Repl.PromptRenderer.WriteCommandStart();
                        try
                        {
                            shellRunner.Run(line);
                        }
                        catch (RuntimeError ex)
                        {
                            PrintRuntimeError(ex);
                        }
                        finally
                        {
                            Stash.Cli.Repl.PromptRenderer.WriteCommandEnd(vm.LastExitCode);
                        }
                        continue;
                    }
                    // Stash mode falls through to the lex/parse/execute path below.
                }

                // ── Stash lex / parse / execute path ─────────────────────
                var lexer = new Lexer(line, "<stdin>");
                List<Token> tokens = lexer.ScanTokens();

                if (lexer.Errors.Count > 0)
                {
                    foreach (string error in lexer.Errors)
                    {
                        Console.Error.WriteLine($"[lex error] {error}");
                    }

                    continue;
                }

                // Determine whether the input looks like statements or a bare expression.
                bool isStatementMode = LooksLikeStatements(tokens);

                if (isStatementMode)
                {
                    // --- Statement mode ---
                    var parser = new Parser(tokens);
                    List<Stmt> statements = parser.ParseProgram();

                    if (parser.Errors.Count > 0)
                    {
                        foreach (string error in parser.Errors)
                        {
                            Console.Error.WriteLine($"[parse error] {error}");
                        }

                        continue;
                    }

                    try
                    {
                        SemanticResolver.Resolve(statements);
                        Chunk chunk = Compiler.Compile(statements);
                        Stash.Cli.Repl.PromptRenderer.WriteCommandStart();
                        try
                        {
                            vm.ExecuteRepl(chunk);
                        }
                        catch (RuntimeError ex)
                        {
                            PrintRuntimeError(ex);
                        }
                        finally
                        {
                            Stash.Cli.Repl.PromptRenderer.WriteCommandEnd(vm.LastExitCode);
                        }
                    }
                    catch (RuntimeError ex)
                    {
                        PrintRuntimeError(ex);
                    }
                }
                else
                {
                    // --- Expression mode ---
                    var parser = new Parser(tokens);
                    Expr expr = parser.Parse();

                    if (parser.Errors.Count > 0)
                    {
                        foreach (string error in parser.Errors)
                        {
                            Console.Error.WriteLine($"[parse error] {error}");
                        }

                        continue;
                    }

                    try
                    {
                        Chunk chunk = Compiler.CompileExpression(expr);
                        Stash.Cli.Repl.PromptRenderer.WriteCommandStart();
                        try
                        {
                            object? result = vm.Execute(chunk);
                            if (result is not null)
                            {
                                Console.WriteLine(RuntimeValues.Stringify(result));
                            }
                        }
                        catch (RuntimeError ex)
                        {
                            PrintRuntimeError(ex);
                        }
                        finally
                        {
                            Stash.Cli.Repl.PromptRenderer.WriteCommandEnd(vm.LastExitCode);
                        }
                    }
                    catch (RuntimeError ex)
                    {
                        PrintRuntimeError(ex);
                    }
                }
            }
        }
        finally
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
            _activeVM = null;
        }
    }

    /// <summary>Creates built-in globals dictionary for the bytecode VM.</summary>
    private static Dictionary<string, StashValue> CreateVMGlobals()
    {
        return StdlibDefinitions.CreateVMGlobals(StashCapabilities.All);
    }

    /// <summary>Module loading callback for the bytecode VM.</summary>
    private static Chunk LoadModuleForVM(string modulePath, string? currentFile)
    {
        string? basePath = currentFile is not null ? System.IO.Path.GetDirectoryName(currentFile) : null;
        string fullPath;

        if (System.IO.Path.IsPathRooted(modulePath))
        {
            fullPath = modulePath;
        }
        else if (basePath is not null)
        {
            fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, modulePath));
        }
        else
        {
            fullPath = System.IO.Path.GetFullPath(modulePath);
        }

        string? resolvedPath = Stash.Common.ModuleResolver.ResolveFilePath(fullPath);
        if (resolvedPath is null && basePath is not null && Stash.Common.ModuleResolver.IsBareSpecifier(modulePath))
            resolvedPath = Stash.Common.ModuleResolver.ResolvePackageImport(modulePath, basePath);

        if (resolvedPath is null)
            throw new RuntimeError($"Cannot find module '{modulePath}'.", null);

        string source = System.IO.File.ReadAllText(resolvedPath);
        var lexer = new Lexer(source, resolvedPath);
        List<Token> tokens = lexer.ScanTokens();
        if (lexer.Errors.Count > 0)
            throw new RuntimeError($"Lex errors in module '{resolvedPath}': {lexer.Errors[0]}", null);

        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        if (parser.Errors.Count > 0)
            throw new RuntimeError($"Parse errors in module '{resolvedPath}': {parser.Errors[0]}", null);

        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts);
    }

    /// <summary>
    /// Determines whether the token stream looks like it contains statements
    /// (as opposed to a bare expression).
    /// </summary>
    private static bool LooksLikeStatements(List<Token> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        // Check first token for statement-starting keywords.
        TokenType first = tokens[0].Type;
        if (first == TokenType.Let || first == TokenType.Const || first == TokenType.Fn ||
            first == TokenType.Struct || first == TokenType.Enum || first == TokenType.Import ||
            first == TokenType.If || first == TokenType.While || first == TokenType.For ||
            first == TokenType.Return || first == TokenType.Break || first == TokenType.Continue)
        {
            return true;
        }

        // Check if first token is 'args' identifier (contextual keyword)
        if (first == TokenType.Identifier && tokens[0].Lexeme == "args")
        {
            return true;
        }

        // Check for semicolons or braces anywhere in the token list.
        foreach (Token token in tokens)
        {
            if (token.Type == TokenType.Semicolon || token.Type == TokenType.LeftBrace)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Prints bytecode disassembly for the given chunk and all nested function chunks.</summary>
    /// <param name="chunk">The top-level compiled chunk to disassemble.</param>
    /// <param name="sourceText">Optional source text for inline source annotations.</param>
    private static void PrintDisassembly(Chunk chunk, string? sourceText = null)
    {
        var options = new DisassemblerOptions
        {
            Color = !Console.IsOutputRedirected,
            SourceText = sourceText,
        };
        Console.Out.WriteLine(Disassembler.DisassembleAll(chunk, options));
    }

    private static void CompileToFile(string path, string? outputPath, bool strip, bool optimize)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            System.Environment.Exit(66);
            return;
        }

        string source = System.IO.File.ReadAllText(path);

        var lexer = new Lexer(source, path);
        List<Token> tokens = lexer.ScanTokens();
        if (lexer.Errors.Count > 0)
        {
            foreach (string error in lexer.Errors)
                Console.Error.WriteLine($"[lex error] {error}");
            System.Environment.Exit(65);
            return;
        }

        var parser = new Parser(tokens);
        List<Stmt> statements = parser.ParseProgram();
        if (parser.Errors.Count > 0)
        {
            foreach (string error in parser.Errors)
                Console.Error.WriteLine($"[parse error] {error}");
            System.Environment.Exit(65);
            return;
        }

        try
        {
            SemanticResolver.Resolve(statements);
            Chunk chunk = Compiler.Compile(statements);

            string outPath = outputPath ?? System.IO.Path.ChangeExtension(path, ".stashc");
            BytecodeWriter.Write(outPath, chunk, includeDebugInfo: !strip, optimized: optimize, sourceText: source, embedSource: true);
            Console.WriteLine($"Compiled {path} \u2192 {outPath}");
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
        }
    }

    private static void RunBytecodeFile(string path, string[] scriptArgs, bool disassemble)
    {
        try
        {
            Chunk chunk = BytecodeReader.Read(path);

            if (disassemble)
            {
                string? embeddedSource = BytecodeReader.ReadEmbeddedSource(path);
                PrintDisassembly(chunk, embeddedSource);
                return;
            }

            var globals = CreateVMGlobals();
            var vm = new VirtualMachine(globals);
            _activeVM = vm;
            vm.CurrentFile = path;
            vm.Output = Console.Out;
            vm.ErrorOutput = Console.Error;
            vm.Input = Console.In;
            vm.ScriptArgs = scriptArgs;
            vm.ModuleLoader = LoadModuleForVM;
            vm.Execute(chunk);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"[bytecode error] {ex.Message}");
            System.Environment.Exit(65);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
        }
        finally
        {
            _activeVM?.CleanupTrackedProcesses();
            _activeVM?.CleanupTrackedWatchers();
            _activeVM = null;
        }
    }

    private static void VerifyBytecodeFile(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            System.Environment.Exit(66);
            return;
        }

        try
        {
            Chunk chunk = BytecodeReader.Read(path);
            Console.WriteLine($"Valid .stashc file: {path}");
            Console.WriteLine($"  Functions: {CountFunctions(chunk)}");
            Console.WriteLine($"  Constants: {chunk.Constants.Length}");
            Console.WriteLine($"  Code size: {chunk.Code.Length} bytes");
            Console.WriteLine($"  Globals: {chunk.GlobalSlotCount}");
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Invalid bytecode file: {ex.Message}");
            System.Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading bytecode file: {ex.Message}");
            System.Environment.Exit(1);
        }
    }

    private static int CountFunctions(Chunk chunk)
    {
        int count = 0;
        foreach (StashValue c in chunk.Constants)
        {
            if (c.AsObj is Chunk)
                count++;
        }
        return count;
    }

    /// <summary>Formats and prints a runtime error to stderr.</summary>
    /// <param name="ex">The runtime error to report.</param>
    private static void PrintRuntimeError(RuntimeError ex)
    {
        string errorType = ex.ErrorType ?? "RuntimeError";

        if (ex.CallStack is { Count: > 0 })
        {
            Console.Error.WriteLine($"{errorType}: {ex.Message}");
            foreach (var frame in ex.CallStack)
                Console.Error.WriteLine($"  at {frame.FunctionName} ({frame.Span})");

            PrintSourceContext(ex.CallStack[0]);
        }
        else if (ex.Span is { } span)
        {
            string location = string.IsNullOrEmpty(span.File)
                ? $"{span.StartLine}:{span.StartColumn}"
                : $"{span.File}:{span.StartLine}:{span.StartColumn}";
            Console.Error.WriteLine($"[runtime error at {location}] {ex.Message}");
        }
        else
        {
            Console.Error.WriteLine($"[runtime error] {ex.Message}");
        }
    }

    private static readonly System.Collections.Generic.Dictionary<string, string[]?> _sourceCache = new();

    private static void PrintSourceContext(StackFrame frame)
    {
        string? file = frame.Span.File;
        if (string.IsNullOrEmpty(file) || file == "<stdin>")
            return;

        string[] lines;
        try
        {
            if (!_sourceCache.TryGetValue(file, out string[]? cached))
            {
                cached = System.IO.File.ReadAllLines(file, System.Text.Encoding.UTF8);
                _sourceCache[file] = cached;
            }
            if (cached is null) return;
            lines = cached;
        }
        catch (System.Exception)
        {
            return;
        }

        int errorLine = frame.Span.StartLine; // 1-based
        if (errorLine < 1 || errorLine > lines.Length)
            return;

        int firstLine = Math.Max(1, errorLine - 1);
        int lastLine = Math.Min(lines.Length, errorLine + 1);
        int lineNumWidth = lastLine.ToString().Length;

        Console.Error.WriteLine();
        for (int lineNum = firstLine; lineNum <= lastLine; lineNum++)
        {
            string lineContent = lines[lineNum - 1];
            string prefix = lineNum == errorLine ? " > " : "   ";
            Console.Error.WriteLine($"{prefix}{lineNum.ToString().PadLeft(lineNumWidth)} | {lineContent}");

            if (lineNum == errorLine && frame.Span.StartColumn > 0)
            {
                int indent = prefix.Length + lineNumWidth + 3;
                int col = frame.Span.StartColumn - 1;
                int length = Math.Max(1, frame.Span.EndColumn > frame.Span.StartColumn
                    ? frame.Span.EndColumn - frame.Span.StartColumn
                    : 1);
                Console.Error.Write(new string(' ', indent + col));
                Console.Error.WriteLine(new string('^', length));
            }
        }
    }
}
