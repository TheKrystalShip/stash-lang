// ============================================================================
// Stash REPL — Phase 1 (Expression Evaluator)
//
// A simple Read-Eval-Print Loop that processes one line at a time through
// three pipeline stages:
//
//   1. Lex:       Source text → token list       (Lexer)
//   2. Parse:     Token list  → AST              (Parser)
//   3. Interpret: AST         → runtime value    (Interpreter)
//
// Each stage can fail independently. If lexing produces errors, parsing is
// skipped for that input. If parsing produces errors, interpretation is
// skipped. This keeps the REPL resilient — a bad input line never crashes
// the session.
//
// Results are printed to stdout; lex/parse/runtime errors go to stderr so
// they can be separated in piped/scripted usage.
//
// The loop exits on either the "exit" command or Ctrl+D (EOF), which causes
// Console.ReadLine() to return null.
// ============================================================================

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting;

namespace Stash;

public class Program
{
    public static void Main(string[] args)
    {
        // The REPL loop is implemented at the bottom of this file, after the test classes.
        // The tests are defined here in Program.cs to avoid circular dependencies between
        // Stash and Stash.Tests projects. In a larger codebase, we would likely want to
        // split these into separate files, but for this small project it's simpler to keep
        // them together.

        Console.WriteLine("Stash v0.1 — Phase 1 REPL");
        Console.WriteLine("Type an expression to evaluate, or 'exit' to quit.");

        // A single Interpreter instance is reused across all REPL iterations.
        // In Phase 1 this has no observable effect (the interpreter is stateless),
        // but in Phase 2 and beyond the interpreter will hold a variable environment
        // that persists across lines, so reusing the instance is essential.
        var interpreter = new Interpreter();

        while (true)
        {
            Console.Write("stash> ");
            string? line = Console.ReadLine();

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
            // Convert the raw source line into a list of tokens. If the lexer
            // encounters invalid characters or malformed literals, it records
            // errors and we skip to the next prompt.
            var lexer = new Lexer(line);
            List<Token> tokens = lexer.ScanTokens();

            if (lexer.Errors.Count > 0)
            {
                foreach (string error in lexer.Errors)
                {
                    Console.Error.WriteLine($"[lex error] {error}");
                }

                continue;
            }

            // --- Stage 2: Parse ---
            // Build an expression AST from the token stream. Parse errors
            // (e.g., mismatched parentheses, unexpected tokens) are collected
            // and reported without throwing.
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

            // --- Stage 3: Interpret ---
            // Walk the AST and compute a runtime value. RuntimeError exceptions
            // are caught and displayed with source-location info when available.
            try
            {
                object? result = interpreter.Interpret(expr);
                Console.WriteLine(interpreter.Stringify(result));
            }
            catch (RuntimeError ex)
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

    }
}
