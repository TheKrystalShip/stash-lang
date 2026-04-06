using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

/// <summary>
/// Regression tests: verifies that enums registered via NamespaceBuilder.Enum()
/// are available as runtime values, not just metadata.
/// See: NamespaceBuilder.Enum() must call _namespace.Define() like Struct() does.
/// </summary>
public class NamespaceBuilderEnumTests : StashTestBase
{
    // ── Global enums ─────────────────────────────────────────────────────────

    [Fact]
    public void GlobalEnum_Backoff_IsAccessible()
    {
        var result = Run("let result = typeof(Backoff);");
        Assert.Equal("enum", result);
    }

    [Fact]
    public void GlobalEnum_Backoff_MembersAccessible()
    {
        var result = Run("let result = Backoff.Fixed;");
        Assert.IsType<StashEnumValue>(result);
    }

    [Fact]
    public void GlobalEnum_Backoff_AllMembers()
    {
        var result = Run("""
            let result = "${Backoff.Fixed},${Backoff.Linear},${Backoff.Exponential}";
            """);
        Assert.Equal("Backoff.Fixed,Backoff.Linear,Backoff.Exponential", result);
    }

    // ── Namespaced enums ─────────────────────────────────────────────────────

    [Fact]
    public void TaskEnum_Status_IsAccessible()
    {
        var result = Run("let result = typeof(task.Status);");
        Assert.Equal("enum", result);
    }

    [Fact]
    public void TaskEnum_Status_MembersAccessible()
    {
        var result = Run("let result = task.Status.Running;");
        Assert.IsType<StashEnumValue>(result);
    }

    [Fact]
    public void SysEnum_Signal_IsAccessible()
    {
        var result = Run("let result = typeof(sys.Signal);");
        Assert.Equal("enum", result);
    }

    [Fact]
    public void SysEnum_Signal_MembersAccessible()
    {
        var result = Run("let result = sys.Signal.SIGINT;");
        Assert.IsType<StashEnumValue>(result);
    }

    [Fact]
    public void FsEnum_WatchEventType_IsAccessible()
    {
        var result = Run("let result = typeof(fs.WatchEventType);");
        Assert.Equal("enum", result);
    }

    [Fact]
    public void FsEnum_WatchEventType_MembersAccessible()
    {
        var result = Run("let result = fs.WatchEventType.Created;");
        Assert.IsType<StashEnumValue>(result);
    }

    // ── Enum identity and equality ───────────────────────────────────────────

    [Fact]
    public void EnumValue_Equality_SameMember()
    {
        var result = Run("let result = Backoff.Fixed == Backoff.Fixed;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void EnumValue_Inequality_DifferentMembers()
    {
        var result = Run("let result = Backoff.Fixed == Backoff.Linear;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void EnumValue_Nameof_ReturnsQualifiedName()
    {
        var result = Run("let result = nameof(Backoff.Exponential);");
        Assert.Equal("Backoff.Exponential", result);
    }
}
