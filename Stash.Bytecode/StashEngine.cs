namespace Stash.Bytecode;

using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Stdlib;
using Stash.Runtime.Types;
using Stash.Stdlib;

/// <summary>
/// High-level API for embedding the Stash scripting language in a C# application.
/// Wraps the lexer, parser, and interpreter into a single easy-to-use interface.
/// </summary>
/// <example>
/// <code>
/// var engine = new StashEngine();
/// engine.SetGlobal("playerName", "Alice");
/// engine.SetGlobal("getHealth", engine.CreateFunction("getHealth", 0,
///     (args) => player.Health));
///
/// var result = engine.Execute("'Hello, ' + playerName");
/// var greeting = engine.GetGlobal("greeting");
/// </code>
/// </example>
public class StashEngine
{
    private readonly StashCapabilities _capabilities;
    private readonly List<IStdlibProvider> _additionalProviders = [];
    private readonly List<Action<VirtualMachine>> _pendingTypeRegistrations = [];
    private VirtualMachine? _vm;
    private TextWriter _output = TextWriter.Null;
    private TextWriter _errorOutput = TextWriter.Null;
    private TextReader _input = TextReader.Null;
    private CancellationToken _cancellationToken;
    private long _stepLimit;

    /// <summary>
    /// Creates a new Stash scripting engine with all capabilities enabled.
    /// </summary>
    public StashEngine() : this(StashCapabilities.All)
    {
    }

    /// <summary>
    /// Creates a new Stash scripting engine with the specified capabilities.
    /// Use <see cref="StashCapabilities.None"/> for a fully sandboxed environment.
    /// </summary>
    public StashEngine(StashCapabilities capabilities)
    {
        _capabilities = capabilities;
    }

    /// <summary>
    /// Gets or sets the text writer used for script output (io.println, io.print).
    /// Defaults to <see cref="TextWriter.Null"/>; set explicitly before execution.
    /// </summary>
    public TextWriter Output
    {
        get => _output;
        set { _output = value; if (_vm is not null)
            {
                _vm.Output = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the text writer used for error output.
    /// Defaults to <see cref="TextWriter.Null"/>; set explicitly before execution.
    /// </summary>
    public TextWriter ErrorOutput
    {
        get => _errorOutput;
        set { _errorOutput = value; if (_vm is not null)
            {
                _vm.ErrorOutput = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the text reader used for script input (io.readLine).
    /// Defaults to <see cref="TextReader.Null"/>; set explicitly before execution.
    /// </summary>
    public TextReader Input
    {
        get => _input;
        set { _input = value; if (_vm is not null)
            {
                _vm.Input = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a cancellation token to abort script execution.
    /// </summary>
    public CancellationToken CancellationToken
    {
        get => _cancellationToken;
        set => _cancellationToken = value;
    }

    /// <summary>
    /// Gets or sets the maximum number of statements the engine will execute
    /// before throwing <see cref="StepLimitExceededException"/>.
    /// A value of 0 (default) means no limit.
    /// </summary>
    public long StepLimit
    {
        get => _stepLimit;
        set { _stepLimit = value; if (_vm is not null)
            {
                _vm.StepLimit = value;
            }
        }
    }

    /// <summary>
    /// Gets the number of statements executed since the last reset.
    /// </summary>
    public long StepCount => _vm?.StepCount ?? 0;

    /// <summary>
    /// When true (default), the bytecode peephole optimizer runs during compilation,
    /// fusing common instruction sequences into superinstructions.
    /// </summary>
    public bool OptimizeBytecode { get; set; } = true;

    /// <summary>
    /// When true (default), the trivial dead-code elimination pass runs during compilation,
    /// removing pure instructions whose result register is never read.
    /// </summary>
    public bool EnableDce { get; set; } = true;

    /// <summary>
    /// When true (default), compilation uses the pass-pipeline framework (CFG construction +
    /// registered optimization passes).  Set to false to use the legacy direct-mutation path.
    /// </summary>
    public bool EnableOptimizationPipeline { get; set; } = true;

    /// <summary>
    /// Adds a custom stdlib provider whose namespaces and globals will be
    /// merged into the VM alongside Stash's built-in standard library.
    /// Must be called before the first script execution.
    /// </summary>
    public StashEngine AddStdlibProvider(IStdlibProvider provider)
    {
        if (_vm is not null)
            throw new InvalidOperationException("Cannot add stdlib providers after the VM has been created.");
        _additionalProviders.Add(provider);
        return this;
    }

    /// <summary>
    /// Register an external CLR type with the VM for typeof and is operator support.
    /// Must be called before executing any scripts.
    /// </summary>
    public StashEngine RegisterType<T>(string vmTypeName, Func<object, bool>? predicate = null) where T : class
    {
        if (_vm is not null)
            throw new InvalidOperationException("Cannot register types after the VM has been created. Call RegisterType before executing scripts.");

        _pendingTypeRegistrations.Add(vm =>
        {
            vm.RegisterTypeName<T>(vmTypeName);
            vm.RegisterTypeCheck(vmTypeName, predicate ?? (obj => obj is T));
        });
        return this;
    }

    /// <summary>Creates and configures the bytecode VM with built-in globals.</summary>
    private VirtualMachine EnsureVM()
    {
        if (_vm is not null)
        {
            return _vm;
        }

        Dictionary<string, StashValue> vmGlobals;
        if (_additionalProviders.Count > 0)
        {
            var composer = new StdlibComposer()
                .Add(new StashStdlibProvider())
                .WithCapabilities(_capabilities);
            foreach (IStdlibProvider provider in _additionalProviders)
                composer.Add(provider);
            vmGlobals = composer.Build();
        }
        else
        {
            vmGlobals = StdlibDefinitions.CreateVMGlobals(_capabilities);
        }

        _vm = new VirtualMachine(vmGlobals, _cancellationToken)
        {
            Output = _output,
            ErrorOutput = _errorOutput,
            Input = _input,
            StepLimit = _stepLimit,
            EmbeddedMode = true,
            ModuleLoader = LoadModuleForVM
        };
        foreach (var registration in _pendingTypeRegistrations)
            registration(_vm);
        return _vm;
    }

    /// <summary>Module loading callback for the bytecode VM.</summary>
    private Chunk LoadModuleForVM(string modulePath, string? currentFile) =>
        StashCompilationPipeline.LoadModule(modulePath, currentFile);

    /// <summary>
    /// Executes Stash source code as statements (e.g., variable declarations, function
    /// definitions, control flow). Returns null for statements.
    /// </summary>
    /// <param name="source">Stash source code to execute.</param>
    /// <returns>A result containing any errors that occurred, or success.</returns>
    public ExecutionResult Run(string source)
    {
        var (statements, errors) = ParseStatements(source);
        if (errors.Count > 0)
        {
            return new ExecutionResult(null, errors);
        }

        try
        {
            SemanticResolver.Resolve(statements);
            Chunk chunk = Compiler.Compile(statements);
            var vm = EnsureVM();
            SyncVMSettings(vm);
            object? result = vm.Execute(chunk);
            return new ExecutionResult(result, []);
        }
        catch (ExitException ex)
        {
            return new ExecutionResult(null, [$"Script exited with code {ex.ExitCode}"]);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult(null, ["Script execution was cancelled."]);
        }
        catch (StepLimitExceededException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
        catch (RuntimeError ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
    }

    /// <summary>
    /// Evaluates a Stash expression and returns its value.
    /// </summary>
    /// <param name="expression">A Stash expression (e.g., "2 + 2", "playerName").</param>
    /// <returns>A result containing the computed value or any errors.</returns>
    public ExecutionResult Evaluate(string expression)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.ScanTokens();

        if (lexer.Errors.Count > 0)
        {
            return new ExecutionResult(null, lexer.Errors);
        }

        var parser = new Parser(tokens);
        var expr = parser.Parse();

        if (parser.Errors.Count > 0)
        {
            return new ExecutionResult(null, parser.Errors);
        }

        try
        {
            Chunk chunk = Compiler.CompileExpression(expr);
            var vm = EnsureVM();
            SyncVMSettings(vm);
            object? result = vm.Execute(chunk);
            return new ExecutionResult(result, []);
        }
        catch (ExitException ex)
        {
            return new ExecutionResult(null, [$"Script exited with code {ex.ExitCode}"]);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult(null, ["Script execution was cancelled."]);
        }
        catch (StepLimitExceededException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
        catch (RuntimeError ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
    }

    private void SyncVMSettings(VirtualMachine vm)
    {
        vm.StepLimit = _stepLimit;
        vm.Output = _output;
        vm.ErrorOutput = _errorOutput;
        vm.Input = _input;
    }

    /// <summary>
    /// Pre-compiles Stash source code into a reusable script object.
    /// The source is lexed and parsed once; the resulting AST can be executed
    /// multiple times without re-parsing.
    /// </summary>
    /// <param name="source">Stash source code to compile.</param>
    /// <returns>A compiled script, or a result with errors if compilation failed.</returns>
    public StashScript? Compile(string source)
    {
        var (statements, errors) = ParseStatements(source);
        if (errors.Count > 0)
        {
            return null;
        }

        return new StashScript(statements);
    }

    /// <summary>
    /// Pre-compiles Stash source code, returning a result with errors if compilation failed.
    /// </summary>
    public StashScript? Compile(string source, out IReadOnlyList<string> errors)
    {
        var (statements, parseErrors) = ParseStatements(source);
        errors = parseErrors;
        if (parseErrors.Count > 0)
        {
            return null;
        }

        return new StashScript(statements);
    }

    /// <summary>
    /// Executes a pre-compiled script.
    /// </summary>
    public ExecutionResult Run(StashScript script)
    {
        if (!script.IsResolved)
        {
            SemanticResolver.Resolve(script.Statements);
            script.IsResolved = true;
        }

        try
        {
            Chunk chunk = Compiler.Compile(script.Statements);
            var vm = EnsureVM();
            SyncVMSettings(vm);
            object? result = vm.Execute(chunk);
            return new ExecutionResult(result, []);
        }
        catch (ExitException ex)
        {
            return new ExecutionResult(null, [$"Script exited with code {ex.ExitCode}"]);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult(null, ["Script execution was cancelled."]);
        }
        catch (StepLimitExceededException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
        catch (RuntimeError ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
    }

    /// <summary>
    /// Loads and executes a Stash script file. Sets the file path for import resolution.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the .stash file.</param>
    /// <returns>A result containing any errors that occurred, or success.</returns>
    public ExecutionResult RunFile(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);

        if (!File.Exists(fullPath))
        {
            return new ExecutionResult(null, [$"File not found: {fullPath}"]);
        }

        string source = File.ReadAllText(fullPath);
        var vm = EnsureVM();
        vm.CurrentFile = fullPath;
        return Run(source);
    }

    /// <summary>
    /// Sets a global variable accessible to Stash scripts.
    /// Supports CLR types: long, double, string, bool, null, List&lt;object?&gt;.
    /// </summary>
    public void SetGlobal(string name, object? value)
    {
        EnsureVM().Globals[name] = StashValue.FromObject(value);
    }

    /// <summary>
    /// Gets the value of a global variable set by a Stash script.
    /// Returns null if the variable doesn't exist.
    /// </summary>
    public object? GetGlobal(string name)
    {
        return EnsureVM().Globals.TryGetValue(name, out StashValue value) ? value.ToObject() : null;
    }

    /// <summary>
    /// Creates a built-in function that can be registered as a global.
    /// </summary>
    /// <param name="name">Function name (for error messages).</param>
    /// <param name="arity">Number of parameters (-1 for variadic).</param>
    /// <param name="body">The C# implementation. Receives a list of arguments and returns a value.</param>
    /// <returns>A function object that can be passed to <see cref="SetGlobal"/>.</returns>
    public BuiltInFunction CreateFunction(string name, int arity, Func<List<object?>, object?> body)
    {
        return new BuiltInFunction(name, arity, (ctx, args) =>
        {
            var list = new List<object?>(args.Length);
            foreach (StashValue sv in args)
                list.Add(sv.ToObject());
            return StashValue.FromObject(body(list));
        });
    }

    /// <summary>
    /// Creates a built-in function with access to the interpreter instance.
    /// Use this when the function needs to call interpreter methods (e.g., output, stringify).
    /// </summary>
    public BuiltInFunction CreateFunction(string name, int arity, Func<IInterpreterContext, List<object?>, object?> body)
    {
        return new BuiltInFunction(name, arity, (ctx, args) =>
        {
            var list = new List<object?>(args.Length);
            foreach (StashValue sv in args)
                list.Add(sv.ToObject());
            return StashValue.FromObject(body(ctx, list));
        });
    }

    /// <summary>
    /// Converts a Stash runtime value to its string representation.
    /// </summary>
    public string Stringify(object? value) => StashTypeConverter.Stringify(value);

    /// <summary>
    /// Converts a Stash dictionary to a .NET dictionary.
    /// Keys are converted to strings via <see cref="Stringify"/>.
    /// </summary>
    public Dictionary<string, object?> ToDictionary(object? value) => StashTypeConverter.ToDictionary(value);

    /// <summary>
    /// Converts a Stash struct instance to a .NET dictionary of field name → value.
    /// </summary>
    public Dictionary<string, object?> ToFieldDictionary(object? value) => StashTypeConverter.ToFieldDictionary(value);

    /// <summary>
    /// Converts a Stash array to a .NET list.
    /// </summary>
    public List<StashValue> ToList(object? value) => StashTypeConverter.ToList(value);

    /// <summary>
    /// Creates a Stash dictionary from a .NET dictionary.
    /// </summary>
    public StashDictionary CreateDictionary(IDictionary<string, object?> values) => StashTypeConverter.CreateDictionary(values);

    /// <summary>Lexes and parses Stash source code into a list of statements, collecting any errors.</summary>
    private (List<Stmt> Statements, List<string> Errors) ParseStatements(string source) =>
        StashCompilationPipeline.ParseStatements(source);
}

/// <summary>
/// The result of executing or evaluating Stash code.
/// </summary>
public class ExecutionResult
{
    /// <summary>The return value (for expressions) or null (for statements).</summary>
    public object? Value { get; }

    /// <summary>Any errors that occurred during lexing, parsing, or execution.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True if execution completed without errors.</summary>
    public bool Success => Errors.Count == 0;

    /// <summary>Creates a new execution result.</summary>
    /// <param name="value">The result value (for expressions) or null (for statements).</param>
    /// <param name="errors">Any error messages from lexing, parsing, or execution.</param>
    public ExecutionResult(object? value, List<string> errors)
    {
        Value = value;
        Errors = errors;
    }
}

/// <summary>
/// A pre-compiled Stash script that can be executed multiple times
/// without re-lexing and re-parsing.
/// </summary>
public class StashScript
{
    /// <summary>Gets the pre-parsed AST statements.</summary>
    internal List<Stmt> Statements { get; }
    /// <summary>Gets or sets whether the resolver has been run on these statements.</summary>
    internal bool IsResolved { get; set; }

    /// <summary>Creates a new pre-compiled script from parsed statements.</summary>
    /// <param name="statements">The parsed AST statements.</param>
    internal StashScript(List<Stmt> statements)
    {
        Statements = statements;
    }
}
