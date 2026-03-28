namespace Stash.Interpreting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Stash.Common;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Interpreting.Exceptions;
using Stash.Stdlib;

/// <summary>
/// Tree-walk interpreter that evaluates a Stash AST by visiting each expression node
/// and computing a runtime value.
/// </summary>
/// <remarks>
/// <para>
/// The interpreter implements <see cref="IExprVisitor{T}"/> with <c>object?</c> as the return
/// type because Stash is dynamically typed. Runtime values are represented as CLR objects:
/// <see cref="long"/> for integers, <see cref="double"/> for floating-point numbers,
/// <see cref="string"/> for text, <see cref="bool"/> for booleans, and <c>null</c> for the
/// absence of a value.
/// </para>
/// <para>
/// This is a tree-walk interpreter — it traverses the AST directly via the visitor pattern
/// rather than compiling to bytecode. This approach is simple to implement and debug, which
/// is ideal for Phase 1 of the language. A bytecode VM is planned for a future version.
/// </para>
/// <para>
/// <b>Truthiness rules</b> (per the Stash specification): <c>false</c>, <c>null</c>,
/// <c>0</c> (long), <c>0.0</c> (double), and <c>""</c> (empty string) are falsy.
/// Everything else is truthy.
/// </para>
/// <para>
/// <b>Equality semantics</b>: No type coercion is performed for equality checks.
/// <c>5 == "5"</c> is <c>false</c>, and <c>0 == false</c> is <c>false</c>. This is a
/// deliberate design choice to prevent subtle bugs common in languages with loose equality.
/// </para>
/// <para>
/// <b>Short-circuit evaluation</b>: The <c>&amp;&amp;</c> and <c>||</c> operators return
/// actual operand values, not coerced booleans. For example, <c>null || "default"</c> returns
/// <c>"default"</c>, and <c>"value" &amp;&amp; false</c> returns <c>false</c>. This enables
/// idiomatic patterns like <c>name || "anonymous"</c>.
/// </para>
/// <para>
/// <b>Numeric type promotion</b>: When a <see cref="long"/> and <see cref="double"/> appear
/// together in arithmetic, the <c>long</c> is promoted to <c>double</c> and the result is
/// <c>double</c>. When both operands are <c>long</c>, the result stays <c>long</c>
/// (integer arithmetic, including truncating division).
/// </para>
/// <para>
/// <b>String concatenation via <c>+</c></b>: When either operand of <c>+</c> is a string,
/// the other is converted using <see cref="Stringify"/> and the result is a concatenated
/// string. This matches the Stash spec's type coercion rules for the <c>+</c> operator.
/// </para>
/// </remarks>
public partial class Interpreter : IExprVisitor<object?>, IStmtVisitor<object?>, IInterpreterContext
{
    /// <summary>The global scope environment containing built-in functions and top-level declarations.</summary>
    private readonly Environment _globals;
    /// <summary>The capability flags controlling which built-in namespaces are available.</summary>
    private readonly StashCapabilities _capabilities;
    /// <summary>Maximum allowed statement count. Zero means unlimited.</summary>
    private long _stepLimit;
    /// <summary>The attached debugger, if any. When non-null, debug hooks are invoked during execution.</summary>
    private IDebugger? _debugger;
    /// <summary>Set of all source file paths loaded during execution (main script + imports). Used for DAP "loadedSources".</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _loadedSources = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Shared module cache ensuring each module is loaded at most once, even across parallel tasks. Keyed by resolved absolute path.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Environment> _sharedModuleCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per-module lock objects for serializing first-time module loading.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _moduleLocks = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The test harness for reporting test results. When set, <c>test()</c> and <c>describe()</c> report to it.</summary>
    private Stash.Runtime.ITestHarness? _testHarness;
    /// <summary>Resolver-computed scope distances for variable references, enabling O(1) lookup at runtime. Thread-safe for concurrent resolver writes during parallel execution.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Expr, (int Distance, int Slot)> _locals = new(ReferenceEqualityComparer.Instance);
    /// <summary>The raw script arguments, parsed by <c>args</c> declarations.</summary>
    internal string[] ScriptArgs = Array.Empty<string>();
    /// <summary>The mutable per-execution state. Encapsulates environment, call stack, I/O, and other state that varies per execution path.</summary>
    internal ExecutionContext _ctx;
    private readonly object _cleanupLock = new();
    internal TaskRegistry TaskRegistry { get; private set; }

    // Private shims — allow partial-class files to continue using short field names while state lives in _ctx.
    private Environment _environment { get => _ctx.Environment; set => _ctx.Environment = value; }
    private string? _pendingStdin { get => _ctx.PendingStdin; set => _ctx.PendingStdin = value; }
    private HashSet<string> _importStack => _ctx.ImportStack;
    private string? _currentFile { get => _ctx.CurrentFile; set => _ctx.CurrentFile = value; }
    private List<CallFrame> _callStack => _ctx.CallStack;

    /// <summary>
    /// Gets or sets the current file path being executed.
    /// Used for resolving relative import paths.
    /// </summary>
    public string? CurrentFile
    {
        get => _ctx.CurrentFile;
        set => _ctx.CurrentFile = value;
    }

    /// <summary>
    /// Sets the script arguments that will be parsed by an <c>args</c> declaration.
    /// </summary>
    public void SetScriptArgs(string[] args)
    {
        ScriptArgs = args;
    }

    /// <summary>
    /// Gets or sets the debugger. When set, debug hooks are invoked during execution.
    /// </summary>
    public IDebugger? Debugger
    {
        get => _debugger;
        set => _debugger = value;
    }

    /// <summary>
    /// Logical thread ID for debugging. The main interpreter uses 1; forked interpreters
    /// for task.run() get unique IDs assigned by the task namespace.
    /// </summary>
    public int DebugThreadId { get; set; } = 1;

    /// <summary>
    /// Gets or sets the test harness. When set, test() and describe() report results to it.
    /// </summary>
    public Stash.Runtime.ITestHarness? TestHarness
    {
        get => _testHarness;
        set => _testHarness = value;
    }

    /// <summary>
    /// Gets or sets the current describe block name for nesting test names.
    /// </summary>
    public string? CurrentDescribe
    {
        get => _ctx.CurrentDescribe;
        set => _ctx.CurrentDescribe = value;
    }

    /// <summary>
    /// Gets or sets the test filter patterns. When set, only tests whose fully
    /// qualified name matches one of the patterns will execute.
    /// </summary>
    public string[]? TestFilter
    {
        get => _ctx.TestFilter;
        set => _ctx.TestFilter = value;
    }

    /// <summary>
    /// Gets or sets whether the interpreter is in test discovery mode.
    /// In discovery mode, test() records names and locations but does not execute test bodies.
    /// </summary>
    public bool DiscoveryMode
    {
        get => _ctx.DiscoveryMode;
        set => _ctx.DiscoveryMode = value;
    }

    /// <summary>Gets the stack of <c>beforeEach</c> hook lists.</summary>
    internal List<List<IStashCallable>> BeforeEachHooks => _ctx.BeforeEachHooks;
    /// <summary>Gets the stack of <c>afterEach</c> hook lists.</summary>
    internal List<List<IStashCallable>> AfterEachHooks => _ctx.AfterEachHooks;
    /// <summary>Gets the stack of <c>afterAll</c> hook lists.</summary>
    internal List<List<IStashCallable>> AfterAllHooks => _ctx.AfterAllHooks;

    /// <summary>
    /// Gets or sets the output writer used by io.println and io.print.
    /// Defaults to Console.Out. Override to capture or redirect output.
    /// </summary>
    public TextWriter Output
    {
        get => _ctx.Output;
        set => _ctx.Output = value;
    }

    /// <summary>
    /// Gets or sets the error output writer.
    /// Defaults to Console.Error. Override to capture or redirect error output.
    /// </summary>
    public TextWriter ErrorOutput
    {
        get => _ctx.ErrorOutput;
        set => _ctx.ErrorOutput = value;
    }

    /// <summary>
    /// Gets or sets the input reader used by io.readLine.
    /// Defaults to Console.In. Override to supply input programmatically.
    /// </summary>
    public TextReader Input
    {
        get => _ctx.Input;
        set => _ctx.Input = value;
    }

    /// <summary>
    /// When true, process.exit() throws <see cref="ExitException"/> instead of
    /// terminating the host process, and process.exec() is disabled.
    /// Set this to true when embedding the interpreter in another application.
    /// </summary>
    public bool EmbeddedMode { get; set; }

    /// <summary>
    /// Gets or sets a cancellation token that can be used to abort script execution.
    /// When the token is cancelled, the interpreter throws <see cref="ScriptCancelledException"/>
    /// at the next statement boundary.
    /// </summary>
    public CancellationToken CancellationToken
    {
        get => _ctx.CancellationToken;
        set => _ctx.CancellationToken = value;
    }

    /// <summary>
    /// Gets or sets the maximum number of statements the interpreter will execute
    /// before throwing <see cref="StepLimitExceededException"/>.
    /// A value of 0 (default) means no limit.
    /// </summary>
    public long StepLimit
    {
        get => _stepLimit;
        set => _stepLimit = value;
    }

    /// <summary>
    /// Gets the number of statements executed since the last reset or start of execution.
    /// </summary>
    public long StepCount => _ctx.StepCount;

    /// <summary>
    /// Gets the current call stack as a read-only list.
    /// </summary>
    public IReadOnlyList<CallFrame> CallStack => _ctx.CallStack;

    /// <summary>
    /// Gets all source files that have been loaded during execution (main script + imports).
    /// Used for DAP "loadedSources" request.
    /// </summary>
    public IReadOnlyCollection<string> LoadedSources => new List<string>(_loadedSources.Keys);

    /// <summary>
    /// Gets the source span of the statement currently being executed.
    /// Null when the interpreter is not executing. Useful for DAP to determine
    /// the current position when paused.
    /// </summary>
    public SourceSpan? CurrentSpan => _ctx.CurrentSpan;

    /// <summary>The last caught error. Used by lastError() built-in and try expressions.</summary>
    internal object? LastError
    {
        get => _ctx.LastError;
        set => _ctx.LastError = value;
    }

    /// <summary>Background processes tracked for cleanup. Used by ProcessBuiltIns.</summary>
    internal List<(StashInstance Handle, System.Diagnostics.Process OsProcess)> TrackedProcesses => _ctx.TrackedProcesses;

    /// <summary>Cache mapping process handles to wait results. Used by ProcessBuiltIns.</summary>
    internal Dictionary<StashInstance, StashInstance> ProcessWaitCache => _ctx.ProcessWaitCache;

    /// <summary>Process exit callbacks. Used by ProcessBuiltIns.</summary>
    internal Dictionary<StashInstance, List<IStashCallable>> ProcessExitCallbacks => _ctx.ProcessExitCallbacks;

    // ── Sub-interface explicit interface implementations ───────────────────────────────────────
    object? IExecutionContext.LastError { get => _ctx.LastError; set => _ctx.LastError = value; }
    string[]? IExecutionContext.ScriptArgs => ScriptArgs;
    IInterpreterContext IInterpreterContext.Fork(System.Threading.CancellationToken cancellationToken) => Fork(Environment.Snapshot(_ctx.Environment), cancellationToken);
    IInterpreterContext IInterpreterContext.ForkParallel(System.Threading.CancellationToken cancellationToken) => Fork(Environment.Snapshot(_ctx.Environment), cancellationToken, attachDebugger: false);
    object? IExecutionContext.Debugger => _debugger;
    string IExecutionContext.ExpandTilde(string path) => Interpreter.ExpandTilde(path);
    void IExecutionContext.NotifyOutput(string category, string text) => _debugger?.OnOutput(category, text);
    void IExecutionContext.EmitExit(int code) { CleanupTrackedProcesses(); if (EmbeddedMode) throw new Stash.Interpreting.Exceptions.ExitException(code); System.Environment.Exit(code); }
    List<(StashInstance Handle, System.Diagnostics.Process Process)> IProcessContext.TrackedProcesses => _ctx.TrackedProcesses;
    Dictionary<StashInstance, StashInstance> IProcessContext.ProcessWaitCache => _ctx.ProcessWaitCache;
    Dictionary<StashInstance, List<IStashCallable>> IProcessContext.ProcessExitCallbacks => _ctx.ProcessExitCallbacks;
    Stash.Runtime.ITestHarness? ITestContext.TestHarness { get => _testHarness; set => _testHarness = value; }
    List<List<IStashCallable>> ITestContext.BeforeEachHooks => _ctx.BeforeEachHooks;
    List<List<IStashCallable>> ITestContext.AfterEachHooks => _ctx.AfterEachHooks;
    List<List<IStashCallable>> ITestContext.AfterAllHooks => _ctx.AfterAllHooks;
    object? ITemplateContext.CompileAndRenderTemplate(string template, StashDictionary data, string? basePath) { var r = new Stash.Interpreting.Templating.TemplateRenderer(this, basePath); return r.Render(template, data); }
    object? ITemplateContext.CompileTemplate(string template) { var l = new Stash.Interpreting.Templating.TemplateLexer(template); var t = l.Scan(); var p = new Stash.Interpreting.Templating.TemplateParser(t); return p.Parse(); }
    object? ITemplateContext.RenderCompiledTemplate(object? compiled, StashDictionary data) { if (compiled is not List<Stash.Interpreting.Templating.TemplateNode> nodes) throw new RuntimeError("'tpl.render' expects a string or compiled template as the first argument."); var r = new Stash.Interpreting.Templating.TemplateRenderer(this); return r.Render(nodes, data); }

    /// <summary>
    /// Gets the global environment. Useful for DAP to enumerate global variables.
    /// </summary>
    public Environment Globals => _globals;

    /// <summary>Creates a new interpreter with all capabilities enabled.</summary>
    public Interpreter() : this(StashCapabilities.All)
    {
    }

    /// <summary>Creates a new interpreter with the specified capability restrictions.</summary>
    /// <param name="capabilities">The capability flags controlling which built-in namespaces are registered.</param>
    public Interpreter(StashCapabilities capabilities)
    {
        _capabilities = capabilities;
        _globals = new Environment();
        _ctx = new ExecutionContext(_globals);
        TaskRegistry = new TaskRegistry();
        DefineBuiltIns();
    }

    /// <summary>
    /// Private constructor for creating a forked child interpreter.
    /// Shares immutable state with the parent; uses a separate ExecutionContext.
    /// </summary>
    private Interpreter(Interpreter parent, ExecutionContext forkedContext)
    {
        _globals = parent._globals;
        _capabilities = parent._capabilities;
        _stepLimit = parent._stepLimit;
        _debugger = parent._debugger;
        _loadedSources = parent._loadedSources;
        _sharedModuleCache = parent._sharedModuleCache;
        _moduleLocks = parent._moduleLocks;
        _testHarness = parent._testHarness;
        _locals = parent._locals;          // ConcurrentDictionary — safe to share
        TaskRegistry = parent.TaskRegistry; // Shared — tasks from any fork can be awaited by any other
        ScriptArgs = parent.ScriptArgs;
        EmbeddedMode = parent.EmbeddedMode;
        _ctx = forkedContext;
    }

    /// <summary>
    /// Creates a forked child interpreter for parallel task execution.
    /// The child shares the global scope, resolver cache, and immutable configuration,
    /// but gets an independent <see cref="ExecutionContext"/> with a snapshotted
    /// environment chain rooted at the given <paramref name="taskScope"/>.
    /// </summary>
    /// <param name="taskScope">The environment scope to use as the child's current environment.
    /// Typically a snapshot of the closure environment.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation of the task.</param>
    public Interpreter Fork(Environment taskScope, CancellationToken cancellationToken = default, bool attachDebugger = true)
    {
        // Upgrade to synchronized writers on first fork to prevent interleaved output.
        // The same wrapper is shared between parent and all children so they share the lock.
        if (_ctx.Output is not SynchronizedTextWriter)
        {
            _ctx.Output = new SynchronizedTextWriter(_ctx.Output);
        }
        if (_ctx.ErrorOutput is not SynchronizedTextWriter)
        {
            _ctx.ErrorOutput = new SynchronizedTextWriter(_ctx.ErrorOutput);
        }

        var forkedCtx = new ExecutionContext(taskScope)
        {
            Output = _ctx.Output,
            ErrorOutput = _ctx.ErrorOutput,
            Input = _ctx.Input,
            CurrentFile = _ctx.CurrentFile,
            CancellationToken = cancellationToken,
        };
        var child = new Interpreter(this, forkedCtx);
        if (!attachDebugger)
        {
            child._debugger = null;
        }
        return child;
    }

    /// <summary>
    /// Records the lexical scope distance for a variable reference, as computed by the Resolver.
    /// </summary>
    public void Resolve(Expr expr, int distance, int slot)
    {
        _locals[expr] = (distance, slot);
    }

    /// <summary>
    /// Evaluates a parsed expression AST and returns the resulting runtime value.
    /// </summary>
    /// <param name="expression">The root <see cref="Expr"/> node to evaluate.</param>
    /// <returns>
    /// The computed value: a <see cref="long"/>, <see cref="double"/>, <see cref="string"/>,
    /// <see cref="bool"/>, or <c>null</c>.
    /// </returns>
    /// <exception cref="RuntimeError">
    /// Thrown if evaluation fails (e.g., type mismatch, division by zero, undefined variable).
    /// </exception>
    public object? Interpret(Expr expression)
    {
        return expression.Accept(this);
    }

    /// <summary>Interprets a list of statements: resolves variable references, then executes each statement sequentially.</summary>
    /// <param name="statements">The top-level statements to execute.</param>
    /// <exception cref="RuntimeError">Thrown on runtime errors, including misplaced <c>break</c>, <c>continue</c>, or <c>return</c> at the top level.</exception>
    public void Interpret(List<Stmt> statements)
    {
        // Track loaded source
        if (_currentFile is not null && _loadedSources.TryAdd(_currentFile, 0))
        {
            _debugger?.OnSourceLoaded(_currentFile);
        }

        // Resolve variable references for O(1) lookup
        var resolver = new Resolver(this);
        resolver.Resolve(statements);

        try
        {
            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }
        }
        catch (BreakException)
        {
            var err = new RuntimeError("'break' used outside of a loop.");
            _debugger?.OnError(err, _ctx.CallStack, DebugThreadId);
            throw err;
        }
        catch (ContinueException)
        {
            var err = new RuntimeError("'continue' used outside of a loop.");
            _debugger?.OnError(err, _ctx.CallStack, DebugThreadId);
            throw err;
        }
        catch (ReturnException)
        {
            var err = new RuntimeError("'return' used outside of a function.");
            _debugger?.OnError(err, _ctx.CallStack, DebugThreadId);
            throw err;
        }
    }

    /// <summary>Executes a single statement, checking for cancellation and step limits, invoking debug hooks, and dispatching to the visitor.</summary>
    /// <param name="stmt">The statement to execute.</param>
    private void Execute(Stmt stmt)
    {
        if (_ctx.CancellationToken.IsCancellationRequested)
        {
            throw new ScriptCancelledException();
        }

        if (_stepLimit > 0 && ++_ctx.StepCount > _stepLimit)
        {
            throw new StepLimitExceededException(_stepLimit);
        }

        _ctx.CurrentSpan = stmt.Span;
        _debugger?.OnBeforeExecute(stmt.Span, _ctx.Environment, DebugThreadId);
        stmt.Accept(this);
    }

    /// <summary>
    /// Runs the variable resolver on the given statements without executing them.
    /// </summary>
    public void ResolveStatements(List<Stmt> statements)
    {
        var resolver = new Resolver(this);
        resolver.Resolve(statements);
    }

    /// <summary>
    /// Executes pre-resolved statements, skipping the resolver pass.
    /// Used by <see cref="StashEngine"/> for pre-compiled scripts.
    /// </summary>
    internal void InterpretResolved(List<Stmt> statements)
    {
        if (_currentFile is not null && _loadedSources.TryAdd(_currentFile, 0))
        {
            _debugger?.OnSourceLoaded(_currentFile);
        }

        try
        {
            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }
        }
        catch (BreakException)
        {
            var err = new RuntimeError("'break' used outside of a loop.");
            _debugger?.OnError(err, _ctx.CallStack, DebugThreadId);
            throw err;
        }
        catch (ContinueException)
        {
            var err = new RuntimeError("'continue' used outside of a loop.");
            _debugger?.OnError(err, _ctx.CallStack, DebugThreadId);
            throw err;
        }
        catch (ReturnException)
        {
            // A return at the top level is silently consumed — the value is discarded.
            // This mirrors function semantics: a bare `return;` outside a function
            // is harmless rather than an error.
        }
    }

    /// <summary>
    /// Resets the step counter to zero. Call this before re-executing scripts
    /// with the same interpreter instance.
    /// </summary>
    public void ResetStepCount()
    {
        _ctx.StepCount = 0;
    }

    /// <summary>Executes a list of statements in the given environment, restoring the previous environment afterwards.</summary>
    /// <param name="statements">The statements to execute.</param>
    /// <param name="environment">The environment (scope) to execute in.</param>
    public void ExecuteBlock(List<Stmt> statements, Environment environment)
    {
        Environment previous = _ctx.Environment;
        try
        {
            _ctx.Environment = environment;
            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }
        }
        finally
        {
            _ctx.Environment = previous;
        }
    }

    /// <summary>
    /// Evaluates an expression in the given environment, then restores the previous environment.
    /// Used by <see cref="StashLambda"/> for expression-body lambdas.
    /// </summary>
    public object? EvaluateInEnvironment(Expr expr, Environment environment)
    {
        Environment previous = _ctx.Environment;
        bool previousAdHocEval = _ctx.IsAdHocEval;
        try
        {
            _ctx.Environment = environment;
            _ctx.IsAdHocEval = true;
            return expr.Accept(this);
        }
        finally
        {
            _ctx.Environment = previous;
            _ctx.IsAdHocEval = previousAdHocEval;
        }
    }

    /// <summary>
    /// Parses and evaluates a string expression in the given environment.
    /// Used for DAP "evaluate" requests (watch expressions, debug console, hover).
    /// Returns a tuple of (result value, error message or null).
    /// </summary>
    public (object? Value, string? Error) EvaluateString(string expression, Environment environment)
    {
        try
        {
            var lexer = new Lexer(expression, "<eval>");
            var tokens = lexer.ScanTokens();
            if (lexer.Errors.Count > 0)
            {
                return (null, lexer.Errors[0].ToString());
            }

            var parser = new Parser(tokens);
            Expr expr = parser.Parse();
            if (parser.Errors.Count > 0)
            {
                return (null, parser.Errors[0].ToString());
            }

            object? result = EvaluateInEnvironment(expr, environment);
            return (result, null);
        }
        catch (RuntimeError e)
        {
            return (null, e.Message);
        }
    }

    /// <summary>Registers all built-in function namespaces into the global environment, respecting capability flags.</summary>
    private void DefineBuiltIns()
    {
        // Load global functions (capability-filtered by GlobalBuiltIns itself)
        var globalDef = StdlibDefinitions.GetGlobals(_capabilities);
        foreach (var (name, fn) in globalDef.RuntimeFunctions)
            _globals.Define(name, fn);

        // Load namespace definitions from the central cache, filtering by caller capabilities
        foreach (var nsDef in StdlibDefinitions.Namespaces)
        {
            if (nsDef.RequiredCapability != StashCapabilities.None &&
                !_capabilities.HasFlag(nsDef.RequiredCapability))
                continue;

            _globals.Define(nsDef.Name, nsDef.Namespace);
        }

    }

    /// <summary>
    /// Cleans up all tracked processes on script exit.
    /// Sends SIGTERM, waits up to 3 seconds, then SIGKILL.
    /// </summary>
    public void CleanupTrackedProcesses()
    {
        List<(StashInstance Handle, System.Diagnostics.Process OsProcess)> snapshot;
        lock (_cleanupLock)
        {
            snapshot = new List<(StashInstance, System.Diagnostics.Process)>(TrackedProcesses);
            TrackedProcesses.Clear();
            ProcessExitCallbacks.Clear();
        }

        foreach (var (_, osProcess) in snapshot)
        {
            try
            {
                if (!osProcess.HasExited)
                {
                    osProcess.Kill(false); // SIGTERM on Linux
                    if (!osProcess.WaitForExit(3000))
                    {
                        osProcess.Kill(true); // SIGKILL
                    }
                }
            }
            catch { /* Process may have already exited */ }

            try { osProcess.Dispose(); }
            catch { /* Best-effort disposal */ }
        }
    }
}
