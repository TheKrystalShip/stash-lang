namespace Stash.Tests.Lsp.Completion;

using System.Collections.Generic;
using System.Linq;
using Stash.Lsp.Completion.Snippets;
using Xunit;

/// <summary>
/// Unit tests for <see cref="SnippetValidator"/>.
/// One test per validation rule (seven rules total) plus the tabstop-strip behaviour.
/// </summary>
public class SnippetValidatorTests
{
    // ── Rule 1: Prefix shape ────────────────────────────────────────────────────

    [Fact]
    public void Validate_Rule1_EmptyPrefix_IsRejected()
    {
        var raw = MakeRaw(prefix: "", body: "let x = 1;");
        var result = Validate(("Empty Prefix", raw));

        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
        Assert.Contains("prefix", result.Errors[0].Reason);
    }

    [Fact]
    public void Validate_Rule1_InvalidPrefixStartsWithDigit_IsRejected()
    {
        var raw = MakeRaw(prefix: "1invalid", body: "let x = 1;");
        var result = Validate(("Bad Prefix", raw));

        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
        Assert.Contains("prefix", result.Errors[0].Reason);
    }

    [Fact]
    public void Validate_Rule1_DotSeparatedPrefix_IsAccepted()
    {
        // e.g. "test.it" is a valid prefix (allows dots per brief spec)
        var raw = MakeRaw(prefix: "test.it", body: "let x = 1;");
        var result = Validate(("Test.It", raw));

        Assert.Single(result.Valid);
        Assert.Empty(result.Errors);
    }

    // ── Rule 2: Body presence ───────────────────────────────────────────────────

    [Fact]
    public void Validate_Rule2_EmptyBody_IsRejected()
    {
        var raw = MakeRaw(prefix: "mysnip", body: "");
        var result = Validate(("Empty Body", raw));

        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
        Assert.Contains("body", result.Errors[0].Reason);
    }

    [Fact]
    public void Validate_Rule2_WhitespaceOnlyBody_IsRejected()
    {
        var raw = MakeRaw(prefix: "mysnip", body: "   ");
        var result = Validate(("Whitespace Body", raw));

        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
        Assert.Contains("body", result.Errors[0].Reason);
    }

    // ── Rule 3: Lex success ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_Rule3_UnterminatedStringInBody_IsRejected()
    {
        // An unterminated string literal should fail to lex.
        var raw = MakeRaw(prefix: "badlex", body: "let x = \"unterminated;");
        var result = Validate(("Lex Failure", raw));

        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
        Assert.Contains("lex", result.Errors[0].Reason);
    }

    // ── Rule 4: Parse success ───────────────────────────────────────────────────

    [Fact]
    public void Validate_Rule4_IncompleteExpression_IsRejected()
    {
        // "let x =" is incomplete and should fail to parse.
        var raw = MakeRaw(prefix: "badparse", body: "let x =");
        var result = Validate(("Parse Failure", raw));

        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
        Assert.Contains("parse", result.Errors[0].Reason);
    }

    [Fact]
    public void Validate_Rule4_ValidStatement_IsAccepted()
    {
        var raw = MakeRaw(prefix: "okparse", body: "let x = 42;");
        var result = Validate(("Valid Parse", raw));

        Assert.Single(result.Valid);
        Assert.Empty(result.Errors);
    }

    // ── Rule 5: Tabstop syntax well-formed ─────────────────────────────────────

    [Fact]
    public void Validate_Rule5_BareDollarSign_IsRejected()
    {
        // A lone $ not followed by digit, {, ", ( is invalid.
        var raw = MakeRaw(prefix: "badtab", body: "let x = $;");
        var result = Validate(("Bad Tabstop", raw));

        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
        Assert.Contains("$", result.Errors[0].Reason);
    }

    [Fact]
    public void Validate_Rule5_ValidTabstopForms_AreAccepted()
    {
        // ${1:name} — default-text placeholder (strips to identifier)
        // $0 — final cursor position (strips to empty)
        // Both forms are valid tabstop syntax and produce a parseable body.
        var rawBody = "fn ${1:name}() {\n\t$0\n}";
        var raw = MakeRaw(prefix: "validtabs", body: rawBody);
        var result = Validate(("Valid Tabstops", raw));

        Assert.Single(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_Rule5_StashInterpolationDollarQuote_IsAllowed()
    {
        // $"..." is Stash string interpolation — should NOT be rejected as a bad tabstop.
        var raw = MakeRaw(prefix: "interp", body: "let s = $\"hello world\";");
        var result = Validate(("Interpolation", raw));

        Assert.Single(result.Valid);
        Assert.Empty(result.Errors);
    }

    // ── Rule 6: Scope value ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_Rule6_UnknownScope_IsRejected()
    {
        var raw = MakeRaw(prefix: "scoped", body: "let x = 1;", scope: "invalid-scope-value");
        var result = Validate(("Bad Scope", raw));

        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
        Assert.Contains("scope", result.Errors[0].Reason);
    }

    [Fact]
    public void Validate_Rule6_AbsentScope_DefaultsToAny()
    {
        var raw = MakeRaw(prefix: "noscope", body: "let x = 1;", scope: null);
        var result = Validate(("No Scope", raw));

        Assert.Single(result.Valid);
        Assert.Empty(result.Errors);
        Assert.Equal(SnippetScope.Any, result.Valid[0].Scope);
    }

    [Fact]
    public void Validate_Rule6_FnBodyScope_IsAccepted()
    {
        var raw = MakeRaw(prefix: "fnscoped", body: "let x = 1;", scope: "fn-body");
        var result = Validate(("Fn Scope", raw));

        Assert.Single(result.Valid);
        Assert.Equal(SnippetScope.FnBody, result.Valid[0].Scope);
    }

    // ── Rule 7: Per-source uniqueness ───────────────────────────────────────────

    [Fact]
    public void Validate_Rule7_DuplicatePrefixScope_BothRejected()
    {
        var raw1 = MakeRaw(prefix: "dup", body: "let x = 1;");
        var raw2 = MakeRaw(prefix: "dup", body: "let y = 2;");
        var result = Validate(("Dup First", raw1), ("Dup Second", raw2));

        Assert.Empty(result.Valid);
        // Both duplicates should produce errors (all duplicates of the key are rejected)
        Assert.True(result.Errors.Count >= 2, $"Expected at least 2 errors but got {result.Errors.Count}");
        Assert.All(result.Errors, e => Assert.Contains("dup", e.Reason));
    }

    [Fact]
    public void Validate_Rule7_SamePrefixDifferentScope_Accepted()
    {
        var raw1 = MakeRaw(prefix: "mysnip", body: "let x = 1;", scope: "fn-body");
        var raw2 = MakeRaw(prefix: "mysnip", body: "let y = 2;", scope: "top-level");
        var result = Validate(("Snip FnBody", raw1), ("Snip TopLevel", raw2));

        Assert.Equal(2, result.Valid.Count);
        Assert.Empty(result.Errors);
    }

    // ── Tabstop-strip behaviour ─────────────────────────────────────────────────

    [Fact]
    public void StripTabstops_BareDollarN_ReplacedWithSyntheticIdentifier()
    {
        var stripped = SnippetValidator.StripTabstops("let $1 = $2;");
        Assert.Equal("let __snip_1 = __snip_2;", stripped);
    }

    [Fact]
    public void StripTabstops_DollarZero_SubstitutedWithNullStatement()
    {
        // $0 is the final cursor position — substituted with `null;`, a benign Stash
        // statement that parses cleanly in any block-body position. Empty-string
        // substitution would silently rely on empty blocks being legal; `null;` makes
        // the snippet validator's parse check honest.
        var stripped = SnippetValidator.StripTabstops("let x = 1; $0");
        Assert.Equal("let x = 1; null;", stripped);
    }

    [Fact]
    public void StripTabstops_BracedTabstop_ReplacedWithSyntheticIdentifier()
    {
        var stripped = SnippetValidator.StripTabstops("fn ${1}() {}");
        Assert.Equal("fn __snip_1() {}", stripped);
    }

    [Fact]
    public void StripTabstops_DefaultPlaceholder_LiftedIntoBody()
    {
        // ${1:myVar} → myVar (the default text replaces the whole tabstop)
        var stripped = SnippetValidator.StripTabstops("let ${1:myVar} = ${2:value};");
        Assert.Equal("let myVar = value;", stripped);
    }

    [Fact]
    public void StripTabstops_IdentifierSlot_ParsesAsValidStash()
    {
        // A placeholder like ${1:item} in a for-in loop becomes "item" — a valid identifier.
        // $0 (final cursor) becomes empty.
        var body = "for (let ${1:item} in ${2:collection}) {\n\t$0\n}";
        var stripped = SnippetValidator.StripTabstops(body);
        // After strip: "for (let item in collection) {\n\t\n}"
        Assert.Contains("item", stripped);
        Assert.Contains("collection", stripped);
        Assert.DoesNotContain("__snip_0", stripped);  // $0 → empty
    }

    [Fact]
    public void StripTabstops_StashInterpolation_PassedThrough()
    {
        // $"..." must not be stripped — it's Stash string interpolation.
        var stripped = SnippetValidator.StripTabstops("let s = $\"hello\";");
        Assert.Equal("let s = $\"hello\";", stripped);
    }

    [Fact]
    public void StripTabstops_ChoiceTabstop_FirstOptionUsed()
    {
        // ${1|int,string|} → int (first option)
        var stripped = SnippetValidator.StripTabstops("let x: ${1|int,string|} = __snip_2;");
        Assert.Contains("int", stripped);
        Assert.DoesNotContain("string", stripped);
    }

    // ── Snippet Id shape ────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidSnippet_IdIsSourceColonPrefixColonScope()
    {
        var raw = MakeRaw(prefix: "mysnip", body: "let x = 1;");
        var result = Validate(("My Snippet", raw));

        Assert.Single(result.Valid);
        Assert.Equal("Bundled:mysnip:Any", result.Valid[0].Id);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static SnippetValidator.Result Validate(params (string DisplayName, RawSnippet Raw)[] entries)
        => SnippetValidator.Validate(entries, SnippetSourceKind.Bundled);

    private static RawSnippet MakeRaw(string prefix, string body, string? scope = null)
    {
        var elem = System.Text.Json.JsonDocument.Parse($"\"{EscapeJson(body)}\"").RootElement;
        return new RawSnippet
        {
            Prefix = prefix,
            Body = elem,
            Scope = scope,
        };
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
}
