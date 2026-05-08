namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers global built-in functions and types — the Stash names that are
/// reachable without a namespace qualifier (<c>typeof</c>, <c>len</c>, <c>range</c>, …)
/// plus all globally-visible structs and enums (error types, ExecOptions, etc.).
/// </summary>
[StashNamespace(Name = "")]
public static partial class GlobalBuiltIns
{
    // ── Functions ────────────────────────────────────────────────────────────

    /// <summary>Returns the type name of a value as a string.</summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns>A string such as 'int', 'string', 'array', 'dict', 'null', 'bool', 'float', or a struct/enum name.</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    public static StashValue Typeof(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        StashValue val = args[0];

        if (val.IsNull) return StashValue.FromObj("null");
        if (val.IsInt) return StashValue.FromObj("int");
        if (val.IsFloat) return StashValue.FromObj("float");
        if (val.IsBool) return StashValue.FromObj("bool");
        if (val.IsByte) return StashValue.FromObj("byte");

        object? obj = val.AsObj;
        return StashValue.FromObj(obj switch
        {
            null => "null",
            string => "string",
            List<StashValue> => "array",
            StashTypedArray ta => $"{ta.ElementTypeName}[]",
            StashSecret => "secret",
            StashError => "Error",
            StashInstance => "struct",
            StashStruct => "struct",
            StashEnumValue => "enum",
            StashEnum => "enum",
            StashInterface => "interface",
            StashDictionary => "dict",
            StashRange => "range",
            StashFuture => "Future",
            StashNamespace => "namespace",
            StashDuration => "duration",
            StashByteSize => "bytes",
            StashIpAddress => "ip",
            StashSemVer => "semver",
            IStashCallable => "function",
            _ => obj.GetType().Name.Contains("BoundMethod") ? "function" : ctx.ResolveRegisteredTypeName(obj)
        });
    }

    /// <summary>Parses a string into a semver value.</summary>
    /// <param name="value">A semantic version string in the format MAJOR.MINOR.PATCH (e.g. '1.2.3' or '2.0.0-beta.1').</param>
    /// <returns>A semver value representing the parsed version.</returns>
    [StashFn(ReturnType = "semver")]
    public static StashValue Semver(string value)
    {
        if (StashSemVer.TryParse(value, out StashSemVer? result))
        {
            return StashValue.FromObj(result!);
        }

        string detail = StashSemVer.ValidateFormat(value) ?? $"Invalid semantic version '{value}'.";
        throw new RuntimeError(detail, null);
    }

    /// <summary>Returns the name of a variable, function, struct, or enum as a string.</summary>
    /// <param name="value">The value to name.</param>
    /// <returns>The name of the value, or its type name for primitives.</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    public static StashValue Nameof(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        StashValue val = args[0];

        if (val.IsNull) return StashValue.FromObj("null");
        if (val.IsInt) return StashValue.FromObj("int");
        if (val.IsFloat) return StashValue.FromObj("float");
        if (val.IsBool) return StashValue.FromObj("bool");
        if (val.IsByte) return StashValue.FromObj("byte");

        object? obj = val.AsObj;
        return StashValue.FromObj(obj switch
        {
            null => "null",
            string => "string",
            List<StashValue> => "array",
            StashSecret => "secret",
            StashError => "Error",
            StashInstance inst => inst.TypeName,
            StashStruct s => s.Name,
            StashEnumValue ev => $"{ev.TypeName}.{ev.MemberName}",
            StashEnum e => e.Name,
            StashInterface i => i.Name,
            StashDictionary => "dict",
            StashRange => "range",
            StashFuture => "Future",
            StashNamespace => "namespace",
            Stash.Runtime.BuiltInFunction bf => bf.Name,
            IStashCallable c => c.Name ?? "function",
            _ => "unknown"
        });
    }

    /// <summary>Returns the length of a string, array, or dictionary.</summary>
    /// <param name="value">A string, array, or dict.</param>
    /// <returns>The number of characters, elements, or entries.</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    public static StashValue Len(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        StashValue val = args[0];
        if (val.IsObj)
        {
            object? obj = val.AsObj;
            if (obj is StashSecret sec)
            {
                object? inner = sec.InnerValue.IsObj ? sec.InnerValue.AsObj : null;
                if (inner is string innerStr) return StashValue.FromInt((long)innerStr.Length);
                if (inner is List<StashValue> innerList) return StashValue.FromInt((long)innerList.Count);
                if (inner is StashDictionary innerDict) return StashValue.FromInt((long)innerDict.Count);
                throw new RuntimeError("Argument to 'len' must be a string, array, or dictionary.");
            }
            if (obj is string s) return StashValue.FromInt((long)s.Length);
            if (obj is List<StashValue> svList) return StashValue.FromInt((long)svList.Count);
            if (obj is StashTypedArray typedArr) return StashValue.FromInt((long)typedArr.Count);
            if (obj is StashDictionary dict) return StashValue.FromInt((long)dict.Count);
        }
        throw new RuntimeError("Argument to 'len' must be a string, array, or dictionary.");
    }

    /// <summary>Returns the last error that was caught in a try/catch block, or null if none.</summary>
    /// <returns>The last caught Error value, or null.</returns>
    [StashFn(ReturnType = "Error")]
    public static StashValue LastError(IInterpreterContext ctx)
        => ctx.LastError is not null ? StashValue.FromObj(ctx.LastError) : StashValue.Null;

    /// <summary>Generates an array of integers. With one argument, generates 0..n-1. With two, generates start..end-1. With three, uses the given step.</summary>
    /// <param name="start_or_end">End (exclusive) when called with 1 arg, or start when called with 2-3 args.</param>
    /// <param name="end">End (exclusive) when called with 2 or 3 args.</param>
    /// <param name="step">Step size (positive or negative); must not be zero.</param>
    /// <returns>An array of integers.</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    public static StashValue Range(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1 || args.Length > 3) throw new RuntimeError("'range' expects 1 to 3 arguments.");
        long start, end, step;
        if (args.Length == 1)
        {
            var e = SvArgs.Long(args, 0, "range");
            start = 0; end = e; step = 1;
        }
        else if (args.Length == 2)
        {
            var s = SvArgs.Long(args, 0, "range");
            var e = SvArgs.Long(args, 1, "range");
            start = s; end = e; step = 1;
        }
        else
        {
            var s = SvArgs.Long(args, 0, "range");
            var e = SvArgs.Long(args, 1, "range");
            var st = SvArgs.Long(args, 2, "range");
            if (st == 0)
            {
                throw new RuntimeError("'range' step cannot be zero.");
            }

            start = s; end = e; step = st;
        }

        var result = new List<StashValue>();
        if (step > 0)
        {
            for (long i = start; i < end; i += step)
            {
                result.Add(StashValue.FromInt(i));
            }
        }
        else
        {
            for (long i = start; i > end; i += step)
            {
                result.Add(StashValue.FromInt(i));
            }
        }
        return StashValue.FromObj(result);
    }

    /// <summary>Terminates the program with the given exit code (default 0). Runs all pending defer blocks before terminating. Cannot be caught by try/catch.</summary>
    /// <param name="code">The exit code to return to the OS. Defaults to 0.</param>
    /// <returns>never</returns>
    [StashFn(Capability = StashCapabilities.Environment, ReturnType = "never")]
    public static void Exit(IInterpreterContext ctx, long code = 0L)
    {
        EmitExitImpl(ctx, code);
    }

    /// <summary>Returns a hash code for the given value.</summary>
    /// <param name="value">The value to hash.</param>
    /// <returns>An integer hash code.</returns>
    [StashFn]
    public static long Hash(StashValue value)
    {
        if (value.IsNull) return 0L;
        return (long)value.ToObject()!.GetHashCode();
    }

    /// <summary>Wraps a value as a secret. Secrets auto-redact when printed or interpolated.</summary>
    /// <param name="value">The value to protect.</param>
    /// <returns>A secret-wrapped value.</returns>
    [StashFn(ReturnType = "secret")]
    public static StashValue Secret(StashValue value)
        => StashValue.FromObj(new StashSecret(value));

    /// <summary>Unwraps a secret value, returning the real underlying value.</summary>
    /// <param name="value">The secret to unwrap.</param>
    /// <returns>The original value.</returns>
    [StashFn(ReturnType = "any")]
    public static StashValue Reveal([StashParam(Type = "secret")] StashValue value)
    {
        if (value.IsObj && value.AsObj is StashSecret sec)
        {
            return sec.Reveal();
        }
        throw new RuntimeError("Argument to 'reveal' must be a secret.");
    }

    // ── Enums ────────────────────────────────────────────────────────────────

    /// <summary>Backoff strategy for retry blocks.</summary>
    [StashEnum]
    public enum Backoff { Fixed, Linear, Exponential }

    /// <summary>POSIX-style signals used by process.signal and StreamingProcess.signal.</summary>
    [StashEnum]
    public enum Signal { Hup, Int, Quit, Kill, Usr1, Usr2, Term }

    /// <summary>Execution mode for process.exec and process.pipeline.</summary>
    [StashEnum]
    public enum ExecMode { Capture, Passthrough, Stream }

    // ── Command execution structs ────────────────────────────────────────────

    /// <summary>Options for process.exec and process.pipeline.</summary>
    [StashStruct]
    public sealed record ExecOptions
    {
        [StashField(Type = "ExecMode?")] public string? Mode { get; init; }
        public bool Strict { get; init; }
        [StashField(Type = "RedirectSpec?")] public string? Redirect { get; init; }
        [StashField(Type = "string?")] public string? Cwd { get; init; }
        [StashField(Type = "dict?")] public StashDictionary? Env { get; init; }
    }

    /// <summary>Output redirect specification for process.exec.</summary>
    [StashStruct]
    public sealed record RedirectSpec
    {
        public string Stream { get; init; } = "";
        public string Target { get; init; } = "";
        public bool Append { get; init; }
    }

    /// <summary>One stage in a process.pipeline call.</summary>
    [StashStruct]
    public sealed record PipelineStage
    {
        public string Program { get; init; } = "";
        public List<StashValue> Args { get; init; } = new();
    }

    /// <summary>Handle to a running external process spawned by streaming command syntax.</summary>
    [StashStruct]
    public sealed record StreamingProcess
    {
        public long Pid { get; init; }
        [StashField(Type = "int?")] public long? ExitCode { get; init; }
        [StashField(Type = "Signal?")] public string? Signal { get; init; }
    }

    // ── Typed error structs ──────────────────────────────────────────────────

    /// <summary>Error type. Returned by `try` on failure. Has `.message`, `.type`, and `.stack` fields.</summary>
    [StashStruct(Name = "Error")]
    public sealed record ErrorStruct
    {
        public string Message { get; init; } = "";
        public string Type { get; init; } = "";
        [StashField(Type = "array")] public List<StashValue> Stack { get; init; } = new();
    }

    [StashStruct(Name = StashErrorTypes.ValueError)]
    public sealed record ValueErrorStruct { public string Message { get; init; } = ""; }

    [StashStruct(Name = StashErrorTypes.TypeError)]
    public sealed record TypeErrorStruct { public string Message { get; init; } = ""; }

    [StashStruct(Name = StashErrorTypes.ParseError)]
    public sealed record ParseErrorStruct { public string Message { get; init; } = ""; }

    [StashStruct(Name = StashErrorTypes.IndexError)]
    public sealed record IndexErrorStruct { public string Message { get; init; } = ""; }

    [StashStruct(Name = StashErrorTypes.IOError)]
    public sealed record IOErrorStruct { public string Message { get; init; } = ""; }

    [StashStruct(Name = StashErrorTypes.NotSupportedError)]
    public sealed record NotSupportedErrorStruct { public string Message { get; init; } = ""; }

    [StashStruct(Name = StashErrorTypes.TimeoutError)]
    public sealed record TimeoutErrorStruct { public string Message { get; init; } = ""; }

    [StashStruct(Name = StashErrorTypes.CommandError)]
    public sealed record CommandErrorStruct
    {
        public string Message { get; init; } = "";
        public long ExitCode { get; init; }
        public string Stderr { get; init; } = "";
        public string Stdout { get; init; } = "";
        public string Command { get; init; } = "";
    }

    [StashStruct(Name = StashErrorTypes.LockError)]
    public sealed record LockErrorStruct
    {
        public string Message { get; init; } = "";
        public string Path { get; init; } = "";
    }

    [StashStruct(Name = StashErrorTypes.AliasError)]
    public sealed record AliasErrorStruct
    {
        public string Message { get; init; } = "";
        [StashField(Type = "string?")] public string? AliasName { get; init; }
        [StashField(Type = "string?")] public string? Detail { get; init; }
    }

    [StashStruct(Name = StashErrorTypes.StateError)]
    public sealed record StateErrorStruct { public string Message { get; init; } = ""; }

    [StashStruct(Name = StashErrorTypes.CancellationError)]
    public sealed record CancellationErrorStruct { public string Message { get; init; } = ""; }

    // ── Alias-related structs ────────────────────────────────────────────────

    /// <summary>Source location of an alias definition.</summary>
    [StashStruct]
    public sealed record SourceLoc
    {
        public string File { get; init; } = "";
        public long Line { get; init; }
    }

    /// <summary>Information about a single alias parameter.</summary>
    [StashStruct]
    public sealed record ParamInfo
    {
        public string Name { get; init; } = "";
        [StashField(Type = "string?")] public string? Type { get; init; }
        public bool Rest { get; init; }
        [StashField(Type = "any?")] public StashValue Default { get; init; }
    }

    /// <summary>Options accepted by alias.add.</summary>
    [StashStruct]
    public sealed record AliasOptions
    {
        [StashField(Type = "string?")] public string? Description { get; init; }
        [StashField(Type = "function?")] public IStashCallable? Before { get; init; }
        [StashField(Type = "function?")] public IStashCallable? After { get; init; }
        [StashField(Type = "string?")] public string? Confirm { get; init; }
        public bool Override { get; init; }
    }

    /// <summary>Detail record describing a registered alias.</summary>
    [StashStruct]
    public sealed record AliasInfo
    {
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Body { get; init; } = "";
        public List<StashValue> Params { get; init; } = new();
        [StashField(Type = "string?")] public string? Description { get; init; }
        public bool HasBefore { get; init; }
        public bool HasAfter { get; init; }
        [StashField(Type = "string?")] public string? Confirm { get; init; }
        public string Source { get; init; } = "";
        [StashField(Type = "SourceLoc?")] public string? SourceLoc { get; init; }
    }

    // ── Prompt-related structs ───────────────────────────────────────────────

    /// <summary>Git portion of a prompt context.</summary>
    [StashStruct]
    public sealed record PromptGit
    {
        public bool IsInRepo { get; init; }
        public string Branch { get; init; } = "";
        public bool IsDirty { get; init; }
        public long StagedCount { get; init; }
        public long UnstagedCount { get; init; }
        public long UntrackedCount { get; init; }
        public long Ahead { get; init; }
        public long Behind { get; init; }
    }

    /// <summary>Context value passed to user-defined shell prompt callbacks.</summary>
    [StashStruct]
    public sealed record PromptContext
    {
        public string Cwd { get; init; } = "";
        public string CwdAbsolute { get; init; } = "";
        public string User { get; init; } = "";
        public string Host { get; init; } = "";
        public string HostFull { get; init; } = "";
        public double Time { get; init; }
        public long LastExitCode { get; init; }
        public long LineNumber { get; init; }
        public string Mode { get; init; } = "";
        public string HostColor { get; init; } = "";
        [StashField(Type = "PromptGit")] public string Git { get; init; } = "";
    }

    // ── Retry-related structs ────────────────────────────────────────────────

    /// <summary>Options accepted by retry blocks.</summary>
    [StashStruct]
    public sealed record RetryOptions
    {
        [StashField(Type = "duration")] public StashValue Delay { get; init; }
        [StashField(Type = "Backoff")] public string Backoff { get; init; } = "";
        [StashField(Type = "duration")] public StashValue MaxDelay { get; init; }
        public bool Jitter { get; init; }
        [StashField(Type = "duration")] public StashValue Timeout { get; init; }
        public List<StashValue> On { get; init; } = new();
    }

    /// <summary>Attempt context exposed to retry block bodies as `attempt`.</summary>
    [StashStruct]
    public sealed record RetryContext
    {
        public long Current { get; init; }
        public long Max { get; init; }
        public long Remaining { get; init; }
        [StashField(Type = "duration")] public StashValue Elapsed { get; init; }
        public List<StashValue> Errors { get; init; } = new();
    }

    // ── Completion-related structs ───────────────────────────────────────────

    /// <summary>Context passed to completion callbacks.</summary>
    [StashStruct]
    public sealed record CompletionContext
    {
        public string Command { get; init; } = "";
        public List<StashValue> Args { get; init; } = new();
        public string Current { get; init; } = "";
        public long Position { get; init; }
        public string Mode { get; init; } = "";
    }

    /// <summary>Result returned from a completion callback.</summary>
    [StashStruct]
    public sealed record CompletionResult
    {
        [StashField(Name = "replace_start")] public long ReplaceStart { get; init; }
        [StashField(Name = "replace_end")] public long ReplaceEnd { get; init; }
        public List<StashValue> Candidates { get; init; } = new();
        [StashField(Name = "common_prefix")] public string CommonPrefix { get; init; } = "";
    }

    // ── Class-level non-Stash members ────────────────────────────────────────

    /// <summary>
    /// Maps a Signal enum member name to its POSIX signal number.
    /// Used by process.signal() to accept both Signal.Term and raw integers.
    /// Members not in the table are forwarded as-is via the integer path.
    /// </summary>
    public static readonly System.Collections.Generic.Dictionary<string, long> SignalNumbers = new()
    {
        ["Hup"]  = 1,
        ["Int"]  = 2,
        ["Quit"] = 3,
        ["Kill"] = 9,
        ["Usr1"] = 10,
        ["Usr2"] = 12,
        ["Term"] = 15,
    };

    /// <summary>
    /// Shared implementation for the global <c>exit()</c> and <c>env.exit()</c> built-ins.
    /// Cleans up tracked processes (via Process capability if available) then exits.
    /// </summary>
    internal static void EmitExitImpl(IInterpreterContext ctx, long code)
    {
        ctx.CleanupTrackedProcesses();
        ctx.EmitExit((int)code);
    }
}
