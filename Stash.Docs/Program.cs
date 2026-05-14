namespace Stash.Docs;

using System;
using System.IO;

/// <summary>
/// CLI entry point for generated Markdown references. With no arguments it
/// overwrites every generated document in <c>docs/</c>. Use <c>--stdlib</c>,
/// <c>--bytecode</c>, or <c>--all</c> for explicit generation modes. A single
/// non-option argument remains a backward-compatible standard-library target path.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string repoRoot = FindRepoRoot();

            if (args.Length == 0)
            {
                WriteStandardLibraryReference(Path.Combine(repoRoot, ReferenceGenerator.DefaultRelativePath));
                WriteBytecodeReference(repoRoot, Path.Combine(repoRoot, BytecodeInstructionReferenceGenerator.DefaultRelativePath));
            }
            else if (args.Length == 1 && args[0] == "--all")
            {
                WriteStandardLibraryReference(Path.Combine(repoRoot, ReferenceGenerator.DefaultRelativePath));
                WriteBytecodeReference(repoRoot, Path.Combine(repoRoot, BytecodeInstructionReferenceGenerator.DefaultRelativePath));
            }
            else if (args.Length == 1 && args[0] == "--stdlib")
            {
                WriteStandardLibraryReference(Path.Combine(repoRoot, ReferenceGenerator.DefaultRelativePath));
            }
            else if (args.Length == 1 && args[0] == "--bytecode")
            {
                WriteBytecodeReference(repoRoot, Path.Combine(repoRoot, BytecodeInstructionReferenceGenerator.DefaultRelativePath));
            }
            else
            {
                // Backward-compatible mode: a single path still means "write the
                // standard-library reference there", matching the original CLI.
                WriteStandardLibraryReference(Path.GetFullPath(args[0]));
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Stash.Docs: " + ex.Message);
            return 1;
        }
    }

    private static void WriteStandardLibraryReference(string target)
    {
        ReferenceGenerator.WriteTo(target);
        Console.WriteLine("Wrote " + target);
    }

    private static void WriteBytecodeReference(string repoRoot, string target)
    {
        string sourcePath = Path.Combine(repoRoot, BytecodeInstructionReferenceGenerator.DefaultSourceRelativePath);
        BytecodeInstructionReferenceGenerator.WriteTo(target, sourcePath);
        Console.WriteLine("Wrote " + target);
    }

    private static string FindRepoRoot()
    {
        string cwd = Directory.GetCurrentDirectory();
        for (int i = 0; i < 10 && cwd != null; i++)
        {
            if (File.Exists(Path.Combine(cwd, "Stash.sln")))
                return cwd;
            cwd = Path.GetDirectoryName(cwd)!;
        }

        // Walk up from the binary location looking for the repo root marker
        // (Stash.sln). Falling back to AppContext.BaseDirectory keeps the
        // command working from a published build, from `dotnet run`, and
        // from xUnit test hosts.
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && current != null; i++)
        {
            if (File.Exists(Path.Combine(current, "Stash.sln")))
                return current;
            current = Path.GetDirectoryName(current)!;
        }

        throw new InvalidOperationException(
            "Could not locate repo root (Stash.sln). Pass an explicit output path as the first argument.");
    }
}
