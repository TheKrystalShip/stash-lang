namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Scheduler;
using Stash.Scheduler.Logging;
using Stash.Scheduler.Models;
using Stash.Stdlib.Abstractions;

[StashNamespace(Capability = StashCapabilities.Process | StashCapabilities.FileSystem)]
public static partial class SchedulerBuiltIns
{
    // ── Struct declarations ───────────────────────────────────────────────────

    /// <summary>Definition for installing a Stash script as an OS-managed service.</summary>
    [StashStruct]
    public sealed record ServiceDef(
        string Name,
        string ScriptPath,
        string? Description,
        string? Schedule,
        string? WorkingDir,
        [property: StashField(Type = "dict")] Dictionary<string, StashValue>? Env,
        string? User,
        bool AutoStart,
        bool RestartOnFailure,
        long MaxRestarts,
        long RestartDelaySec,
        [property: StashField(Type = "dict")] Dictionary<string, StashValue>? PlatformExtras,
        bool System);

    /// <summary>Status of an installed OS-managed service.</summary>
    [StashStruct]
    public sealed record ServiceStatus(
        string Name,
        string State,
        string? Schedule,
        string? ScriptPath,
        string? WorkingDir,
        string? User,
        string? LastRunTime,
        string? NextRunTime,
        long? LastExitCode,
        long RestartCount,
        string? Mode,
        string? Platform);

    /// <summary>Summary info for a listed OS-managed service.</summary>
    [StashStruct]
    public sealed record ServiceInfo(
        string Name,
        string State,
        string? Schedule,
        string? LastRunTime,
        string? NextRunTime);

    // ── Functions ────────────────────────────────────────────────────────────

    /// <summary>Install a Stash script as an OS-managed service.</summary>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Install(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var defInstance = SvArgs.Instance(args, 0, "ServiceDef", "scheduler.install");
        var definition = ConvertToServiceDefinition(defInstance);
        var manager = ServiceManagerFactory.Create(definition.SystemMode);
        var result = manager.Install(definition);
        if (!result.Success)
            throw new RuntimeError($"scheduler.install: {result.Error}");
        return StashValue.True;
    }

    /// <summary>Remove an installed service and its artifacts.</summary>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Uninstall(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "scheduler.uninstall");
        bool system = args.Length > 1 && !args[1].IsNull ? SvArgs.Bool(args, 1, "scheduler.uninstall") : false;
        var manager = ServiceManagerFactory.Create(system);
        var result = manager.Uninstall(name);
        if (!result.Success)
            throw new RuntimeError($"scheduler.uninstall: {result.Error}");
        return StashValue.True;
    }

    /// <summary>Start a stopped service.</summary>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Start(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "scheduler.start");
        bool system = args.Length > 1 && !args[1].IsNull ? SvArgs.Bool(args, 1, "scheduler.start") : false;
        var manager = ServiceManagerFactory.Create(system);
        var result = manager.Start(name);
        if (!result.Success)
            throw new RuntimeError($"scheduler.start: {result.Error}");
        return StashValue.True;
    }

    /// <summary>Stop a running service.</summary>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Stop(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "scheduler.stop");
        bool system = args.Length > 1 && !args[1].IsNull ? SvArgs.Bool(args, 1, "scheduler.stop") : false;
        var manager = ServiceManagerFactory.Create(system);
        var result = manager.Stop(name);
        if (!result.Success)
            throw new RuntimeError($"scheduler.stop: {result.Error}");
        return StashValue.True;
    }

    /// <summary>Restart a service.</summary>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Restart(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "scheduler.restart");
        bool system = args.Length > 1 && !args[1].IsNull ? SvArgs.Bool(args, 1, "scheduler.restart") : false;
        var manager = ServiceManagerFactory.Create(system);
        var result = manager.Restart(name);
        if (!result.Success)
            throw new RuntimeError($"scheduler.restart: {result.Error}");
        return StashValue.True;
    }

    /// <summary>Enable auto-start on boot.</summary>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Enable(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "scheduler.enable");
        bool system = args.Length > 1 && !args[1].IsNull ? SvArgs.Bool(args, 1, "scheduler.enable") : false;
        var manager = ServiceManagerFactory.Create(system);
        var result = manager.Enable(name);
        if (!result.Success)
            throw new RuntimeError($"scheduler.enable: {result.Error}");
        return StashValue.True;
    }

    /// <summary>Disable auto-start on boot.</summary>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Disable(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "scheduler.disable");
        bool system = args.Length > 1 && !args[1].IsNull ? SvArgs.Bool(args, 1, "scheduler.disable") : false;
        var manager = ServiceManagerFactory.Create(system);
        var result = manager.Disable(name);
        if (!result.Success)
            throw new RuntimeError($"scheduler.disable: {result.Error}");
        return StashValue.True;
    }

    /// <summary>Get the current status of a service.</summary>
    [StashFn(Raw = true, ReturnType = "ServiceStatus")]
    private static StashValue Status(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "scheduler.status");
        bool system = args.Length > 1 && !args[1].IsNull ? SvArgs.Bool(args, 1, "scheduler.status") : false;
        var manager = ServiceManagerFactory.Create(system);
        var status = manager.GetStatus(name);
        return StashValue.FromObj(ConvertFromServiceStatus(status));
    }

    /// <summary>List all Stash-managed services.</summary>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue List(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        bool system = args.Length > 0 && !args[0].IsNull ? SvArgs.Bool(args, 0, "scheduler.list") : false;
        var manager = ServiceManagerFactory.Create(system);
        var services = manager.List();
        var result = new List<StashValue>(services.Count);
        foreach (var svc in services)
            result.Add(StashValue.FromObj(ConvertFromServiceInfo(svc)));
        return StashValue.FromObj(result);
    }

    /// <summary>Read service log lines.</summary>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Logs(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "scheduler.logs");
        int lines = args.Length > 1 && !args[1].IsNull ? (int)SvArgs.Long(args, 1, "scheduler.logs") : 50;
        string? date = args.Length > 2 && !args[2].IsNull ? SvArgs.String(args, 2, "scheduler.logs") : null;
        var logLines = ServiceLogManager.ReadLines(name, lines, date);
        var result = new List<StashValue>(logLines.Count);
        foreach (string line in logLines)
            result.Add(StashValue.FromObj(line));
        return StashValue.FromObj(result);
    }

    /// <summary>Check if the OS service manager is available.</summary>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Available(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        try
        {
            var manager = ServiceManagerFactory.Create();
            return StashValue.FromBool(manager.IsAvailable());
        }
        catch
        {
            return StashValue.False;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static ServiceDefinition ConvertToServiceDefinition(StashInstance inst)
    {
        string name       = (string)inst.GetField("name",       null).ToObject()!;
        string scriptPath = (string)inst.GetField("scriptPath", null).ToObject()!;

        StashValue descVal = inst.GetField("description", null);
        string? description = descVal.IsNull ? null : (string)descVal.ToObject()!;

        StashValue scheduleVal = inst.GetField("schedule", null);
        string? schedule = scheduleVal.IsNull ? null : (string)scheduleVal.ToObject()!;

        StashValue workingDirVal = inst.GetField("workingDir", null);
        string? workingDir = workingDirVal.IsNull ? null : (string)workingDirVal.ToObject()!;

        StashValue userVal = inst.GetField("user", null);
        string? user = userVal.IsNull ? null : (string)userVal.ToObject()!;

        StashValue autoStartVal = inst.GetField("autoStart", null);
        bool autoStart = autoStartVal.IsNull ? true : autoStartVal.AsBool;

        StashValue restartOnFailureVal = inst.GetField("restartOnFailure", null);
        bool restartOnFailure = restartOnFailureVal.IsNull ? true : restartOnFailureVal.AsBool;

        StashValue maxRestartsVal = inst.GetField("maxRestarts", null);
        int maxRestarts = maxRestartsVal.IsNull ? 0 : (int)maxRestartsVal.AsInt;

        StashValue restartDelayVal = inst.GetField("restartDelaySec", null);
        int restartDelaySec = restartDelayVal.IsNull ? 5 : (int)restartDelayVal.AsInt;

        StashValue systemVal = inst.GetField("system", null);
        bool systemMode = systemVal.IsNull ? false : systemVal.AsBool;

        Dictionary<string, string>? env = null;
        StashValue envVal = inst.GetField("env", null);
        if (!envVal.IsNull && envVal.ToObject() is StashDictionary envDict)
        {
            env = new Dictionary<string, string>(envDict.Count);
            foreach (var kvp in envDict.GetAllEntries())
                env[kvp.Key.ToString()!] = kvp.Value.ToObject()?.ToString() ?? "";
        }

        Dictionary<string, string>? platformExtras = null;
        StashValue extrasVal = inst.GetField("platformExtras", null);
        if (!extrasVal.IsNull && extrasVal.ToObject() is StashDictionary extrasDict)
        {
            platformExtras = new Dictionary<string, string>(extrasDict.Count);
            foreach (var kvp in extrasDict.GetAllEntries())
                platformExtras[kvp.Key.ToString()!] = kvp.Value.ToObject()?.ToString() ?? "";
        }

        return new ServiceDefinition
        {
            Name             = name,
            ScriptPath       = scriptPath,
            Description      = description,
            Schedule         = schedule,
            WorkingDirectory = workingDir,
            Environment      = env,
            User             = user,
            AutoStart        = autoStart,
            RestartOnFailure = restartOnFailure,
            MaxRestarts      = maxRestarts,
            RestartDelaySec  = restartDelaySec,
            SystemMode       = systemMode,
            PlatformExtras   = platformExtras,
        };
    }

    private static StashInstance ConvertFromServiceStatus(Stash.Scheduler.Models.ServiceStatus status)
    {
        return new StashInstance("ServiceStatus", new Dictionary<string, StashValue>
        {
            ["name"]         = StashValue.FromObj(status.Name),
            ["state"]        = StashValue.FromObj(FormatState(status.State)),
            ["schedule"]     = status.Schedule     != null ? StashValue.FromObj(status.Schedule)                        : StashValue.Null,
            ["scriptPath"]   = status.ScriptPath   != null ? StashValue.FromObj(status.ScriptPath)                      : StashValue.Null,
            ["workingDir"]   = status.WorkingDirectory != null ? StashValue.FromObj(status.WorkingDirectory)            : StashValue.Null,
            ["user"]         = status.User         != null ? StashValue.FromObj(status.User)                            : StashValue.Null,
            ["lastRunTime"]  = status.LastRunTime   != null ? StashValue.FromObj(status.LastRunTime.Value.ToString("O")) : StashValue.Null,
            ["nextRunTime"]  = status.NextRunTime   != null ? StashValue.FromObj(status.NextRunTime.Value.ToString("O")) : StashValue.Null,
            ["lastExitCode"] = status.LastExitCode  != null ? StashValue.FromInt(status.LastExitCode.Value)              : StashValue.Null,
            ["restartCount"] = StashValue.FromInt(status.RestartCount),
            ["mode"]         = status.Mode         != null ? StashValue.FromObj(status.Mode)                            : StashValue.Null,
            ["platform"]     = status.PlatformInfo != null ? StashValue.FromObj(status.PlatformInfo)                    : StashValue.Null,
        });
    }

    private static StashInstance ConvertFromServiceInfo(Stash.Scheduler.Models.ServiceInfo info)
    {
        return new StashInstance("ServiceInfo", new Dictionary<string, StashValue>
        {
            ["name"]        = StashValue.FromObj(info.Name),
            ["state"]       = StashValue.FromObj(FormatState(info.State)),
            ["schedule"]    = info.Schedule    != null ? StashValue.FromObj(info.Schedule)                        : StashValue.Null,
            ["lastRunTime"] = info.LastRunTime != null ? StashValue.FromObj(info.LastRunTime.Value.ToString("O")) : StashValue.Null,
            ["nextRunTime"] = info.NextRunTime != null ? StashValue.FromObj(info.NextRunTime.Value.ToString("O")) : StashValue.Null,
        });
    }

    private static string FormatState(Stash.Scheduler.Models.ServiceState state) => state switch
    {
        Stash.Scheduler.Models.ServiceState.Active    => "active",
        Stash.Scheduler.Models.ServiceState.Running   => "running",
        Stash.Scheduler.Models.ServiceState.Inactive  => "inactive",
        Stash.Scheduler.Models.ServiceState.Stopped   => "stopped",
        Stash.Scheduler.Models.ServiceState.Failed    => "failed",
        Stash.Scheduler.Models.ServiceState.Orphaned  => "orphaned",
        Stash.Scheduler.Models.ServiceState.Unmanaged => "unmanaged",
        _                      => "unknown",
    };
}
