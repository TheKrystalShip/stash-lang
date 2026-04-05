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
using Stash.Bytecode;
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

        string? commandString = null;
        bool debug = false;
        bool test = false;
        bool testList = false;
        string? testFilter = null;
        string? scriptPath = null;
        int scriptArgStart = -1;

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
            else if (args[i] == "--debug" && scriptPath is null && commandString is null)
            {
                debug = true;
            }
            else if (args[i] == "--test" && scriptPath is null && commandString is null)
            {
                test = true;
            }
            else if (args[i] == "--test-list" && scriptPath is null && commandString is null)
            {
                testList = true;
                test = true;  // --test-list implies --test
            }
            else if (args[i].StartsWith("--test-filter=") && scriptPath is null && commandString is null)
            {
                testFilter = args[i]["--test-filter=".Length..];
            }
            else if (args[i] == "--" && scriptPath is null && commandString is null)
            {
                // Everything after -- becomes script args (for stdin piping)
                scriptArgStart = i + 1;
                break;
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
            RunSource(commandString, "<command>", scriptArgs);
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
            RunSource(source, "<stdin>", scriptArgs);
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

        // Mode 4: Script file execution
        if (scriptPath is not null)
        {
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
                RunFile(scriptPath, scriptArgs);
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
    private static void RunSource(string source, string sourceName, string[] scriptArgs)
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
    private static void RunFile(string path, string[] scriptArgs)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            System.Environment.Exit(66);
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
        Console.WriteLine("Stash v0.5 — Type statements or expressions, or 'exit' to quit.");

        var editor = new LineEditor();

        var globals = CreateVMGlobals();
        var vm = new VirtualMachine(globals);
        _activeVM = vm;
        vm.Output = Console.Out;
        vm.ErrorOutput = Console.Error;
        vm.Input = Console.In;
        vm.ModuleLoader = LoadModuleForVM;

        try
        {
            while (true)
            {
                string? line = editor.ReadLine("stash> ");

                // null means EOF (Ctrl+D on Unix, Ctrl+Z+Enter on Windows).
                if (line is null || line == "exit")
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // --- Stage 1: Lex ---
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
                        vm.ExecuteRepl(chunk);
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
    private static Dictionary<string, object?> CreateVMGlobals()
    {
        var globals = new Dictionary<string, object?>();

        var globalDef = StdlibDefinitions.GetGlobals(StashCapabilities.All);
        foreach (var (name, fn) in globalDef.RuntimeFunctions)
            globals[name] = fn;

        foreach (var nsDef in StdlibDefinitions.Namespaces)
            globals[nsDef.Name] = nsDef.Namespace;

        globals["Backoff"] = new StashEnum("Backoff", new List<string> { "Fixed", "Linear", "Exponential" });
        globals["RetryOptions"] = new StashStruct("RetryOptions",
            new List<string> { "delay", "backoff", "maxDelay", "jitter", "timeout", "on" },
            new Dictionary<string, IStashCallable>());

        return globals;
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

    /// <summary>Formats and prints a runtime error to stderr.</summary>
    /// <param name="ex">The runtime error to report.</param>
    private static void PrintRuntimeError(RuntimeError ex)
    {
        if (ex.Span is not null)
        {
            Console.Error.WriteLine($"[runtime error at {ex.Span.StartLine}:{ex.Span.StartColumn}] {ex.Message}");
        }
        else
        {
            Console.Error.WriteLine($"[runtime error] {ex.Message}");
        }
    }
}
