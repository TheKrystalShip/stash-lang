namespace Stash.Interpreting;

using System;
using Stash.Interpreting.BuiltIns;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Stash.Common;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting.Types;
using Stash.Interpreting.Exceptions;

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
public partial class Interpreter : IExprVisitor<object?>, IStmtVisitor<object?>
{
    /// <summary>The global scope environment containing built-in functions and top-level declarations.</summary>
    private readonly Environment _globals;
    /// <summary>The current lexical scope. Changes as the interpreter enters and exits blocks.</summary>
    private Environment _environment;
    /// <summary>Pending standard input to pipe into the next command execution.</summary>
    private string? _pendingStdin;
    /// <summary>The message from the last caught <see cref="RuntimeError"/> in a <c>try?</c> expression. Exposed via <c>err.last()</c>.</summary>
    internal string? LastError;
    /// <summary>Cache of already-loaded modules, keyed by resolved absolute path. Prevents re-execution on repeated imports.</summary>
    private readonly Dictionary<string, Environment> _moduleCache = new();
    /// <summary>Set of module paths currently being imported, used to detect circular imports.</summary>
    private readonly HashSet<string> _importStack = new();
    /// <summary>The file path of the script currently being executed. Used for resolving relative imports.</summary>
    private string? _currentFile;
    /// <summary>The runtime call stack, tracking active function invocations for debugging and error reporting.</summary>
    private readonly List<CallFrame> _callStack = new();
    /// <summary>The attached debugger, if any. When non-null, debug hooks are invoked during execution.</summary>
    private IDebugger? _debugger;
    /// <summary>Set of all source file paths loaded during execution (main script + imports). Used for DAP "loadedSources".</summary>
    private readonly HashSet<string> _loadedSources = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The test harness for reporting test results. When set, <c>test()</c> and <c>describe()</c> report to it.</summary>
    private Stash.Testing.ITestHarness? _testHarness;
    /// <summary>The name of the current <c>describe</c> block, used to namespace test names.</summary>
    private string? _currentDescribe;
    /// <summary>Optional test name filter patterns. When set, only matching tests execute.</summary>
    private string[]? _testFilter;
    /// <summary>When true, <c>test()</c> records names and locations without executing test bodies.</summary>
    private bool _discoveryMode;
    /// <summary>Stack of <c>beforeEach</c> hook lists, one per nested <c>describe</c> block.</summary>
    private readonly List<List<IStashCallable>> _beforeEachHooks = new();
    /// <summary>Stack of <c>afterEach</c> hook lists, one per nested <c>describe</c> block.</summary>
    private readonly List<List<IStashCallable>> _afterEachHooks = new();
    /// <summary>Stack of <c>afterAll</c> hook lists, one per nested <c>describe</c> block.</summary>
    private readonly List<List<IStashCallable>> _afterAllHooks = new();
    /// <summary>The source span of the statement currently being executed. Used by the debugger to track position.</summary>
    private SourceSpan? _currentSpan;
    /// <summary>The raw script arguments, parsed by <c>args</c> declarations.</summary>
    internal string[] ScriptArgs = Array.Empty<string>();
    /// <summary>Background processes launched by <c>process.spawn()</c>, tracked for cleanup on script exit.</summary>
    internal readonly List<(StashInstance Handle, System.Diagnostics.Process OsProcess)> TrackedProcesses = new();
    /// <summary>Cache mapping process handles to their wait results, preventing duplicate <c>process.wait()</c> calls.</summary>
    internal readonly Dictionary<StashInstance, StashInstance> ProcessWaitCache = new(ReferenceEqualityComparer.Instance);
    /// <summary>Callbacks registered via <c>process.onExit()</c> to run when a tracked process exits.</summary>
    internal readonly Dictionary<StashInstance, List<IStashCallable>> ProcessExitCallbacks = new(ReferenceEqualityComparer.Instance);
    /// <summary>Resolver-computed scope distances for variable references, enabling O(1) lookup at runtime.</summary>
    private readonly Dictionary<Expr, int> _locals = new(ReferenceEqualityComparer.Instance);
    /// <summary>When true, variable lookups walk the scope chain instead of using resolver distances. Set during DAP evaluate requests.</summary>
    private bool _isAdHocEval = false;
    /// <summary>The output writer for <c>io.println</c> and <c>io.print</c>. Defaults to <see cref="Console.Out"/>.</summary>
    private TextWriter _output = Console.Out;
    /// <summary>The error output writer. Defaults to <see cref="Console.Error"/>.</summary>
    private TextWriter _errorOutput = Console.Error;
    /// <summary>The input reader for <c>io.readLine</c>. Defaults to <see cref="Console.In"/>.</summary>
    private TextReader _input = Console.In;
    /// <summary>The capability flags controlling which built-in namespaces are available.</summary>
    private readonly StashCapabilities _capabilities;
    /// <summary>Token checked at each statement boundary to support cooperative cancellation.</summary>
    private CancellationToken _cancellationToken;
    /// <summary>Number of statements executed since the last reset.</summary>
    private long _stepCount;
    /// <summary>Maximum allowed statement count. Zero means unlimited.</summary>
    private long _stepLimit;

    /// <summary>
    /// Gets or sets the current file path being executed.
    /// Used for resolving relative import paths.
    /// </summary>
    public string? CurrentFile
    {
        get => _currentFile;
        set => _currentFile = value;
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
    /// Gets or sets the test harness. When set, test() and describe() report results to it.
    /// </summary>
    public Stash.Testing.ITestHarness? TestHarness
    {
        get => _testHarness;
        set => _testHarness = value;
    }

    /// <summary>
    /// Gets or sets the current describe block name for nesting test names.
    /// </summary>
    public string? CurrentDescribe
    {
        get => _currentDescribe;
        set => _currentDescribe = value;
    }

    /// <summary>
    /// Gets or sets the test filter patterns. When set, only tests whose fully
    /// qualified name matches one of the patterns will execute.
    /// </summary>
    public string[]? TestFilter
    {
        get => _testFilter;
        set => _testFilter = value;
    }

    /// <summary>
    /// Gets or sets whether the interpreter is in test discovery mode.
    /// In discovery mode, test() records names and locations but does not execute test bodies.
    /// </summary>
    public bool DiscoveryMode
    {
        get => _discoveryMode;
        set => _discoveryMode = value;
    }

    /// <summary>Gets the stack of <c>beforeEach</c> hook lists.</summary>
    internal List<List<IStashCallable>> BeforeEachHooks => _beforeEachHooks;
    /// <summary>Gets the stack of <c>afterEach</c> hook lists.</summary>
    internal List<List<IStashCallable>> AfterEachHooks => _afterEachHooks;
    /// <summary>Gets the stack of <c>afterAll</c> hook lists.</summary>
    internal List<List<IStashCallable>> AfterAllHooks => _afterAllHooks;

    /// <summary>
    /// Gets or sets the output writer used by io.println and io.print.
    /// Defaults to Console.Out. Override to capture or redirect output.
    /// </summary>
    public TextWriter Output
    {
        get => _output;
        set => _output = value;
    }

    /// <summary>
    /// Gets or sets the error output writer.
    /// Defaults to Console.Error. Override to capture or redirect error output.
    /// </summary>
    public TextWriter ErrorOutput
    {
        get => _errorOutput;
        set => _errorOutput = value;
    }

    /// <summary>
    /// Gets or sets the input reader used by io.readLine.
    /// Defaults to Console.In. Override to supply input programmatically.
    /// </summary>
    public TextReader Input
    {
        get => _input;
        set => _input = value;
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
        get => _cancellationToken;
        set => _cancellationToken = value;
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
    public long StepCount => _stepCount;

    /// <summary>
    /// Gets the current call stack as a read-only list.
    /// </summary>
    public IReadOnlyList<CallFrame> CallStack => _callStack;

    /// <summary>
    /// Gets all source files that have been loaded during execution (main script + imports).
    /// Used for DAP "loadedSources" request.
    /// </summary>
    public IReadOnlyCollection<string> LoadedSources => _loadedSources;

    /// <summary>
    /// Gets the source span of the statement currently being executed.
    /// Null when the interpreter is not executing. Useful for DAP to determine
    /// the current position when paused.
    /// </summary>
    public SourceSpan? CurrentSpan => _currentSpan;

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
        _environment = _globals;
        DefineBuiltIns();
    }

    /// <summary>
    /// Records the lexical scope distance for a variable reference, as computed by the Resolver.
    /// </summary>
    public void Resolve(Expr expr, int distance)
    {
        _locals[expr] = distance;
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
        if (_currentFile is not null && _loadedSources.Add(_currentFile))
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
            _debugger?.OnError(err, _callStack);
            throw err;
        }
        catch (ContinueException)
        {
            var err = new RuntimeError("'continue' used outside of a loop.");
            _debugger?.OnError(err, _callStack);
            throw err;
        }
        catch (ReturnException)
        {
            var err = new RuntimeError("'return' used outside of a function.");
            _debugger?.OnError(err, _callStack);
            throw err;
        }
    }

    /// <summary>Executes a single statement, checking for cancellation and step limits, invoking debug hooks, and dispatching to the visitor.</summary>
    /// <param name="stmt">The statement to execute.</param>
    private void Execute(Stmt stmt)
    {
        if (_cancellationToken.IsCancellationRequested)
        {
            throw new ScriptCancelledException();
        }

        if (_stepLimit > 0 && ++_stepCount > _stepLimit)
        {
            throw new StepLimitExceededException(_stepLimit);
        }

        _currentSpan = stmt.Span;
        _debugger?.OnBeforeExecute(stmt.Span, _environment);
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
        if (_currentFile is not null && _loadedSources.Add(_currentFile))
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
            _debugger?.OnError(err, _callStack);
            throw err;
        }
        catch (ContinueException)
        {
            var err = new RuntimeError("'continue' used outside of a loop.");
            _debugger?.OnError(err, _callStack);
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
        _stepCount = 0;
    }

    /// <summary>Executes a list of statements in the given environment, restoring the previous environment afterwards.</summary>
    /// <param name="statements">The statements to execute.</param>
    /// <param name="environment">The environment (scope) to execute in.</param>
    public void ExecuteBlock(List<Stmt> statements, Environment environment)
    {
        Environment previous = _environment;
        try
        {
            _environment = environment;
            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }
        }
        finally
        {
            _environment = previous;
        }
    }

    /// <summary>
    /// Evaluates an expression in the given environment, then restores the previous environment.
    /// Used by <see cref="StashLambda"/> for expression-body lambdas.
    /// </summary>
    public object? EvaluateInEnvironment(Expr expr, Environment environment)
    {
        Environment previous = _environment;
        bool previousAdHocEval = _isAdHocEval;
        try
        {
            _environment = environment;
            _isAdHocEval = true;
            return expr.Accept(this);
        }
        finally
        {
            _environment = previous;
            _isAdHocEval = previousAdHocEval;
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
        // Core built-ins are always registered (typeof, len, hash, range, etc.)
        GlobalBuiltIns.Register(_globals, _capabilities);

        // Safe built-ins — always available (pure computation, no system access)
        IoBuiltIns.Register(_globals);
        ConvBuiltIns.Register(_globals);
        ArrBuiltIns.Register(_globals);
        DictBuiltIns.Register(_globals);
        StrBuiltIns.Register(_globals);
        MathBuiltIns.Register(_globals);
        TimeBuiltIns.Register(_globals);
        JsonBuiltIns.Register(_globals);
        IniBuiltIns.Register(_globals);
        ConfigBuiltIns.Register(_globals);
        TestBuiltIns.Register(_globals);
        PathBuiltIns.Register(_globals);
        TplBuiltIns.Register(_globals);
        StoreBuiltIns.Register(_globals);
        CryptoBuiltIns.Register(_globals);
        EncodingBuiltIns.Register(_globals);
        TermBuiltIns.Register(_globals);
        SysBuiltIns.Register(_globals);

        // Capability-gated built-ins
        if (_capabilities.HasFlag(StashCapabilities.Environment))
        {
            EnvBuiltIns.Register(_globals);
        }

        if (_capabilities.HasFlag(StashCapabilities.Process))
        {
            ProcessBuiltIns.Register(_globals);
            ArgsBuiltIns.Register(_globals);
        }

        if (_capabilities.HasFlag(StashCapabilities.FileSystem))
        {
            FsBuiltIns.Register(_globals);
        }

        if (_capabilities.HasFlag(StashCapabilities.Network))
        {
            HttpBuiltIns.Register(_globals);
        }

        // Freeze all built-in namespaces for optimal read performance.
        foreach (var binding in _globals.GetAllBindings())
        {
            if (binding.Value is Types.StashNamespace ns)
            {
                ns.Freeze();
            }
        }
    }

    /// <summary>
    /// Cleans up all tracked processes on script exit.
    /// Sends SIGTERM, waits up to 3 seconds, then SIGKILL.
    /// </summary>
    public void CleanupTrackedProcesses()
    {
        foreach (var (_, osProcess) in TrackedProcesses)
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
        }
        TrackedProcesses.Clear();
        ProcessExitCallbacks.Clear();
    }
}
