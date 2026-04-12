namespace Stash.Dap;

using System;
using System.Threading.Tasks;

public class Program
{
    private const string Version = "0.5.0";

    public static async Task Main(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg is "--version" or "-v")
            {
                Console.WriteLine($"stash-dap {Version}");
                Environment.Exit(0);
            }

            if (arg is "--help" or "-h")
            {
                Console.WriteLine($"""
                    stash-dap v{Version} — Debug Adapter Protocol server for Stash

                    Usage: stash-dap [options]

                    The DAP server communicates over stdin/stdout using the Debug Adapter
                    Protocol. It is typically launched by an editor (e.g. VS Code) and not
                    invoked directly.

                    Options:
                      -h, --help       Show this help message
                      -v, --version    Show version information

                    VS Code Extension: Install 'stash-lang' from the VS Code marketplace.
                    """);
                Environment.Exit(0);
            }

            if (arg.StartsWith('-'))
            {
                Console.Error.WriteLine($"stash-dap: unknown option '{arg}'");
                Console.Error.WriteLine("Run 'stash-dap --help' for usage information.");
                Environment.Exit(64);
            }
        }

        await StashDebugServer.RunAsync();
    }
}
