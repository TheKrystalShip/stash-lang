using System;
using System.Collections.Generic;
using System.IO;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="ArgExpander"/> covering the §6 expansion pipeline:
/// interpolation, tilde expansion, word splitting, and glob expansion.
/// </summary>
public class ShellArgExpansionTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static VirtualMachine MakeVm()
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return vm;
    }

    private static List<string> Expand(string rawArgs, VirtualMachine? vm = null)
        => ArgExpander.Expand(rawArgs, vm ?? MakeVm(), span: null);

    // ── Word splitting ────────────────────────────────────────────────────────

    [Fact]
    public void Expand_EmptyString_ReturnsEmptyList()
    {
        var result = Expand("");
        Assert.Empty(result);
    }

    [Fact]
    public void Expand_SingleArg_ReturnsSingleElement()
    {
        var result = Expand("hello");
        Assert.Equal(["hello"], result);
    }

    [Fact]
    public void Expand_MultipleSpaceDelimited_ReturnsSplit()
    {
        var result = Expand("-l -a /tmp");
        Assert.Equal(["-l", "-a", "/tmp"], result);
    }

    [Fact]
    public void Expand_TabSeparated_SplitsOnTab()
    {
        var result = Expand("a\tb");
        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public void Expand_LeadingTrailingSpaces_TrimsWords()
    {
        var result = Expand("  a  b  ");
        Assert.Equal(["a", "b"], result);
    }

    // ── Tilde expansion ───────────────────────────────────────────────────────

    [Fact]
    public void Expand_BareTilde_ExpandsToHome()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = Expand("~");
        Assert.Equal([home], result);
    }

    [Fact]
    public void Expand_TildeSlash_ExpandsToHomeDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = Expand("~/projects");
        Assert.Single(result);
        Assert.StartsWith(home, result[0], StringComparison.Ordinal);
        Assert.Contains("projects", result[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_TildeInMiddleOfWord_NotExpanded()
    {
        var result = Expand("foo~bar");
        Assert.Equal(["foo~bar"], result);
    }

    // ── Single and double quotes ──────────────────────────────────────────────

    [Fact]
    public void Expand_DoubleQuoted_PreservesSpaces()
    {
        var result = Expand("\"hello world\"");
        Assert.Equal(["hello world"], result);
    }

    [Fact]
    public void Expand_SingleQuoted_PreservesSpaces()
    {
        var result = Expand("'hello world'");
        Assert.Equal(["hello world"], result);
    }

    [Fact]
    public void Expand_QuotedAndUnquoted_MergeIntoOneWord()
    {
        var result = Expand("foo\"bar\"");
        Assert.Equal(["foobar"], result);
    }

    [Fact]
    public void Expand_UnquotedThenQuoted_SeparateWords()
    {
        var result = Expand("a \"b c\"");
        Assert.Equal(["a", "b c"], result);
    }

    // ── Escape sequences ──────────────────────────────────────────────────────

    [Fact]
    public void Expand_EscapedDollar_ProducesLiteralDollar()
    {
        var result = Expand(@"\$literal");
        Assert.Equal(["$literal"], result);
    }

    [Fact]
    public void Expand_EscapedBackslash_ProducesLiteralBackslash()
    {
        var result = Expand(@"\\path");
        Assert.Equal([@"\path"], result);
    }

    // ── ${expr} interpolation ─────────────────────────────────────────────────

    [Fact]
    public void Expand_SimpleInterpolation_EvaluatesExpression()
    {
        var result = Expand("${1 + 2}");
        Assert.Equal(["3"], result);
    }

    [Fact]
    public void Expand_InterpolatedStringResult_NotWordSplit()
    {
        // ${expr} result contains spaces → must NOT be word-split.
        var vm = MakeVm();
        vm.Globals["greeting"] = StashValue.FromObject("hello world");
        var result = ArgExpander.Expand("${greeting}", vm, null);
        // Should be a single arg, not two.
        Assert.Single(result);
        Assert.Equal("hello world", result[0]);
    }

    [Fact]
    public void Expand_InterpolationInDoubleQuotes_Interpolates()
    {
        var result = Expand("\"prefix-${2 * 3}-suffix\"");
        Assert.Equal(["prefix-6-suffix"], result);
    }

    [Fact]
    public void Expand_InterpolationInSingleQuotes_Interpolates()
    {
        // Stash semantics: single quotes still interpolate ${}.
        var result = Expand("'value-${10}-end'");
        Assert.Equal(["value-10-end"], result);
    }

    [Fact]
    public void Expand_MultipleInterpolations_EachExpanded()
    {
        var result = Expand("${1} and ${2}");
        Assert.Equal(["1", "and", "2"], result);
    }

    // ── --flag=value style args ───────────────────────────────────────────────

    [Fact]
    public void Expand_FlagEqualsValue_PreservedAsSingleArg()
    {
        var result = Expand("--output=/tmp/out.txt");
        Assert.Equal(["--output=/tmp/out.txt"], result);
    }

    // ── Glob expansion ────────────────────────────────────────────────────────

    [Fact]
    public void Expand_GlobNoMatch_ThrowsRuntimeError()
    {
        // A glob that can't match in a random temp dir.
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string pattern = Path.Combine(dir, "*.xyz");
            var ex = Assert.Throws<RuntimeError>(() => Expand(pattern));
            Assert.Contains("did not match", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: false);
        }
    }

    [Fact]
    public void Expand_GlobInDoubleQuotes_TreatedAsLiteral()
    {
        // Quoted globs are NOT expanded.
        var result = Expand("\"*.txt\"");
        Assert.Equal(["*.txt"], result);
    }

    [Fact]
    public void Expand_GlobMatchesFiles_ReturnsMatches()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "");
            string pattern = Path.Combine(dir, "*.txt");
            var result = Expand(pattern);
            Assert.Equal(2, result.Count);
            Assert.All(result, r => Assert.EndsWith(".txt", r, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
