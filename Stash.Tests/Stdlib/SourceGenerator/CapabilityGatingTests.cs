namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Linq;
using Stash.Runtime;
using Stash.Tests.Stdlib.SourceGenerator.Fixtures;
using Xunit;

public sealed class CapabilityGatingTests
{
    [Fact]
    public void GatedFunction_CapabilityAbsent_NotRegistered()
    {
        var def = CapabilityFixture.Define(StashCapabilities.None);

        Assert.Contains(def.Functions, f => f.Name == "always");
        Assert.DoesNotContain(def.Functions, f => f.Name == "gatedFn");
        Assert.False(def.Namespace.HasMember("gatedFn"));
    }

    [Fact]
    public void GatedFunction_CapabilityPresent_Registered()
    {
        var def = CapabilityFixture.Define(StashCapabilities.Environment);

        Assert.Contains(def.Functions, f => f.Name == "always");
        Assert.Contains(def.Functions, f => f.Name == "gatedFn");
        Assert.True(def.Namespace.HasMember("gatedFn"));
    }

    [Fact]
    public void GatedFunction_AllCapabilities_Registered()
    {
        var def = CapabilityFixture.Define(StashCapabilities.All);

        Assert.Contains(def.Functions, f => f.Name == "gatedFn");
    }

    [Fact]
    public void GatedFunction_DifferentCapability_NotRegistered()
    {
        // FileSystem ≠ Environment; gatedFn should be filtered out.
        var def = CapabilityFixture.Define(StashCapabilities.FileSystem);

        Assert.DoesNotContain(def.Functions, f => f.Name == "gatedFn");
    }
}
