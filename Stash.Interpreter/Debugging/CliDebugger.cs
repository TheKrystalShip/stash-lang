namespace Stash.Debugging;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Interpreting;
using StashEnvironment = Stash.Interpreting.Environment;
using Stash.Runtime;
using Stash.Interpreting.Types;
using Stash.Runtime.Types;

/// <summary>
/// An interactive command-line debugger for Stash scripts that implements
/// <see cref="IDebugger"/> using a text-based REPL.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CliDebugger"/> provides a GDB-style debugging experience directly in
/// the terminal. When attached to the <see cref="Interpreter"/>, execution pauses at
/// configured <see cref="Breakpoint">breakpoints</see> or after each step command,
/// and the user is dropped into an interactive <c>debug&gt;</c> prompt.
/// </para>
/// <para>
/// <strong>Typical workflow:</strong>
/// <list type="number">
///   <item><description>
///     Call <see cref="Initialize"/> before starting interpretation to let the user
///     set initial breakpoints and configure the session.
///   </description></item>
///   <item><description>
///     Attach the instance to the <see cref="Interpreter"/> and call
///     <see cref="SetInterpreter"/> so that expression evaluation works in
///     conditional breakpoints and the <c>eval</c> command.
///   </description></item>
///   <item><description>
///     At each pause the user can inspect variables (<c>print</c>, <c>locals</c>,
///     <c>scopes</c>), navigate the call stack (<c>stack</c>), manage breakpoints
///     (<c>break</c>, <c>clear</c>, <c>breakpoints</c>), evaluate arbitrary
///     expressions (<c>eval</c>), or resume execution (<c>continue</c>,
///     <c>step</c>, <c>next</c>, <c>out</c>).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// For a DAP-based debugger that integrates with VS Code, see the
/// <c>DebugSession</c> class in the <c>Stash.Dap</c> project.
/// </para>
/// </remarks>
public class CliDebugger : IDebugger
{
    /// <summary>The ordered list of all user-defined <see cref="Breakpoint">breakpoints</see>.</summary>
    private readonly List<Breakpoint> _breakpoints = new();
    /// <summary>
    /// When <see langword="true"/>, pause at the very next statement regardless of
    /// depth (step-into mode). Cleared after the next pause.
    /// </summary>
    private bool _stepInto;
    /// <summary>
    /// When <see langword="true"/>, pause at the next statement whose call depth is
    /// less than or equal to <see cref="_pausedDepth"/> (step-over mode).
    /// Cleared after the next pause.
    /// </summary>
    private bool _stepOver;
    /// <summary>
    /// When <see langword="true"/>, pause when the call depth drops below
    /// <see cref="_pausedDepth"/> (step-out mode). Cleared after the next pause.
    /// </summary>
    private bool _stepOut;
    /// <summary>
    /// The call depth recorded at the last pause, used as the reference depth for
    /// step-over and step-out decisions.
    /// </summary>
    private int _pausedDepth;
    /// <summary>
    /// The current call-stack depth, incremented on
    /// <see cref="OnFunctionEnter"/> and decremented on <see cref="OnFunctionExit"/>.
    /// </summary>
    private int _currentDepth;
    /// <summary>
    /// The <see cref="SourceSpan"/> of the statement that was most recently presented
    /// to <see cref="OnBeforeExecute"/>.
    /// </summary>
    private SourceSpan? _currentSpan;
    /// <summary>
    /// The <see cref="IDebugScope"/> (scope chain)
    /// active at the most recently executed statement.
    /// </summary>
    private IDebugScope? _currentEnv;
    /// <summary>
    /// The live call stack, kept current by the <see cref="Interpreter"/> via
    /// <see cref="SetCallStack"/>.
    /// </summary>
    private IReadOnlyList<CallFrame>? _callStack;
    /// <summary>
    /// Reference to the running <see cref="Interpreter"/>, used to evaluate Stash
    /// expressions for conditional breakpoints and the <c>eval</c> command.
    /// Set via <see cref="SetInterpreter"/>.
    /// </summary>
    private Interpreter? _interpreter;

    /// <summary>
    /// Prints a welcome banner and enters the initial <c>debug&gt;</c> prompt so the
    /// user can configure breakpoints before execution begins.
    /// </summary>
    /// <remarks>
    /// Call this method once, before starting interpretation, so that the user has a
    /// chance to set breakpoints via the <c>break</c> command and then issue
    /// <c>run</c> to start the script.
    /// </remarks>
    public void Initialize()
    {
        Console.WriteLine("Stash Debugger — Type 'help' for commands, 'run' to start execution.");
        Prompt();
    }

    /// <inheritdoc />
    public void OnBeforeExecute(SourceSpan span, IDebugScope env, int threadId)
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
                if (bp.Condition is not null && _interpreter is not null
                    && _currentEnv is StashEnvironment condEnv)
                {
                    var (value, error) = _interpreter.EvaluateString(bp.Condition, condEnv);
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
    public void OnFunctionEnter(string name, SourceSpan callSite, IDebugScope env, int threadId)
    {
        _currentDepth++;
    }

    /// <inheritdoc />
    public void OnFunctionExit(string name, int threadId)
    {
        _currentDepth--;
    }

    /// <inheritdoc />
    public void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack, int threadId)
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
    /// Sets the call-stack reference that the debugger uses when the user issues the
    /// <c>stack</c> command or when <see cref="OnError"/> fires.
    /// </summary>
    /// <remarks>
    /// The <see cref="Interpreter"/> should call this method each time it updates the
    /// call stack so that the <see cref="CliDebugger"/> always has an up-to-date view.
    /// </remarks>
    /// <param name="callStack">
    /// The current ordered list of <see cref="CallFrame"/> objects, index 0 being the
    /// outermost script frame.
    /// </param>
    public void SetCallStack(IReadOnlyList<CallFrame> callStack)
    {
        _callStack = callStack;
    }

    /// <summary>
    /// Sets the <see cref="Interpreter"/> reference used to evaluate Stash expressions
    /// for conditional breakpoints and the <c>eval</c> command.
    /// </summary>
    /// <remarks>
    /// Must be called before execution starts. Without this reference, the
    /// <c>eval</c> command and condition evaluation in <see cref="Breakpoint.Condition"/>
    /// will be unavailable.
    /// </remarks>
    /// <param name="interpreter">The <see cref="Interpreter"/> instance to associate with this debugger.</param>
    public void SetInterpreter(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    /// <summary>
    /// Runs the interactive debugger REPL, reading and dispatching user commands until
    /// one of the resume commands (<c>run</c>, <c>step</c>, <c>next</c>, <c>out</c>)
    /// returns control to the interpreter.
    /// </summary>
    /// <remarks>
    /// Reads lines from <see cref="Console.In"/> and dispatches to the appropriate
    /// handler method. On EOF (e.g., non-interactive stdin), the loop exits and
    /// execution continues uninterrupted.
    /// </remarks>
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

    /// <summary>
    /// Handles the <c>break</c> command: sets a <see cref="Breakpoint"/> at the given
    /// location with an optional <c>if &lt;condition&gt;</c> guard.
    /// </summary>
    /// <remarks>
    /// Accepted forms:
    /// <list type="bullet">
    ///   <item><description><c>break</c> — set at the current source location.</description></item>
    ///   <item><description><c>break &lt;line&gt;</c> — set at the given line in the current file.</description></item>
    ///   <item><description><c>break &lt;file&gt;:&lt;line&gt;</c> — set at an explicit file and line.</description></item>
    ///   <item><description><c>break &lt;loc&gt; if &lt;expr&gt;</c> — set a conditional breakpoint.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="parts">The whitespace-split tokens of the user's input line.</param>
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

    /// <summary>
    /// Handles the <c>clear</c> command: removes one or all <see cref="Breakpoint">breakpoints</see>
    /// identified by numeric ID, source location, or the absence of any argument.
    /// </summary>
    /// <remarks>
    /// Accepted forms:
    /// <list type="bullet">
    ///   <item><description><c>clear</c> — removes all breakpoints.</description></item>
    ///   <item><description><c>clear &lt;id&gt;</c> — removes the breakpoint with that numeric ID.</description></item>
    ///   <item><description><c>clear &lt;line&gt;</c> — removes the breakpoint at that line in the current file.</description></item>
    ///   <item><description><c>clear &lt;file&gt;:&lt;line&gt;</c> — removes the breakpoint at an explicit location.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="parts">The whitespace-split tokens of the user's input line.</param>
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

    /// <summary>
    /// Handles the <c>print</c> command: looks up a variable by name in the current
    /// scope chain and prints its value to the console.
    /// </summary>
    /// <param name="parts">
    /// The whitespace-split tokens of the user's input line.
    /// <c>parts[1]</c> must be the variable name to inspect.
    /// </param>
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

        var currentEnv = _currentEnv as StashEnvironment;
        object? value = null;
        if (currentEnv != null && (currentEnv.TryGet(varName, out value) || TryGetFromChain(varName, out value)))
        {
            Console.WriteLine($"  {varName} = {FormatValue(value)}");
        }
        else
        {
            // Fall back to walking the scope chain via Get
            try
            {
                if (currentEnv == null)
                {
                    Console.WriteLine($"  Undefined variable '{varName}'.");
                    return;
                }
                value = currentEnv.Get(varName);
                Console.WriteLine($"  {varName} = {FormatValue(value)}");
            }
            catch (RuntimeError)
            {
                Console.WriteLine($"  Undefined variable '{varName}'.");
            }
        }
    }

    /// <summary>
    /// Walks the entire scope chain of <see cref="_currentEnv"/> looking for a binding
    /// with the given name.
    /// </summary>
    /// <param name="name">The variable name to look up.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, contains the bound value;
    /// otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a binding for <paramref name="name"/> was found in any
    /// scope; <see langword="false"/> otherwise.
    /// </returns>
    private bool TryGetFromChain(string name, out object? value)
    {
        foreach (var scope in _currentEnv!.GetScopeChain())
        {
            if (scope is StashEnvironment env && env.TryGet(name, out value))
            {
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Handles the <c>eval</c> command: parses and evaluates an arbitrary Stash
    /// expression in the current scope and prints the result.
    /// </summary>
    /// <remarks>
    /// Requires both <see cref="_interpreter"/> and <see cref="_currentEnv"/> to be
    /// non-<see langword="null"/>. The full input line is passed in addition to the
    /// split <paramref name="parts"/> so that multi-token expressions are preserved.
    /// </remarks>
    /// <param name="parts">The whitespace-split tokens of the user's input line (used for length check).</param>
    /// <param name="fullInput">The raw input line, used to extract the expression after the command keyword.</param>
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
        if (_currentEnv is not StashEnvironment evalEnv)
        {
            Console.WriteLine("  Expression evaluation unavailable in this scope.");
            return;
        }
        var (result, error) = _interpreter.EvaluateString(expression, evalEnv);

        if (error is not null)
        {
            Console.WriteLine($"  Error: {error}");
        }
        else
        {
            Console.WriteLine($"  = {FormatValue(result)}");
        }
    }

    /// <summary>
    /// Handles the <c>scopes</c> command: walks the full scope chain and prints every
    /// scope tier along with all of its variable bindings.
    /// </summary>
    /// <remarks>
    /// Each tier is labelled as <c>[Global]</c>, <c>[Local]</c>, or <c>[Closure]</c>
    /// based on its position in the chain. Constant bindings are annotated with
    /// <c>(const)</c>. Values are formatted via <see cref="FormatValue"/>.
    /// </remarks>
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
            string kind = scope.EnclosingScope is null ? "Global" : (depth == 0 ? "Local" : "Closure");
            var bindings = scope.GetAllBindings().ToList();
            Console.WriteLine($"  [{kind}] ({bindings.Count} variables)");
            foreach (var (name, value) in bindings)
            {
                string constMark = (scope is StashEnvironment envScope && envScope.IsConstant(name)) ? " (const)" : "";
                Console.WriteLine($"    {name}{constMark} = {FormatValue(value)}");
            }
            depth++;
        }
    }

    /// <summary>
    /// Handles the <c>locals</c> command: prints all variable bindings in the
    /// innermost (current) scope.
    /// </summary>
    /// <remarks>
    /// Only the top-level environment frame is inspected; enclosing closure and global
    /// scopes are not shown. Use <c>scopes</c> to see the full scope chain.
    /// </remarks>
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
            string constMark = (_currentEnv is StashEnvironment envScope && envScope.IsConstant(name)) ? " (const)" : "";
            Console.WriteLine($"  {name}{constMark} = {FormatValue(value)}");
        }
    }

    /// <summary>
    /// Formats a Stash runtime value as a human-readable string for display in the
    /// debugger REPL.
    /// </summary>
    /// <remarks>
    /// Handles all built-in Stash value types: <see langword="null"/>, <see cref="bool"/>,
    /// <see cref="string"/>, <see cref="long"/>, <see cref="double"/>,
    /// <see cref="List{T}">List&lt;object?&gt;</see>, <c>StashInstance</c>,
    /// <c>StashEnumValue</c>, <c>UserCallable</c>,
    /// <c>BuiltInFunction</c>, and <c>StashNamespace</c>. Unknown types fall back to
    /// <see cref="object.ToString"/>.
    /// </remarks>
    /// <param name="value">The Stash runtime value to format. May be <see langword="null"/>.</param>
    /// <returns>A human-readable string representation of <paramref name="value"/>.</returns>
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
        if (value is UserCallable uc)
        {
            return uc.ToString()!;
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

    /// <summary>
    /// Prints the call stack to the console in reverse order (most recent frame first),
    /// mirroring the display of common debuggers and stack-trace formatters.
    /// </summary>
    /// <param name="callStack">
    /// The ordered list of <see cref="CallFrame"/> objects to display, index 0 being
    /// the outermost script frame.
    /// </param>
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

    /// <summary>
    /// Prints the full list of available debugger commands and their aliases to the
    /// console.
    /// </summary>
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
