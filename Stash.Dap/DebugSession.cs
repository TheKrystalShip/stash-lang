namespace Stash.Dap;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Stash.Common;
using Stash.Debugging;
using Stash.Interpreting;
using Stash.Interpreting.Types;
using Stash.Lexing;
using Stash.Parsing;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Server;
using DapBreakpoint = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Breakpoint;
using StashBreakpoint = Stash.Debugging.Breakpoint;
using StashEnv = Stash.Interpreting.Environment;

/// <summary>
/// Core bridge between the DAP protocol and the Stash interpreter.
/// Implements <see cref="IDebugger"/> so the interpreter calls into this class
/// at every statement and function entry/exit. DAP handlers call into this class
/// to control execution flow, query the call stack, and inspect variables.
/// </summary>
public class DebugSession : IDebugger
{
    private const int ThreadId = 1; // Single-threaded interpreter

    // ── Core references ───────────────────────────────────────────────────────

    private IDebugAdapterServer? _server;
    private Interpreter? _interpreter;

    // ── Pause/resume synchronization ─────────────────────────────────────────

    /// <summary>
    /// Gate starts signaled (true) so the interpreter runs freely.
    /// Reset to block the interpreter thread; Set to unblock it.
    /// </summary>
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
    private volatile bool _isPaused;
    private volatile bool _pauseRequested;
    private PauseReason _pauseReason;
    private SourceSpan? _pausedAtSpan;
    private StashEnv? _pausedEnvironment;

    // ── Stepping state ────────────────────────────────────────────────────────

    private enum StepMode { None, StepIn, StepOver, StepOut }

    private volatile StepMode _stepMode = StepMode.None;
    private int _stepDepth; // call stack depth when step was initiated

    // ── Breakpoints ───────────────────────────────────────────────────────────

    // All breakpoints for a file, keyed by normalized file path
    private readonly ConcurrentDictionary<string, List<StashBreakpoint>> _breakpoints = new();

    // Function breakpoints, keyed by function name
    private readonly Dictionary<string, FunctionBreakpointEntry> _functionBreakpoints = new();

    // ── Variable references ───────────────────────────────────────────────────

    // DAP uses integer IDs to reference variable containers (scopes, arrays, dicts, etc.)
    private long _nextVariableReference = 1;
    private readonly Dictionary<long, VariableContainer> _variableReferences = new();

    // ── Loaded sources ────────────────────────────────────────────────────────

    private readonly HashSet<string> _loadedSources = new();
    private readonly object _loadedSourcesLock = new();

    // ── Session state ─────────────────────────────────────────────────────────

    private bool _stopOnEntry;
    private volatile bool _breakOnAllExceptions;

    private volatile bool _terminated;

    private string? _scriptPath;
    private string? _workingDirectory;

    // Blocks the interpreter thread until the client sends configurationDone,
    // ensuring breakpoints are set before execution starts.
    private readonly ManualResetEventSlim _configurationDone = new(initialState: false);

    // Explicit alias to avoid ambiguity with OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread
    private System.Threading.Thread? _interpreterThread;

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class FunctionBreakpointEntry
    {
        public required string Name { get; init; }
        public string? Condition { get; init; }
        public string? HitCondition { get; init; }
        public int HitCount { get; set; }
        public int Id { get; init; }
    }

    private static int _nextFunctionBpId;

    private sealed class VariableContainer
    {
        /// <summary>When set, the container holds an Environment scope to enumerate.</summary>
        public StashEnv? Environment { get; init; }

        /// <summary>When set, the container holds a complex value to expand (array/dict/instance).</summary>
        public object? Value { get; init; }

        public string Name { get; init; } = "";

        /// <summary>When true, only show built-in bindings (BuiltInFunction, StashNamespace).</summary>
        public bool BuiltInsOnly { get; init; }

        /// <summary>When true, exclude built-in bindings from this scope.</summary>
        public bool ExcludeBuiltIns { get; init; }
    }

    /// <summary>
    /// TextWriter that forwards writes to DAP output events.
    /// Used to redirect interpreter stdout/stderr through the debug adapter.
    /// </summary>
    private sealed class DapOutputWriter : TextWriter
    {
        private readonly DebugSession _session;
        private readonly string _category;

        public DapOutputWriter(DebugSession session, string category)
        {
            _session = session;
            _category = category;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value)
        {
            if (value != null)
            {
                _session.SendOutput(_category, value);
            }
        }

        public override void WriteLine(string? value)
        {
            _session.SendOutput(_category, (value ?? "") + "\n");
        }
    }

    // ── IDebugger properties ──────────────────────────────────────────────────

    public bool StopOnEntry => _stopOnEntry;
    public bool IsPauseRequested => _pauseRequested;

    // ── Diagnostic logging ────────────────────────────────────────────────────

    private static readonly string _logFile = Path.Combine(
        System.Environment.GetEnvironmentVariable("HOME") ?? "/tmp",
        ".stash-dap.log");

    private static void Trace(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.Error.WriteLine($"[stash-dap] {message}");
        Console.Error.Flush();
        try { File.AppendAllText(_logFile, line + "\n"); } catch { }
    }

    public static void TraceStatic(string message) => Trace(message);

    // ── Public API called by DAP handlers ─────────────────────────────────────

    /// <summary>Stores the server reference. Must be called before Launch.</summary>
    public void SetServer(IDebugAdapterServer server)
    {
        _server = server;
        Trace("Server reference set");
    }

    /// <summary>
    /// Starts a debug session: creates the interpreter, then launches the interpreter
    /// thread which waits for ConfigurationDone before executing the script.
    /// </summary>
    public void Launch(string scriptPath, string? workingDirectory, bool stopOnEntry, string[]? args, bool testMode = false, string? testFilter = null)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Script path is required.", nameof(scriptPath));
        }

        Trace($"Launch: {scriptPath}, stopOnEntry={stopOnEntry}");

        _scriptPath = NormalizePath(scriptPath);
        _workingDirectory = workingDirectory;
        _stopOnEntry = stopOnEntry;

        _interpreter = new Interpreter();
        _interpreter.Debugger = this;
        _interpreter.CurrentFile = _scriptPath;
        if (args is { Length: > 0 })
        {
            _interpreter.SetScriptArgs(args);
        }

        // Redirect interpreter output through DAP
        _interpreter.Output = new DapOutputWriter(this, "stdout");
        _interpreter.ErrorOutput = new DapOutputWriter(this, "stderr");

        // Configure test mode if requested
        if (testMode)
        {
            var reporter = new Stash.Testing.TapReporter(_interpreter.Output);
            _interpreter.TestHarness = reporter;

            if (testFilter is not null)
            {
                _interpreter.TestFilter = testFilter.Split(';', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        _interpreterThread = new System.Threading.Thread(() =>
        {
            // Wait until the client has sent all SetBreakpoints + ConfigurationDone
            _configurationDone.Wait();
            Trace("Interpreter thread: configuration received, starting execution");

            try
            {
                if (!string.IsNullOrEmpty(_workingDirectory))
                {
                    Directory.SetCurrentDirectory(_workingDirectory);
                }

                var source = File.ReadAllText(_scriptPath!);
                var lexer = new Lexer(source, _scriptPath);
                var tokens = lexer.ScanTokens();

                if (lexer.Errors.Any())
                {
                    foreach (var err in lexer.Errors)
                    {
                        SendOutput("stderr", err + "\n");
                    }

                    return;
                }

                var parser = new Parser(tokens);
                var stmts = parser.ParseProgram();

                if (parser.Errors.Any())
                {
                    foreach (var err in parser.Errors)
                    {
                        SendOutput("stderr", err + "\n");
                    }

                    return;
                }

                _interpreter.Interpret(stmts);

                // If running in test mode, emit TAP plan
                if (_interpreter.TestHarness is Stash.Testing.TapReporter tapReporter)
                {
                    tapReporter.OnRunComplete(tapReporter.Passed, tapReporter.Failed, tapReporter.Skipped);
                }
            }
            catch (RuntimeError ex)
            {
                Trace($"Interpreter error: {ex.Message}");
                SendOutput("stderr", ex.Message + "\n");
            }
            catch (OperationCanceledException) { /* Terminated via Disconnect */ }
            catch (ThreadInterruptedException) { /* Terminated via Disconnect */ }
            catch (Exception ex)
            {
                Trace($"Interpreter error: {ex.Message}");
                SendOutput("stderr", ex.Message + "\n");
            }
            finally
            {
                Trace("Interpreter thread: execution complete");
                SendTerminated();
                SendExited(0);
            }
        });

        _interpreterThread.IsBackground = true;
        _interpreterThread.Name = "StashInterpreter";
        _interpreterThread.Start();
    }

    /// <summary>Signals that client configuration is complete and execution can begin.</summary>
    public void ConfigurationDone()
    {
        Trace($"ConfigurationDone: {_breakpoints.Count} source files with breakpoints");
        foreach (var (file, bps) in _breakpoints)
        {
            Trace($"  {file}: lines {string.Join(", ", bps.Select(b => b.Line))}");
        }

        Trace("ConfigurationDone: releasing interpreter thread");
        _configurationDone.Set();
    }

    /// <summary>
    /// Replaces all breakpoints for the given source file.
    /// Returns DAP Breakpoint objects with verification status.
    /// </summary>
    public IReadOnlyList<DapBreakpoint> SetBreakpoints(string path, IEnumerable<SourceBreakpoint> sourceBreakpoints)
    {
        var normalized = NormalizePath(path);
        var stashBps = new List<StashBreakpoint>();
        var dapBps = new List<DapBreakpoint>();
        var bpList = sourceBreakpoints.ToList();
        Trace($"SetBreakpoints: {path} ({bpList.Count} breakpoints)");
        var source = new Source { Path = normalized, Name = Path.GetFileName(normalized) };

        foreach (var sb in bpList)
        {
            var bp = new StashBreakpoint(normalized, sb.Line)
            {
                Condition = sb.Condition,
                HitCondition = sb.HitCondition,
                LogMessage = sb.LogMessage,
                Verified = true,
                ActualLine = sb.Line,
            };
            stashBps.Add(bp);
            dapBps.Add(new DapBreakpoint
            {
                Id = bp.Id,
                Verified = true,
                Line = sb.Line,
                Source = source,
            });
        }

        _breakpoints[normalized] = stashBps;
        return dapBps;
    }

    /// <summary>
    /// Replaces all function breakpoints with the given list.
    /// Returns DAP Breakpoint objects for each function breakpoint.
    /// </summary>
    public IReadOnlyList<DapBreakpoint> SetFunctionBreakpoints(IEnumerable<FunctionBreakpoint> breakpoints)
    {
        var dapBps = new List<DapBreakpoint>();

        lock (_functionBreakpoints)
        {
            _functionBreakpoints.Clear();

            foreach (var fbp in breakpoints)
            {
                var id = Interlocked.Increment(ref _nextFunctionBpId);
                var entry = new FunctionBreakpointEntry
                {
                    Name = fbp.Name,
                    Condition = fbp.Condition,
                    HitCondition = fbp.HitCondition,
                    Id = id,
                };
                _functionBreakpoints[fbp.Name] = entry;

                dapBps.Add(new DapBreakpoint
                {
                    Id = id,
                    Verified = true,
                });
            }
        }

        Trace($"SetFunctionBreakpoints: {dapBps.Count} function breakpoints");
        return dapBps;
    }

    /// <summary>Resumes execution without stepping constraints.</summary>
    public void Continue()
    {
        Trace("Continue");
        _stepMode = StepMode.None;
        Resume();
    }

    /// <summary>Steps over the next statement (does not enter function calls).</summary>
    public void Next()
    {
        _stepMode = StepMode.StepOver;
        _stepDepth = _interpreter?.CallStack.Count ?? 0;
        Resume();
    }

    /// <summary>Steps into the next statement or function call.</summary>
    public void StepIn()
    {
        _stepMode = StepMode.StepIn;
        Resume();
    }

    /// <summary>Runs until the current function returns.</summary>
    public void StepOut()
    {
        var depth = _interpreter?.CallStack.Count ?? 0;
        if (depth == 0)
        {
            // At top-level; treat as continue since there's nothing to step out of
            Continue();
            return;
        }
        _stepMode = StepMode.StepOut;
        _stepDepth = depth;
        Resume();
    }

    /// <summary>Requests the interpreter to pause at the next statement.</summary>
    public void Pause()
    {
        _pauseRequested = true;
    }

    /// <summary>Returns the current call stack as DAP StackFrame objects.</summary>
    public IReadOnlyList<StackFrame> GetStackTrace()
    {
        var frames = new List<StackFrame>();
        if (_interpreter == null)
        {
            return frames;
        }

        var callStack = _interpreter.CallStack;

        if (callStack.Count == 0)
        {
            // Executing at global (top-level) scope — single frame
            frames.Add(MakeScriptFrame(0, _pausedAtSpan));
        }
        else
        {
            // Top frame: current execution point inside the innermost function
            var topFrame = callStack[callStack.Count - 1];
            frames.Add(new StackFrame
            {
                Id = topFrame.Id,
                Name = topFrame.FunctionName,
                Source = MakeSource(_pausedAtSpan?.File),
                Line = _pausedAtSpan?.StartLine ?? 0,
                Column = _pausedAtSpan?.StartColumn ?? 0,
                EndLine = _pausedAtSpan?.EndLine,
                EndColumn = _pausedAtSpan?.EndColumn,
            });

            // Intermediate frames: each shows where the inner function was called from
            for (int i = callStack.Count - 2; i >= 0; i--)
            {
                var frame = callStack[i];
                var callSite = callStack[i + 1].CallSite; // where callStack[i+1] was invoked
                frames.Add(new StackFrame
                {
                    Id = frame.Id,
                    Name = frame.FunctionName,
                    Source = MakeSource(callSite?.File),
                    Line = callSite?.StartLine ?? 0,
                    Column = callSite?.StartColumn ?? 0,
                    EndLine = callSite?.EndLine,
                    EndColumn = callSite?.EndColumn,
                });
            }

            // Synthetic script frame at the bottom: where the outermost function was called from
            var outerCallSite = callStack[0].CallSite;
            frames.Add(MakeScriptFrame(0, outerCallSite));
        }

        return frames;
    }

    /// <summary>Returns DAP Scope objects for the given stack frame ID.</summary>
    public IReadOnlyList<Scope> GetScopes(int frameId)
    {
        var scopes = new List<Scope>();
        if (_interpreter == null)
        {
            return scopes;
        }

        var env = ResolveEnvironmentForFrame(frameId);
        if (env == null)
        {
            return scopes;
        }

        var chain = env.GetScopeChain().ToList();
        for (int i = 0; i < chain.Count; i++)
        {
            bool isGlobalEnv = i == chain.Count - 1;

            var kind = (i == 0 && chain.Count == 1) ? ScopeKind.Local
                     : i == 0 ? ScopeKind.Local
                     : isGlobalEnv ? ScopeKind.Global
                     : ScopeKind.Closure;

            var name = kind switch
            {
                ScopeKind.Local => "Local",
                ScopeKind.Global => "Global",
                _ => "Closure",
            };

            // For the global environment, split into user bindings and built-ins
            if (isGlobalEnv)
            {
                var bindings = chain[i].GetAllBindings().ToList();
                int userCount = 0;
                int builtInCount = 0;
                foreach (var (_, value) in bindings)
                {
                    if (IsBuiltInBinding(value))
                    {
                        builtInCount++;
                    }
                    else
                    {
                        userCount++;
                    }
                }

                // User-defined scope (excludes built-ins)
                if (userCount > 0)
                {
                    long userRef;
                    lock (_variableReferences)
                    {
                        userRef = AllocateVariableReference(new VariableContainer
                        {
                            Environment = chain[i],
                            ExcludeBuiltIns = true,
                        });
                    }
                    scopes.Add(new Scope
                    {
                        Name = name,
                        VariablesReference = userRef,
                        NamedVariables = userCount,
                        Expensive = false,
                    });
                }

                // Standard Library scope (built-ins only, collapsed by default)
                if (builtInCount > 0)
                {
                    long builtInRef;
                    lock (_variableReferences)
                    {
                        builtInRef = AllocateVariableReference(new VariableContainer
                        {
                            Environment = chain[i],
                            BuiltInsOnly = true,
                        });
                    }
                    scopes.Add(new Scope
                    {
                        Name = "Standard Library",
                        VariablesReference = builtInRef,
                        NamedVariables = builtInCount,
                        Expensive = true,
                    });
                }
            }
            else
            {
                long varRef;
                lock (_variableReferences)
                {
                    varRef = AllocateVariableReference(new VariableContainer { Environment = chain[i] });
                }

                var debugScope = new DebugScope(kind, name, chain[i]);
                scopes.Add(new Scope
                {
                    Name = name,
                    VariablesReference = varRef,
                    NamedVariables = debugScope.VariableCount,
                    Expensive = false,
                });
            }
        }

        return scopes;
    }

    /// <summary>
    /// Returns the child variables for a variable reference ID.
    /// Handles environment scopes, arrays, dictionaries, and struct instances.
    /// </summary>
    public IReadOnlyList<Variable> GetVariables(int variableReference)
    {
        VariableContainer? container;
        lock (_variableReferences)
        {
            _variableReferences.TryGetValue(variableReference, out container);
        }

        if (container == null)
        {
            return Array.Empty<Variable>();
        }

        var variables = new List<Variable>();

        if (container.Environment != null)
        {
            foreach (var (varName, value) in container.Environment.GetAllBindings().OrderBy(kv => kv.Key))
            {
                bool isBuiltIn = IsBuiltInBinding(value);
                if (container.BuiltInsOnly && !isBuiltIn)
                {
                    continue;
                }

                if (container.ExcludeBuiltIns && isBuiltIn)
                {
                    continue;
                }

                variables.Add(FormatVariable(varName, value));
            }
        }
        else
        {
            switch (container.Value)
            {
                case List<object?> list:
                    for (int i = 0; i < list.Count; i++)
                    {
                        variables.Add(FormatVariable($"[{i}]", list[i]));
                    }

                    break;

                case StashDictionary dict:
                    foreach (var key in dict.Keys())
                    {
                        variables.Add(FormatVariable(RuntimeValues.Stringify(key), dict.Get(key!)));
                    }

                    break;

                case StashInstance instance:
                    foreach (var (fieldName, fieldValue) in instance.GetFields())
                    {
                        variables.Add(FormatVariable(fieldName, fieldValue));
                    }

                    if (instance.Struct?.Methods is { Count: > 0 } methods)
                    {
                        foreach (var (methodName, method) in methods.OrderBy(kv => kv.Key))
                        {
                            variables.Add(FormatVariable(methodName, new StashBoundMethod(instance, method)));
                        }
                    }

                    break;

                case StashNamespace ns:
                    foreach (var (memberName, memberValue) in ns.GetAllMembers().OrderBy(kv => kv.Key))
                    {
                        variables.Add(FormatVariable(memberName, memberValue));
                    }

                    break;

                case StashEnum en:
                    foreach (var memberName in en.Members)
                    {
                        var memberValue = en.GetMember(memberName);
                        if (memberValue != null)
                        {
                            variables.Add(FormatVariable(memberName, memberValue));
                        }
                    }
                    break;
            }
        }

        return variables;
    }

    /// <summary>Evaluates an expression in the context of the given frame and returns the result string.</summary>
    public string Evaluate(string expression, int? frameId)
    {
        Trace($"Evaluate: {expression}");
        if (_interpreter == null)
        {
            return "No interpreter";
        }

        var env = frameId.HasValue
            ? (ResolveEnvironmentForFrame(frameId.Value) ?? _interpreter.Globals)
            : (_pausedEnvironment ?? _interpreter.Globals);

        var (value, error) = _interpreter.EvaluateString(expression, env);
        return error != null ? $"Error: {error}" : RuntimeValues.Stringify(value);
    }

    /// <summary>
    /// Sets a variable's value in the given variable container.
    /// The value string is parsed and evaluated as a Stash expression.
    /// Returns the updated DAP Variable.
    /// </summary>
    public Variable SetVariable(int variablesReference, string name, string value)
    {
        if (_interpreter == null)
        {
            throw new InvalidOperationException("No interpreter active.");
        }

        VariableContainer? container;
        lock (_variableReferences)
        {
            _variableReferences.TryGetValue(variablesReference, out container);
        }

        if (container == null)
        {
            throw new InvalidOperationException($"Unknown variable reference: {variablesReference}");
        }

        // Parse and evaluate the new value expression
        var env = _pausedEnvironment ?? _interpreter.Globals;
        var (newValue, error) = _interpreter.EvaluateString(value, env);
        if (error != null)
        {
            throw new InvalidOperationException($"Failed to evaluate value: {error}");
        }

        // Apply the new value to the appropriate container
        if (container.Environment != null)
        {
            // Environment scope variable
            if (!container.Environment.Contains(name))
            {
                throw new InvalidOperationException($"Variable '{name}' not found in scope.");
            }

            if (container.Environment.IsConstant(name))
            {
                throw new InvalidOperationException($"Cannot modify constant '{name}'.");
            }

            container.Environment.Assign(name, newValue);
        }
        else if (container.Value is List<object?> list)
        {
            // Array element — name is like "[0]", "[1]", etc.
            var indexStr = name.TrimStart('[').TrimEnd(']');
            if (!int.TryParse(indexStr, out var index) || index < 0 || index >= list.Count)
            {
                throw new InvalidOperationException($"Invalid array index: {name}");
            }

            list[index] = newValue;
        }
        else if (container.Value is StashDictionary dict)
        {
            // Dictionary entry — name is the key
            dict.Set(name, newValue);
        }
        else if (container.Value is StashInstance instance)
        {
            // Struct field
            try
            {
                instance.SetField(name, newValue, null);
            }
            catch (RuntimeError ex)
            {
                throw new InvalidOperationException(ex.Message);
            }
        }
        else
        {
            throw new InvalidOperationException($"Cannot set variable in this context.");
        }

        return FormatVariable(name, newValue);
    }

    /// <summary>Returns all source files that have been loaded during this session.</summary>
    public IReadOnlyList<Source> GetLoadedSources()
    {
        lock (_loadedSourcesLock)
        {
            return _loadedSources
                .Select(p => new Source { Path = p, Name = Path.GetFileName(p) })
                .ToList();
        }
    }

    /// <summary>Terminates the debug session and unblocks the interpreter thread if paused.</summary>
    public void Disconnect()
    {
        Trace("Disconnect");
        _terminated = true;

        lock (_variableReferences)
        {
            _variableReferences.Clear();
        }

        lock (_loadedSourcesLock)
        {
            _loadedSources.Clear();
        }

        if (_isPaused)
        {
            _pauseGate.Set();
        }

        _interpreterThread?.Interrupt();
    }

    /// <summary>Configures which exception categories should trigger a break.</summary>
    public void SetExceptionBreakpoints(IEnumerable<string> filters)
    {
        _breakOnAllExceptions = false;
        foreach (var filter in filters)
        {
            if (filter is "all" or "uncaught")
            {
                _breakOnAllExceptions = true;
            }
        }
        Trace($"SetExceptionBreakpoints: breakOnAll={_breakOnAllExceptions}");
    }

    // ── IDebugger implementation ───────────────────────────────────────────────

    public void OnBeforeExecute(SourceSpan span, StashEnv environment)
    {
        if (_terminated)
        {
            throw new OperationCanceledException("Debug session terminated.");
        }

        _pausedAtSpan = span;
        _pausedEnvironment = environment;

        bool shouldPause = false;
        PauseReason reason = PauseReason.Step;

        if (_pauseRequested)
        {
            _pauseRequested = false;
            shouldPause = true;
            reason = PauseReason.Pause;
        }
        else if (_stopOnEntry)
        {
            _stopOnEntry = false; // Only fire once
            shouldPause = true;
            reason = PauseReason.Entry;
        }
        else if (ShouldStopForStep())
        {
            shouldPause = true;
            reason = PauseReason.Step;
        }
        else if (CheckBreakpointAtSpan(span, environment))
        {
            shouldPause = true;
            reason = PauseReason.Breakpoint;
        }

        if (shouldPause)
        {
            Trace($"Paused at {span.File}:{span.StartLine} reason={reason}");
            _isPaused = true;
            _pauseReason = reason;
            lock (_variableReferences)
            {
                _variableReferences.Clear();
                _nextVariableReference = 1;
            }
            _pauseGate.Reset(); // Block interpreter thread
            SendStopped(reason);
            _pauseGate.Wait(); // Wait until Continue/Step/StepIn/StepOut is called
            _isPaused = false;
        }
    }

    public void OnFunctionEnter(string functionName, SourceSpan callSite, StashEnv localScope)
    {
        // Check for function breakpoint hit — snapshot entry under lock
        FunctionBreakpointEntry? entry;
        int hitCount;
        lock (_functionBreakpoints)
        {
            if (!_functionBreakpoints.TryGetValue(functionName, out entry))
            {
                return;
            }

            entry.HitCount++;
            hitCount = entry.HitCount;
        }

        // Evaluate condition if present
        if (entry.Condition != null && _interpreter != null)
        {
            var (condValue, condError) = _interpreter.EvaluateString(entry.Condition, localScope);
            if (condError != null || !RuntimeValues.IsTruthy(condValue))
            {
                return;
            }
        }

        // Check hit condition if present
        if (entry.HitCondition != null && !EvaluateHitCondition(entry.HitCondition, hitCount))
        {
            return;
        }

        Trace($"Function breakpoint hit: {functionName}");
        _pausedAtSpan = callSite;
        _pausedEnvironment = localScope;
        _isPaused = true;
        _pauseReason = PauseReason.FunctionBreakpoint;
        lock (_variableReferences)
        {
            _variableReferences.Clear();
            _nextVariableReference = 1;
        }
        _pauseGate.Reset();
        SendStopped(PauseReason.FunctionBreakpoint);
        _pauseGate.Wait();
        _isPaused = false;
    }

    public void OnFunctionExit(string functionName)
    {
        // Step-out detection happens via ShouldStopForStep checking CallStack.Count
    }

    public void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack)
    {
        SendOutput("stderr", $"Runtime error: {error.Message}\n");

        if (_breakOnAllExceptions)
        {
            _pausedAtSpan = error.Span;
            _isPaused = true;
            _pauseReason = PauseReason.Exception;
            lock (_variableReferences)
            {
                _variableReferences.Clear();
                _nextVariableReference = 1;
            }
            _pauseGate.Reset();
            SendStopped(PauseReason.Exception, error.Message);
            _pauseGate.Wait();
            _isPaused = false;
        }
    }

    public void OnSourceLoaded(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        filePath = NormalizePath(filePath);

        bool isNew;
        lock (_loadedSourcesLock)
        {
            isNew = _loadedSources.Add(filePath);
        }

        if (isNew)
        {
            _server?.SendLoadedSource(new LoadedSourceEvent
            {
                Reason = LoadedSourceReason.New,
                Source = new Source { Path = filePath, Name = Path.GetFileName(filePath) },
            });
        }
    }

    public void OnExecutionComplete()
    {
        // Handled in the Launch thread's finally block (SendTerminated + SendExited)
    }

    public void OnOutput(string category, string text)
    {
        SendOutput(category, text);
    }

    public bool ShouldBreakOnException(RuntimeError error) => _breakOnAllExceptions;

    public bool ShouldBreakOnFunctionEntry(string functionName)
    {
        lock (_functionBreakpoints)
        {
            return _functionBreakpoints.ContainsKey(functionName);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void Resume()
    {
        _isPaused = false;
        _pauseGate.Set();
    }

    private void SendOutput(string category, string text)
    {
        if (_server == null)
        {
            return;
        }

        var outputCategory = category switch
        {
            "stdout" => OutputEventCategory.StandardOutput,
            "stderr" => OutputEventCategory.StandardError,
            _ => OutputEventCategory.Console,
        };
        _server.SendOutput(new OutputEvent { Category = outputCategory, Output = text });
    }

    private void SendTerminated()
    {
        _server?.SendTerminated(new TerminatedEvent());
    }

    private void SendExited(int exitCode)
    {
        _server?.SendExited(new ExitedEvent { ExitCode = exitCode });
    }

    private void SendStopped(PauseReason reason, string? description = null)
    {
        if (_server == null)
        {
            return;
        }

        var stopReason = reason switch
        {
            PauseReason.Breakpoint => StoppedEventReason.Breakpoint,
            PauseReason.Step => StoppedEventReason.Step,
            PauseReason.Pause => StoppedEventReason.Pause,
            PauseReason.Exception => StoppedEventReason.Exception,
            PauseReason.Entry => StoppedEventReason.Entry,
            PauseReason.FunctionBreakpoint => StoppedEventReason.FunctionBreakpoint,
            _ => StoppedEventReason.Pause,
        };

        _server.SendStopped(new StoppedEvent
        {
            Reason = stopReason,
            ThreadId = ThreadId,
            Description = description,
            AllThreadsStopped = true,
        });
    }

    /// <summary>
    /// Allocates a new variable reference ID for the given container.
    /// Caller must hold the lock on <c>_variableReferences</c>.
    /// </summary>
    private long AllocateVariableReference(VariableContainer container)
    {
        var id = _nextVariableReference++;
        _variableReferences[id] = container;
        return id;
    }

    /// <summary>Allocates a variable reference for an expandable complex value (array/dict/instance).</summary>
    private long AllocateExpansion(string name, object? value)
    {
        lock (_variableReferences)
        {
            return AllocateVariableReference(new VariableContainer { Value = value, Name = name });
        }
    }

    private static string NormalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        // Canonicalize Windows drive letter to uppercase for consistent dictionary lookup
        if (fullPath.Length >= 2 && fullPath[1] == ':')
        {
            return char.ToUpperInvariant(fullPath[0]) + fullPath[1..];
        }
        return fullPath;
    }

    private static Source? MakeSource(string? file)
    {
        if (file == null)
        {
            return null;
        }

        return new Source { Path = NormalizePath(file), Name = Path.GetFileName(file) };
    }

    private static StackFrame MakeScriptFrame(int id, SourceSpan? span)
    {
        return new StackFrame
        {
            Id = id,
            Name = "<script>",
            Source = MakeSource(span?.File),
            Line = span?.StartLine ?? 0,
            Column = span?.StartColumn ?? 0,
            EndLine = span?.EndLine,
            EndColumn = span?.EndColumn,
        };
    }

    /// <summary>
    /// Resolves the active <see cref="StashEnv"/> for a given DAP frame ID.
    /// The top frame uses <c>_pausedEnvironment</c> (the innermost active scope).
    /// Deeper frames use the <see cref="CallFrame.LocalScope"/> recorded on entry.
    /// </summary>
    private StashEnv? ResolveEnvironmentForFrame(int frameId)
    {
        if (_interpreter == null)
        {
            return null;
        }

        var callStack = _interpreter.CallStack;

        // Global/script scope when no functions are on the stack
        if (callStack.Count == 0 && frameId == 0)
        {
            return _pausedEnvironment ?? _interpreter.Globals;
        }

        // Top frame — use the active (innermost) paused environment
        if (callStack.Count > 0 && callStack[callStack.Count - 1].Id == frameId)
        {
            return _pausedEnvironment ?? callStack[callStack.Count - 1].LocalScope;
        }

        // Intermediate frames — use the environment recorded when the function was entered
        for (int i = callStack.Count - 2; i >= 0; i--)
        {
            if (callStack[i].Id == frameId)
            {
                return callStack[i].LocalScope;
            }
        }

        // Synthetic script frame at the bottom
        if (frameId == 0)
        {
            return _interpreter.Globals;
        }

        return null;
    }

    /// <summary>
    /// Checks whether the current execution span hits any breakpoint.
    /// Handles conditions, hit counts, and logpoints.
    /// Returns true only if execution should pause.
    /// </summary>
    private bool CheckBreakpointAtSpan(SourceSpan span, StashEnv environment)
    {
        if (span.File == null)
        {
            return false;
        }

        var normalized = NormalizePath(span.File);
        if (!_breakpoints.TryGetValue(normalized, out var bpList))
        {
            return false;
        }

        StashBreakpoint? hit = null;
        foreach (var bp in bpList)
        {
            if (bp.Line == span.StartLine)
            {
                hit = bp;
                break;
            }
        }

        if (hit == null)
        {
            return false;
        }

        // Evaluate condition expression if present
        if (hit.Condition != null && _interpreter != null)
        {
            var (condValue, condError) = _interpreter.EvaluateString(hit.Condition, environment);
            if (condError != null || !RuntimeValues.IsTruthy(condValue))
            {
                return false;
            }
        }

        // Increment hit count; check hit condition if present
        var hitCount = hit.IncrementHitCount();
        if (hit.HitCondition != null && !EvaluateHitCondition(hit.HitCondition, hitCount))
        {
            return false;
        }

        // Logpoint: interpolate the message and send as output — do NOT pause
        if (hit.IsLogpoint)
        {
            var msg = InterpolateLogMessage(hit.LogMessage!, environment);
            SendOutput("console", msg + "\n");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Evaluates a hit condition expression such as <c>"== 5"</c>, <c>">= 3"</c>, or <c>"% 2 == 0"</c>.
    /// Returns true if the condition is satisfied or cannot be parsed.
    /// </summary>
    private static bool EvaluateHitCondition(string condition, int hitCount)
    {
        var c = condition.Trim();
        if (c.StartsWith("== ") && int.TryParse(c[3..], out var eq))
        {
            return hitCount == eq;
        }

        if (c.StartsWith(">= ") && int.TryParse(c[3..], out var gte))
        {
            return hitCount >= gte;
        }

        if (c.StartsWith("> ") && int.TryParse(c[2..], out var gt))
        {
            return hitCount > gt;
        }

        if (c.StartsWith("<= ") && int.TryParse(c[3..], out var lte))
        {
            return hitCount <= lte;
        }

        if (c.StartsWith("< ") && int.TryParse(c[2..], out var lt))
        {
            return hitCount < lt;
        }

        if (c.StartsWith("% "))
        {
            var parts = c[2..].Split("==", 2);
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out var mod)
                && int.TryParse(parts[1].Trim(), out var rem))
            {
                return hitCount % mod == rem;
            }
        }
        if (int.TryParse(c, out var n))
        {
            return hitCount == n;
        }

        return true;
    }

    /// <summary>
    /// Replaces <c>{expression}</c> placeholders in a logpoint template with evaluated values.
    /// </summary>
    private string InterpolateLogMessage(string template, StashEnv environment)
    {
        if (_interpreter == null)
        {
            return template;
        }

        var sb = new StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{' && i + 1 < template.Length)
            {
                var end = template.IndexOf('}', i + 1);
                if (end > i)
                {
                    var expr = template[(i + 1)..end];
                    var (value, error) = _interpreter.EvaluateString(expr, environment);
                    sb.Append(error != null ? $"{{error: {error}}}" : RuntimeValues.Stringify(value));
                    i = end + 1;
                    continue;
                }
            }
            sb.Append(template[i++]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates a DAP <see cref="Variable"/> from a name/value pair.
    /// For complex types (arrays, dicts, instances), allocates a variable reference
    /// so the client can expand them.
    /// </summary>
    private Variable FormatVariable(string name, object? value)
    {
        string type;
        string displayValue;
        long variablesReference = 0;

        switch (value)
        {
            case null:
                type = "null";
                displayValue = "null";
                break;

            case long l:
                type = "int";
                displayValue = l.ToString();
                break;

            case double d:
                type = "float";
                displayValue = d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;

            case bool b:
                type = "bool";
                displayValue = b ? "true" : "false";
                break;

            case string s:
                type = "string";
                displayValue = $"\"{s}\"";
                break;

            case List<object?> list:
                type = "array";
                displayValue = $"array[{list.Count}]";
                variablesReference = AllocateExpansion(name, value);
                break;

            case StashDictionary dict:
                type = "dict";
                displayValue = $"dict[{dict.Count}]";
                variablesReference = AllocateExpansion(name, value);
                break;

            case StashInstance instance:
                type = instance.TypeName;
                displayValue = $"{instance.TypeName} {{...}}";
                variablesReference = AllocateExpansion(name, value);
                break;

            case StashFunction fn:
                type = "function";
                displayValue = fn.ToString() ?? "<fn>";
                break;

            case StashBoundMethod bm:
                type = "function";
                displayValue = bm.ToString();
                break;

            case StashLambda:
                type = "function";
                displayValue = "<lambda>";
                break;

            case StashEnumValue enumVal:
                type = "enum";
                displayValue = enumVal.ToString();
                break;

            case BuiltInFunction fn:
                type = "function";
                displayValue = fn.ToString();
                break;

            case StashNamespace ns:
                type = "namespace";
                displayValue = ns.ToString();
                variablesReference = AllocateExpansion(name, value);
                break;

            case StashEnum en:
                type = "enum";
                displayValue = $"<enum {en.Name}> ({en.Members.Count} members)";
                variablesReference = AllocateExpansion(name, value);
                break;

            case StashStruct st:
                type = "struct";
                var methodCount = st.Methods.Count;
                displayValue = methodCount > 0
                    ? $"<struct {st.Name}> ({string.Join(", ", st.Fields)}) [{methodCount} method(s)]"
                    : $"<struct {st.Name}> ({string.Join(", ", st.Fields)})";
                break;

            default:
                type = value.GetType().Name;
                displayValue = RuntimeValues.Stringify(value);
                break;
        }

        return new Variable
        {
            Name = name,
            Value = displayValue,
            Type = type,
            VariablesReference = variablesReference,
        };
    }

    /// <summary>
    /// Returns true if the value is a language built-in (function or namespace).
    /// Used to separate built-ins from user-defined bindings in the debug panel.
    /// </summary>
    private static bool IsBuiltInBinding(object? value) => value is BuiltInFunction or StashNamespace;

    /// <summary>
    /// Determines whether the current step mode requires pausing at this point.
    /// Mutates <c>_stepMode</c> to <c>None</c> when a step completes.
    /// </summary>
    private bool ShouldStopForStep()
    {
        var depth = _interpreter?.CallStack.Count ?? 0;
        switch (_stepMode)
        {
            case StepMode.StepIn:
                // Stop at the very next statement, regardless of depth
                _stepMode = StepMode.None;
                return true;

            case StepMode.StepOver:
                // Stop when we return to the same or shallower call depth
                if (depth <= _stepDepth)
                {
                    _stepMode = StepMode.None;
                    return true;
                }
                return false;

            case StepMode.StepOut:
                // Stop when we return to a shallower call depth
                if (depth < _stepDepth)
                {
                    _stepMode = StepMode.None;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }
}
