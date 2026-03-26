using System;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg token</c> command for managing API tokens.
/// </summary>
public static class TokenCommand
{
    public static void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }

        string subcommand = args[0];
        string[] subArgs = args[1..];

        switch (subcommand)
        {
            case "create":
                Create(subArgs);
                break;
            case "list":
            case "ls":
                ListTokens(subArgs);
                break;
            case "revoke":
                Revoke(subArgs);
                break;
            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                break;
            default:
                Console.Error.WriteLine($"Unknown token subcommand: {subcommand}");
                Console.Error.WriteLine();
                PrintHelp();
                break;
        }
    }

    private static void Create(string[] args)
    {
        string? scope = null;
        string? description = null;
        string? expiresIn = null;
        string? registryUrl = null;
        string? cliToken = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scope":
                    if (i + 1 < args.Length) scope = args[++i];
                    break;
                case "--description":
                    if (i + 1 < args.Length) description = args[++i];
                    break;
                case "--expires-in":
                    if (i + 1 < args.Length) expiresIn = args[++i];
                    break;
                case "--registry":
                    if (i + 1 < args.Length) registryUrl = args[++i];
                    break;
                case "--token":
                    if (i + 1 < args.Length) cliToken = args[++i];
                    break;
            }
        }

        registryUrl = UserConfig.ResolveRegistryUrl(registryUrl);
        var client = ResolveClient(registryUrl, cliToken);

        var result = client.CreateToken(scope, description, expiresIn);
        if (result == null)
            throw new InvalidOperationException("Token creation failed.");

        Console.WriteLine($"Token created successfully.");
        Console.WriteLine($"  Token:       {result.Token}");
        Console.WriteLine($"  Token ID:    {result.TokenId}");
        Console.WriteLine($"  Scope:       {result.Scope}");
        Console.WriteLine($"  Expires at:  {result.ExpiresAt:u}");
        if (result.Description != null)
            Console.WriteLine($"  Description: {result.Description}");
        Console.WriteLine();
        Console.WriteLine("Save this token — it will not be shown again.");
    }

    private static void ListTokens(string[] args)
    {
        string? registryUrl = null;
        string? cliToken = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--registry":
                    if (i + 1 < args.Length) registryUrl = args[++i];
                    break;
                case "--token":
                    if (i + 1 < args.Length) cliToken = args[++i];
                    break;
            }
        }

        registryUrl = UserConfig.ResolveRegistryUrl(registryUrl);
        var client = ResolveClient(registryUrl, cliToken);

        var result = client.ListTokens();
        if (result == null || result.Tokens.Count == 0)
        {
            Console.WriteLine("No API tokens found.");
            return;
        }

        Console.WriteLine($"{"ID",-38} {"Scope",-10} {"Expires",-22} {"Description"}");
        Console.WriteLine(new string('-', 90));
        foreach (var token in result.Tokens)
        {
            string desc = token.Description ?? "";
            Console.WriteLine($"{token.TokenId,-38} {token.Scope,-10} {token.ExpiresAt.ToString("u"),-22} {desc}");
        }
    }

    private static void Revoke(string[] args)
    {
        string? tokenId = null;
        string? registryUrl = null;
        string? cliToken = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--registry":
                    if (i + 1 < args.Length) registryUrl = args[++i];
                    break;
                case "--token":
                    if (i + 1 < args.Length) cliToken = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith("--") && tokenId == null)
                        tokenId = args[i];
                    break;
            }
        }

        if (string.IsNullOrEmpty(tokenId))
            throw new InvalidOperationException("Token ID is required. Usage: stash pkg token revoke <tokenId>");

        registryUrl = UserConfig.ResolveRegistryUrl(registryUrl);
        var client = ResolveClient(registryUrl, cliToken);

        client.RevokeToken(tokenId);
        Console.WriteLine($"Token {tokenId} has been revoked.");
    }

    /// <summary>
    /// Resolves a RegistryClient from CLI token, STASH_TOKEN env var, or config file.
    /// </summary>
    private static RegistryClient ResolveClient(string registryUrl, string? cliToken)
    {
        string? token = cliToken
            ?? Environment.GetEnvironmentVariable("STASH_TOKEN");

        if (!string.IsNullOrEmpty(token))
            return new RegistryClient(registryUrl, token);

        var config = UserConfig.Load();
        var entry = config.GetEntry(registryUrl);
        if (entry?.Token == null)
            throw new InvalidOperationException(
                $"Not logged in to registry '{registryUrl}'. Run 'stash pkg login' first, set the STASH_TOKEN environment variable, or use --token.");

        return new RegistryClient(
            registryUrl,
            entry.Token,
            entry.RefreshToken,
            entry.ExpiresAt,
            entry.MachineId,
            registryUrl);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: stash pkg token <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  create    Create a new API token");
        Console.WriteLine("  list, ls  List your API tokens");
        Console.WriteLine("  revoke    Revoke an API token by ID");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --registry <url>         Registry URL");
        Console.WriteLine("  --token <value>          Use an existing token for authentication");
        Console.WriteLine("  --scope <scope>          Token scope: read, publish, admin (create only)");
        Console.WriteLine("  --description <text>     Token description (create only)");
        Console.WriteLine("  --expires-in <duration>  Token lifetime, e.g. 30d, 12h (create only)");
    }
}
