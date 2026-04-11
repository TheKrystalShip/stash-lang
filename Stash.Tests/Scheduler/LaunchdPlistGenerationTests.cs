namespace Stash.Tests.Scheduler;

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Stash.Scheduler;
using Stash.Scheduler.Models;
using Stash.Scheduler.Platforms;

public class LaunchdPlistGenerationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LaunchdServiceManager UserModeManager() => new LaunchdServiceManager(systemMode: false);
    private static LaunchdServiceManager SystemModeManager() => new LaunchdServiceManager(systemMode: true);

    /// <summary>Finds a value element immediately following the given key in a plist dict.</summary>
    private static XElement? FindValue(XElement dict, string key)
    {
        XNode? node = dict.FirstNode;
        while (node is not null)
        {
            if (node is XElement keyEl && keyEl.Name == "key" && keyEl.Value == key)
            {
                // Return next sibling element
                XNode? next = keyEl.NextNode;
                while (next is not null)
                {
                    if (next is XElement valueEl) return valueEl;
                    next = next.NextNode;
                }
            }
            node = node.NextNode;
        }
        return null;
    }

    private static XElement GetRootDict(XDocument doc)
    {
        return doc.Root!.Element("dict")!;
    }

    // ── Label format ─────────────────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_LongRunning_LabelIsCom_Stash_Format()
    {
        var def = new ServiceDefinition { Name = "health-check", ScriptPath = "/scripts/check.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/scripts/check.stash");

        XElement root = GetRootDict(doc);
        XElement? labelEl = FindValue(root, "Label");
        Assert.NotNull(labelEl);
        Assert.Equal("com.stash.health-check", labelEl!.Value);
    }

    // ── Long-running service ──────────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_LongRunning_HasKeepAliveTrue()
    {
        var def = new ServiceDefinition { Name = "monitor", ScriptPath = "/bin/monitor.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/bin/monitor.stash");

        XElement root = GetRootDict(doc);
        XElement? keepAlive = FindValue(root, "KeepAlive");
        Assert.NotNull(keepAlive);
        Assert.Equal("true", keepAlive!.Name.LocalName);
    }

    [Fact]
    public void GeneratePlist_LongRunning_HasRunAtLoadTrue()
    {
        var def = new ServiceDefinition { Name = "monitor", ScriptPath = "/bin/monitor.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/bin/monitor.stash");

        XElement root = GetRootDict(doc);
        XElement? runAtLoad = FindValue(root, "RunAtLoad");
        Assert.NotNull(runAtLoad);
        Assert.Equal("true", runAtLoad!.Name.LocalName);
    }

    [Fact]
    public void GeneratePlist_LongRunning_ProgramArgumentsContainsStashAndScript()
    {
        var def = new ServiceDefinition { Name = "monitor", ScriptPath = "/bin/monitor.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/bin/monitor.stash");

        XElement root = GetRootDict(doc);
        XElement? argsEl = FindValue(root, "ProgramArguments");
        Assert.NotNull(argsEl);

        List<string> args = argsEl!.Elements("string").Select(e => e.Value).ToList();
        Assert.Equal("stash", args[0]);
        Assert.Equal("/bin/monitor.stash", args[1]);
    }

    [Fact]
    public void GeneratePlist_LongRunning_NoStartCalendarInterval()
    {
        var def = new ServiceDefinition { Name = "monitor", ScriptPath = "/bin/monitor.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/bin/monitor.stash");

        string xml = doc.ToString();
        Assert.DoesNotContain("StartCalendarInterval", xml);
    }

    // ── Periodic service ──────────────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_Periodic_HasStartCalendarInterval()
    {
        var def = new ServiceDefinition
        {
            Name = "backup",
            ScriptPath = "/scripts/backup.stash",
            Schedule = "0 3 * * *"
        };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/scripts/backup.stash");

        XElement root = GetRootDict(doc);
        XElement? sci = FindValue(root, "StartCalendarInterval");
        Assert.NotNull(sci);
    }

    [Fact]
    public void GeneratePlist_Periodic_NoKeepAliveNoRunAtLoad()
    {
        var def = new ServiceDefinition
        {
            Name = "backup",
            ScriptPath = "/scripts/backup.stash",
            Schedule = "0 3 * * *"
        };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/scripts/backup.stash");

        string xml = doc.ToString();
        Assert.DoesNotContain("KeepAlive", xml);
        Assert.DoesNotContain("RunAtLoad", xml);
    }

    [Fact]
    public void GeneratePlist_Periodic_StartCalendarIntervalHasCorrectEntry()
    {
        // "0 3 * * *" → Minute=0, Hour=3 (1 entry)
        var def = new ServiceDefinition
        {
            Name = "backup",
            ScriptPath = "/scripts/backup.stash",
            Schedule = "0 3 * * *"
        };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/scripts/backup.stash");

        XElement root = GetRootDict(doc);
        XElement? sci = FindValue(root, "StartCalendarInterval");
        Assert.NotNull(sci);

        List<XElement> entries = sci!.Elements("dict").ToList();
        Assert.Single(entries);

        XElement entry = entries[0];
        XElement? minuteEl = FindValue(entry, "Minute");
        XElement? hourEl   = FindValue(entry, "Hour");
        Assert.NotNull(minuteEl);
        Assert.NotNull(hourEl);
        Assert.Equal("0", minuteEl!.Value);
        Assert.Equal("3", hourEl!.Value);
    }

    // ── Environment variables ─────────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_WithEnvironment_HasEnvironmentVariablesDict()
    {
        var def = new ServiceDefinition
        {
            Name = "myapp",
            ScriptPath = "/app/run.stash",
            Environment = new Dictionary<string, string>
            {
                ["API_URL"] = "https://api.example.com",
                ["LOG_LEVEL"] = "debug"
            }
        };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/app/run.stash");

        XElement root = GetRootDict(doc);
        XElement? envEl = FindValue(root, "EnvironmentVariables");
        Assert.NotNull(envEl);
        Assert.Equal("dict", envEl!.Name.LocalName);

        XElement? apiUrl = FindValue(envEl, "API_URL");
        XElement? logLevel = FindValue(envEl, "LOG_LEVEL");
        Assert.NotNull(apiUrl);
        Assert.NotNull(logLevel);
        Assert.Equal("https://api.example.com", apiUrl!.Value);
        Assert.Equal("debug", logLevel!.Value);
    }

    [Fact]
    public void GeneratePlist_WithNoEnvironment_NoEnvironmentVariablesKey()
    {
        var def = new ServiceDefinition { Name = "myapp", ScriptPath = "/app/run.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/app/run.stash");

        string xml = doc.ToString();
        Assert.DoesNotContain("EnvironmentVariables", xml);
    }

    // ── Working directory ─────────────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_WithWorkingDirectory_UsesSpecifiedDirectory()
    {
        var def = new ServiceDefinition
        {
            Name = "myapp",
            ScriptPath = "/app/run.stash",
            WorkingDirectory = "/opt/myapp"
        };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/app/run.stash");

        XElement root = GetRootDict(doc);
        XElement? wdEl = FindValue(root, "WorkingDirectory");
        Assert.NotNull(wdEl);
        Assert.Equal("/opt/myapp", wdEl!.Value);
    }

    [Fact]
    public void GeneratePlist_NoWorkingDirectory_FallsBackToScriptDirectory()
    {
        var def = new ServiceDefinition { Name = "myapp", ScriptPath = "/opt/myapp/run.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/opt/myapp/run.stash");

        XElement root = GetRootDict(doc);
        XElement? wdEl = FindValue(root, "WorkingDirectory");
        Assert.NotNull(wdEl);
        Assert.Equal("/opt/myapp", wdEl!.Value);
    }

    // ── Log paths ─────────────────────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_HasStandardOutPath()
    {
        var def = new ServiceDefinition { Name = "logger", ScriptPath = "/scripts/log.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/scripts/log.stash");

        XElement root = GetRootDict(doc);
        XElement? stdOut = FindValue(root, "StandardOutPath");
        Assert.NotNull(stdOut);
        Assert.Contains("logger", stdOut!.Value);
        Assert.Contains("current.log", stdOut.Value);
    }

    [Fact]
    public void GeneratePlist_HasStandardErrorPath()
    {
        var def = new ServiceDefinition { Name = "logger", ScriptPath = "/scripts/log.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/scripts/log.stash");

        XElement root = GetRootDict(doc);
        XElement? stdErr = FindValue(root, "StandardErrorPath");
        Assert.NotNull(stdErr);
        Assert.Contains("logger", stdErr!.Value);
        Assert.Contains("current.log", stdErr.Value);
    }

    [Fact]
    public void GeneratePlist_StdOutAndStdErrAreTheSamePath()
    {
        var def = new ServiceDefinition { Name = "logger", ScriptPath = "/scripts/log.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/scripts/log.stash");

        XElement root = GetRootDict(doc);
        string? stdOut = FindValue(root, "StandardOutPath")?.Value;
        string? stdErr = FindValue(root, "StandardErrorPath")?.Value;
        Assert.Equal(stdOut, stdErr);
    }

    // ── User field (system mode) ──────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_SystemModeWithUser_HasUserNameKey()
    {
        var def = new ServiceDefinition
        {
            Name = "daemon",
            ScriptPath = "/services/daemon.stash",
            User = "nobody",
            SystemMode = true
        };
        var manager = SystemModeManager();

        XDocument doc = manager.GeneratePlist(def, "/services/daemon.stash");

        XElement root = GetRootDict(doc);
        XElement? userEl = FindValue(root, "UserName");
        Assert.NotNull(userEl);
        Assert.Equal("nobody", userEl!.Value);
    }

    [Fact]
    public void GeneratePlist_UserModeIgnoresUserField()
    {
        var def = new ServiceDefinition
        {
            Name = "daemon",
            ScriptPath = "/services/daemon.stash",
            User = "nobody"
        };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/services/daemon.stash");

        string xml = doc.ToString();
        Assert.DoesNotContain("UserName", xml);
    }

    // ── Platform extras ───────────────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_WithPlatformExtras_AddsCustomKeys()
    {
        var def = new ServiceDefinition
        {
            Name = "myapp",
            ScriptPath = "/app/run.stash",
            PlatformExtras = new Dictionary<string, string>
            {
                ["ThrottleInterval"] = "60"
            }
        };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/app/run.stash");

        XElement root = GetRootDict(doc);
        XElement? extraEl = FindValue(root, "ThrottleInterval");
        Assert.NotNull(extraEl);
        Assert.Equal("60", extraEl!.Value);
    }

    // ── Cron expansion: ExpandCronToCalendarIntervals ─────────────────────────

    [Fact]
    public void ExpandCron_AllWildcards_ReturnsSingleEmptyDict()
    {
        // "* * * * *" → run every minute, single entry with no keys
        CronExpression expr = CronExpression.Parse("* * * * *");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Single(result);
        Assert.Empty(result[0]);
    }

    [Fact]
    public void ExpandCron_MinuteFixed_ReturnsSingleEntryWithMinute()
    {
        // "0 * * * *" → Minute=0
        CronExpression expr = CronExpression.Parse("0 * * * *");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Single(result);
        Assert.Equal(0, result[0]["Minute"]);
        Assert.False(result[0].ContainsKey("Hour"));
    }

    [Fact]
    public void ExpandCron_StepMinute_ReturnsCorrectCount()
    {
        // "*/5 * * * *" → 12 entries (0,5,10,15,20,25,30,35,40,45,50,55)
        CronExpression expr = CronExpression.Parse("*/5 * * * *");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Equal(12, result.Count);
        List<int> minutes = result.Select(d => d["Minute"]).OrderBy(m => m).ToList();
        Assert.Equal(new[] { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55 }, minutes);
    }

    [Fact]
    public void ExpandCron_HourAndMinuteFixed_ReturnsSingleEntry()
    {
        // "0 9 * * *" → Minute=0, Hour=9 (1 entry)
        CronExpression expr = CronExpression.Parse("0 9 * * *");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Single(result);
        Assert.Equal(0, result[0]["Minute"]);
        Assert.Equal(9, result[0]["Hour"]);
    }

    [Fact]
    public void ExpandCron_HourRangeAndDowRange_ReturnsCorrectCrossProduct()
    {
        // "0 9-17 * * 1-5" → 9 hours × 5 weekdays = 45 entries
        CronExpression expr = CronExpression.Parse("0 9-17 * * 1-5");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Equal(45, result.Count);
        Assert.All(result, d => Assert.Equal(0, d["Minute"]));
        List<int> hours = result.Select(d => d["Hour"]).Distinct().OrderBy(h => h).ToList();
        Assert.Equal(new[] { 9, 10, 11, 12, 13, 14, 15, 16, 17 }, hours);
        List<int> weekdays = result.Select(d => d["Weekday"]).Distinct().OrderBy(w => w).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, weekdays);
    }

    [Fact]
    public void ExpandCron_FirstDayOfMonth_ReturnsSingleEntry()
    {
        // "0 0 1 * *" → Minute=0, Hour=0, Day=1 (1 entry)
        CronExpression expr = CronExpression.Parse("0 0 1 * *");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Single(result);
        Assert.Equal(0, result[0]["Minute"]);
        Assert.Equal(0, result[0]["Hour"]);
        Assert.Equal(1, result[0]["Day"]);
    }

    [Fact]
    public void ExpandCron_SundayWeekday_ReturnsSingleEntry()
    {
        // "0 0 * * 0" → Minute=0, Hour=0, Weekday=0 (Sunday)
        CronExpression expr = CronExpression.Parse("0 0 * * 0");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Single(result);
        Assert.Equal(0, result[0]["Minute"]);
        Assert.Equal(0, result[0]["Hour"]);
        Assert.Equal(0, result[0]["Weekday"]);
    }

    [Fact]
    public void ExpandCron_ExceedsLimit_ThrowsInvalidOperationException()
    {
        // "*/15 9-17 * * 1-5" → 4 × 9 × 5 = 180 entries → over limit
        CronExpression expr = CronExpression.Parse("*/15 9-17 * * 1-5");

        Assert.Throws<InvalidOperationException>(() =>
            LaunchdServiceManager.ExpandCronToCalendarIntervals(expr));
    }

    [Fact]
    public void ExpandCron_WithinLimit_Succeeds()
    {
        // "0,30 9-11 * * 1-5" → 2 × 3 × 5 = 30 entries → within limit
        CronExpression expr = CronExpression.Parse("0,30 9-11 * * 1-5");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Equal(30, result.Count);
    }

    [Fact]
    public void ExpandCron_ListMinuteValues_IncludesAllListedValues()
    {
        // "1,3,5 * * * *" → 3 entries with minutes 1, 3, 5
        CronExpression expr = CronExpression.Parse("1,3,5 * * * *");

        IReadOnlyList<Dictionary<string, int>> result = LaunchdServiceManager.ExpandCronToCalendarIntervals(expr);

        Assert.Equal(3, result.Count);
        List<int> minutes = result.Select(d => d["Minute"]).OrderBy(m => m).ToList();
        Assert.Equal(new[] { 1, 3, 5 }, minutes);
    }

    // ── Plist document structure ──────────────────────────────────────────────

    [Fact]
    public void GeneratePlist_DocumentHasPlistDoctype()
    {
        var def = new ServiceDefinition { Name = "myapp", ScriptPath = "/app/run.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/app/run.stash");

        Assert.NotNull(doc.DocumentType);
        Assert.Equal("plist", doc.DocumentType!.Name);
    }

    [Fact]
    public void GeneratePlist_RootElementIsPlistWithVersion10()
    {
        var def = new ServiceDefinition { Name = "myapp", ScriptPath = "/app/run.stash" };
        var manager = UserModeManager();

        XDocument doc = manager.GeneratePlist(def, "/app/run.stash");

        Assert.NotNull(doc.Root);
        Assert.Equal("plist", doc.Root!.Name.LocalName);
        Assert.Equal("1.0", doc.Root.Attribute("version")?.Value);
    }

    // ── ParseLaunchctlListState ───────────────────────────────────────────────

    [Fact]
    public void ParseLaunchctlListState_NonZeroExitWithSidecar_ReturnsOrphaned()
    {
        var sidecar = new SidecarData { Name = "test", ScriptPath = "/a.stash" };
        ServiceState result = LaunchdServiceManager.ParseLaunchctlListState(1, "", sidecar);
        Assert.Equal(ServiceState.Orphaned, result);
    }

    [Fact]
    public void ParseLaunchctlListState_NonZeroExitNoSidecar_ReturnsUnknown()
    {
        ServiceState result = LaunchdServiceManager.ParseLaunchctlListState(1, "", null);
        Assert.Equal(ServiceState.Unknown, result);
    }

    [Fact]
    public void ParseLaunchctlListState_ZeroExitNoSidecar_ReturnsUnmanaged()
    {
        ServiceState result = LaunchdServiceManager.ParseLaunchctlListState(0, "{}", null);
        Assert.Equal(ServiceState.Unmanaged, result);
    }

    [Fact]
    public void ParseLaunchctlListState_WithPidRunning_ReturnsRunning()
    {
        var sidecar = new SidecarData { Name = "test", ScriptPath = "/a.stash" };
        string output = """
            {
                "Label" = "com.stash.test";
                "LastExitStatus" = 0;
                "PID" = 1234;
            };
            """;
        ServiceState result = LaunchdServiceManager.ParseLaunchctlListState(0, output, sidecar);
        Assert.Equal(ServiceState.Running, result);
    }

    [Fact]
    public void ParseLaunchctlListState_NoPidWithNonZeroExit_ReturnsFailed()
    {
        var sidecar = new SidecarData { Name = "test", ScriptPath = "/a.stash" };
        string output = """
            {
                "Label" = "com.stash.test";
                "LastExitStatus" = 256;
            };
            """;
        ServiceState result = LaunchdServiceManager.ParseLaunchctlListState(0, output, sidecar);
        Assert.Equal(ServiceState.Failed, result);
    }

    [Fact]
    public void ParseLaunchctlListState_NoPidWithZeroExit_ReturnsActive()
    {
        var sidecar = new SidecarData { Name = "test", ScriptPath = "/a.stash" };
        string output = """
            {
                "Label" = "com.stash.test";
                "LastExitStatus" = 0;
            };
            """;
        ServiceState result = LaunchdServiceManager.ParseLaunchctlListState(0, output, sidecar);
        Assert.Equal(ServiceState.Active, result);
    }
}
