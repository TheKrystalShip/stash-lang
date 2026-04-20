namespace Stash.Tests.Interpreting;

public class ArchiveBuiltInsTests : TempDirectoryFixture
{
    public ArchiveBuiltInsTests() : base("stash_archive_test") { }

    // ── ZIP Creation ────────────────────────────────────────────────────────

    [Fact]
    public void Zip_CreateArchive_Success()
    {
        var inputFile = Path.Combine(TestDir, "input.txt");
        var outputZip = Path.Combine(TestDir, "output.zip");
        File.WriteAllText(inputFile, "Hello, World!");

        var result = Run($"let result = archive.zip(\"{Escape(outputZip)}\", \"{Escape(inputFile)}\");");
        Assert.Equal(outputZip, result);
        Assert.True(File.Exists(outputZip));
    }

    [Fact]
    public void Zip_CreateFromArray_Success()
    {
        var file1 = Path.Combine(TestDir, "file1.txt");
        var file2 = Path.Combine(TestDir, "file2.txt");
        var outputZip = Path.Combine(TestDir, "multi.zip");
        File.WriteAllText(file1, "File 1");
        File.WriteAllText(file2, "File 2");

        var result = Run($"let result = archive.zip(\"{Escape(outputZip)}\", [\"{Escape(file1)}\", \"{Escape(file2)}\"]);");
        Assert.Equal(outputZip, result);
        Assert.True(File.Exists(outputZip));
    }

    [Fact]
    public void Zip_WithCompressionLevel_Success()
    {
        var inputFile = Path.Combine(TestDir, "compress.txt");
        var outputZip = Path.Combine(TestDir, "compressed.zip");
        File.WriteAllText(inputFile, new string('A', 10000));

        var result = Run($@"
            let opts = archive.ArchiveOptions {{ compressionLevel: 9 }};
            let result = archive.zip(""{Escape(outputZip)}"", ""{Escape(inputFile)}"", opts);
        ");
        Assert.Equal(outputZip, result);
        Assert.True(File.Exists(outputZip));
    }

    [Fact]
    public void Zip_Directory_Success()
    {
        var subDir = Path.Combine(TestDir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "a.txt"), "A");
        File.WriteAllText(Path.Combine(subDir, "b.txt"), "B");
        var outputZip = Path.Combine(TestDir, "dir.zip");

        var result = Run($"let result = archive.zip(\"{Escape(outputZip)}\", \"{Escape(subDir)}\");");
        Assert.Equal(outputZip, result);
        Assert.True(File.Exists(outputZip));
    }

    [Fact]
    public void Zip_EmptyInput_ThrowsError()
    {
        var outputZip = Path.Combine(TestDir, "empty.zip");
        RunExpectingError($"archive.zip(\"{Escape(outputZip)}\", []);");
    }

    [Fact]
    public void Zip_NonExistentFile_ThrowsError()
    {
        var outputZip = Path.Combine(TestDir, "missing.zip");
        RunExpectingError($"archive.zip(\"{Escape(outputZip)}\", \"/nonexistent/path.txt\");");
    }

    [Fact]
    public void Zip_Overwrite_False_ThrowsError()
    {
        var inputFile = Path.Combine(TestDir, "input_ow.txt");
        var outputZip = Path.Combine(TestDir, "existing.zip");
        File.WriteAllText(inputFile, "data");
        File.WriteAllText(outputZip, "existing zip");

        RunExpectingError($"archive.zip(\"{Escape(outputZip)}\", \"{Escape(inputFile)}\");");
    }

    [Fact]
    public void Zip_Overwrite_True_Succeeds()
    {
        var inputFile = Path.Combine(TestDir, "input_ow2.txt");
        var outputZip = Path.Combine(TestDir, "existing2.zip");
        File.WriteAllText(inputFile, "data");
        File.WriteAllText(outputZip, "old data");
        var oldSize = new FileInfo(outputZip).Length;

        var result = Run($@"
            let opts = archive.ArchiveOptions {{ overwrite: true }};
            let result = archive.zip(""{Escape(outputZip)}"", ""{Escape(inputFile)}"", opts);
        ");
        Assert.Equal(outputZip, result);
        Assert.NotEqual(oldSize, new FileInfo(outputZip).Length);
    }

    // ── ZIP Extraction ──────────────────────────────────────────────────────

    [Fact]
    public void Unzip_ExtractAll_Success()
    {
        var inputFile = Path.Combine(TestDir, "extract_input.txt");
        var archivePath = Path.Combine(TestDir, "extract.zip");
        var outputDir = Path.Combine(TestDir, "extracted");
        File.WriteAllText(inputFile, "Extract me!");

        RunStatements($"archive.zip(\"{Escape(archivePath)}\", \"{Escape(inputFile)}\");");

        var result = Run($"let result = archive.unzip(\"{Escape(archivePath)}\", \"{Escape(outputDir)}\");");
        var files = (List<object?>)result!;
        Assert.Single(files);
        Assert.True(File.Exists(Path.Combine(outputDir, "extract_input.txt")));
    }

    [Fact]
    public void Unzip_WithFilter_Success()
    {
        var file1 = Path.Combine(TestDir, "filter1.txt");
        var file2 = Path.Combine(TestDir, "filter2.log");
        var archivePath = Path.Combine(TestDir, "filter.zip");
        var outputDir = Path.Combine(TestDir, "filtered");
        File.WriteAllText(file1, "Text file");
        File.WriteAllText(file2, "Log file");

        RunStatements($"archive.zip(\"{Escape(archivePath)}\", [\"{Escape(file1)}\", \"{Escape(file2)}\"]);");

        var result = Run($@"
            let opts = archive.ArchiveOptions {{ filter: ""*.txt"" }};
            let result = archive.unzip(""{Escape(archivePath)}"", ""{Escape(outputDir)}"", opts);
        ");
        var files = (List<object?>)result!;
        Assert.Single(files);
        Assert.Contains("filter1.txt", (string)files[0]!);
    }

    [Fact]
    public void Unzip_NonExistentFile_ThrowsError()
    {
        var outputDir = Path.Combine(TestDir, "out");
        RunExpectingError($"archive.unzip(\"/nonexistent.zip\", \"{Escape(outputDir)}\");");
    }

    [Fact]
    public void Unzip_InvalidArchive_ThrowsError()
    {
        var fakePath = Path.Combine(TestDir, "fake.zip");
        var outputDir = Path.Combine(TestDir, "out2");
        File.WriteAllText(fakePath, "not a zip file");
        RunExpectingError($"archive.unzip(\"{Escape(fakePath)}\", \"{Escape(outputDir)}\");");
    }

    [Fact]
    public void Unzip_PreservePaths_True()
    {
        var subDir = Path.Combine(TestDir, "preserve");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "nested");
        var archivePath = Path.Combine(TestDir, "preserve.zip");
        var outputDir = Path.Combine(TestDir, "preserve_out");

        RunStatements($"archive.zip(\"{Escape(archivePath)}\", \"{Escape(subDir)}\");");

        var result = Run($@"
            let opts = archive.ArchiveOptions {{ preservePaths: true }};
            let result = archive.unzip(""{Escape(archivePath)}"", ""{Escape(outputDir)}"", opts);
        ");
        var files = (List<object?>)result!;
        Assert.Single(files);
        // Path should contain the nested structure
        Assert.Contains("preserve", (string)files[0]!);
    }

    [Fact]
    public void Unzip_PreservePaths_False()
    {
        var subDir = Path.Combine(TestDir, "nopreserve");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "flat.txt"), "flat");
        var archivePath = Path.Combine(TestDir, "nopreserve.zip");
        var outputDir = Path.Combine(TestDir, "nopreserve_out");

        RunStatements($"archive.zip(\"{Escape(archivePath)}\", \"{Escape(subDir)}\");");

        var result = Run($@"
            let opts = archive.ArchiveOptions {{ preservePaths: false }};
            let result = archive.unzip(""{Escape(archivePath)}"", ""{Escape(outputDir)}"", opts);
        ");
        var files = (List<object?>)result!;
        Assert.Single(files);
        // File should be directly in outputDir
        Assert.True(File.Exists(Path.Combine(outputDir, "flat.txt")));
    }

    // ── TAR Creation ────────────────────────────────────────────────────────

    [Fact]
    public void Tar_CreateArchive_Success()
    {
        var inputFile = Path.Combine(TestDir, "tar_input.txt");
        var outputTar = Path.Combine(TestDir, "output.tar");
        File.WriteAllText(inputFile, "TAR content");

        var result = Run($"let result = archive.tar(\"{Escape(outputTar)}\", \"{Escape(inputFile)}\");");
        Assert.Equal(outputTar, result);
        Assert.True(File.Exists(outputTar));
    }

    [Fact]
    public void Tar_CreateFromArray_Success()
    {
        var file1 = Path.Combine(TestDir, "tar1.txt");
        var file2 = Path.Combine(TestDir, "tar2.txt");
        var outputTar = Path.Combine(TestDir, "multi.tar");
        File.WriteAllText(file1, "TAR 1");
        File.WriteAllText(file2, "TAR 2");

        var result = Run($"let result = archive.tar(\"{Escape(outputTar)}\", [\"{Escape(file1)}\", \"{Escape(file2)}\"]);");
        Assert.Equal(outputTar, result);
        Assert.True(File.Exists(outputTar));
    }

    [Fact]
    public void Tar_CreateGzipped_Success()
    {
        var inputFile = Path.Combine(TestDir, "tgz_input.txt");
        var outputTgz = Path.Combine(TestDir, "output.tar.gz");
        File.WriteAllText(inputFile, new string('B', 10000));

        var result = Run($"let result = archive.tar(\"{Escape(outputTgz)}\", \"{Escape(inputFile)}\");");
        Assert.Equal(outputTgz, result);
        Assert.True(File.Exists(outputTgz));
        // Verify it's actually gzipped
        var bytes = File.ReadAllBytes(outputTgz);
        Assert.Equal(0x1f, bytes[0]);
        Assert.Equal(0x8b, bytes[1]);
    }

    [Fact]
    public void Tar_EmptyInput_ThrowsError()
    {
        var outputTar = Path.Combine(TestDir, "empty.tar");
        RunExpectingError($"archive.tar(\"{Escape(outputTar)}\", []);");
    }

    // ── TAR Extraction ──────────────────────────────────────────────────────

    [Fact]
    public void Untar_ExtractAll_Success()
    {
        var inputFile = Path.Combine(TestDir, "untar_input.txt");
        var archivePath = Path.Combine(TestDir, "untar.tar");
        var outputDir = Path.Combine(TestDir, "untarred");
        File.WriteAllText(inputFile, "UNTAR content");

        RunStatements($"archive.tar(\"{Escape(archivePath)}\", \"{Escape(inputFile)}\");");

        var result = Run($"let result = archive.untar(\"{Escape(archivePath)}\", \"{Escape(outputDir)}\");");
        var files = (List<object?>)result!;
        Assert.Single(files);
        Assert.True(File.Exists(Path.Combine(outputDir, "untar_input.txt")));
    }

    [Fact]
    public void Untar_Gzipped_Success()
    {
        var inputFile = Path.Combine(TestDir, "untgz_input.txt");
        var archivePath = Path.Combine(TestDir, "untgz.tar.gz");
        var outputDir = Path.Combine(TestDir, "untgzed");
        File.WriteAllText(inputFile, "GZIPPED TAR content");

        RunStatements($"archive.tar(\"{Escape(archivePath)}\", \"{Escape(inputFile)}\");");

        var result = Run($"let result = archive.untar(\"{Escape(archivePath)}\", \"{Escape(outputDir)}\");");
        var files = (List<object?>)result!;
        Assert.Single(files);
        Assert.True(File.Exists(Path.Combine(outputDir, "untgz_input.txt")));
    }

    [Fact]
    public void Untar_WithPreservePaths_Success()
    {
        var subDir = Path.Combine(TestDir, "tarpreserve");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "tarnested.txt"), "nested tar");
        var archivePath = Path.Combine(TestDir, "tarpreserve.tar");
        var outputDir = Path.Combine(TestDir, "tarpreserve_out");

        RunStatements($"archive.tar(\"{Escape(archivePath)}\", \"{Escape(subDir)}\");");

        var result = Run($@"
            let opts = archive.ArchiveOptions {{ preservePaths: true }};
            let result = archive.untar(""{Escape(archivePath)}"", ""{Escape(outputDir)}"", opts);
        ");
        var files = (List<object?>)result!;
        Assert.Single(files);
    }

    [Fact]
    public void Untar_NonExistentFile_ThrowsError()
    {
        var outputDir = Path.Combine(TestDir, "tarout");
        RunExpectingError($"archive.untar(\"/nonexistent.tar\", \"{Escape(outputDir)}\");");
    }

    // ── GZIP ────────────────────────────────────────────────────────────────

    [Fact]
    public void Gzip_CompressFile_Success()
    {
        var inputFile = Path.Combine(TestDir, "gzip_input.txt");
        File.WriteAllText(inputFile, new string('X', 5000));

        var result = Run($"let result = archive.gzip(\"{Escape(inputFile)}\");");
        var outputPath = (string)result!;
        Assert.Equal(inputFile + ".gz", outputPath);
        Assert.True(File.Exists(outputPath));
        // Compressed should be smaller
        Assert.True(new FileInfo(outputPath).Length < new FileInfo(inputFile).Length);
    }

    [Fact]
    public void Gzip_CustomOutputPath_Success()
    {
        var inputFile = Path.Combine(TestDir, "gzip_custom.txt");
        var outputFile = Path.Combine(TestDir, "custom.compressed");
        File.WriteAllText(inputFile, "Custom output path");

        var result = Run($"let result = archive.gzip(\"{Escape(inputFile)}\", \"{Escape(outputFile)}\");");
        Assert.Equal(outputFile, result);
        Assert.True(File.Exists(outputFile));
    }

    [Fact]
    public void Gzip_NonExistentFile_ThrowsError()
    {
        RunExpectingError("archive.gzip(\"/nonexistent/file.txt\");");
    }

    // ── GUNZIP ──────────────────────────────────────────────────────────────

    [Fact]
    public void Gunzip_DecompressFile_Success()
    {
        var inputFile = Path.Combine(TestDir, "gunzip_input.txt");
        var gzipFile = inputFile + ".gz";
        var originalContent = "Original content to decompress";
        File.WriteAllText(inputFile, originalContent);

        RunStatements($"archive.gzip(\"{Escape(inputFile)}\");");
        File.Delete(inputFile);

        var result = Run($"let result = archive.gunzip(\"{Escape(gzipFile)}\");");
        Assert.Equal(inputFile, result);
        Assert.True(File.Exists(inputFile));
        Assert.Equal(originalContent, File.ReadAllText(inputFile));
    }

    [Fact]
    public void Gunzip_CustomOutputPath_Success()
    {
        var inputFile = Path.Combine(TestDir, "gunzip_custom.txt");
        var gzipFile = inputFile + ".gz";
        var outputFile = Path.Combine(TestDir, "decompressed_custom.txt");
        File.WriteAllText(inputFile, "Decompress to custom path");

        RunStatements($"archive.gzip(\"{Escape(inputFile)}\");");

        var result = Run($"let result = archive.gunzip(\"{Escape(gzipFile)}\", \"{Escape(outputFile)}\");");
        Assert.Equal(outputFile, result);
        Assert.True(File.Exists(outputFile));
    }

    [Fact]
    public void Gunzip_InvalidFile_ThrowsError()
    {
        var fakePath = Path.Combine(TestDir, "fake.gz");
        File.WriteAllText(fakePath, "not gzipped");
        RunExpectingError($"archive.gunzip(\"{Escape(fakePath)}\");");
    }

    [Fact]
    public void Gunzip_NonExistentFile_ThrowsError()
    {
        RunExpectingError("archive.gunzip(\"/nonexistent/file.gz\");");
    }

    // ── LIST ────────────────────────────────────────────────────────────────

    [Fact]
    public void List_ZipArchive_ReturnsEntries()
    {
        var file1 = Path.Combine(TestDir, "list1.txt");
        var file2 = Path.Combine(TestDir, "list2.txt");
        var archivePath = Path.Combine(TestDir, "list.zip");
        File.WriteAllText(file1, "List 1");
        File.WriteAllText(file2, "List 2");

        RunStatements($"archive.zip(\"{Escape(archivePath)}\", [\"{Escape(file1)}\", \"{Escape(file2)}\"]);");

        var result = Run($"let result = len(archive.list(\"{Escape(archivePath)}\"));");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void List_TarArchive_ReturnsEntries()
    {
        var file1 = Path.Combine(TestDir, "tarlist1.txt");
        var file2 = Path.Combine(TestDir, "tarlist2.txt");
        var archivePath = Path.Combine(TestDir, "list.tar");
        File.WriteAllText(file1, "TAR List 1");
        File.WriteAllText(file2, "TAR List 2");

        RunStatements($"archive.tar(\"{Escape(archivePath)}\", [\"{Escape(file1)}\", \"{Escape(file2)}\"]);");

        var result = Run($"let result = len(archive.list(\"{Escape(archivePath)}\"));");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void List_ArchiveEntry_HasCorrectFields()
    {
        var inputFile = Path.Combine(TestDir, "entry_fields.txt");
        var archivePath = Path.Combine(TestDir, "entry.zip");
        File.WriteAllText(inputFile, "Entry content");

        RunStatements($"archive.zip(\"{Escape(archivePath)}\", \"{Escape(inputFile)}\");");

        var result = Run($@"
            let entries = archive.list(""{Escape(archivePath)}"");
            let entry = entries[0];
            let result = entry.name != null && entry.size >= 0 && typeof(entry.isDirectory) == ""bool"";
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void List_NonExistentFile_ThrowsError()
    {
        RunExpectingError("archive.list(\"/nonexistent.zip\");");
    }

    [Fact]
    public void List_UnsupportedFormat_ThrowsError()
    {
        var fakePath = Path.Combine(TestDir, "fake.rar");
        File.WriteAllText(fakePath, "not supported");
        RunExpectingError($"archive.list(\"{Escape(fakePath)}\");");
    }

    // ── Roundtrip Tests ─────────────────────────────────────────────────────

    [Fact]
    public void Zip_Roundtrip_PreservesContent()
    {
        var inputFile = Path.Combine(TestDir, "roundtrip.txt");
        var archivePath = Path.Combine(TestDir, "roundtrip.zip");
        var outputDir = Path.Combine(TestDir, "roundtrip_out");
        var content = "Roundtrip test content with special chars: àéïõü €$@#";
        File.WriteAllText(inputFile, content);

        RunStatements($"archive.zip(\"{Escape(archivePath)}\", \"{Escape(inputFile)}\");");
        RunStatements($@"
            let opts = archive.ArchiveOptions {{ preservePaths: false }};
            archive.unzip(""{Escape(archivePath)}"", ""{Escape(outputDir)}"", opts);
        ");

        var extracted = File.ReadAllText(Path.Combine(outputDir, "roundtrip.txt"));
        Assert.Equal(content, extracted);
    }

    [Fact]
    public void Gzip_Roundtrip_PreservesContent()
    {
        var inputFile = Path.Combine(TestDir, "gzip_rt.txt");
        var gzFile = inputFile + ".gz";
        var outputFile = Path.Combine(TestDir, "gzip_rt_out.txt");
        var content = "GZIP roundtrip content";
        File.WriteAllText(inputFile, content);

        RunStatements($"archive.gzip(\"{Escape(inputFile)}\");");
        RunStatements($"archive.gunzip(\"{Escape(gzFile)}\", \"{Escape(outputFile)}\");");

        Assert.Equal(content, File.ReadAllText(outputFile));
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}
