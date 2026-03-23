using System;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg login</c> command for authenticating with a registry
/// and storing the resulting bearer token in the user configuration.
/// </summary>
/// <remarks>
/// <para>
/// Prompts for a username and password (the password is read without echo using
/// <see cref="ReadPassword"/>), calls <see cref="RegistryClient.Login"/>, and
/// persists the returned token via <see cref="UserConfig.SetToken"/>.
/// </para>
/// <para>
/// If no default registry is currently configured the logged-in registry is
/// automatically set as the default.
/// </para>
/// </remarks>
public static class LoginCommand
{
    /// <summary>
    /// Executes the login command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg login</c>.  The
    /// <c>--registry &lt;url&gt;</c> flag is required and specifies the registry to
    /// authenticate with.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the username or password is blank, or when authentication fails.
    /// </exception>
    public static void Execute(string[] args)
    {
        var (registryUrl, _) = RegistryResolver.Resolve(args, requireExplicit: true);

        var config = UserConfig.Load();

        Console.Write("Username: ");
        string? username = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(username))
        {
            throw new InvalidOperationException("Username is required.");
        }

        Console.Write("Password: ");
        string? password = ReadPassword();
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Password is required.");
        }

        var client = new RegistryClient(registryUrl);
        string? token = client.Login(username, password);
        if (token == null)
        {
            throw new InvalidOperationException("Login failed. Check your credentials.");
        }

        config.SetToken(registryUrl, token);
        Console.WriteLine($"Logged in as {username} to {registryUrl}.");

        if (string.IsNullOrEmpty(config.DefaultRegistry))
        {
            config.DefaultRegistry = registryUrl;
            config.Save();
            Console.WriteLine($"  Default registry set to {registryUrl}");
        }
    }

    /// <summary>
    /// Reads a password from standard input without echoing characters to the console,
    /// supporting backspace deletion.
    /// </summary>
    /// <returns>The password string entered by the user.</returns>
    private static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
            }
            else if (key.Key != ConsoleKey.Backspace)
            {
                password.Append(key.KeyChar);
            }
        }
        return password.ToString();
    }
}
