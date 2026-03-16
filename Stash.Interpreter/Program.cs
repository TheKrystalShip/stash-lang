// ============================================================================
// Stash — Phase 5 (Script File Execution + REPL)
//
// Entry point supporting two modes:
//
//   stash                → REPL mode
//   stash <script.stash> → execute script file
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
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting;
using Stash.Testing;

namespace Stash;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            RunRepl();
            return;
        }

        bool debug = false;
        bool test = false;
        bool testList = false;
        string? testFilter = null;
        string? scriptPath = null;
        int scriptArgStart = -1;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--debug" && scriptPath is null)
            {
                debug = true;
            }
            else if (args[i] == "--test" && scriptPath is null)
            {
                test = true;
            }
            else if (args[i] == "--test-list" && scriptPath is null)
            {
                testList = true;
                test = true;  // --test-list implies --test
            }
            else if (args[i].StartsWith("--test-filter=") && scriptPath is null)
            {
                testFilter = args[i]["--test-filter=".Length..];
            }
            else if (scriptPath is null)
            {
                scriptPath = args[i];
                scriptArgStart = i + 1;
            }
            else
            {
                break; // remaining args belong to the script
            }
        }

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

        if (scriptPath is null)
        {
            RunRepl();
            return;
        }

        // Collect the script's arguments (everything after the script path)
        string[] scriptArgs = scriptArgStart >= 0 && scriptArgStart < args.Length
            ? args[scriptArgStart..]
            : Array.Empty<string>();

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
    }

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

        // Stage 3: Interpret
        var interpreter = new Interpreter();
        interpreter.CurrentFile = path;
        interpreter.SetScriptArgs(scriptArgs);
        try
        {
            interpreter.Interpret(statements);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
        }
        finally
        {
            interpreter.CleanupTrackedProcesses();
        }
    }

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

        var interpreter = new Interpreter();
        interpreter.CurrentFile = path;
        interpreter.SetScriptArgs(scriptArgs);

        var debugger = new CliDebugger();
        interpreter.Debugger = debugger;
        debugger.SetCallStack(interpreter.CallStack);
        debugger.SetInterpreter(interpreter);
        debugger.Initialize();

        try
        {
            interpreter.Interpret(statements);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
        }
        finally
        {
            interpreter.CleanupTrackedProcesses();
        }

        Console.WriteLine("Script execution completed.");
    }

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

        var interpreter = new Interpreter();
        interpreter.CurrentFile = path;
        interpreter.SetScriptArgs(scriptArgs);

        // Attach both debugger and test harness
        var debugger = new CliDebugger();
        interpreter.Debugger = debugger;
        debugger.SetCallStack(interpreter.CallStack);
        debugger.SetInterpreter(interpreter);
        debugger.Initialize();

        var reporter = new TapReporter();
        interpreter.TestHarness = reporter;

        if (testFilter is not null)
        {
            interpreter.TestFilter = testFilter.Split(';', StringSplitOptions.RemoveEmptyEntries);
        }
        if (testList)
        {
            interpreter.DiscoveryMode = true;
        }

        try
        {
            interpreter.Interpret(statements);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
            return;
        }
        finally
        {
            interpreter.CleanupTrackedProcesses();
        }

        // Emit TAP plan and exit with appropriate code
        reporter.OnRunComplete(reporter.Passed, reporter.Failed, reporter.Skipped);
        Console.WriteLine("Script execution completed.");

        if (reporter.Failed > 0)
        {
            System.Environment.Exit(1);
        }
    }

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

        // Stage 3: Interpret with test harness
        var interpreter = new Interpreter();
        interpreter.CurrentFile = path;
        interpreter.SetScriptArgs(scriptArgs);

        var reporter = new TapReporter();
        interpreter.TestHarness = reporter;

        if (testFilter is not null)
        {
            interpreter.TestFilter = testFilter.Split(';', StringSplitOptions.RemoveEmptyEntries);
        }
        if (testList)
        {
            interpreter.DiscoveryMode = true;
        }

        try
        {
            interpreter.Interpret(statements);
        }
        catch (RuntimeError ex)
        {
            PrintRuntimeError(ex);
            System.Environment.Exit(70);
            return;
        }
        finally
        {
            interpreter.CleanupTrackedProcesses();
        }

        // Emit TAP plan and exit with appropriate code
        reporter.OnRunComplete(reporter.Passed, reporter.Failed, reporter.Skipped);

        if (reporter.Failed > 0)
        {
            System.Environment.Exit(1);
        }
    }

    private static void RunRepl()
    {
        Console.WriteLine("Stash v0.5 — Type statements or expressions, or 'exit' to quit.");

        // A single Interpreter instance is reused across all REPL iterations.
        // The interpreter holds a variable environment that persists across lines,
        // so reusing the instance is essential.
        var interpreter = new Interpreter();
        var editor = new LineEditor();

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
                // If it contains a semicolon, brace, or starts with a keyword that begins
                // a statement/declaration, parse as a program; otherwise parse as expression.
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
                        interpreter.Interpret(statements);
                    }
                    catch (RuntimeError ex)
                    {
                        PrintRuntimeError(ex);
                    }
                }
                else
                {
                    // --- Expression mode (backward-compatible) ---
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
                        object? result = interpreter.Interpret(expr);
                        if (result is not null)
                        {
                            Console.WriteLine(interpreter.Stringify(result));
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
            interpreter.CleanupTrackedProcesses();
        }
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
