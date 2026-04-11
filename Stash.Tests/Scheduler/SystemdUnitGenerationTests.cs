namespace Stash.Tests.Scheduler;

using Stash.Scheduler;
using Stash.Scheduler.Models;

public class SystemdUnitGenerationTests
{
    // ── CronExpression to systemd calendar — additional edge cases ───────────

    [Theory]
    [InlineData("0 0 * * *",    "*-*-* 00:00:00")]
    [InlineData("*/10 * * * *", "*-*-* *:0/10:00")]
    [InlineData("0 */2 * * *",  "*-*-* 0/2:00:00")]
    [InlineData("0 0 1,15 * *", "*-*-01,15 00:00:00")]
    public void CronToSystemd_MatrixCases_ProducesExpectedCalendar(string cron, string expected)
    {
        var expr = CronExpression.Parse(cron);

        Assert.Equal(expected, expr.ToSystemdCalendar());
    }

    // ── ServiceResult ────────────────────────────────────────────────────────

    [Fact]
    public void ServiceResult_Ok_IsSuccess()
    {
        ServiceResult result = ServiceResult.Ok();

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ServiceResult_Fail_IsNotSuccess()
    {
        ServiceResult result = ServiceResult.Fail("something went wrong");

        Assert.False(result.Success);
    }

    [Fact]
    public void ServiceResult_Fail_ErrorMessage()
    {
        const string message = "something went wrong";

        ServiceResult result = ServiceResult.Fail(message);

        Assert.Equal(message, result.Error);
    }

    // ── ServiceDefinition defaults ───────────────────────────────────────────

    [Fact]
    public void ServiceDefinition_Defaults_AreCorrect()
    {
        var def = new ServiceDefinition
        {
            Name = "test",
            ScriptPath = "/tmp/test.stash"
        };

        Assert.True(def.AutoStart);
        Assert.True(def.RestartOnFailure);
        Assert.Equal(0, def.MaxRestarts);
        Assert.Equal(5, def.RestartDelaySec);
        Assert.False(def.SystemMode);
    }

    // ── ServiceState enum ────────────────────────────────────────────────────

    [Fact]
    public void ServiceState_HasExpectedValues()
    {
        var values = System.Enum.GetValues<ServiceState>();

        Assert.Contains(ServiceState.Unknown,   values);
        Assert.Contains(ServiceState.Active,    values);
        Assert.Contains(ServiceState.Inactive,  values);
        Assert.Contains(ServiceState.Running,   values);
        Assert.Contains(ServiceState.Stopped,   values);
        Assert.Contains(ServiceState.Failed,    values);
        Assert.Contains(ServiceState.Orphaned,  values);
        Assert.Contains(ServiceState.Unmanaged, values);
    }
}
