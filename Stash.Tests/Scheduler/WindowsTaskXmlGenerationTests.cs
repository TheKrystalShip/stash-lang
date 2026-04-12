namespace Stash.Tests.Scheduler;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Stash.Scheduler;
using Stash.Scheduler.Models;
using Stash.Scheduler.Platforms;

public class WindowsTaskXmlGenerationTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WindowsTaskServiceManager UserModeManager() =>
        new WindowsTaskServiceManager(systemMode: false);

    private static WindowsTaskServiceManager SystemModeManager() =>
        new WindowsTaskServiceManager(systemMode: true);

    private XElement? GetElement(XDocument doc, params string[] path)
    {
        XElement? current = doc.Root;
        foreach (string name in path)
        {
            current = current?.Element(Ns + name);
            if (current is null) return null;
        }
        return current;
    }

    private string? GetText(XDocument doc, params string[] path) =>
        GetElement(doc, path)?.Value;

    private XElement GetActions(XDocument doc) =>
        doc.Root!.Element(Ns + "Actions")!;

    private XElement GetSettings(XDocument doc) =>
        doc.Root!.Element(Ns + "Settings")!;

    private XElement GetTriggers(XDocument doc) =>
        doc.Root!.Element(Ns + "Triggers")!;

    private XElement GetPrincipal(XDocument doc) =>
        doc.Root!.Element(Ns + "Principals")!.Element(Ns + "Principal")!;

    private XElement GetRegistrationInfo(XDocument doc) =>
        doc.Root!.Element(Ns + "RegistrationInfo")!;

    // ── RegistrationInfo ──────────────────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_RegistrationInfo_HasCorrectDescription()
    {
        var def = new ServiceDefinition
        {
            Name = "myservice",
            ScriptPath = "/scripts/run.stash",
            Description = "My test service"
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/scripts/run.stash");

        XElement reg = GetRegistrationInfo(doc);
        Assert.Equal("My test service", reg.Element(Ns + "Description")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_RegistrationInfo_AuthorIsStash()
    {
        var def = new ServiceDefinition { Name = "svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement reg = GetRegistrationInfo(doc);
        Assert.Equal("Stash", reg.Element(Ns + "Author")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_NoDescription_FallsBackToServiceName()
    {
        var def = new ServiceDefinition { Name = "fallback-svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement reg = GetRegistrationInfo(doc);
        Assert.Contains("fallback-svc", reg.Element(Ns + "Description")?.Value ?? "");
    }

    // ── Long-running service ──────────────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_LongRunning_HasBootTrigger()
    {
        var def = new ServiceDefinition { Name = "daemon", ScriptPath = "/daemon.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/daemon.stash");

        XElement triggers = GetTriggers(doc);
        XElement? bootTrigger = triggers.Element(Ns + "BootTrigger");
        Assert.NotNull(bootTrigger);
    }

    [Fact]
    public void GenerateTaskXml_LongRunning_NoCalendarTrigger()
    {
        var def = new ServiceDefinition { Name = "daemon", ScriptPath = "/daemon.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/daemon.stash");

        XElement triggers = GetTriggers(doc);
        Assert.Null(triggers.Element(Ns + "CalendarTrigger"));
    }

    [Fact]
    public void GenerateTaskXml_LongRunning_ExecutionTimeLimitIsZero()
    {
        var def = new ServiceDefinition { Name = "daemon", ScriptPath = "/daemon.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/daemon.stash");

        XElement settings = GetSettings(doc);
        Assert.Equal("PT0S", settings.Element(Ns + "ExecutionTimeLimit")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_LongRunning_WithRestartOnFailure_HasRestartOnFailureSettings()
    {
        var def = new ServiceDefinition
        {
            Name = "daemon",
            ScriptPath = "/daemon.stash",
            RestartOnFailure = true,
            RestartDelaySec = 10,
            MaxRestarts = 3
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/daemon.stash");

        XElement settings = GetSettings(doc);
        XElement? restart = settings.Element(Ns + "RestartOnFailure");
        Assert.NotNull(restart);
        Assert.Equal("PT10S", restart!.Element(Ns + "Interval")?.Value);
        Assert.Equal("3", restart.Element(Ns + "Count")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_LongRunning_UnlimitedRestarts_UsesLargeCount()
    {
        var def = new ServiceDefinition
        {
            Name = "daemon",
            ScriptPath = "/daemon.stash",
            RestartOnFailure = true,
            MaxRestarts = 0 // unlimited
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/daemon.stash");

        XElement settings = GetSettings(doc);
        XElement? restart = settings.Element(Ns + "RestartOnFailure");
        Assert.NotNull(restart);
        Assert.Equal("999999", restart!.Element(Ns + "Count")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_LongRunning_NoRestartOnFailure_NoRestartOnFailureElement()
    {
        var def = new ServiceDefinition
        {
            Name = "daemon",
            ScriptPath = "/daemon.stash",
            RestartOnFailure = false
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/daemon.stash");

        XElement settings = GetSettings(doc);
        Assert.Null(settings.Element(Ns + "RestartOnFailure"));
    }

    // ── Periodic service ──────────────────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_Periodic_HasCalendarTrigger()
    {
        var def = new ServiceDefinition
        {
            Name = "backup",
            ScriptPath = "/backup.stash",
            Schedule = "0 3 * * *"
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/backup.stash");

        XElement triggers = GetTriggers(doc);
        Assert.NotNull(triggers.Element(Ns + "CalendarTrigger"));
    }

    [Fact]
    public void GenerateTaskXml_Periodic_NoBootTrigger()
    {
        var def = new ServiceDefinition
        {
            Name = "backup",
            ScriptPath = "/backup.stash",
            Schedule = "0 3 * * *"
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/backup.stash");

        XElement triggers = GetTriggers(doc);
        Assert.Null(triggers.Element(Ns + "BootTrigger"));
    }

    [Fact]
    public void GenerateTaskXml_Periodic_ExecutionTimeLimitIs72H()
    {
        var def = new ServiceDefinition
        {
            Name = "backup",
            ScriptPath = "/backup.stash",
            Schedule = "0 3 * * *"
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/backup.stash");

        XElement settings = GetSettings(doc);
        Assert.Equal("PT72H", settings.Element(Ns + "ExecutionTimeLimit")?.Value);
    }

    // ── Actions / command wrapping ────────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_Actions_CommandIsCmdExe()
    {
        var def = new ServiceDefinition { Name = "svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        Assert.Equal("cmd.exe", exec.Element(Ns + "Command")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_Actions_ArgumentsStartWithSlashC()
    {
        var def = new ServiceDefinition { Name = "svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        string? args = exec.Element(Ns + "Arguments")?.Value;
        Assert.NotNull(args);
        Assert.StartsWith("/c", args!);
    }

    [Fact]
    public void GenerateTaskXml_Actions_ArgumentsContainScriptPath()
    {
        var def = new ServiceDefinition
        {
            Name = "svc",
            ScriptPath = @"C:\scripts\run.stash"
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, @"C:\scripts\run.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        Assert.Contains(@"C:\scripts\run.stash", exec.Element(Ns + "Arguments")?.Value ?? "");
    }

    [Fact]
    public void GenerateTaskXml_Actions_ArgumentsContainLogRedirection()
    {
        var def = new ServiceDefinition { Name = "logger", ScriptPath = "/logger.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/logger.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        string args = exec.Element(Ns + "Arguments")?.Value ?? "";
        Assert.Contains(">>", args);
        Assert.Contains("2>&1", args);
    }

    [Fact]
    public void GenerateTaskXml_Actions_ArgumentsContainLoggerServiceName()
    {
        var def = new ServiceDefinition { Name = "mylogger", ScriptPath = "/log.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/log.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        Assert.Contains("mylogger", exec.Element(Ns + "Arguments")?.Value ?? "");
    }

    [Fact]
    public void GenerateTaskXml_WithWorkingDirectory_UsesSpecifiedDirectory()
    {
        var def = new ServiceDefinition
        {
            Name = "svc",
            ScriptPath = "/opt/svc/run.stash",
            WorkingDirectory = "/opt/svc"
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/opt/svc/run.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        Assert.Equal("/opt/svc", exec.Element(Ns + "WorkingDirectory")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_NoWorkingDirectory_FallsBackToScriptDirectory()
    {
        // Use a forward-slash path so Path.GetDirectoryName works correctly on Linux/macOS
        const string scriptPath = "/opt/scripts/run.stash";
        var def = new ServiceDefinition { Name = "svc", ScriptPath = scriptPath };

        XDocument doc = UserModeManager().GenerateTaskXml(def, scriptPath);

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        Assert.Equal("/opt/scripts", exec.Element(Ns + "WorkingDirectory")?.Value);
    }

    // ── Environment variables ─────────────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_WithEnvironment_InjectsSetCommandsInArguments()
    {
        var def = new ServiceDefinition
        {
            Name = "svc",
            ScriptPath = "/svc.stash",
            Environment = new Dictionary<string, string>
            {
                ["API_KEY"] = "abc123",
                ["LOG_LEVEL"] = "debug"
            }
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        string args = exec.Element(Ns + "Arguments")?.Value ?? "";
        Assert.Contains("API_KEY", args);
        Assert.Contains("abc123", args);
        Assert.Contains("LOG_LEVEL", args);
        Assert.Contains("debug", args);
    }

    [Fact]
    public void GenerateTaskXml_WithEnvironment_SetCommandsPrecedeStashInArguments()
    {
        var def = new ServiceDefinition
        {
            Name = "svc",
            ScriptPath = "/svc.stash",
            Environment = new Dictionary<string, string> { ["MY_VAR"] = "value" }
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        string args = exec.Element(Ns + "Arguments")?.Value ?? "";
        int setIdx = args.IndexOf("MY_VAR");
        int stashIdx = args.IndexOf("stash ");
        Assert.True(setIdx < stashIdx, "set commands should appear before stash invocation");
    }

    [Fact]
    public void GenerateTaskXml_NoEnvironment_ArgumentsDoNotContainSet()
    {
        var def = new ServiceDefinition { Name = "svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement exec = GetActions(doc).Element(Ns + "Exec")!;
        string args = exec.Element(Ns + "Arguments")?.Value ?? "";
        Assert.DoesNotContain(" set ", args);
    }

    // ── Enabled / disabled ────────────────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_AutoStartTrue_EnabledIsTrue()
    {
        var def = new ServiceDefinition
        {
            Name = "svc",
            ScriptPath = "/svc.stash",
            AutoStart = true
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement settings = GetSettings(doc);
        Assert.Equal("true", settings.Element(Ns + "Enabled")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_AutoStartFalse_EnabledIsFalse()
    {
        var def = new ServiceDefinition
        {
            Name = "svc",
            ScriptPath = "/svc.stash",
            AutoStart = false
        };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement settings = GetSettings(doc);
        Assert.Equal("false", settings.Element(Ns + "Enabled")?.Value);
    }

    // ── Principal (user/system mode) ──────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_SystemMode_UserIdIsSystemSid()
    {
        var def = new ServiceDefinition
        {
            Name = "daemon",
            ScriptPath = "/daemon.stash",
            SystemMode = true
        };

        XDocument doc = SystemModeManager().GenerateTaskXml(def, "/daemon.stash");

        XElement principal = GetPrincipal(doc);
        Assert.Equal("S-1-5-18", principal.Element(Ns + "UserId")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_SystemModeWithExplicitUser_UsesSpecifiedUser()
    {
        var def = new ServiceDefinition
        {
            Name = "daemon",
            ScriptPath = "/daemon.stash",
            User = "DOMAIN\\svcaccount",
            SystemMode = true
        };

        XDocument doc = SystemModeManager().GenerateTaskXml(def, "/daemon.stash");

        XElement principal = GetPrincipal(doc);
        Assert.Equal(@"DOMAIN\svcaccount", principal.Element(Ns + "UserId")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_UserMode_RunLevelIsLeastPrivilege()
    {
        var def = new ServiceDefinition { Name = "svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement principal = GetPrincipal(doc);
        Assert.Equal("LeastPrivilege", principal.Element(Ns + "RunLevel")?.Value);
    }

    // ── Platform extras ───────────────────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_BlockedPlatformExtrasKey_ReturnsFailure()
    {
        string tempScript = Path.Combine(Path.GetTempPath(), $"stash-test-{Guid.NewGuid()}.stash");
        System.IO.File.WriteAllText(tempScript, "// test");
        try
        {
            var def = new ServiceDefinition
            {
                Name = "svc",
                ScriptPath = tempScript,
                PlatformExtras = new Dictionary<string, string>
                {
                    ["Command"] = "evil.exe"
                }
            };

            ServiceResult result = UserModeManager().Install(def);

            Assert.False(result.Success);
            Assert.Contains("Command", result.Error ?? "");
        }
        finally
        {
            if (System.IO.File.Exists(tempScript)) System.IO.File.Delete(tempScript);
        }
    }

    [Fact]
    public void GenerateTaskXml_BlockedKeyArguments_ReturnsFailure()
    {
        string tempScript = Path.Combine(Path.GetTempPath(), $"stash-test-{Guid.NewGuid()}.stash");
        System.IO.File.WriteAllText(tempScript, "// test");
        try
        {
            var def = new ServiceDefinition
            {
                Name = "svc",
                ScriptPath = tempScript,
                PlatformExtras = new Dictionary<string, string>
                {
                    ["Arguments"] = "/c malicious"
                }
            };

            ServiceResult result = UserModeManager().Install(def);

            Assert.False(result.Success);
            Assert.Contains("Arguments", result.Error ?? "");
        }
        finally
        {
            if (System.IO.File.Exists(tempScript)) System.IO.File.Delete(tempScript);
        }
    }

    // ── Settings defaults ─────────────────────────────────────────────────────

    [Fact]
    public void GenerateTaskXml_MultipleInstancesPolicy_IsIgnoreNew()
    {
        var def = new ServiceDefinition { Name = "svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement settings = GetSettings(doc);
        Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_DisallowStartIfOnBatteries_IsFalse()
    {
        var def = new ServiceDefinition { Name = "svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement settings = GetSettings(doc);
        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")?.Value);
    }

    [Fact]
    public void GenerateTaskXml_StartWhenAvailable_IsTrue()
    {
        var def = new ServiceDefinition { Name = "svc", ScriptPath = "/svc.stash" };

        XDocument doc = UserModeManager().GenerateTaskXml(def, "/svc.stash");

        XElement settings = GetSettings(doc);
        Assert.Equal("true", settings.Element(Ns + "StartWhenAvailable")?.Value);
    }

    // ── CronToTriggers: repetition cases ─────────────────────────────────────

    [Fact]
    public void CronToTriggers_EveryMinute_ReturnsOneRepetitionTrigger()
    {
        // "* * * * *" → 1-minute Repetition
        CronExpression expr = CronExpression.Parse("* * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Single(triggers);
        Assert.Equal(Ns + "CalendarTrigger", triggers[0].Name);
        XElement? rep = triggers[0].Element(Ns + "Repetition");
        Assert.NotNull(rep);
        Assert.Equal("PT1M", rep!.Element(Ns + "Interval")?.Value);
    }

    [Fact]
    public void CronToTriggers_Every5Minutes_ReturnsRepetitionWith5MinInterval()
    {
        // "*/5 * * * *" → PT5M Repetition
        CronExpression expr = CronExpression.Parse("*/5 * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Single(triggers);
        XElement? rep = triggers[0].Element(Ns + "Repetition");
        Assert.NotNull(rep);
        Assert.Equal("PT5M", rep!.Element(Ns + "Interval")?.Value);
    }

    [Fact]
    public void CronToTriggers_Every15Minutes_ReturnsRepetitionWith15MinInterval()
    {
        // "*/15 * * * *" → PT15M Repetition
        CronExpression expr = CronExpression.Parse("*/15 * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Single(triggers);
        XElement? rep = triggers[0].Element(Ns + "Repetition");
        Assert.NotNull(rep);
        Assert.Equal("PT15M", rep!.Element(Ns + "Interval")?.Value);
    }

    [Fact]
    public void CronToTriggers_RepetitionTrigger_HasScheduleByDay()
    {
        CronExpression expr = CronExpression.Parse("* * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        XElement? sbd = triggers[0].Element(Ns + "ScheduleByDay");
        Assert.NotNull(sbd);
        Assert.Equal("1", sbd!.Element(Ns + "DaysInterval")?.Value);
    }

    [Fact]
    public void CronToTriggers_RepetitionTrigger_StopAtDurationEndIsFalse()
    {
        CronExpression expr = CronExpression.Parse("*/5 * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        XElement? rep = triggers[0].Element(Ns + "Repetition");
        Assert.Equal("false", rep?.Element(Ns + "StopAtDurationEnd")?.Value);
    }

    // ── CronToTriggers: daily ─────────────────────────────────────────────────

    [Fact]
    public void CronToTriggers_DailyAt9_ReturnsOneCalendarTrigger()
    {
        // "0 9 * * *" → CalendarTrigger at 09:00 daily
        CronExpression expr = CronExpression.Parse("0 9 * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Single(triggers);
        Assert.Equal(Ns + "CalendarTrigger", triggers[0].Name);
    }

    [Fact]
    public void CronToTriggers_DailyAt9_HasCorrectStartBoundary()
    {
        CronExpression expr = CronExpression.Parse("0 9 * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        string? sb = triggers[0].Element(Ns + "StartBoundary")?.Value;
        Assert.NotNull(sb);
        Assert.Contains("09:00", sb!);
    }

    [Fact]
    public void CronToTriggers_DailyAt9_HasScheduleByDay()
    {
        CronExpression expr = CronExpression.Parse("0 9 * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.NotNull(triggers[0].Element(Ns + "ScheduleByDay"));
        Assert.Null(triggers[0].Element(Ns + "ScheduleByWeek"));
    }

    // ── CronToTriggers: weekly ────────────────────────────────────────────────

    [Fact]
    public void CronToTriggers_WeekdaysAt9_ReturnsOneCalendarTrigger()
    {
        // "0 9 * * 1-5" → CalendarTrigger with ScheduleByWeek (Mon-Fri)
        CronExpression expr = CronExpression.Parse("0 9 * * 1-5");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Single(triggers);
    }

    [Fact]
    public void CronToTriggers_WeekdaysAt9_HasScheduleByWeek()
    {
        CronExpression expr = CronExpression.Parse("0 9 * * 1-5");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.NotNull(triggers[0].Element(Ns + "ScheduleByWeek"));
    }

    [Fact]
    public void CronToTriggers_WeekdaysAt9_HasCorrectDaysOfWeek()
    {
        // DOW 1-5 = Mon, Tue, Wed, Thu, Fri
        CronExpression expr = CronExpression.Parse("0 9 * * 1-5");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        XElement? dow = triggers[0].Element(Ns + "ScheduleByWeek")?.Element(Ns + "DaysOfWeek");
        Assert.NotNull(dow);
        Assert.NotNull(dow!.Element(Ns + "Monday"));
        Assert.NotNull(dow.Element(Ns + "Tuesday"));
        Assert.NotNull(dow.Element(Ns + "Wednesday"));
        Assert.NotNull(dow.Element(Ns + "Thursday"));
        Assert.NotNull(dow.Element(Ns + "Friday"));
        Assert.Null(dow.Element(Ns + "Saturday"));
        Assert.Null(dow.Element(Ns + "Sunday"));
    }

    [Fact]
    public void CronToTriggers_SundayOnly_HasSundayInDaysOfWeek()
    {
        CronExpression expr = CronExpression.Parse("0 0 * * 0");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        XElement? dow = triggers[0].Element(Ns + "ScheduleByWeek")?.Element(Ns + "DaysOfWeek");
        Assert.NotNull(dow);
        Assert.NotNull(dow!.Element(Ns + "Sunday"));
        Assert.Null(dow.Element(Ns + "Monday"));
    }

    // ── CronToTriggers: monthly ───────────────────────────────────────────────

    [Fact]
    public void CronToTriggers_FirstDayOfMonth_ReturnsOneCalendarTrigger()
    {
        // "0 0 1 * *" → CalendarTrigger on day 1 of month
        CronExpression expr = CronExpression.Parse("0 0 1 * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Single(triggers);
    }

    [Fact]
    public void CronToTriggers_FirstDayOfMonth_HasScheduleByMonth()
    {
        CronExpression expr = CronExpression.Parse("0 0 1 * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.NotNull(triggers[0].Element(Ns + "ScheduleByMonth"));
    }

    [Fact]
    public void CronToTriggers_FirstDayOfMonth_HasDay1InDaysOfMonth()
    {
        CronExpression expr = CronExpression.Parse("0 0 1 * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        XElement? dom = triggers[0].Element(Ns + "ScheduleByMonth")?.Element(Ns + "DaysOfMonth");
        Assert.NotNull(dom);
        List<string> days = dom!.Elements(Ns + "Day").Select(e => e.Value).ToList();
        Assert.Single(days);
        Assert.Equal("1", days[0]);
    }

    [Fact]
    public void CronToTriggers_FirstDayOfMonth_HasAllMonthsListed()
    {
        CronExpression expr = CronExpression.Parse("0 0 1 * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        XElement? months = triggers[0].Element(Ns + "ScheduleByMonth")?.Element(Ns + "Months");
        Assert.NotNull(months);
        // All 12 months should be listed
        Assert.Equal(12, months!.Elements().Count());
    }

    // ── CronToTriggers: multiple triggers ─────────────────────────────────────

    [Fact]
    public void CronToTriggers_TwoHours_ReturnsTwoTriggers()
    {
        // "0 9,17 * * *" → two CalendarTriggers (09:00 and 17:00)
        CronExpression expr = CronExpression.Parse("0 9,17 * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Equal(2, triggers.Count);
    }

    [Fact]
    public void CronToTriggers_TwoHours_HasCorrectStartBoundaries()
    {
        CronExpression expr = CronExpression.Parse("0 9,17 * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        List<string> bounds = triggers
            .Select(t => t.Element(Ns + "StartBoundary")?.Value ?? "")
            .OrderBy(s => s)
            .ToList();

        Assert.Contains(bounds, b => b.Contains("09:00"));
        Assert.Contains(bounds, b => b.Contains("17:00"));
    }

    [Fact]
    public void CronToTriggers_WeeklyAtTwoTimes_ReturnsTwoTriggers()
    {
        // "0 9,17 * * 1-5" → two weekly CalendarTriggers
        CronExpression expr = CronExpression.Parse("0 9,17 * * 1-5");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Equal(2, triggers.Count);
        Assert.All(triggers, t => Assert.NotNull(t.Element(Ns + "ScheduleByWeek")));
    }

    // ── CronToTriggers: hourly repetition ─────────────────────────────────────

    [Fact]
    public void CronToTriggers_EveryHourAtMinute0_ReturnsSinglePT60MRepetitionTrigger()
    {
        // "0 * * * *" → single CalendarTrigger with PT60M Repetition
        CronExpression expr = CronExpression.Parse("0 * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Single(triggers);
        XElement? rep = triggers[0].Element(Ns + "Repetition");
        Assert.NotNull(rep);
        Assert.Equal("PT60M", rep!.Element(Ns + "Interval")?.Value);
    }

    [Fact]
    public void CronToTriggers_EveryHourAtMinute0_StartsAtMinute0()
    {
        CronExpression expr = CronExpression.Parse("0 * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        string? sb = triggers[0].Element(Ns + "StartBoundary")?.Value;
        Assert.NotNull(sb);
        Assert.Contains("00:00", sb!);
    }

    [Fact]
    public void CronToTriggers_EveryHourAtMinute30_ReturnsSinglePT60MTrigger()
    {
        // "30 * * * *" → single CalendarTrigger with PT60M starting at :30
        CronExpression expr = CronExpression.Parse("30 * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Single(triggers);
        XElement? rep = triggers[0].Element(Ns + "Repetition");
        Assert.NotNull(rep);
        Assert.Equal("PT60M", rep!.Element(Ns + "Interval")?.Value);
        Assert.Contains("00:30", triggers[0].Element(Ns + "StartBoundary")?.Value ?? "");
    }

    [Fact]
    public void CronToTriggers_EveryHourAtMinutes5And35_ReturnsTwoPT60MTriggers()
    {
        // "5,35 * * * *" → two CalendarTriggers, each PT60M
        CronExpression expr = CronExpression.Parse("5,35 * * * *");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Equal(2, triggers.Count);
        Assert.All(triggers, t =>
        {
            XElement? rep = t.Element(Ns + "Repetition");
            Assert.NotNull(rep);
            Assert.Equal("PT60M", rep!.Element(Ns + "Interval")?.Value);
        });
    }

    [Fact]
    public void CronToTriggers_HourlyOnWeekdays_Returns24WeeklyTriggers()
    {
        // "0 * * * 1-5" → 24 CalendarTriggers (one per hour), each with ScheduleByWeek
        CronExpression expr = CronExpression.Parse("0 * * * 1-5");

        IReadOnlyList<XElement> triggers = WindowsTaskServiceManager.CronToTriggers(expr);

        Assert.Equal(24, triggers.Count);
        Assert.All(triggers, t => Assert.NotNull(t.Element(Ns + "ScheduleByWeek")));
    }
}
