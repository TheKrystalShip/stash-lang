namespace Stash.Debugging;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using StashEnv = Stash.Interpreting.Environment;
using Stash.Interpreting;
using Stash.Interpreting.Types;

/// <summary>
/// An interactive command-line debugger for Stash scripts.
/// Supports breakpoints (with conditions), stepping, variable inspection,
/// scope chain viewing, and stack traces.
/// </summary>
public class CliDebugger : IDebugger
{
    /// <summary>The list of user-defined breakpoints.</summary>
    private readonly List<Breakpoint> _breakpoints = new();
    /// <summary>When true, pause at the very next statement (step-into mode).</summary>
    private bool _stepInto;
    /// <summary>When true, pause at the next statement at the same or shallower call depth (step-over mode).</summary>
    private bool _stepOver;
    /// <summary>When true, pause when returning to a shallower call depth (step-out mode).</summary>
    private bool _stepOut;
    /// <summary>The call depth at which the debugger last paused, used for step-over and step-out logic.</summary>
    private int _pausedDepth;
    /// <summary>The current call stack depth, incremented on function entry and decremented on exit.</summary>
    private int _currentDepth;
    /// <summary>The source span of the most recently executed statement.</summary>
    private SourceSpan? _currentSpan;
    /// <summary>The environment (scope) at the most recently executed statement.</summary>
    private StashEnv? _currentEnv;
    /// <summary>The current call stack, set by the interpreter via <see cref="SetCallStack"/>.</summary>
    private IReadOnlyList<CallFrame>? _callStack;
    /// <summary>The interpreter instance, used for evaluating expressions in conditional breakpoints and the <c>eval</c> command.</summary>
    private Interpreter? _interpreter;

    /// <summary>
    /// Prompts the user for initial breakpoints before starting execution.
    /// </summary>
    public void Initialize()
    {
        Console.WriteLine("Stash Debugger — Type 'help' for commands, 'run' to start execution.");
        Prompt();
    }

    /// <inheritdoc />
    public void OnBeforeExecute(SourceSpan span, StashEnv env)
    {
        _currentSpan = span;
        _currentEnv = env;
        bool shouldPause = false;
        Breakpoint? hitBreakpoint = null;

        // Check breakpoints
        foreach (var bp in _breakpoints)
        {
            if (bp.File == span.File && bp.Line == span.StartLine)
            {
                bp.IncrementHitCount();

                // Check condition if present
                if (bp.Condition is not null && _interpreter is not null)
                {
                    var (value, error) = _interpreter.EvaluateString(bp.Condition, env);
                    if (error is not null || !RuntimeValues.IsTruthy(value))
                    {
                        continue;
                    }
                }

                if (bp.IsLogpoint)
                {
                    Console.WriteLine($"  [logpoint] {bp.LogMessage}");
                    continue;
                }

                shouldPause = true;
                hitBreakpoint = bp;
                break;
            }
        }

        // Check step modes
        if (!shouldPause)
        {
            if (_stepInto)
            {
                shouldPause = true;
            }
            else if (_stepOver && _currentDepth <= _pausedDepth)
            {
                shouldPause = true;
            }
            else if (_stepOut && _currentDepth < _pausedDepth)
            {
                shouldPause = true;
            }
        }

        if (shouldPause)
        {
            _stepInto = false;
            _stepOver = false;
            _stepOut = false;
            if (hitBreakpoint is not null)
            {
                Console.WriteLine($"Breakpoint {hitBreakpoint.Id} hit at {span.File}:{span.StartLine}:{span.StartColumn} (hit count: {hitBreakpoint.HitCount})");
            }
            else
            {
                Console.WriteLine($"Paused at {span.File}:{span.StartLine}:{span.StartColumn}");
            }
            Prompt();
        }
    }

    /// <inheritdoc />
    public void OnFunctionEnter(string name, SourceSpan callSite, StashEnv env)
    {
        _currentDepth++;
    }

    /// <inheritdoc />
    public void OnFunctionExit(string name)
    {
        _currentDepth--;
    }

    /// <inheritdoc />
    public void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack)
    {
        _callStack = callStack;
        Console.WriteLine($"Runtime error: {error.Message}");
        if (error.Span is not null)
        {
            Console.WriteLine($"  at {error.Span.File}:{error.Span.StartLine}:{error.Span.StartColumn}");
        }
        PrintCallStack(callStack);
    }

    /// <summary>
    /// Sets the call stack reference for use in debugger commands.
    /// Called by the interpreter to keep the debugger's view of the call stack current.
    /// </summary>
    public void SetCallStack(IReadOnlyList<CallFrame> callStack)
    {
        _callStack = callStack;
    }

    /// <summary>
    /// Sets the interpreter reference for expression evaluation in conditional breakpoints.
    /// </summary>
    public void SetInterpreter(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    /// <summary>Runs the interactive debugger REPL loop, reading and executing user commands until execution resumes.</summary>
    private void Prompt()
    {
        while (true)
        {
            Console.Write("debug> ");
            string? input = Console.ReadLine();

            if (input is null)
            {
                // EOF — continue execution
                return;
            }

            input = input.Trim();
            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();

            switch (command)
            {
                case "run" or "r" or "continue" or "c":
                    return;

                case "step" or "s":
                    _stepInto = true;
                    _pausedDepth = _currentDepth;
                    return;

                case "next" or "n":
                    _stepOver = true;
                    _pausedDepth = _currentDepth;
                    return;

                case "out" or "o":
                    _stepOut = true;
                    _pausedDepth = _currentDepth;
                    return;

                case "break" or "b":
                    HandleBreakpoint(parts);
                    break;

                case "clear":
                    HandleClearBreakpoint(parts);
                    break;

                case "print" or "p":
                    HandlePrint(parts);
                    break;

                case "eval" or "e":
                    HandleEval(parts, input);
                    break;

                case "scopes":
                    HandleScopes();
                    break;

                case "locals":
                    HandleLocals();
                    break;

                case "stack" or "bt":
                    if (_callStack is not null)
                    {
                        PrintCallStack(_callStack);
                    }
                    else
                    {
                        Console.WriteLine("  (no call stack available)");
                    }
                    break;

                case "breakpoints" or "bl":
                    if (_breakpoints.Count == 0)
                    {
                        Console.WriteLine("  No breakpoints set.");
                    }
                    else
                    {
                        foreach (var bp in _breakpoints.OrderBy(bp => bp.File).ThenBy(bp => bp.Line))
                        {
                            string info = $"  [{bp.Id}] {bp.File}:{bp.Line}";
                            if (bp.Condition is not null)
                            {
                                info += $" (when {bp.Condition})";
                            }

                            if (bp.IsLogpoint)
                            {
                                info += $" (log: {bp.LogMessage})";
                            }

                            info += $" hits={bp.HitCount}";
                            Console.WriteLine(info);
                        }
                    }
                    break;

                case "help" or "h":
                    PrintHelp();
                    break;

                case "quit" or "q":
                    System.Environment.Exit(0);
                    break;

                default:
                    Console.WriteLine($"Unknown command: '{command}'. Type 'help' for available commands.");
                    break;
            }
        }
    }

    /// <summary>Handles the <c>break</c> command: sets a breakpoint at the specified location with an optional condition.</summary>
    private void HandleBreakpoint(string[] parts)
    {
        if (parts.Length < 2)
        {
            // Set breakpoint at current location
            if (_currentSpan is not null)
            {
                var bp = new Breakpoint(_currentSpan.File, _currentSpan.StartLine);
                _breakpoints.Add(bp);
                Console.WriteLine($"  Breakpoint {bp.Id} set at {_currentSpan.File}:{_currentSpan.StartLine}");
            }
            else
            {
                Console.WriteLine("  Usage: break <file>:<line> or break <line> [if <condition>]");
            }
            return;
        }

        // Parse optional condition: break <location> if <condition>
        string? condition = null;
        int locationEndIdx = parts.Length;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Equals("if", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
            {
                condition = string.Join(' ', parts.Skip(i + 1));
                locationEndIdx = i;
                break;
            }
        }

        string location = parts[1];
        Breakpoint? newBp = null;

        if (location.Contains(':'))
        {
            int colonIdx = location.LastIndexOf(':');
            string file = location.Substring(0, colonIdx);
            if (int.TryParse(location.Substring(colonIdx + 1), out int line))
            {
                newBp = new Breakpoint(file, line);
            }
            else
            {
                Console.WriteLine("  Invalid line number.");
                return;
            }
        }
        else if (int.TryParse(location, out int line))
        {
            string file = _currentSpan?.File ?? "<stdin>";
            newBp = new Breakpoint(file, line);
        }
        else
        {
            Console.WriteLine("  Usage: break <file>:<line> or break <line> [if <condition>]");
            return;
        }

        if (newBp is not null)
        {
            newBp.Condition = condition;
            _breakpoints.Add(newBp);
            string msg = $"  Breakpoint {newBp.Id} set at {newBp.File}:{newBp.Line}";
            if (condition is not null)
            {
                msg += $" (when {condition})";
            }

            Console.WriteLine(msg);
        }
    }

    /// <summary>Handles the <c>clear</c> command: removes breakpoints by ID, location, or clears all.</summary>
    private void HandleClearBreakpoint(string[] parts)
    {
        if (parts.Length < 2)
        {
            _breakpoints.Clear();
            Console.WriteLine("  All breakpoints cleared.");
            return;
        }

        // Try clearing by breakpoint ID first
        if (int.TryParse(parts[1], out int idOrLine))
        {
            // Check if it's a breakpoint ID
            var byId = _breakpoints.FindIndex(bp => bp.Id == idOrLine);
            if (byId >= 0)
            {
                var bp = _breakpoints[byId];
                _breakpoints.RemoveAt(byId);
                Console.WriteLine($"  Breakpoint {bp.Id} removed at {bp.File}:{bp.Line}");
                return;
            }

            // Otherwise treat as line number in current file
            string file = _currentSpan?.File ?? "<stdin>";
            var byLine = _breakpoints.FindIndex(bp => bp.File == file && bp.Line == idOrLine);
            if (byLine >= 0)
            {
                var bp = _breakpoints[byLine];
                _breakpoints.RemoveAt(byLine);
                Console.WriteLine($"  Breakpoint {bp.Id} removed at {bp.File}:{bp.Line}");
            }
            else
            {
                Console.WriteLine($"  No breakpoint at {file}:{idOrLine}");
            }
            return;
        }

        string location = parts[1];
        if (location.Contains(':'))
        {
            int colonIdx = location.LastIndexOf(':');
            string file = location.Substring(0, colonIdx);
            if (int.TryParse(location.Substring(colonIdx + 1), out int line))
            {
                var idx = _breakpoints.FindIndex(bp => bp.File == file && bp.Line == line);
                if (idx >= 0)
                {
                    var bp = _breakpoints[idx];
                    _breakpoints.RemoveAt(idx);
                    Console.WriteLine($"  Breakpoint {bp.Id} removed at {bp.File}:{bp.Line}");
                }
                else
                {
                    Console.WriteLine($"  No breakpoint at {file}:{line}");
                }
            }
        }
    }

    /// <summary>Handles the <c>print</c> command: looks up a variable by name and displays its value.</summary>
    private void HandlePrint(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("  Usage: print <variable>");
            return;
        }

        string varName = parts[1];
        if (_currentEnv is null)
        {
            Console.WriteLine("  No environment available.");
            return;
        }

        if (_currentEnv.TryGet(varName, out object? value) || TryGetFromChain(varName, out value))
        {
            Console.WriteLine($"  {varName} = {FormatValue(value)}");
        }
        else
        {
            // Fall back to walking the scope chain via Get
            try
            {
                value = _currentEnv.Get(varName);
                Console.WriteLine($"  {varName} = {FormatValue(value)}");
            }
            catch (RuntimeError)
            {
                Console.WriteLine($"  Undefined variable '{varName}'.");
            }
        }
    }

    /// <summary>Walks the scope chain looking for a variable by name.</summary>
    private bool TryGetFromChain(string name, out object? value)
    {
        foreach (var scope in _currentEnv!.GetScopeChain())
        {
            if (scope.TryGet(name, out value))
            {
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>Handles the <c>eval</c> command: evaluates an arbitrary expression in the current scope.</summary>
    private void HandleEval(string[] parts, string fullInput)
    {
        if (_interpreter is null || _currentEnv is null)
        {
            Console.WriteLine("  No interpreter or environment available.");
            return;
        }

        // Extract expression after "eval " prefix
        int spaceIdx = fullInput.IndexOf(' ');
        if (spaceIdx < 0 || spaceIdx + 1 >= fullInput.Length)
        {
            Console.WriteLine("  Usage: eval <expression>");
            return;
        }

        string expression = fullInput.Substring(spaceIdx + 1).Trim();
        var (result, error) = _interpreter.EvaluateString(expression, _currentEnv);

        if (error is not null)
        {
            Console.WriteLine($"  Error: {error}");
        }
        else
        {
            Console.WriteLine($"  = {FormatValue(result)}");
        }
    }

    /// <summary>Handles the <c>scopes</c> command: displays all scopes in the scope chain with their variables.</summary>
    private void HandleScopes()
    {
        if (_currentEnv is null)
        {
            Console.WriteLine("  No environment available.");
            return;
        }

        int depth = 0;
        foreach (var scope in _currentEnv.GetScopeChain())
        {
            string kind = scope.Enclosing is null ? "Global" : (depth == 0 ? "Local" : "Closure");
            var bindings = scope.GetAllBindings().ToList();
            Console.WriteLine($"  [{kind}] ({bindings.Count} variables)");
            foreach (var (name, value) in bindings)
            {
                string constMark = scope.IsConstant(name) ? " (const)" : "";
                Console.WriteLine($"    {name}{constMark} = {FormatValue(value)}");
            }
            depth++;
        }
    }

    /// <summary>Handles the <c>locals</c> command: displays variables in the current (innermost) scope.</summary>
    private void HandleLocals()
    {
        if (_currentEnv is null)
        {
            Console.WriteLine("  No environment available.");
            return;
        }

        var bindings = _currentEnv.GetAllBindings().ToList();
        if (bindings.Count == 0)
        {
            Console.WriteLine("  (no local variables)");
            return;
        }

        foreach (var (name, value) in bindings)
        {
            string constMark = _currentEnv.IsConstant(name) ? " (const)" : "";
            Console.WriteLine($"  {name}{constMark} = {FormatValue(value)}");
        }
    }

    /// <summary>Formats a Stash runtime value for display in the debugger REPL.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A human-readable string representation.</returns>
    public static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        if (value is string s)
        {
            return $"\"{s}\"";
        }

        if (value is long l)
        {
            return l.ToString();
        }

        if (value is double d)
        {
            return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (value is List<object?> list)
        {
            var elements = list.Select(FormatValue);
            return $"[{string.Join(", ", elements)}]";
        }
        if (value is StashInstance instance)
        {
            var fields = instance.GetFields();
            var fieldStrs = fields.Select(f => $"{f.Key}: {FormatValue(f.Value)}");
            return $"{instance.TypeName} {{ {string.Join(", ", fieldStrs)} }}";
        }
        if (value is StashEnumValue enumVal)
        {
            return enumVal.ToString();
        }
        if (value is StashFunction fn)
        {
            return fn.ToString();
        }
        if (value is StashLambda)
        {
            return "<lambda>";
        }
        if (value is BuiltInFunction bif)
        {
            return bif.ToString();
        }
        if (value is StashNamespace ns)
        {
            return $"<namespace {ns.Name}>";
        }
        return value.ToString() ?? "null";
    }

    /// <summary>Prints the call stack to the console, most recent frame first.</summary>
    private static void PrintCallStack(IReadOnlyList<CallFrame> callStack)
    {
        if (callStack.Count == 0)
        {
            Console.WriteLine("  (empty call stack)");
            return;
        }

        for (int i = callStack.Count - 1; i >= 0; i--)
        {
            CallFrame frame = callStack[i];
            string location = $"{frame.CallSite.File}:{frame.CallSite.StartLine}:{frame.CallSite.StartColumn}";
            Console.WriteLine($"  #{callStack.Count - i} {frame.FunctionName}() at {location}");
        }
    }

    /// <summary>Prints the list of available debugger commands to the console.</summary>
    private static void PrintHelp()
    {
        Console.WriteLine("Debugger commands:");
        Console.WriteLine("  run, continue, c       — Continue execution");
        Console.WriteLine("  step, s                — Step into (pause at next statement)");
        Console.WriteLine("  next, n                — Step over (pause at next statement at same depth)");
        Console.WriteLine("  out, o                 — Step out (pause when returning to caller)");
        Console.WriteLine("  break, b <line>        — Set breakpoint at line (current file)");
        Console.WriteLine("  break <file>:<line>    — Set breakpoint at file:line");
        Console.WriteLine("  break <loc> if <expr>  — Set conditional breakpoint");
        Console.WriteLine("  clear [id|location]    — Clear breakpoint by ID or location (or all)");
        Console.WriteLine("  print, p <var>         — Print variable value");
        Console.WriteLine("  eval, e <expr>         — Evaluate an expression");
        Console.WriteLine("  locals                 — Show local variables");
        Console.WriteLine("  scopes                 — Show all scopes and variables");
        Console.WriteLine("  stack, bt              — Print call stack");
        Console.WriteLine("  breakpoints, bl        — List all breakpoints");
        Console.WriteLine("  help, h                — Show this help");
        Console.WriteLine("  quit, q                — Quit debugger");
    }
}
