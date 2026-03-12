namespace Stash.Debugging;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using StashEnv = Stash.Interpreting.Environment;
using Stash.Interpreting;

/// <summary>
/// An interactive command-line debugger for Stash scripts.
/// Supports breakpoints, stepping, variable inspection, and stack traces.
/// </summary>
public class CliDebugger : IDebugger
{
    private readonly HashSet<(string File, int Line)> _breakpoints = new();
    private bool _stepInto;
    private bool _stepOver;
    private bool _stepOut;
    private int _pausedDepth;
    private int _currentDepth;
    private SourceSpan? _currentSpan;
    private StashEnv? _currentEnv;
    private IReadOnlyList<CallFrame>? _callStack;

    /// <summary>
    /// Prompts the user for initial breakpoints before starting execution.
    /// </summary>
    public void Initialize()
    {
        Console.WriteLine("Stash Debugger — Type 'help' for commands, 'run' to start execution.");
        Prompt();
    }

    public void OnBeforeExecute(SourceSpan span, StashEnv env)
    {
        _currentSpan = span;
        _currentEnv = env;
        bool shouldPause = false;

        // Check breakpoints
        if (_breakpoints.Contains((span.File, span.StartLine)))
        {
            shouldPause = true;
        }

        // Check step modes
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

        if (shouldPause)
        {
            _stepInto = false;
            _stepOver = false;
            _stepOut = false;
            Console.WriteLine($"Paused at {span.File}:{span.StartLine}:{span.StartColumn}");
            Prompt();
        }
    }

    public void OnFunctionEnter(string name, SourceSpan callSite, StashEnv env)
    {
        _currentDepth++;
    }

    public void OnFunctionExit(string name)
    {
        _currentDepth--;
    }

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
                        foreach (var (file, line) in _breakpoints.OrderBy(bp => bp.File).ThenBy(bp => bp.Line))
                        {
                            Console.WriteLine($"  {file}:{line}");
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

    private void HandleBreakpoint(string[] parts)
    {
        if (parts.Length < 2)
        {
            // Set breakpoint at current location
            if (_currentSpan is not null)
            {
                _breakpoints.Add((_currentSpan.File, _currentSpan.StartLine));
                Console.WriteLine($"  Breakpoint set at {_currentSpan.File}:{_currentSpan.StartLine}");
            }
            else
            {
                Console.WriteLine("  Usage: break <file>:<line> or break <line>");
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
                _breakpoints.Add((file, line));
                Console.WriteLine($"  Breakpoint set at {file}:{line}");
            }
            else
            {
                Console.WriteLine("  Invalid line number.");
            }
        }
        else if (int.TryParse(location, out int line))
        {
            // Use current file
            string file = _currentSpan?.File ?? "<stdin>";
            _breakpoints.Add((file, line));
            Console.WriteLine($"  Breakpoint set at {file}:{line}");
        }
        else
        {
            Console.WriteLine("  Usage: break <file>:<line> or break <line>");
        }
    }

    private void HandleClearBreakpoint(string[] parts)
    {
        if (parts.Length < 2)
        {
            _breakpoints.Clear();
            Console.WriteLine("  All breakpoints cleared.");
            return;
        }

        string location = parts[1];
        if (location.Contains(':'))
        {
            int colonIdx = location.LastIndexOf(':');
            string file = location.Substring(0, colonIdx);
            if (int.TryParse(location.Substring(colonIdx + 1), out int line))
            {
                if (_breakpoints.Remove((file, line)))
                {
                    Console.WriteLine($"  Breakpoint removed at {file}:{line}");
                }
                else
                {
                    Console.WriteLine($"  No breakpoint at {file}:{line}");
                }
            }
        }
        else if (int.TryParse(location, out int line))
        {
            string file = _currentSpan?.File ?? "<stdin>";
            if (_breakpoints.Remove((file, line)))
            {
                Console.WriteLine($"  Breakpoint removed at {file}:{line}");
            }
            else
            {
                Console.WriteLine($"  No breakpoint at {file}:{line}");
            }
        }
    }

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

        try
        {
            object? value = _currentEnv.Get(varName);
            Console.WriteLine($"  {varName} = {FormatValue(value)}");
        }
        catch (RuntimeError)
        {
            Console.WriteLine($"  Undefined variable '{varName}'.");
        }
    }

    private static string FormatValue(object? value)
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
        return value.ToString() ?? "null";
    }

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
            Console.WriteLine($"  at {frame.FunctionName}() in {frame.CallSite.File}:{frame.CallSite.StartLine}:{frame.CallSite.StartColumn}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Debugger commands:");
        Console.WriteLine("  run, continue, c  — Continue execution");
        Console.WriteLine("  step, s           — Step into (pause at next statement)");
        Console.WriteLine("  next, n           — Step over (pause at next statement at same depth)");
        Console.WriteLine("  out, o            — Step out (pause when returning to caller)");
        Console.WriteLine("  break, b <line>   — Set breakpoint at line (current file)");
        Console.WriteLine("  break <file>:<line> — Set breakpoint at file:line");
        Console.WriteLine("  clear [location]  — Clear breakpoint (or all breakpoints)");
        Console.WriteLine("  print, p <var>    — Print variable value");
        Console.WriteLine("  stack, bt         — Print call stack");
        Console.WriteLine("  breakpoints, bl   — List all breakpoints");
        Console.WriteLine("  help, h           — Show this help");
        Console.WriteLine("  quit, q           — Quit debugger");
    }
}
