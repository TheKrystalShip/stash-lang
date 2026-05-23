namespace Stash.Tests.Analysis;

using System.Linq;
using Stash.Analysis;

/// <summary>
/// Tests for SA0845 — Assignment to read-only namespace member.
/// Verifies that assigning to built-in namespace members and user-module aliases
/// emits SA0845, while valid assignments produce no false positives.
/// </summary>
public class ReadOnlyNamespaceTests : AnalysisTestBase
{
    // =========================================================================
    // Positive cases — SA0845 must fire
    // =========================================================================

    [Fact]
    public void BuiltInNamespace_AssignToDataMember_EmitsSA0845()
    {
        // cli.argv is a built-in DataMember; assignment must be rejected.
        var diagnostics = Validate("cli.argv = [];");
        Assert.Contains(diagnostics, d => d.Code == "SA0845");
    }

    [Fact]
    public void BuiltInNamespace_AssignToConstant_EmitsSA0845()
    {
        // math.PI is a built-in Constant; assignment must also be rejected.
        var diagnostics = Validate("math.PI = 0;");
        Assert.Contains(diagnostics, d => d.Code == "SA0845");
    }

    [Fact]
    public void SA0845_Message_ContainsQualifiedName()
    {
        var diagnostics = Validate("cli.argv = [];");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0845");
        Assert.NotNull(d);
        Assert.Contains("cli.argv", d!.Message);
    }

    [Fact]
    public void SA0845_Message_SaysReadOnly()
    {
        var diagnostics = Validate("math.PI = 0;");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0845");
        Assert.NotNull(d);
        Assert.Contains("read-only", d!.Message);
    }

    [Fact]
    public void SA0845_Severity_IsError()
    {
        var diagnostics = Validate("cli.argv = [];");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0845");
        Assert.NotNull(d);
        Assert.Equal(DiagnosticLevel.Error, d!.Level);
    }

    [Fact]
    public void UserModuleAlias_AssignToExport_EmitsSA0845()
    {
        // After `import "./mod.stash" as mod;`, `mod.PI = 0` must emit SA0845
        // because the receiver is a module alias (ImportAsStmt).
        var source = """
            import "./mod.stash" as mod;
            mod.PI = 0;
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0845");
    }

    [Fact]
    public void UserModuleAlias_Assignment_MessageContainsQualifiedName()
    {
        var source = """
            import "./mod.stash" as mod;
            mod.PI = 0;
            """;
        var diagnostics = Validate(source);
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0845");
        Assert.NotNull(d);
        Assert.Contains("mod.PI", d!.Message);
    }

    [Fact]
    public void BuiltInNamespace_AssignToAnyField_EmitsSA0845()
    {
        // Even assigning to a function-kind member should fire SA0845 on the namespace receiver.
        var diagnostics = Validate("io.println = null;");
        Assert.Contains(diagnostics, d => d.Code == "SA0845");
    }

    // =========================================================================
    // Negative cases — SA0845 must NOT fire
    // =========================================================================

    [Fact]
    public void StructInstance_DotAssign_DoesNotEmitSA0845()
    {
        // Assigning to a struct field is perfectly valid.
        var source = """
            struct Point { x: int; y: int; }
            let p = Point { x: 1, y: 2 };
            p.x = 10;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0845");
    }

    [Fact]
    public void DictDotAssign_DoesNotEmitSA0845()
    {
        // Dictionary dot-assignment is allowed.
        var source = """
            let d = { "key": 1 };
            d.key = 2;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0845");
    }

    [Fact]
    public void NonNamespaceVariable_DotAssign_DoesNotEmitSA0845()
    {
        // Assignment to an arbitrary variable's field must not produce SA0845.
        var diagnostics = Validate("let x = null; x.foo = 1;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0845");
    }

    [Fact]
    public void BuiltInNamespace_BareRead_DoesNotEmitSA0845()
    {
        // Reading a namespace member is valid.
        var diagnostics = Validate("let x = cli.argv;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0845");
    }
}
