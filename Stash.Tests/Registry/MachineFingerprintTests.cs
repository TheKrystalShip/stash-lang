using Stash.Cli.PackageManager;
using Xunit;

namespace Stash.Tests.Registry;

public sealed class MachineFingerprintTests
{
    [Fact]
    public void Generate_ReturnsDeterministicValue()
    {
        string first = MachineFingerprint.Generate();
        string second = MachineFingerprint.Generate();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Generate_ReturnsNonEmptyHexString()
    {
        string fingerprint = MachineFingerprint.Generate();

        Assert.NotEmpty(fingerprint);
        Assert.Equal(64, fingerprint.Length); // SHA-256 produces 64 hex chars
        Assert.Matches("^[0-9a-f]+$", fingerprint);
    }
}
