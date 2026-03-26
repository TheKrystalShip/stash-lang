using System;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg unpublish</c> command for removing a specific
/// version of a package from the registry.
/// </summary>
/// <remarks>
/// <para>
/// Parses a <c>&lt;name&gt;@&lt;version&gt;</c> specifier, authenticates using
/// the token stored in <see cref="UserConfig"/>, and calls
/// <see cref="RegistryClient.Unpublish"/> to delete the specified version from
/// the registry.
/// </para>
/// <para>
/// Registry resolution uses <see cref="RegistryResolver"/>; an authenticated
/// session is required.
/// </para>
/// </remarks>
public static class UnpublishCommand
{
    /// <summary>
    /// Executes the unpublish command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg unpublish</c>. The first
    /// positional argument must be a <c>&lt;name&gt;@&lt;version&gt;</c>
    /// specifier (e.g. <c>my-package@1.0.0</c>). The <c>--registry &lt;url&gt;</c>
    /// flag optionally overrides the default registry.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the specifier is missing or does not contain a version.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the user is not logged in to the target registry.
    /// </exception>
    public static void Execute(string[] args)
    {
        string? spec = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--registry" && i + 1 < args.Length)
            {
                i++;
            }
            else if (spec == null)
            {
                spec = args[i];
            }
        }

        if (spec == null)
        {
            throw new ArgumentException("Usage: stash pkg unpublish <name>@<version>");
        }

        var (registryUrl, _) = RegistryResolver.Resolve(args);

        int atIdx = spec.LastIndexOf('@');
        if (atIdx <= 0)
        {
            throw new ArgumentException("Format: <name>@<version>. Example: stash pkg unpublish my-package@1.0.0");
        }

        string packageName = spec[..atIdx];
        string version = spec[(atIdx + 1)..];

        var config = UserConfig.Load();
        var entry = config.GetEntry(registryUrl);
        if (entry?.Token == null)
        {
            throw new InvalidOperationException($"Not logged in to registry '{registryUrl}'. Run 'stash pkg login'.");
        }

        var client = new RegistryClient(registryUrl, entry.Token, entry.RefreshToken,
            entry.ExpiresAt, entry.MachineId, registryUrl);
        client.Unpublish(packageName, version);

        Console.WriteLine($"Unpublished {packageName}@{version} from {registryUrl}.");
    }
}
