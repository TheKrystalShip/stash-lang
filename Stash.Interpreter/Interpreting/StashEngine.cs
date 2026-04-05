namespace Stash.Interpreting;

using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Interpreting.Exceptions;
using ExitException = Stash.Runtime.ExitException;
using ScriptCancelledException = Stash.Runtime.ScriptCancelledException;
using StepLimitExceededException = Stash.Runtime.StepLimitExceededException;

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
    /// <summary>The underlying interpreter instance that executes Stash code.</summary>
    private readonly Interpreter _interpreter;
    private readonly StashCapabilities _capabilities;
    private VirtualMachine? _vm;

    /// <summary>
    /// Gets or sets the execution backend. Defaults to <see cref="ExecutionBackend.Bytecode"/>.
    /// </summary>
    public ExecutionBackend Backend { get; set; } = ExecutionBackend.Bytecode;

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
        _interpreter = new Interpreter(capabilities);
        _interpreter.EmbeddedMode = true;
    }

    /// <summary>
    /// Gets or sets the text writer used for script output (io.println, io.print).
    /// Defaults to <see cref="Console.Out"/>.
    /// </summary>
    public TextWriter Output
    {
        get => _interpreter.Output;
        set => _interpreter.Output = value;
    }

    /// <summary>
    /// Gets or sets the text writer used for error output.
    /// Defaults to <see cref="Console.Error"/>.
    /// </summary>
    public TextWriter ErrorOutput
    {
        get => _interpreter.ErrorOutput;
        set => _interpreter.ErrorOutput = value;
    }

    /// <summary>
    /// Gets or sets the text reader used for script input (io.readLine).
    /// Defaults to <see cref="Console.In"/>.
    /// </summary>
    public TextReader Input
    {
        get => _interpreter.Input;
        set => _interpreter.Input = value;
    }

    /// <summary>
    /// Gets or sets a cancellation token to abort script execution.
    /// When cancelled, the engine throws <see cref="ScriptCancelledException"/>
    /// at the next statement boundary.
    /// </summary>
    public CancellationToken CancellationToken
    {
        get => _interpreter.CancellationToken;
        set => _interpreter.CancellationToken = value;
    }

    /// <summary>
    /// Gets or sets the maximum number of statements the engine will execute
    /// before throwing <see cref="StepLimitExceededException"/>.
    /// A value of 0 (default) means no limit.
    /// </summary>
    public long StepLimit
    {
        get => _interpreter.StepLimit;
        set => _interpreter.StepLimit = value;
    }

    /// <summary>
    /// Gets the number of statements executed since the last reset.
    /// </summary>
    public long StepCount => Backend == ExecutionBackend.Bytecode && _vm is not null
        ? _vm.StepCount
        : _interpreter.StepCount;

    /// <summary>
    /// Provides direct access to the underlying interpreter for advanced scenarios.
    /// </summary>
    public Interpreter Interpreter => _interpreter;

    /// <summary>Creates and configures the bytecode VM with built-in globals.</summary>
    private VirtualMachine EnsureVM()
    {
        if (_vm is not null)
            return _vm;

        var vmGlobals = new Dictionary<string, object?>();

        // Register global functions (capability-filtered)
        var globalDef = StdlibDefinitions.GetGlobals(_capabilities);
        foreach (var (name, fn) in globalDef.RuntimeFunctions)
            vmGlobals[name] = fn;

        // Register namespace definitions, filtering by capabilities
        foreach (var nsDef in StdlibDefinitions.Namespaces)
        {
            if (nsDef.RequiredCapability != StashCapabilities.None &&
                !_capabilities.HasFlag(nsDef.RequiredCapability))
                continue;

            vmGlobals[nsDef.Name] = nsDef.Namespace;
        }

        // Register built-in types for retry blocks
        vmGlobals["Backoff"] = new StashEnum("Backoff", new List<string> { "Fixed", "Linear", "Exponential" });
        vmGlobals["RetryOptions"] = new StashStruct("RetryOptions",
            new List<string> { "delay", "backoff", "maxDelay", "jitter", "timeout", "on" },
            new Dictionary<string, IStashCallable>());

        _vm = new VirtualMachine(vmGlobals, CancellationToken);
        _vm.Output = Output;
        _vm.ErrorOutput = ErrorOutput;
        _vm.Input = Input;
        _vm.StepLimit = StepLimit;
        _vm.EmbeddedMode = true;
        _vm.ModuleLoader = LoadModuleForVM;
        return _vm;
    }

    /// <summary>Module loading callback for the bytecode VM.</summary>
    private Chunk LoadModuleForVM(string modulePath, string? currentFile)
    {
        string? basePath = currentFile is not null ? Path.GetDirectoryName(currentFile) : null;
        string fullPath;

        if (Path.IsPathRooted(modulePath))
        {
            fullPath = modulePath;
        }
        else if (basePath is not null)
        {
            fullPath = Path.GetFullPath(Path.Combine(basePath, modulePath));
        }
        else
        {
            fullPath = Path.GetFullPath(modulePath);
        }

        string? resolvedPath = ModuleResolver.ResolveFilePath(fullPath);
        if (resolvedPath is null && basePath is not null && ModuleResolver.IsBareSpecifier(modulePath))
            resolvedPath = ModuleResolver.ResolvePackageImport(modulePath, basePath);

        if (resolvedPath is null)
            throw new RuntimeError($"Cannot find module '{modulePath}'.", null);

        string source = File.ReadAllText(resolvedPath);
        var lexer = new Lexer(source, resolvedPath);
        var tokens = lexer.ScanTokens();
        if (lexer.Errors.Count > 0)
            throw new RuntimeError($"Lex errors in module '{resolvedPath}': {lexer.Errors[0]}", null);

        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        if (parser.Errors.Count > 0)
            throw new RuntimeError($"Parse errors in module '{resolvedPath}': {parser.Errors[0]}", null);

        _interpreter.ResolveStatements(stmts);
        return Compiler.Compile(stmts);
    }

    /// <summary>
    /// Executes Stash source code as statements (e.g., variable declarations, function
    /// definitions, control flow). Returns null for statements.
    /// </summary>
    /// <param name="source">Stash source code to execute.</param>
    /// <returns>A result containing any errors that occurred, or success.</returns>
    public ExecutionResult Run(string source)
    {
        _interpreter.ResetStepCount();
        var (statements, errors) = ParseStatements(source);
        if (errors.Count > 0)
        {
            return new ExecutionResult(null, errors);
        }

        if (Backend == ExecutionBackend.Bytecode)
            return RunBytecode(statements);

        return RunTreeWalk(statements);
    }

    private ExecutionResult RunBytecode(List<Stmt> statements)
    {
        try
        {
            _interpreter.ResolveStatements(statements);
            Chunk chunk = Compiler.Compile(statements);
            var vm = EnsureVM();
            vm.StepLimit = StepLimit;
            vm.Output = Output;
            vm.ErrorOutput = ErrorOutput;
            vm.Input = Input;
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
        catch (Stash.Runtime.StepLimitExceededException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
        catch (RuntimeError ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
    }

    private ExecutionResult RunBytecodeResolved(List<Stmt> statements)
    {
        try
        {
            Chunk chunk = Compiler.Compile(statements);
            var vm = EnsureVM();
            vm.StepLimit = StepLimit;
            vm.Output = Output;
            vm.ErrorOutput = ErrorOutput;
            vm.Input = Input;
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
        catch (Stash.Runtime.StepLimitExceededException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
        catch (RuntimeError ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
    }

    private ExecutionResult RunTreeWalk(List<Stmt> statements)
    {
        try
        {
            _interpreter.Interpret(statements);
            return new ExecutionResult(null, []);
        }
        catch (ExitException ex)
        {
            return new ExecutionResult(null, [$"Script exited with code {ex.ExitCode}"]);
        }
        catch (ScriptCancelledException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
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
        _interpreter.ResetStepCount();
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

        if (Backend == ExecutionBackend.Bytecode)
            return EvaluateBytecode(expr);

        return EvaluateTreeWalk(expr);
    }

    private ExecutionResult EvaluateBytecode(Expr expr)
    {
        try
        {
            Chunk chunk = Compiler.CompileExpression(expr);
            var vm = EnsureVM();
            vm.StepLimit = StepLimit;
            vm.Output = Output;
            vm.ErrorOutput = ErrorOutput;
            vm.Input = Input;
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
        catch (Stash.Runtime.StepLimitExceededException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
        catch (RuntimeError ex)
        {
            return new ExecutionResult(null, [ex.Message]);
        }
    }

    private ExecutionResult EvaluateTreeWalk(Expr expr)
    {
        try
        {
            var value = _interpreter.Interpret(expr);
            return new ExecutionResult(value, []);
        }
        catch (ExitException ex)
        {
            return new ExecutionResult(null, [$"Script exited with code {ex.ExitCode}"]);
        }
        catch (ScriptCancelledException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
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
        _interpreter.ResetStepCount();

        if (!script.IsResolved)
        {
            _interpreter.ResolveStatements(script.Statements);
            script.IsResolved = true;
        }

        if (Backend == ExecutionBackend.Bytecode)
            return RunBytecodeResolved(script.Statements);

        try
        {
            _interpreter.InterpretResolved(script.Statements);
            return new ExecutionResult(null, []);
        }
        catch (ExitException ex)
        {
            return new ExecutionResult(null, [$"Script exited with code {ex.ExitCode}"]);
        }
        catch (ScriptCancelledException ex)
        {
            return new ExecutionResult(null, [ex.Message]);
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
        _interpreter.CurrentFile = fullPath;
        if (Backend == ExecutionBackend.Bytecode)
        {
            var vm = EnsureVM();
            vm.CurrentFile = fullPath;
        }
        return Run(source);
    }

    /// <summary>
    /// Sets a global variable accessible to Stash scripts.
    /// Supports CLR types: long, double, string, bool, null, List&lt;object?&gt;.
    /// </summary>
    public void SetGlobal(string name, object? value)
    {
        _interpreter.Globals.Define(name, value);
    }

    /// <summary>
    /// Gets the value of a global variable set by a Stash script.
    /// Returns null if the variable doesn't exist.
    /// </summary>
    public object? GetGlobal(string name)
    {
        return _interpreter.Globals.TryGet(name, out var value) ? value : null;
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
        return new BuiltInFunction(name, arity, (interp, args) => body(args));
    }

    /// <summary>
    /// Creates a built-in function with access to the interpreter instance.
    /// Use this when the function needs to call interpreter methods (e.g., output, stringify).
    /// </summary>
    public BuiltInFunction CreateFunction(string name, int arity, Func<IInterpreterContext, List<object?>, object?> body)
    {
        return new BuiltInFunction(name, arity, body);
    }

    /// <summary>
    /// Converts a Stash runtime value to its string representation.
    /// </summary>
    public string Stringify(object? value) => _interpreter.Stringify(value);

    /// <summary>
    /// Converts a Stash dictionary to a .NET dictionary.
    /// Keys are converted to strings via <see cref="Stringify"/>.
    /// </summary>
    public Dictionary<string, object?> ToDictionary(object? value)
    {
        if (value is not StashDictionary dict)
        {
            throw new ArgumentException($"Expected a Stash dictionary, got {value?.GetType().Name ?? "null"}.");
        }

        var result = new Dictionary<string, object?>();
        foreach (var entry in dict.RawEntries())
        {
            result[_interpreter.Stringify(entry.Key)] = entry.Value;
        }
        return result;
    }

    /// <summary>
    /// Converts a Stash struct instance to a .NET dictionary of field name → value.
    /// </summary>
    public Dictionary<string, object?> ToFieldDictionary(object? value)
    {
        if (value is not StashInstance instance)
        {
            throw new ArgumentException($"Expected a Stash struct instance, got {value?.GetType().Name ?? "null"}.");
        }

        return new Dictionary<string, object?>(instance.GetFields());
    }

    /// <summary>
    /// Converts a Stash array to a .NET list.
    /// </summary>
    public List<object?> ToList(object? value)
    {
        if (value is not List<object?> list)
        {
            throw new ArgumentException($"Expected a Stash array, got {value?.GetType().Name ?? "null"}.");
        }

        return new List<object?>(list);
    }

    /// <summary>
    /// Creates a Stash dictionary from a .NET dictionary.
    /// </summary>
    public StashDictionary CreateDictionary(IDictionary<string, object?> values)
    {
        var dict = new StashDictionary();
        foreach (var kvp in values)
        {
            dict.Set(kvp.Key, kvp.Value);
        }
        return dict;
    }

    /// <summary>Lexes and parses Stash source code into a list of statements, collecting any errors.</summary>
    /// <param name="source">The Stash source code to parse.</param>
    /// <returns>A tuple of parsed statements and any lex/parse error messages.</returns>
    private (List<Stmt> Statements, List<string> Errors) ParseStatements(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        if (lexer.Errors.Count > 0)
        {
            return ([], lexer.Errors);
        }

        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();

        if (parser.Errors.Count > 0)
        {
            return ([], parser.Errors);
        }

        return (statements, []);
    }
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
