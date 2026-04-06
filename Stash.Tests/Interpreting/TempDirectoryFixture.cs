namespace Stash.Tests.Interpreting;

public abstract class TempDirectoryFixture : StashTestBase, IDisposable
{
    protected readonly string TestDir;

    protected TempDirectoryFixture(string prefix = "stash_test")
    {
        TestDir = Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(TestDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(TestDir, true); } catch { }
        GC.SuppressFinalize(this);
    }
}
