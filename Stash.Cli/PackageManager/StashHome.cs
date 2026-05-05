using System;
using System.IO;

namespace Stash.Cli.PackageManager;

internal static class StashHome
{
    public static string GetStashDir()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("STASH_HOME");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return Path.GetFullPath(overrideDir);
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile) && IsWritableParent(profile))
        {
            return Path.Combine(profile, ".stash");
        }

        string? dotnetCliHome = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME");
        if (!string.IsNullOrWhiteSpace(dotnetCliHome) && IsWritableParent(dotnetCliHome))
        {
            return Path.Combine(dotnetCliHome, ".stash");
        }

        return Path.Combine(Path.GetTempPath(), ".stash");
    }

    private static bool IsWritableParent(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            string probe = Path.Combine(path, $".stash-write-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
