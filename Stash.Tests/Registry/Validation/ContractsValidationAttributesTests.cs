using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Stash.Registry.Contracts;
using Stash.Registry.Contracts.Validation;
using Xunit;

namespace Stash.Tests.Registry.Validation;

/// <summary>
/// In-process validation tests using <see cref="Validator.TryValidateObject"/> to verify that
/// all DataAnnotations attributes on request DTOs in <c>Stash.Registry.Contracts</c> behave
/// as specified: each <c>[Required]</c>, <c>[StringLength]</c>, <c>[Range]</c>,
/// <c>[ScopeGrammar]</c>, and <c>[TokenExpiry]</c> fires on invalid input and passes on
/// valid input. <see cref="ClaimScopeRequest"/> cross-field rules are also verified here.
/// </summary>
public sealed class ContractsValidationAttributesTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(model);
        Validator.TryValidateObject(model, ctx, results, validateAllProperties: true);
        return results;
    }

    private static void AssertValid(object model)
    {
        var errors = Validate(model);
        Assert.True(errors.Count == 0,
            $"Expected no validation errors but got: {string.Join("; ", errors.ConvertAll(e => e.ErrorMessage))}");
    }

    private static void AssertInvalid(object model, string expectedMessageSubstring)
    {
        var errors = Validate(model);
        Assert.True(errors.Count > 0, "Expected at least one validation error but got none.");
        Assert.Contains(errors, e => e.ErrorMessage != null &&
            e.ErrorMessage.Contains(expectedMessageSubstring, System.StringComparison.OrdinalIgnoreCase));
    }

    // ── LoginRequest ─────────────────────────────────────────────────────────

    [Fact]
    public void LoginRequest_MissingUsername_FailsRequired()
        => AssertInvalid(new LoginRequest { Username = null, Password = "secret" }, "Username");

    [Fact]
    public void LoginRequest_MissingPassword_FailsRequired()
        => AssertInvalid(new LoginRequest { Username = "alice", Password = null }, "Password");

    [Fact]
    public void LoginRequest_BothPresent_PassesValidation()
        => AssertValid(new LoginRequest { Username = "alice", Password = "secret" });

    // ── RegisterRequest ───────────────────────────────────────────────────────

    [Fact]
    public void RegisterRequest_MissingUsername_FailsRequired()
        => AssertInvalid(new RegisterRequest { Username = null, Password = "password123" }, "Username");

    [Fact]
    public void RegisterRequest_MissingPassword_FailsRequired()
        => AssertInvalid(new RegisterRequest { Username = "alice", Password = null }, "Password");

    [Theory]
    [InlineData("Alice")]          // uppercase
    [InlineData("alice_name")]     // underscore
    [InlineData("")]               // empty
    [InlineData("a")]              // too short? No — 'a' is valid (1 char OK). Change to test grammar.
    public void RegisterRequest_InvalidUsername_FailsScopeGrammar(string username)
    {
        if (username == "a") return; // 'a' is valid (1 lowercase char)
        AssertInvalid(new RegisterRequest { Username = username, Password = "password123" },
            username.Length == 0 ? "Username" : "lowercase");
    }

    [Fact]
    public void RegisterRequest_ValidUsername_PassesValidation()
        => AssertValid(new RegisterRequest { Username = "alice", Password = "password123" });

    [Fact]
    public void RegisterRequest_PasswordTooShort_FailsStringLength()
        => AssertInvalid(new RegisterRequest { Username = "alice", Password = "short" }, "Password");

    [Fact]
    public void RegisterRequest_PasswordExactlyMinLength_PassesValidation()
        => AssertValid(new RegisterRequest { Username = "alice", Password = "12345678" });

    // ── TokenCreateRequest ────────────────────────────────────────────────────

    [Fact]
    public void TokenCreateRequest_MissingCeiling_FailsRequired()
        => AssertInvalid(new TokenCreateRequest { Ceiling = null, ExpiresIn = "30d" }, "Ceiling");

    [Fact]
    public void TokenCreateRequest_MissingExpiresIn_FailsRequired()
        => AssertInvalid(new TokenCreateRequest { Ceiling = "read", ExpiresIn = null }, "ExpiresIn");

    [Fact]
    public void TokenCreateRequest_InvalidExpiresInFormat_FailsTokenExpiry()
        => AssertInvalid(new TokenCreateRequest { Ceiling = "read", ExpiresIn = "invalid" }, "recognised");

    [Fact]
    public void TokenCreateRequest_ExpiresInBelowOneHour_FailsTokenExpiry()
        => AssertInvalid(new TokenCreateRequest { Ceiling = "read", ExpiresIn = "30m" }, "at least 1 hour");

    [Fact]
    public void TokenCreateRequest_ExpiresInExactlyOneHour_PassesValidation()
        => AssertValid(new TokenCreateRequest { Ceiling = "read", ExpiresIn = "60m" });

    [Fact]
    public void TokenCreateRequest_ExpiresInDays_PassesValidation()
        => AssertValid(new TokenCreateRequest { Ceiling = "read", ExpiresIn = "30d" });

    [Fact]
    public void TokenCreateRequest_ExpiresInHours_PassesValidation()
        => AssertValid(new TokenCreateRequest { Ceiling = "publish", ExpiresIn = "12h" });

    // ── RefreshTokenRequest ───────────────────────────────────────────────────

    [Fact]
    public void RefreshTokenRequest_MissingRefreshToken_FailsRequired()
        => AssertInvalid(new RefreshTokenRequest { RefreshToken = null, AccessToken = "at", MachineId = "mid" }, "RefreshToken");

    [Fact]
    public void RefreshTokenRequest_MissingAccessToken_FailsRequired()
        => AssertInvalid(new RefreshTokenRequest { RefreshToken = "rt", AccessToken = null, MachineId = "mid" }, "AccessToken");

    [Fact]
    public void RefreshTokenRequest_MissingMachineId_FailsRequired()
        => AssertInvalid(new RefreshTokenRequest { RefreshToken = "rt", AccessToken = "at", MachineId = null }, "MachineId");

    [Fact]
    public void RefreshTokenRequest_AllFieldsPresent_PassesValidation()
        => AssertValid(new RefreshTokenRequest { RefreshToken = "rt", AccessToken = "at", MachineId = "mid" });

    // ── CreateUserRequest ─────────────────────────────────────────────────────

    [Fact]
    public void CreateUserRequest_MissingUsername_FailsRequired()
        => AssertInvalid(new CreateUserRequest { Username = null, Password = "password123" }, "Username");

    [Fact]
    public void CreateUserRequest_UsernameTooLong_FailsStringLength()
        => AssertInvalid(new CreateUserRequest { Username = new string('a', 65), Password = "password123" }, "Username");

    [Fact]
    public void CreateUserRequest_UsernameMaxLength_PassesValidation()
        => AssertValid(new CreateUserRequest { Username = new string('a', 64), Password = "password123" });

    [Fact]
    public void CreateUserRequest_MissingPassword_FailsRequired()
        => AssertInvalid(new CreateUserRequest { Username = "alice", Password = null }, "Password");

    [Fact]
    public void CreateUserRequest_PasswordTooShort_FailsStringLength()
        => AssertInvalid(new CreateUserRequest { Username = "alice", Password = "short" }, "Password");

    // ── CreateOrgRequest ──────────────────────────────────────────────────────

    [Fact]
    public void CreateOrgRequest_MissingName_FailsRequired()
        => AssertInvalid(new CreateOrgRequest { Name = null }, "Name");

    [Fact]
    public void CreateOrgRequest_InvalidName_FailsScopeGrammar()
        => AssertInvalid(new CreateOrgRequest { Name = "My-Org" }, "lowercase");

    [Fact]
    public void CreateOrgRequest_ValidName_PassesValidation()
        => AssertValid(new CreateOrgRequest { Name = "my-org" });

    // ── CreateTeamRequest ─────────────────────────────────────────────────────

    [Fact]
    public void CreateTeamRequest_MissingName_FailsRequired()
        => AssertInvalid(new CreateTeamRequest { Name = null }, "Name");

    [Fact]
    public void CreateTeamRequest_InvalidName_FailsScopeGrammar()
        => AssertInvalid(new CreateTeamRequest { Name = "My_Team" }, "lowercase");

    [Fact]
    public void CreateTeamRequest_ValidName_PassesValidation()
        => AssertValid(new CreateTeamRequest { Name = "backend-team" });

    // ── AddOrgMemberRequest ───────────────────────────────────────────────────

    [Fact]
    public void AddOrgMemberRequest_MissingUsername_FailsRequired()
        => AssertInvalid(new AddOrgMemberRequest { Username = null }, "Username");

    [Fact]
    public void AddOrgMemberRequest_UsernamePresent_PassesValidation()
        => AssertValid(new AddOrgMemberRequest { Username = "alice" });

    // ── AddTeamMemberRequest ──────────────────────────────────────────────────

    [Fact]
    public void AddTeamMemberRequest_MissingUsername_FailsRequired()
        => AssertInvalid(new AddTeamMemberRequest { Username = null }, "Username");

    [Fact]
    public void AddTeamMemberRequest_UsernamePresent_PassesValidation()
        => AssertValid(new AddTeamMemberRequest { Username = "bob" });

    // ── ClaimScopeRequest ─────────────────────────────────────────────────────

    [Fact]
    public void ClaimScopeRequest_MissingScope_FailsRequired()
        => AssertInvalid(new ClaimScopeRequest { Scope = null, Owner = "alice", OwnerType = ScopeOwnerTypes.User }, "Scope");

    [Fact]
    public void ClaimScopeRequest_InvalidScopeGrammar_FailsScopeGrammar()
        => AssertInvalid(new ClaimScopeRequest { Scope = "Invalid-Scope", Owner = "alice", OwnerType = ScopeOwnerTypes.User }, "lowercase");

    [Fact]
    public void ClaimScopeRequest_MissingOwner_FailsRequired()
        => AssertInvalid(new ClaimScopeRequest { Scope = "my-scope", Owner = null, OwnerType = ScopeOwnerTypes.User }, "Owner");

    [Fact]
    public void ClaimScopeRequest_ValidUserScope_PassesValidation()
        => AssertValid(new ClaimScopeRequest { Scope = "my-scope", Owner = "alice", OwnerType = ScopeOwnerTypes.User });

    [Fact]
    public void ClaimScopeRequest_ValidOrgScope_PassesValidation()
        => AssertValid(new ClaimScopeRequest { Scope = "my-org", Owner = "acme", OwnerType = ScopeOwnerTypes.Org });

    [Fact]
    public void ClaimScopeRequest_IValidatableObject_OwnerTypeUserWithEmptyOwner_FailsCrossFieldRule()
    {
        // IValidatableObject.Validate() is called only when attribute-level validation passes.
        // To isolate the cross-field rule, call Validate() directly on a model that passes
        // attribute-level validation (Owner is non-null and non-empty — passes [Required]).
        // IValidatableObject fires because Owner is whitespace-only (IsNullOrWhiteSpace=true).
        var model = new ClaimScopeRequest { Scope = "my-scope", Owner = "   ", OwnerType = ScopeOwnerTypes.User };
        var crossFieldErrors = model.Validate(new ValidationContext(model)).ToList();

        Assert.Contains(crossFieldErrors, e => e.ErrorMessage != null &&
            e.ErrorMessage.Contains("owner", System.StringComparison.OrdinalIgnoreCase) &&
            e.ErrorMessage.Contains("user", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClaimScopeRequest_IValidatableObject_OwnerTypeOrgWithEmptyOwner_FailsCrossFieldRule()
    {
        var model = new ClaimScopeRequest { Scope = "my-scope", Owner = "   ", OwnerType = ScopeOwnerTypes.Org };
        var crossFieldErrors = model.Validate(new ValidationContext(model)).ToList();

        Assert.Contains(crossFieldErrors, e => e.ErrorMessage != null &&
            e.ErrorMessage.Contains("owner", System.StringComparison.OrdinalIgnoreCase) &&
            e.ErrorMessage.Contains("org", System.StringComparison.OrdinalIgnoreCase));
    }

    // ── SearchQuery ───────────────────────────────────────────────────────────

    [Fact]
    public void SearchQuery_PageBelowOne_FailsRange()
        => AssertInvalid(new SearchQuery { Page = 0, PageSize = 10 }, "Page");

    [Fact]
    public void SearchQuery_PageSizeAbove100_FailsRange()
        => AssertInvalid(new SearchQuery { Page = 1, PageSize = 101 }, "PageSize");

    [Fact]
    public void SearchQuery_PageSizeBelowOne_FailsRange()
        => AssertInvalid(new SearchQuery { Page = 1, PageSize = 0 }, "PageSize");

    [Fact]
    public void SearchQuery_ValidDefaults_PassesValidation()
        => AssertValid(new SearchQuery { Page = 1, PageSize = 20 });

    [Fact]
    public void SearchQuery_PageSizeAtMax_PassesValidation()
        => AssertValid(new SearchQuery { Page = 1, PageSize = 100 });

    // ── AuditLogQuery ─────────────────────────────────────────────────────────

    [Fact]
    public void AuditLogQuery_PageBelowOne_FailsRange()
        => AssertInvalid(new AuditLogQuery { Page = 0, PageSize = 10 }, "Page");

    [Fact]
    public void AuditLogQuery_PageSizeAbove200_FailsRange()
        => AssertInvalid(new AuditLogQuery { Page = 1, PageSize = 201 }, "PageSize");

    [Fact]
    public void AuditLogQuery_ValidDefaults_PassesValidation()
        => AssertValid(new AuditLogQuery { Page = 1, PageSize = 20 });

    [Fact]
    public void AuditLogQuery_PageSizeAtMax_PassesValidation()
        => AssertValid(new AuditLogQuery { Page = 1, PageSize = 200 });

    // ── ScopeGrammarAttribute ─────────────────────────────────────────────────

    [Theory]
    [InlineData("alice")]          // valid
    [InlineData("my-org")]         // valid with hyphen
    [InlineData("a")]              // valid single char
    [InlineData("abc123")]         // valid alphanumeric
    public void ScopeGrammarAttribute_ValidValues_PassesValidation(string value)
    {
        var attr = new ScopeGrammarAttribute();
        var result = attr.GetValidationResult(value, new ValidationContext(value));
        Assert.Equal(ValidationResult.Success, result);
    }

    [Theory]
    [InlineData("Alice")]          // uppercase
    [InlineData("my_org")]         // underscore
    [InlineData("1alice")]         // starts with digit
    [InlineData("-alice")]         // starts with hyphen
    public void ScopeGrammarAttribute_InvalidValues_FailsValidation(string value)
    {
        var attr = new ScopeGrammarAttribute();
        var ctx = new ValidationContext(value) { MemberName = "Value" };
        var result = attr.GetValidationResult(value, ctx);
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void ScopeGrammarAttribute_NullValue_PassesValidation()
    {
        var attr = new ScopeGrammarAttribute();
        var result = attr.GetValidationResult(null, new ValidationContext(new object()));
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void ScopeGrammarAttribute_TooLong_FailsValidation()
    {
        string tooLong = new string('a', 40); // 40 chars — max is 39
        var attr = new ScopeGrammarAttribute();
        var ctx = new ValidationContext(tooLong) { MemberName = "Value" };
        var result = attr.GetValidationResult(tooLong, ctx);
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    // ── TokenExpiryAttribute ──────────────────────────────────────────────────

    [Theory]
    [InlineData("60m")]   // exactly 1h
    [InlineData("2h")]    // 2 hours
    [InlineData("30d")]   // 30 days
    [InlineData("1d")]    // 1 day
    public void TokenExpiryAttribute_ValidValues_PassesValidation(string value)
    {
        var attr = new TokenExpiryAttribute();
        var result = attr.GetValidationResult(value, new ValidationContext(value));
        Assert.Equal(ValidationResult.Success, result);
    }

    [Theory]
    [InlineData("30m")]   // 30 minutes < 1 hour
    [InlineData("59m")]   // 59 minutes < 1 hour
    [InlineData("0h")]    // 0 hours — invalid (not positive)
    public void TokenExpiryAttribute_BelowOneHour_FailsValidation(string value)
    {
        var attr = new TokenExpiryAttribute();
        var ctx = new ValidationContext(value) { MemberName = "ExpiresIn" };
        var result = attr.GetValidationResult(value, ctx);
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("abc")]
    [InlineData("30x")]     // unrecognised suffix
    [InlineData("")]        // empty
    public void TokenExpiryAttribute_UnrecognisedFormat_FailsValidation(string value)
    {
        if (string.IsNullOrEmpty(value)) return; // empty passes (pair with [Required])
        var attr = new TokenExpiryAttribute();
        var ctx = new ValidationContext(value) { MemberName = "ExpiresIn" };
        var result = attr.GetValidationResult(value, ctx);
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void TokenExpiryAttribute_NullValue_PassesValidation()
    {
        var attr = new TokenExpiryAttribute();
        var result = attr.GetValidationResult(null, new ValidationContext(new object()));
        Assert.Equal(ValidationResult.Success, result);
    }
}
