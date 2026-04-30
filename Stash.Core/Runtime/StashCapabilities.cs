namespace Stash.Runtime;

using System;

/// <summary>
/// Controls which built-in modules are available to Stash scripts.
/// Use these flags to sandbox embedded scripts by restricting access
/// to system resources.
/// </summary>
[Flags]
public enum StashCapabilities
{
    /// <summary>No optional capabilities. Core language features only.</summary>
    None = 0,

    /// <summary>File system access (fs.readFile, fs.writeFile, etc.)</summary>
    FileSystem = 1 << 0,

    /// <summary>Network access (http.get, http.post, etc.)</summary>
    Network = 1 << 1,

    /// <summary>Process management (process.spawn, process.exec, process.exit, etc.)</summary>
    Process = 1 << 2,

    /// <summary>Environment variable access (env.get, env.set, etc.)</summary>
    Environment = 1 << 3,

    /// <summary>Interactive shell mode features (shell.lastExitCode, shell history APIs, prompt hooks).</summary>
    Shell = 1 << 4,

    /// <summary>All capabilities enabled. This is the default for CLI usage.</summary>
    All = FileSystem | Network | Process | Environment | Shell,
}
