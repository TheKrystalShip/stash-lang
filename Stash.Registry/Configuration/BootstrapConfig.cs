namespace Stash.Registry.Configuration;

public sealed class BootstrapConfig
{
    /// <summary>Username to create as admin via env-var seed. Defaults to "admin".</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>Optional email address for the bootstrapped admin. Not stored in the DB.</summary>
    public string AdminEmail { get; set; } = "";

    /// <summary>
    /// Name of the environment variable holding the admin password.
    /// When empty, env-var seeding is disabled.
    /// </summary>
    public string AdminPasswordEnv { get; set; } = "";
}
