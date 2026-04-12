namespace Stash.Tests.Scheduler;

using System.Collections.Generic;
using Stash.Scheduler;
using Stash.Scheduler.Validation;
using Stash.Scheduler.Models;

public class InputValidatorTests
{
    // ── Service name ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("my-service")]
    [InlineData("a")]
    [InlineData("a1_b-c")]
    public void ValidateServiceName_ValidName_Succeeds(string name)
    {
        ServiceResult result = InputValidator.ValidateServiceName(name);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ValidateServiceName_Empty_Fails()
    {
        ServiceResult result = InputValidator.ValidateServiceName("");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ValidateServiceName_StartsWithDigit_Fails()
    {
        ServiceResult result = InputValidator.ValidateServiceName("1abc");

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateServiceName_StartsWithHyphen_Fails()
    {
        ServiceResult result = InputValidator.ValidateServiceName("-abc");

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateServiceName_ContainsDot_Fails()
    {
        ServiceResult result = InputValidator.ValidateServiceName("my.service");

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateServiceName_ContainsSlash_Fails()
    {
        ServiceResult result = InputValidator.ValidateServiceName("my/service");

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateServiceName_ContainsSpace_Fails()
    {
        ServiceResult result = InputValidator.ValidateServiceName("my service");

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateServiceName_TooLong_Fails()
    {
        string longName = "a" + new string('b', 64); // 65 chars

        ServiceResult result = InputValidator.ValidateServiceName(longName);

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateServiceName_MaxLength_Succeeds()
    {
        string maxName = "a" + new string('b', 63); // 64 chars

        ServiceResult result = InputValidator.ValidateServiceName(maxName);

        Assert.True(result.Success);
    }

    // ── Script path ──────────────────────────────────────────────────────────

    [Fact]
    public void ValidateScriptPath_Empty_Fails()
    {
        ValidateScriptPathResult result = InputValidator.ValidateScriptPath("");

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateScriptPath_NonExistentFile_Fails()
    {
        ValidateScriptPathResult result = InputValidator.ValidateScriptPath(
            "/tmp/this_file_should_not_exist_ever_abc123.stash");

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateScriptPath_WrongExtension_Fails()
    {
        string tmpFile = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"stash_test_{System.Guid.NewGuid()}.txt");
        System.IO.File.WriteAllText(tmpFile, "");
        try
        {
            ValidateScriptPathResult result = InputValidator.ValidateScriptPath(tmpFile);

            Assert.False(result.Success);
        }
        finally
        {
            System.IO.File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ValidateScriptPath_ValidStashFile_Succeeds()
    {
        string tmpFile = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"stash_test_{System.Guid.NewGuid()}.stash");
        System.IO.File.WriteAllText(tmpFile, "");
        try
        {
            ValidateScriptPathResult result = InputValidator.ValidateScriptPath(tmpFile);

            Assert.True(result.Success);
            Assert.NotNull(result.ResolvedPath);
        }
        finally
        {
            System.IO.File.Delete(tmpFile);
        }
    }

    // ── Working directory ────────────────────────────────────────────────────

    [Fact]
    public void ValidateWorkingDirectory_Null_Succeeds()
    {
        ServiceResult result = InputValidator.ValidateWorkingDirectory(null);

        Assert.True(result.Success);
    }

    [Fact]
    public void ValidateWorkingDirectory_ExistingDir_Succeeds()
    {
        ServiceResult result = InputValidator.ValidateWorkingDirectory("/tmp");

        Assert.True(result.Success);
    }

    [Fact]
    public void ValidateWorkingDirectory_NonExistentDir_Fails()
    {
        ServiceResult result = InputValidator.ValidateWorkingDirectory(
            "/tmp/this_dir_should_not_exist_ever_abc123xyz");

        Assert.False(result.Success);
    }

    // ── Environment variables ────────────────────────────────────────────────

    [Fact]
    public void ValidateEnvironment_Null_Succeeds()
    {
        ServiceResult result = InputValidator.ValidateEnvironment(null);

        Assert.True(result.Success);
    }

    [Fact]
    public void ValidateEnvironment_ValidKeys_Succeeds()
    {
        var env = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["_VAR"] = "x"
        };

        ServiceResult result = InputValidator.ValidateEnvironment(env);

        Assert.True(result.Success);
    }

    [Fact]
    public void ValidateEnvironment_InvalidKey_Fails()
    {
        var env = new Dictionary<string, string>
        {
            ["1bad"] = "value"
        };

        ServiceResult result = InputValidator.ValidateEnvironment(env);

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidateEnvironment_InvalidKeyWithDash_Fails()
    {
        var env = new Dictionary<string, string>
        {
            ["my-var"] = "value"
        };

        ServiceResult result = InputValidator.ValidateEnvironment(env);

        Assert.False(result.Success);
    }

    // ── Schedule ─────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateSchedule_Null_Succeeds()
    {
        ValidateScheduleResult result = InputValidator.ValidateSchedule(null);

        Assert.True(result.Success);
        Assert.Null(result.ParsedSchedule);
    }

    [Fact]
    public void ValidateSchedule_ValidCron_Succeeds()
    {
        ValidateScheduleResult result = InputValidator.ValidateSchedule("*/5 * * * *");

        Assert.True(result.Success);
        Assert.NotNull(result.ParsedSchedule);
    }

    [Fact]
    public void ValidateSchedule_InvalidCron_Fails()
    {
        ValidateScheduleResult result = InputValidator.ValidateSchedule("invalid");

        Assert.False(result.Success);
        Assert.Null(result.ParsedSchedule);
    }

    // ── Platform extras ──────────────────────────────────────────────────────

    [Fact]
    public void ValidatePlatformExtras_Null_Succeeds()
    {
        ValidatePlatformExtrasResult result = InputValidator.ValidatePlatformExtras(null, systemMode: false);

        Assert.True(result.Success);
    }

    [Fact]
    public void ValidatePlatformExtras_UserMode_BlocksSecurityKey_Fails()
    {
        var extras = new Dictionary<string, string> { ["User"] = "root" };

        ValidatePlatformExtrasResult result = InputValidator.ValidatePlatformExtras(extras, systemMode: false);

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidatePlatformExtras_SystemMode_AllowsSecurityKey_Succeeds()
    {
        var extras = new Dictionary<string, string> { ["User"] = "deploy" };

        ValidatePlatformExtrasResult result = InputValidator.ValidatePlatformExtras(extras, systemMode: true);

        Assert.True(result.Success);
    }

    [Fact]
    public void ValidatePlatformExtras_NewlineInKey_Fails()
    {
        var extras = new Dictionary<string, string> { ["Key\nEvil"] = "value" };

        ValidatePlatformExtrasResult result = InputValidator.ValidatePlatformExtras(extras, systemMode: true);

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidatePlatformExtras_NewlineInValue_Fails()
    {
        var extras = new Dictionary<string, string> { ["Key"] = "val\nue" };

        ValidatePlatformExtrasResult result = InputValidator.ValidatePlatformExtras(extras, systemMode: true);

        Assert.False(result.Success);
    }

    [Fact]
    public void ValidatePlatformExtras_ValidExtras_Succeeds()
    {
        var extras = new Dictionary<string, string> { ["MemoryMax"] = "512M" };

        ValidatePlatformExtrasResult result = InputValidator.ValidatePlatformExtras(extras, systemMode: false);

        Assert.True(result.Success);
    }
}
