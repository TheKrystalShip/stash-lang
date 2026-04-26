namespace Stash.Tests.Interpreting;

public class HttpBuiltInsTests : StashTestBase, IDisposable
{
    private readonly string _testDir;

    public HttpBuiltInsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "stash_http_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    // --- http.head ---

    [Fact]
    public void Head_NonStringUrlThrows()
    {
        RunExpectingError("http.head(123);");
    }

    [Fact]
    public void Head_InvalidSchemeThrows()
    {
        var ex = RunCapturingError("http.head(\"ftp://example.com\");");
        Assert.Contains("must use http://", ex.Message);
    }

    [Fact]
    public void Head_InvalidUrl_Throws()
    {
        var ex = RunCapturingError("http.head(\"http://invalid.test.localhost.invalid\");");
        Assert.Contains("request failed", ex.Message);
    }

    [Fact]
    public void Head_ValidSchemes_FailWithConnectionError()
    {
        // http:// and https:// pass scheme validation; the error should be a connection failure
        var exHttp = RunCapturingError("http.head(\"http://localhost:19999\");");
        Assert.DoesNotContain("must use http://", exHttp.Message);
        Assert.Contains("request failed", exHttp.Message);

        var exHttps = RunCapturingError("http.head(\"https://localhost:19999\");");
        Assert.DoesNotContain("must use http://", exHttps.Message);
        Assert.Contains("request failed", exHttps.Message);
    }

    // --- http.patch ---

    [Fact]
    public void Patch_NonStringUrlThrows()
    {
        RunExpectingError("http.patch(123, \"body\");");
    }

    [Fact]
    public void Patch_NonStringBodyThrows()
    {
        RunExpectingError("http.patch(\"http://example.com\", 123);");
    }

    [Fact]
    public void Patch_InvalidSchemeThrows()
    {
        var ex = RunCapturingError("http.patch(\"ftp://example.com\", \"body\");");
        Assert.Contains("must use http://", ex.Message);
    }

    [Fact]
    public void Patch_InvalidUrl_Throws()
    {
        var ex = RunCapturingError("http.patch(\"http://invalid.test.localhost.invalid\", \"{}\");");
        Assert.Contains("request failed", ex.Message);
    }

    [Fact]
    public void Patch_ValidSchemes_FailWithConnectionError()
    {
        // http:// and https:// pass scheme validation; the error should be a connection failure
        var exHttp = RunCapturingError("http.patch(\"http://localhost:19999\", \"{}\");");
        Assert.DoesNotContain("must use http://", exHttp.Message);
        Assert.Contains("request failed", exHttp.Message);

        var exHttps = RunCapturingError("http.patch(\"https://localhost:19999\", \"{}\");");
        Assert.DoesNotContain("must use http://", exHttps.Message);
        Assert.Contains("request failed", exHttps.Message);
    }

    // --- http.download ---

    [Fact]
    public void Download_NonStringUrlThrows()
    {
        RunExpectingError("http.download(123, \"/tmp/file\");");
    }

    [Fact]
    public void Download_NonStringPathThrows()
    {
        RunExpectingError("http.download(\"http://example.com/file\", 123);");
    }

    [Fact]
    public void Download_InvalidSchemeThrows()
    {
        var ex = RunCapturingError("http.download(\"ftp://example.com/file\", \"/tmp/file\");");
        Assert.Contains("must use http://", ex.Message);
    }

    [Fact]
    public void Download_InvalidUrl_Throws()
    {
        var destPath = Path.Combine(_testDir, "output.txt").Replace("\\", "/");
        var ex = RunCapturingError($"http.download(\"http://invalid.test.localhost.invalid/file\", \"{destPath}\");");
        Assert.Contains("request failed", ex.Message);
    }

    [Fact]
    public void Download_ValidSchemes_FailWithConnectionError()
    {
        var destPath = Path.Combine(_testDir, "output.txt").Replace("\\", "/");

        var exHttp = RunCapturingError($"http.download(\"http://localhost:19999/file\", \"{destPath}\");");
        Assert.DoesNotContain("must use http://", exHttp.Message);
        Assert.Contains("request failed", exHttp.Message);

        var exHttps = RunCapturingError($"http.download(\"https://localhost:19999/file\", \"{destPath}\");");
        Assert.DoesNotContain("must use http://", exHttps.Message);
        Assert.Contains("request failed", exHttps.Message);
    }
}
