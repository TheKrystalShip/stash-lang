namespace Stash.Lsp;

using System;
using System.Threading.Tasks;

public class Program
{
    private const string Version = "0.5.0";

    public static async Task Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--stdio"))
        {
            await StashLanguageServer.RunAsync();
            return;
        }

        foreach (string arg in args)
        {
            if (arg is "--version" or "-v")
            {
                Console.WriteLine($"stash-lsp {Version}");
                Environment.Exit(0);
            }

            if (arg is "--help" or "-h")
            {
                Console.WriteLine($"""
                    stash-lsp v{Version} — Language Server Protocol server for Stash

                    Usage: stash-lsp [options]

                    The LSP server communicates over stdin/stdout using the Language Server
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
                await Console.Error.WriteLineAsync($"stash-lsp: unknown option '{arg}'");
                await Console.Error.WriteLineAsync("Run 'stash-lsp --help' for usage information.");
                Environment.Exit(64);
            }
        }
    }
}
