using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class FsBuiltInsTests : TempDirectoryFixture
{
    public FsBuiltInsTests() : base("stash_fs_test") { }

    // ── fs.createFile ───────────────────────────────────────────────────────

    [Fact]
    public void CreateFile_CreatesNewFile()
    {
        var filePath = Path.Combine(TestDir, "create_new.txt");
        Assert.False(File.Exists(filePath));

        var result = Run($"fs.createFile(\"{filePath}\"); let result = fs.exists(\"{filePath}\");");
        Assert.Equal(true, result);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void CreateFile_UpdatesTimestamp()
    {
        var filePath = Path.Combine(TestDir, "create_ts.txt");
        File.WriteAllText(filePath, "old");
        var pastTime = DateTime.UtcNow.AddSeconds(-60);
        File.SetLastWriteTimeUtc(filePath, pastTime);

        RunStatements($"fs.createFile(\"{filePath}\");");

        var newTime = File.GetLastWriteTimeUtc(filePath);
        Assert.True(newTime > pastTime.AddSeconds(30));
    }

    [Fact]
    public void CreateFile_ReturnsNull()
    {
        var filePath = Path.Combine(TestDir, "create_null.txt");
        var result = Run($"let result = fs.createFile(\"{filePath}\");");
        Assert.Null(result);
    }

    [Fact]
    public void CreateFile_NonStringThrows()
    {
        RunExpectingError("fs.createFile(42);");
    }

    [Fact]
    public void CreateFile_ExistingFileDoesNotThrow()
    {
        var filePath = Path.Combine(TestDir, "create_existing.txt");
        File.WriteAllText(filePath, "content");

        // Should not throw — just updates the timestamp
        var result = Run($"fs.createFile(\"{filePath}\"); let result = fs.exists(\"{filePath}\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void CreateFile_CreatesEmptyFile()
    {
        var filePath = Path.Combine(TestDir, "create_empty.txt");
        RunStatements($"fs.createFile(\"{filePath}\");");

        Assert.True(File.Exists(filePath));
        Assert.Equal(0, new FileInfo(filePath).Length);
    }

    // ── fs.symlink ───────────────────────────────────────────────────────

    [Fact]
    public void Symlink_CreatesLink()
    {
        try
        {
            var targetPath = Path.Combine(TestDir, "symlink_target.txt");
            var linkPath = Path.Combine(TestDir, "symlink_link.txt");
            File.WriteAllText(targetPath, "symlink content");

            RunStatements($"fs.symlink(\"{targetPath}\", \"{linkPath}\");");

            Assert.True(File.Exists(linkPath));
            var linkInfo = new FileInfo(linkPath);
            Assert.True(linkInfo.Attributes.HasFlag(FileAttributes.ReparsePoint));
            Assert.Equal("symlink content", File.ReadAllText(linkPath));
        }
        catch (UnauthorizedAccessException) { return; } // Not supported on this platform
        catch (IOException) { return; } // Skip if symlinks not supported
    }

    [Fact]
    public void Symlink_IsSymlinkReturnsTrueForLink()
    {
        try
        {
            var targetPath = Path.Combine(TestDir, "sym_target2.txt");
            var linkPath = Path.Combine(TestDir, "sym_link2.txt");
            File.WriteAllText(targetPath, "data");

            var result = Run($"fs.symlink(\"{targetPath}\", \"{linkPath}\"); let result = fs.isSymlink(\"{linkPath}\");");
            Assert.Equal(true, result);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }
    }

    [Fact]
    public void Symlink_NonStringTargetThrows()
    {
        RunExpectingError($"fs.symlink(123, \"{Path.Combine(TestDir, "link.txt")}\");");
    }

    [Fact]
    public void Symlink_NonStringPathThrows()
    {
        var targetPath = Path.Combine(TestDir, "sym_target3.txt");
        File.WriteAllText(targetPath, "data");
        RunExpectingError($"fs.symlink(\"{targetPath}\", 99);");
    }

    [Fact]
    public void Symlink_ReturnsNull()
    {
        try
        {
            var targetPath = Path.Combine(TestDir, "sym_null_target.txt");
            var linkPath = Path.Combine(TestDir, "sym_null_link.txt");
            File.WriteAllText(targetPath, "data");

            var result = Run($"let result = fs.symlink(\"{targetPath}\", \"{linkPath}\");");
            Assert.Null(result);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }
    }

    // ── fs.stat ──────────────────────────────────────────────────────────

    [Fact]
    public void Stat_ReturnsDict()
    {
        var filePath = Path.Combine(TestDir, "stat_file.txt");
        File.WriteAllText(filePath, "hello world");

        var result = Run($"let result = fs.stat(\"{filePath}\");");
        Assert.IsType<StashDictionary>(result);
    }

    [Fact]
    public void Stat_FileProperties()
    {
        var filePath = Path.Combine(TestDir, "stat_props.txt");
        File.WriteAllText(filePath, "hello world"); // 11 bytes

        var isFile = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"isFile\");");
        var isDir = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"isDir\");");
        var size = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"size\");");
        var name = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"name\");");

        Assert.Equal(true, isFile);
        Assert.Equal(false, isDir);
        Assert.Equal(11L, size);
        Assert.Equal("stat_props.txt", name);
    }

    [Fact]
    public void Stat_DirProperties()
    {
        var dirPath = Path.Combine(TestDir, "stat_subdir");
        Directory.CreateDirectory(dirPath);

        var isFile = Run($"let result = dict.get(fs.stat(\"{dirPath}\"), \"isFile\");");
        var isDir = Run($"let result = dict.get(fs.stat(\"{dirPath}\"), \"isDir\");");

        Assert.Equal(false, isFile);
        Assert.Equal(true, isDir);
    }

    [Fact]
    public void Stat_HasModifiedTime()
    {
        var filePath = Path.Combine(TestDir, "stat_mtime.txt");
        File.WriteAllText(filePath, "content");

        var modified = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"modified\");");
        Assert.IsType<double>(modified);
        Assert.True((double)modified! > 0);
    }

    [Fact]
    public void Stat_HasCreatedTime()
    {
        var filePath = Path.Combine(TestDir, "stat_ctime.txt");
        File.WriteAllText(filePath, "content");

        var created = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"created\");");
        Assert.IsType<double>(created);
        Assert.True((double)created! > 0);
    }

    [Fact]
    public void Stat_NonexistentPathThrows()
    {
        var missing = Path.Combine(TestDir, "does_not_exist.txt");
        RunExpectingError($"fs.stat(\"{missing}\");");
    }

    [Fact]
    public void Stat_NonStringThrows()
    {
        RunExpectingError("fs.stat(42);");
    }

    [Fact]
    public void Stat_NameMatchesFileName()
    {
        var filePath = Path.Combine(TestDir, "myspecialfile.txt");
        File.WriteAllText(filePath, "data");

        var name = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"name\");");
        Assert.Equal("myspecialfile.txt", name);
    }

    // ── fs.getPermissions ────────────────────────────────────────────────

    [Fact]
    public void GetPermissions_ReturnsFilePermissionsStruct()
    {
        var filePath = Path.Combine(TestDir, "perm_struct.txt");
        File.WriteAllText(filePath, "data");

        var result = Run($"let result = fs.getPermissions(\"{filePath}\");");
        Assert.IsType<StashInstance>(result);
    }

    [Fact]
    public void GetPermissions_OwnerReadWriteExecute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var filePath = Path.Combine(TestDir, "perm_755.txt");
        File.WriteAllText(filePath, "data");
        System.IO.File.SetUnixFileMode(filePath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute |
            System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupExecute |
            System.IO.UnixFileMode.OtherRead | System.IO.UnixFileMode.OtherExecute);

        var read    = Run($"let result = fs.getPermissions(\"{filePath}\").owner.read;");
        var write   = Run($"let result = fs.getPermissions(\"{filePath}\").owner.write;");
        var execute = Run($"let result = fs.getPermissions(\"{filePath}\").owner.execute;");

        Assert.Equal(true, read);
        Assert.Equal(true, write);
        Assert.Equal(true, execute);
    }

    [Fact]
    public void GetPermissions_GroupPermissions()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var filePath = Path.Combine(TestDir, "perm_750.txt");
        File.WriteAllText(filePath, "data");
        System.IO.File.SetUnixFileMode(filePath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute |
            System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupExecute);

        var read    = Run($"let result = fs.getPermissions(\"{filePath}\").group.read;");
        var write   = Run($"let result = fs.getPermissions(\"{filePath}\").group.write;");
        var execute = Run($"let result = fs.getPermissions(\"{filePath}\").group.execute;");

        Assert.Equal(true, read);
        Assert.Equal(false, write);
        Assert.Equal(true, execute);
    }

    [Fact]
    public void GetPermissions_OthersPermissions()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var filePath = Path.Combine(TestDir, "perm_754.txt");
        File.WriteAllText(filePath, "data");
        System.IO.File.SetUnixFileMode(filePath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute |
            System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupWrite | System.IO.UnixFileMode.GroupExecute |
            System.IO.UnixFileMode.OtherRead);

        var read    = Run($"let result = fs.getPermissions(\"{filePath}\").others.read;");
        var write   = Run($"let result = fs.getPermissions(\"{filePath}\").others.write;");
        var execute = Run($"let result = fs.getPermissions(\"{filePath}\").others.execute;");

        Assert.Equal(true, read);
        Assert.Equal(false, write);
        Assert.Equal(false, execute);
    }

    [Fact]
    public void GetPermissions_ReadOnlyFile()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var filePath = Path.Combine(TestDir, "perm_444.txt");
        File.WriteAllText(filePath, "data");
        System.IO.File.SetUnixFileMode(filePath,
            System.IO.UnixFileMode.UserRead |
            System.IO.UnixFileMode.GroupRead |
            System.IO.UnixFileMode.OtherRead);

        var write = Run($"let result = fs.getPermissions(\"{filePath}\").owner.write;");
        Assert.Equal(false, write);
    }

    [Fact]
    public void GetPermissions_NonexistentPathThrows()
    {
        var missing = Path.Combine(TestDir, "no_such_file.txt");
        RunExpectingError($"fs.getPermissions(\"{missing}\");");
    }

    [Fact]
    public void GetPermissions_NonStringThrows()
    {
        RunExpectingError("fs.getPermissions(42);");
    }

    [Fact]
    public void GetPermissions_DirectoryPermissions()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var dirPath = Path.Combine(TestDir, "perm_dir");
        Directory.CreateDirectory(dirPath);
        System.IO.File.SetUnixFileMode(dirPath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute |
            System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupExecute |
            System.IO.UnixFileMode.OtherRead | System.IO.UnixFileMode.OtherExecute);

        var result = Run($"let result = fs.getPermissions(\"{dirPath}\");");
        Assert.IsType<StashInstance>(result);

        var ownerRead = Run($"let result = fs.getPermissions(\"{dirPath}\").owner.read;");
        Assert.Equal(true, ownerRead);
    }

    // ── fs.setPermissions ────────────────────────────────────────────────

    [Fact]
    public void SetPermissions_SetsOwnerReadWriteExecute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        // Source file has 755 — get the struct and apply it to the target.
        var srcPath = Path.Combine(TestDir, "setperm_src_all.txt");
        var dstPath = Path.Combine(TestDir, "setperm_dst_all.txt");
        File.WriteAllText(srcPath, "data");
        File.WriteAllText(dstPath, "data");
        System.IO.File.SetUnixFileMode(srcPath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute |
            System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupExecute |
            System.IO.UnixFileMode.OtherRead | System.IO.UnixFileMode.OtherExecute);
        // Start dst with no execute so we can verify it changes.
        System.IO.File.SetUnixFileMode(dstPath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);

        RunStatements($"fs.setPermissions(\"{dstPath}\", fs.getPermissions(\"{srcPath}\"));");

        var read    = Run($"let result = fs.getPermissions(\"{dstPath}\").owner.read;");
        var write   = Run($"let result = fs.getPermissions(\"{dstPath}\").owner.write;");
        var execute = Run($"let result = fs.getPermissions(\"{dstPath}\").owner.execute;");

        Assert.Equal(true, read);
        Assert.Equal(true, write);
        Assert.Equal(true, execute);
    }

    [Fact]
    public void SetPermissions_RemovesWritePermission()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        // Source file has 444 (no write). Apply its permissions to a writable target.
        var srcPath = Path.Combine(TestDir, "setperm_src_nowrite.txt");
        var dstPath = Path.Combine(TestDir, "setperm_dst_nowrite.txt");
        File.WriteAllText(srcPath, "data");
        File.WriteAllText(dstPath, "data");
        System.IO.File.SetUnixFileMode(srcPath,
            System.IO.UnixFileMode.UserRead |
            System.IO.UnixFileMode.GroupRead |
            System.IO.UnixFileMode.OtherRead);

        RunStatements($"fs.setPermissions(\"{dstPath}\", fs.getPermissions(\"{srcPath}\"));");

        var write = Run($"let result = fs.getPermissions(\"{dstPath}\").owner.write;");
        Assert.Equal(false, write);
    }

    [Fact]
    public void SetPermissions_ReturnsNull()
    {
        var srcPath = Path.Combine(TestDir, "setperm_src_null.txt");
        var dstPath = Path.Combine(TestDir, "setperm_dst_null.txt");
        File.WriteAllText(srcPath, "data");
        File.WriteAllText(dstPath, "data");

        var result = Run($"let result = fs.setPermissions(\"{dstPath}\", fs.getPermissions(\"{srcPath}\"));");
        Assert.Null(result);
    }

    [Fact]
    public void SetPermissions_NonexistentPathThrows()
    {
        var srcPath = Path.Combine(TestDir, "setperm_src_missing.txt");
        var missing = Path.Combine(TestDir, "setperm_dst_missing.txt");
        File.WriteAllText(srcPath, "data");

        RunExpectingError($"fs.setPermissions(\"{missing}\", fs.getPermissions(\"{srcPath}\"));");
    }

    [Fact]
    public void SetPermissions_NonStringPathThrows()
    {
        var srcPath = Path.Combine(TestDir, "setperm_nonstringpath.txt");
        File.WriteAllText(srcPath, "data");

        RunExpectingError($"fs.setPermissions(42, fs.getPermissions(\"{srcPath}\"));");
    }

    [Fact]
    public void SetPermissions_NonStructThrows()
    {
        var filePath = Path.Combine(TestDir, "setperm_badtype.txt");
        File.WriteAllText(filePath, "data");
        RunExpectingError($"fs.setPermissions(\"{filePath}\", \"not-a-struct\");");
    }

    [Fact]
    public void SetPermissions_RoundTrip()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        // Set known permissions on src via SetUnixFileMode, copy to dst, verify dst matches.
        var srcPath = Path.Combine(TestDir, "setperm_src_rt.txt");
        var dstPath = Path.Combine(TestDir, "setperm_dst_rt.txt");
        File.WriteAllText(srcPath, "data");
        File.WriteAllText(dstPath, "data");
        System.IO.File.SetUnixFileMode(srcPath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute |
            System.IO.UnixFileMode.GroupRead);

        RunStatements($"fs.setPermissions(\"{dstPath}\", fs.getPermissions(\"{srcPath}\"));");

        var ownerRead    = Run($"let result = fs.getPermissions(\"{dstPath}\").owner.read;");
        var ownerWrite   = Run($"let result = fs.getPermissions(\"{dstPath}\").owner.write;");
        var ownerExecute = Run($"let result = fs.getPermissions(\"{dstPath}\").owner.execute;");
        var groupWrite   = Run($"let result = fs.getPermissions(\"{dstPath}\").group.write;");
        var othersRead   = Run($"let result = fs.getPermissions(\"{dstPath}\").others.read;");

        Assert.Equal(true, ownerRead);
        Assert.Equal(true, ownerWrite);
        Assert.Equal(true, ownerExecute);
        Assert.Equal(false, groupWrite);
        Assert.Equal(false, othersRead);
    }

    // ── fs.setReadOnly ───────────────────────────────────────────────────

    [Fact]
    public void SetReadOnly_MakesFileReadOnly()
    {
        var filePath = Path.Combine(TestDir, "setro_true.txt");
        File.WriteAllText(filePath, "data");

        RunStatements($"fs.setReadOnly(\"{filePath}\", true);");

        var write = Run($"let result = fs.getPermissions(\"{filePath}\").owner.write;");
        Assert.Equal(false, write);
    }

    [Fact]
    public void SetReadOnly_MakesFileWritable()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var filePath = Path.Combine(TestDir, "setro_false.txt");
        File.WriteAllText(filePath, "data");
        System.IO.File.SetUnixFileMode(filePath,
            System.IO.UnixFileMode.UserRead |
            System.IO.UnixFileMode.GroupRead |
            System.IO.UnixFileMode.OtherRead);

        RunStatements($"fs.setReadOnly(\"{filePath}\", false);");

        var write = Run($"let result = fs.getPermissions(\"{filePath}\").owner.write;");
        Assert.Equal(true, write);
    }

    [Fact]
    public void SetReadOnly_ReturnsNull()
    {
        var filePath = Path.Combine(TestDir, "setro_null.txt");
        File.WriteAllText(filePath, "data");

        var result = Run($"let result = fs.setReadOnly(\"{filePath}\", true);");
        Assert.Null(result);
    }

    [Fact]
    public void SetReadOnly_NonexistentPathThrows()
    {
        var missing = Path.Combine(TestDir, "setro_missing.txt");
        RunExpectingError($"fs.setReadOnly(\"{missing}\", true);");
    }

    [Fact]
    public void SetReadOnly_RoundTrip()
    {
        var filePath = Path.Combine(TestDir, "setro_roundtrip.txt");
        File.WriteAllText(filePath, "data");

        RunStatements($"fs.setReadOnly(\"{filePath}\", true);");
        var writeAfterRO = Run($"let result = fs.getPermissions(\"{filePath}\").owner.write;");
        Assert.Equal(false, writeAfterRO);

        RunStatements($"fs.setReadOnly(\"{filePath}\", false);");
        var writeAfterRW = Run($"let result = fs.getPermissions(\"{filePath}\").owner.write;");
        Assert.Equal(true, writeAfterRW);
    }

    // ── fs.setExecutable ─────────────────────────────────────────────────

    [Fact]
    public void SetExecutable_AddsExecuteBit()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var filePath = Path.Combine(TestDir, "setexec_true.txt");
        File.WriteAllText(filePath, "data");
        System.IO.File.SetUnixFileMode(filePath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);

        RunStatements($"fs.setExecutable(\"{filePath}\", true);");

        var execute = Run($"let result = fs.getPermissions(\"{filePath}\").owner.execute;");
        Assert.Equal(true, execute);
    }

    [Fact]
    public void SetExecutable_RemovesExecuteBit()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var filePath = Path.Combine(TestDir, "setexec_false.txt");
        File.WriteAllText(filePath, "data");
        System.IO.File.SetUnixFileMode(filePath,
            System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute |
            System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupExecute |
            System.IO.UnixFileMode.OtherRead | System.IO.UnixFileMode.OtherExecute);

        RunStatements($"fs.setExecutable(\"{filePath}\", false);");

        var ownerExec = Run($"let result = fs.getPermissions(\"{filePath}\").owner.execute;");
        var groupExec = Run($"let result = fs.getPermissions(\"{filePath}\").group.execute;");
        var otherExec = Run($"let result = fs.getPermissions(\"{filePath}\").others.execute;");

        Assert.Equal(false, ownerExec);
        Assert.Equal(false, groupExec);
        Assert.Equal(false, otherExec);
    }

    [Fact]
    public void SetExecutable_ReturnsNull()
    {
        var filePath = Path.Combine(TestDir, "setexec_null.txt");
        File.WriteAllText(filePath, "data");

        var result = Run($"let result = fs.setExecutable(\"{filePath}\", true);");
        Assert.Null(result);
    }

    [Fact]
    public void SetExecutable_NonexistentPathThrows()
    {
        var missing = Path.Combine(TestDir, "setexec_missing.txt");
        RunExpectingError($"fs.setExecutable(\"{missing}\", true);");
    }
}
