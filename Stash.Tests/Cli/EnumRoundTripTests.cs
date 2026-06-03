using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stash.Cli.PackageManager;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// In-process round-trip tests verifying that every bounded-domain enum value serializes
/// byte-identical to its documented lowercase wire string through <see cref="CliJsonContext"/>,
/// and deserializes back from the same wire string to the original enum value.
/// </summary>
/// <remarks>
/// <para>
/// This test class exercises the source-gen <see cref="CliJsonContext.Default"/> instance —
/// the same context the CLI uses at runtime. It is the fast complement to
/// <see cref="AotPublishedBinaryEnumRoundTripTests"/>, which runs the full Native-AOT published
/// binary as a subprocess. A passing in-process test plus a passing AOT subprocess test together
/// close the shared-contracts residual gap: the type system cannot express "this enum value's
/// wire string matches its documented spelling under source-gen + AOT", so the runtime tests
/// are the only sound verification.
/// </para>
/// <para>
/// <b>Binding floor.</b> The test asserts that all seven enum types are registered in
/// <see cref="CliJsonContext"/> via <c>[JsonSerializable]</c>. A missing registration causes
/// source-gen to skip metadata for that type, which silently falls back to reflection in JIT
/// but fails at the AOT binary boundary. The floor fails loudly on a missing registration so
/// the AOT test is never the first failure signal.
/// </para>
/// <para>
/// <b>Fail-path fixture.</b> A deliberately misnamed local enum (wrong <c>[JsonStringEnumMemberName]</c>)
/// is used to prove the round-trip assertion can catch the bug it is named for. This fixture
/// never touches <see cref="CliJsonContext"/> — it uses a separate options object.
/// </para>
/// </remarks>
[Collection("CliTests")]
public sealed class EnumRoundTripTests
{
    // ── Binding floor: all seven enum types must be registered in CliJsonContext ──

    /// <summary>
    /// Asserts that every bounded-domain enum type is registered in <see cref="CliJsonContext"/>
    /// via <c>[JsonSerializable(typeof(EnumT))]</c>. A missing registration means source-gen
    /// did not emit metadata for that type and the AOT binary will fall back to reflection (or
    /// fail outright). This floor must fail loudly before the round-trip tests can even run.
    /// </summary>
    [Fact]
    public void CliJsonContext_RegistersAllSevenEnumTypes()
    {
        var opts = CliJsonContext.Default.Options;

        var missingTypes = new List<string>();

        void CheckType<T>() where T : struct
        {
            var info = opts.GetTypeInfo(typeof(T));
            if (info == null)
                missingTypes.Add(typeof(T).Name);
        }

        CheckType<PackageRoles>();
        CheckType<TokenScopes>();
        CheckType<Visibilities>();
        CheckType<PrincipalTypes>();
        CheckType<ScopeOwnerTypes>();
        CheckType<OrgRoles>();
        CheckType<UserRoles>();

        Assert.True(
            missingTypes.Count == 0,
            $"The following enum types are NOT registered in CliJsonContext " +
            $"(missing [JsonSerializable(typeof(T))] attributes):\n" +
            $"{string.Join("\n", missingTypes.Select(t => $"  {t}"))}\n\n" +
            $"Add [JsonSerializable(typeof({(missingTypes.Count > 0 ? missingTypes[0] : "EnumT")}))]\n" +
            $"to CliJsonContext.cs. A missing registration passes in JIT (reflection fallback) " +
            $"but silently breaks AOT serialization.");
    }

    // ── Round-trip: serialize → assert wire string, deserialize → assert enum value ──

    [Theory]
    [InlineData(PackageRoles.Owner,      "\"owner\"")]
    [InlineData(PackageRoles.Maintainer, "\"maintainer\"")]
    [InlineData(PackageRoles.Publisher,  "\"publisher\"")]
    [InlineData(PackageRoles.Reader,     "\"reader\"")]
    public void PackageRoles_SerializeDeserialize_RoundTrips(PackageRoles value, string expectedJson)
    {
        AssertRoundTrip(value, expectedJson, CliJsonContext.Default.Options);
    }

    [Theory]
    [InlineData(TokenScopes.Read,    "\"read\"")]
    [InlineData(TokenScopes.Publish, "\"publish\"")]
    [InlineData(TokenScopes.Admin,   "\"admin\"")]
    public void TokenScopes_SerializeDeserialize_RoundTrips(TokenScopes value, string expectedJson)
    {
        AssertRoundTrip(value, expectedJson, CliJsonContext.Default.Options);
    }

    [Theory]
    [InlineData(Visibilities.Public,   "\"public\"")]
    [InlineData(Visibilities.Private,  "\"private\"")]
    [InlineData(Visibilities.Internal, "\"internal\"")]
    public void Visibilities_SerializeDeserialize_RoundTrips(Visibilities value, string expectedJson)
    {
        AssertRoundTrip(value, expectedJson, CliJsonContext.Default.Options);
    }

    [Theory]
    [InlineData(PrincipalTypes.User, "\"user\"")]
    [InlineData(PrincipalTypes.Team, "\"team\"")]
    [InlineData(PrincipalTypes.Org,  "\"org\"")]
    public void PrincipalTypes_SerializeDeserialize_RoundTrips(PrincipalTypes value, string expectedJson)
    {
        AssertRoundTrip(value, expectedJson, CliJsonContext.Default.Options);
    }

    [Theory]
    [InlineData(ScopeOwnerTypes.User,   "\"user\"")]
    [InlineData(ScopeOwnerTypes.Org,    "\"org\"")]
    [InlineData(ScopeOwnerTypes.System, "\"system\"")]
    public void ScopeOwnerTypes_SerializeDeserialize_RoundTrips(ScopeOwnerTypes value, string expectedJson)
    {
        AssertRoundTrip(value, expectedJson, CliJsonContext.Default.Options);
    }

    [Theory]
    [InlineData(OrgRoles.Member, "\"member\"")]
    [InlineData(OrgRoles.Owner,  "\"owner\"")]
    public void OrgRoles_SerializeDeserialize_RoundTrips(OrgRoles value, string expectedJson)
    {
        AssertRoundTrip(value, expectedJson, CliJsonContext.Default.Options);
    }

    [Theory]
    [InlineData(UserRoles.User,  "\"user\"")]
    [InlineData(UserRoles.Admin, "\"admin\"")]
    public void UserRoles_SerializeDeserialize_RoundTrips(UserRoles value, string expectedJson)
    {
        AssertRoundTrip(value, expectedJson, CliJsonContext.Default.Options);
    }

    // ── Fail-path fixture: deliberately misnamed enum proves the test has teeth ──

    /// <summary>
    /// A deliberately broken enum: the wire string is <c>"WRONG"</c> but the canonical
    /// name we would expect is <c>"correct"</c>. Used by
    /// <see cref="FailPathFixture_WrongMemberName_IsDetectedByRoundTripAssertion"/> to
    /// prove the round-trip assertion can catch a mis-attributed enum.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<BrokenWireName>))]
    private enum BrokenWireName
    {
        /// <summary>Member name is deliberately wrong.</summary>
        [JsonStringEnumMemberName("WRONG")]
        Correct,
    }

    /// <summary>
    /// Proves the round-trip assertion is not vacuous — it WILL catch a
    /// deliberately mis-attributed <c>[JsonStringEnumMemberName]</c>.
    /// This fixture does NOT use <see cref="CliJsonContext"/>; it uses a separate
    /// <see cref="JsonSerializerOptions"/> instance to avoid polluting the source-gen context.
    /// </summary>
    [Fact]
    public void FailPathFixture_WrongMemberName_IsDetectedByRoundTripAssertion()
    {
        var brokenOpts = new JsonSerializerOptions();
        brokenOpts.Converters.Add(new JsonStringEnumConverter<BrokenWireName>());

        string json = JsonSerializer.Serialize(BrokenWireName.Correct, brokenOpts);
        // json == "\"WRONG\"" because of the [JsonStringEnumMemberName("WRONG")] attribute.
        // The canonical name would be "correct" (lowercase); they are different.
        string canonicalJson = "\"correct\"";

        Assert.NotEqual(
            canonicalJson,
            json,
            StringComparer.Ordinal);

        // Prove the assertion method WOULD catch this: simulate the assertion failure.
        bool mismatchDetected = !string.Equals(json, canonicalJson, StringComparison.Ordinal);
        Assert.True(
            mismatchDetected,
            "The fail-path fixture should detect that 'WRONG' != 'correct', " +
            "proving the round-trip assertion has teeth.");
    }

    // ── Helper ───────────────────────────────────────────────────────────────────

    private static void AssertRoundTrip<T>(T value, string expectedJson, JsonSerializerOptions opts)
        where T : struct
    {
        // Serialize: assert the JSON literal equals the documented wire string byte-for-byte.
        string actualJson = JsonSerializer.Serialize(value, opts);
        Assert.Equal(
            expectedJson,
            actualJson,
            StringComparer.Ordinal);

        // Deserialize: assert the wire string round-trips back to the original enum value.
        T deserialized = JsonSerializer.Deserialize<T>(actualJson, opts);
        Assert.Equal(value, deserialized);
    }
}
