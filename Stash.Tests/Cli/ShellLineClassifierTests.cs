using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Stdlib;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="ShellLineClassifier"/> covering §4 disambiguation rules.
/// PathExecutableCache accepts an injectable delegate for test isolation.
/// </summary>
public class ShellLineClassifierTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Build a classifier. <paramref name="isExec"/> is the seam for PATH lookups.</summary>
    private static ShellLineClassifier MakeClassifier(
        Func<string, bool>? isExec = null,
        IEnumerable<string>? globals = null)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());

        // Inject any "already declared" globals.
        if (globals != null)
        {
            foreach (string name in globals)
                vm.Globals[name] = Stash.Runtime.StashValue.FromObject(null);
        }

        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(isExec ?? (_ => false)),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        return new ShellLineClassifier(ctx);
    }

    // ── §4.1 Unambiguous Stash tokens ────────────────────────────────────────

    [Fact]
    public void Classify_Keyword_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("let x = 5"));
    }

    [Fact]
    public void Classify_SoftKeyword_Async_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("async fn foo() {}"));
    }

    [Fact]
    public void Classify_SoftKeyword_Defer_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("defer io.println(1)"));
    }

    [Fact]
    public void Classify_NumberLiteral_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("42"));
    }

    [Fact]
    public void Classify_StringLiteral_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("\"hello\""));
    }

    [Fact]
    public void Classify_PathLike_AbsoluteSlash_ReturnsShell()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Shell, clf.Classify("/usr/bin/echo hello"));
    }

    [Fact]
    public void Classify_PathLike_DotSlash_ReturnsShell()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Shell, clf.Classify("./run.sh --fast"));
    }

    [Fact]
    public void Classify_PathLike_DotDotSlash_ReturnsShell()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Shell, clf.Classify("../scripts/deploy.sh"));
    }

    [Fact]
    public void Classify_PathLike_HomeTilde_ReturnsShell()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Shell, clf.Classify("~/bin/myscript"));
    }

    [Fact]
    public void Classify_PathLike_BareTilde_ReturnsShell()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Shell, clf.Classify("~"));
    }

    [Fact]
    public void Classify_OpenParen_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("(1 + 2)"));
    }

    [Fact]
    public void Classify_OpenBracket_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("[1, 2, 3]"));
    }

    [Fact]
    public void Classify_DollarCommandExpr_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("$(ls -la)"));
    }

    // ── §4.4 Identifier disambiguation ───────────────────────────────────────

    [Fact]
    public void Classify_Namespace_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("fs.readFile(\"/etc/hosts\")"));
    }

    [Fact]
    public void Classify_AnotherNamespace_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("str.upper(x)"));
    }

    [Fact]
    public void Classify_DeclaredGlobal_ReturnsStash()
    {
        var clf = MakeClassifier(globals: new[] { "myVar" });
        Assert.Equal(LineMode.Stash, clf.Classify("myVar"));
    }

    [Fact]
    public void Classify_DeclaredGlobal_WithOperator_ReturnsStash()
    {
        var clf = MakeClassifier(globals: new[] { "git" });
        // 'git' is declared → Stash wins even though 'git' is on PATH.
        Assert.Equal(LineMode.Stash, clf.Classify("git = 5"));
    }

    [Fact]
    public void Classify_IdentifierFollowedByDot_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("foo.bar"));
    }

    [Fact]
    public void Classify_IdentifierFollowedByOpenParen_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("foo()"));
    }

    [Fact]
    public void Classify_IdentifierFollowedByEquals_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("foo = 5"));
    }

    [Fact]
    public void Classify_IdentifierFollowedByPlusEquals_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("foo += 1"));
    }

    [Fact]
    public void Classify_IdentifierFollowedByArithmetic_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("foo + 1"));
    }

    [Fact]
    public void Classify_IdentifierFollowedByOpenBracket_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("foo[0]"));
    }

    [Fact]
    public void Classify_OnPathExecutable_ReturnsShell()
    {
        var clf = MakeClassifier(isExec: name => name == "git");
        Assert.Equal(LineMode.Shell, clf.Classify("git status"));
    }

    [Fact]
    public void Classify_KnownShellBuiltin_ReturnsShell()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Shell, clf.Classify("cd /tmp"));
    }

    [Fact]
    public void Classify_UnknownIdentifier_NoPath_ReturnsStash()
    {
        var clf = MakeClassifier(isExec: _ => false);
        Assert.Equal(LineMode.Stash, clf.Classify("unknowncmd --flags"));
    }

    [Fact]
    public void Classify_BlankLine_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify(""));
    }

    [Fact]
    public void Classify_Whitespace_ReturnsStash()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("   "));
    }

    [Fact]
    public void Classify_PipelineWithExecutable_ReturnsShell()
    {
        var clf = MakeClassifier(isExec: name => name == "ls" || name == "grep");
        Assert.Equal(LineMode.Shell, clf.Classify("ls -la | grep txt"));
    }

    [Fact]
    public void Classify_BareIdentifier_OnPath_NoArgs_ReturnsShell()
    {
        // Bare `ls` (end-of-line) — not declared, on PATH → Shell.
        // Spec §4.4 step 3 lookup chain runs even when next token is EOL.
        var clf = MakeClassifier(isExec: name => name == "ls");
        Assert.Equal(LineMode.Shell, clf.Classify("ls"));
    }

    [Fact]
    public void Classify_BareShellBuiltin_NoArgs_ReturnsShell()
    {
        // Bare `pwd` (end-of-line) — shell builtin sugar always wins over the
        // bare-identifier-expression interpretation when no Stash global shadows it.
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Shell, clf.Classify("pwd"));
    }

    [Fact]
    public void Classify_BareUnknown_NotOnPath_ReturnsStash()
    {
        // Bare `xyz` — not declared, not a builtin, not on PATH → Stash
        // (will produce an undefined-identifier error, per spec §4.4 fallback).
        var clf = MakeClassifier(isExec: _ => false);
        Assert.Equal(LineMode.Stash, clf.Classify("xyzNotReal"));
    }

    [Fact]
    public void Classify_IdentifierFollowedBySemicolon_ReturnsStash()
    {
        // Spec §4.4 step 2: `;` after a bare identifier → Stash mode.
        // Without this, `ls;` would route to Shell and the lexer would treat
        // `ls;` as a single program name → "command not found: ls;".
        var clf = MakeClassifier(isExec: name => name == "ls");
        Assert.Equal(LineMode.Stash, clf.Classify("ls;"));
    }

    // ── §8.1 IsShellIncomplete ────────────────────────────────────────────────

    [Fact]
    public void IsShellIncomplete_ShellLineEndingWithPipe_ReturnsTrue()
    {
        var clf = MakeClassifier(isExec: name => name == "ls");
        Assert.True(clf.IsShellIncomplete("ls -la |"));
    }

    [Fact]
    public void IsShellIncomplete_ShellLineNotEndingWithPipe_ReturnsFalse()
    {
        var clf = MakeClassifier(isExec: name => name == "ls");
        Assert.False(clf.IsShellIncomplete("ls -la"));
    }

    [Fact]
    public void IsShellIncomplete_StashLineEndingWithPipe_ReturnsFalse()
    {
        // 'let' is Stash mode → IsShellIncomplete returns false even if trailing '|'.
        var clf = MakeClassifier();
        Assert.False(clf.IsShellIncomplete("let x = 5 |"));
    }

    // ── §4.2 Backslash-force prefix ──────────────────────────────────────────

    [Fact]
    public void Classify_BackslashPrefix_ReturnsShellForced()
    {
        // \ls bypasses the global-symbol check and routes to ShellForced,
        // even when 'ls' is declared as a Stash global.
        var clf = MakeClassifier(globals: new[] { "ls" });
        Assert.Equal(LineMode.ShellForced, clf.Classify("\\ls -la"));
    }

    [Fact]
    public void Classify_BackslashFollowedBySpace_ReturnsStash()
    {
        // '\ ls' — backslash then whitespace — is not the forced prefix; falls to Stash.
        var clf = MakeClassifier(isExec: name => name == "ls");
        Assert.Equal(LineMode.Stash, clf.Classify("\\ ls"));
    }

    [Fact]
    public void Classify_BackslashBangOrder_ReturnsStash()
    {
        // \!ls (reverse order) is NOT supported; treated as Stash.
        var clf = MakeClassifier(isExec: name => name == "ls");
        Assert.Equal(LineMode.Stash, clf.Classify("\\!ls"));
    }

    [Fact]
    public void Classify_BackslashAbsolutePath_ReturnsShellForced()
    {
        var clf = MakeClassifier();
        Assert.Equal(LineMode.ShellForced, clf.Classify("\\/usr/bin/git"));
    }

    // ── §4.3 Bang-strict prefix ───────────────────────────────────────────────

    [Fact]
    public void Classify_BangPrefix_PathResolvable_ReturnsShellStrict()
    {
        // 'ls' not a keyword/global, is on PATH → ShellStrict.
        var clf = MakeClassifier(isExec: name => name == "ls");
        Assert.Equal(LineMode.ShellStrict, clf.Classify("!ls -la"));
    }

    [Fact]
    public void Classify_BangPrefix_DeclaredSymbol_ReturnsStash()
    {
        // !myFlag — 'myFlag' is a declared Stash global → logical-not in Stash.
        var clf = MakeClassifier(globals: new[] { "myFlag" });
        Assert.Equal(LineMode.Stash, clf.Classify("!myFlag"));
    }

    [Fact]
    public void Classify_BangPrefix_Keyword_ReturnsStash()
    {
        // !true / !false → logical-not; 'true'/'false' are Stash keywords.
        var clf = MakeClassifier(isExec: name => name == "true");
        Assert.Equal(LineMode.Stash, clf.Classify("!true"));
        Assert.Equal(LineMode.Stash, clf.Classify("!false"));
    }

    [Fact]
    public void Classify_BangNonIdentifier_ReturnsStash()
    {
        // != is a comparison operator, not a shell command prefix.
        var clf = MakeClassifier();
        Assert.Equal(LineMode.Stash, clf.Classify("!="));
    }

    [Fact]
    public void Classify_BangBackslashPrefix_ReturnsShellStrict()
    {
        // !\ls → ShellStrict (the lexer will set IsForced=true as well).
        var clf = MakeClassifier(isExec: name => name == "ls");
        Assert.Equal(LineMode.ShellStrict, clf.Classify("!\\ls"));
    }
}
