namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>ssh</c> namespace built-in functions for SSH remote command execution.
/// </summary>
/// <remarks>
/// <para>
/// Provides functions for connecting to remote hosts via SSH and executing commands:
/// <c>ssh.connect</c>, <c>ssh.exec</c>, <c>ssh.execAll</c>, <c>ssh.shell</c>,
/// <c>ssh.close</c>, <c>ssh.isConnected</c>, <c>ssh.tunnel</c>, and <c>ssh.closeTunnel</c>.
/// </para>
/// <para>
/// Connection functions return an <c>SshConnection</c> struct instance with <c>host</c>,
/// <c>port</c>, and <c>username</c> fields. This namespace is only registered when the
/// <see cref="StashCapabilities.Network"/> capability is enabled.
/// </para>
/// </remarks>
public static class SshBuiltIns
{
    /// <summary>Maps SshConnection instances to their underlying SshClient. Uses weak references to allow GC.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StashInstance, SshClient> _clients = new();

    /// <summary>Maps SshTunnel instances to their forwarded port.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StashInstance, ForwardedPortLocal> _tunnels = new();

    /// <summary>Maps SshTunnel instances to the SshClient that owns the tunnel.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StashInstance, SshClient> _tunnelClients = new();

    /// <summary>
    /// Registers all <c>ssh</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("ssh");
        ns.RequiresCapability(StashCapabilities.Network);

        // ssh.connect(options) — Connects to a remote host via SSH.
        // Options dict: host (string, required), port (int, default 22), username (string, required),
        // password (string), privateKey (string path), passphrase (string).
        // Returns an SshConnection struct.
        ns.Function("connect", [Param("options", "dict")], (ctx, args) =>
        {
            var options = Args.Dict(args, 0, "ssh.connect");

            var host = options.Get("host") as string
                ?? throw new RuntimeError("ssh.connect: 'host' is required and must be a string.");
            var username = options.Get("username") as string
                ?? throw new RuntimeError("ssh.connect: 'username' is required and must be a string.");

            int port = 22;
            var portVal = options.Get("port");
            if (portVal is long p)
            {
                port = (int)p;
            }

            var password = options.Get("password") as string;
            var privateKeyPath = options.Get("privateKey") as string;
            var passphrase = options.Get("passphrase") as string;

            if (password is null && privateKeyPath is null)
            {
                throw new RuntimeError("ssh.connect: must provide 'password' or 'privateKey'.");
            }

            try
            {
                SshClient client;
                if (privateKeyPath is not null)
                {
                    privateKeyPath = ctx.ExpandTilde(privateKeyPath);
                    PrivateKeyFile keyFile = passphrase is not null
                        ? new PrivateKeyFile(privateKeyPath, passphrase)
                        : new PrivateKeyFile(privateKeyPath);
                    client = new SshClient(host, port, username, keyFile);
                }
                else
                {
                    client = new SshClient(host, port, username, password!);
                }

                client.Connect();

                var instance = new StashInstance("SshConnection", new Dictionary<string, object?>
                {
                    ["host"] = host,
                    ["port"] = (long)port,
                    ["username"] = username
                });
                _clients.Add(instance, client);
                return instance;
            }
            catch (SshAuthenticationException e)
            {
                throw new RuntimeError($"ssh.connect: authentication failed — {e.Message}");
            }
            catch (SshConnectionException e)
            {
                throw new RuntimeError($"ssh.connect: connection failed — {e.Message}");
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.connect: {e.Message}");
            }
            catch (IOException e)
            {
                throw new RuntimeError($"ssh.connect: {e.Message}");
            }
            catch (System.Net.Sockets.SocketException e)
            {
                throw new RuntimeError($"ssh.connect: {e.Message}");
            }
        });

        // ssh.exec(conn, command) — Executes a command on the remote host.
        // Returns a CommandResult struct with stdout, stderr, and exitCode.
        ns.Function("exec", [Param("conn", "SshConnection"), Param("command", "string")], (_, args) =>
        {
            SshClient client = GetClient(args[0], "ssh.exec");
            var command = Args.String(args, 1, "ssh.exec");

            try
            {
                SshCommand cmd = client.RunCommand(command);
                return RuntimeValues.CreateCommandResult(cmd.Result ?? "", cmd.Error ?? "", (long)(cmd.ExitStatus ?? -1));
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.exec: {e.Message}");
            }
        });

        // ssh.execAll(conn, commands) — Executes an array of commands sequentially.
        // Returns an array of CommandResult structs.
        ns.Function("execAll", [Param("conn", "SshConnection"), Param("commands", "array")], (_, args) =>
        {
            SshClient client = GetClient(args[0], "ssh.execAll");
            var commands = Args.List(args, 1, "ssh.execAll");

            try
            {
                var results = new List<object?>();
                foreach (object? item in commands)
                {
                    if (item is not string cmd)
                    {
                        throw new RuntimeError("ssh.execAll: all commands must be strings.");
                    }

                    SshCommand sshCmd = client.RunCommand(cmd);
                    results.Add(RuntimeValues.CreateCommandResult(sshCmd.Result ?? "", sshCmd.Error ?? "", (long)(sshCmd.ExitStatus ?? -1)));
                }
                return results;
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.execAll: {e.Message}");
            }
        });

        // ssh.shell(conn, commands) — Runs commands through an interactive shell stream.
        // Useful for commands requiring a TTY or stateful sessions (sudo, etc.).
        // Returns the combined shell output as a string.
        ns.Function("shell", [Param("conn", "SshConnection"), Param("commands", "array")], (_, args) =>
        {
            SshClient client = GetClient(args[0], "ssh.shell");
            var commands = Args.List(args, 1, "ssh.shell");

            try
            {
                using ShellStream stream = client.CreateShellStream("xterm", 80, 24, 800, 600, 4096);

                foreach (object? item in commands)
                {
                    if (item is not string cmd)
                    {
                        throw new RuntimeError("ssh.shell: all commands must be strings.");
                    }

                    stream.WriteLine(cmd);
                }

                // Allow time for commands to execute and output to arrive
                Thread.Sleep(500);
                return stream.Read();
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.shell: {e.Message}");
            }
        });

        // ssh.close(conn) — Disconnects and disposes the SSH connection.
        ns.Function("close", [Param("conn", "SshConnection")], (_, args) =>
        {
            SshClient client = GetClient(args[0], "ssh.close");

            try
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
                client.Dispose();
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.close: {e.Message}");
            }

            return null;
        });

        // ssh.isConnected(conn) — Returns true if the SSH connection is still active.
        ns.Function("isConnected", [Param("conn", "SshConnection")], (_, args) =>
        {
            SshClient client = GetClient(args[0], "ssh.isConnected");
            return client.IsConnected;
        });

        // ssh.tunnel(conn, options) — Creates a local port forward (SSH tunnel).
        // Options dict: localPort (int, default 0 for auto), remoteHost (string, required), remotePort (int, required).
        // Returns an SshTunnel struct with localPort, remoteHost, and remotePort.
        ns.Function("tunnel", [Param("conn", "SshConnection"), Param("options", "dict")], (_, args) =>
        {
            SshClient client = GetClient(args[0], "ssh.tunnel");
            var options = Args.Dict(args, 1, "ssh.tunnel");

            var remoteHost = options.Get("remoteHost") as string
                ?? throw new RuntimeError("ssh.tunnel: 'remoteHost' is required and must be a string.");

            var remotePortVal = options.Get("remotePort");
            if (remotePortVal is not long remotePort)
            {
                throw new RuntimeError("ssh.tunnel: 'remotePort' is required and must be an integer.");
            }

            uint localPort = 0;
            var localPortVal = options.Get("localPort");
            if (localPortVal is long lp)
            {
                localPort = (uint)lp;
            }

            try
            {
                var forward = new ForwardedPortLocal("127.0.0.1", localPort, remoteHost, (uint)remotePort);
                client.AddForwardedPort(forward);
                forward.Start();

                var instance = new StashInstance("SshTunnel", new Dictionary<string, object?>
                {
                    ["localPort"] = (long)forward.BoundPort,
                    ["remoteHost"] = remoteHost,
                    ["remotePort"] = remotePort
                });
                _tunnels.Add(instance, forward);
                _tunnelClients.Add(instance, client);
                return instance;
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.tunnel: {e.Message}");
            }
        });

        // ssh.closeTunnel(tunnel) — Closes an SSH tunnel (port forward).
        ns.Function("closeTunnel", [Param("tunnel", "SshTunnel")], (_, args) =>
        {
            var inst = Args.Instance(args, 0, "SshTunnel", "ssh.closeTunnel");

            if (!_tunnels.TryGetValue(inst, out ForwardedPortLocal? forward))
            {
                throw new RuntimeError("ssh.closeTunnel: tunnel is invalid.");
            }

            _tunnelClients.TryGetValue(inst, out SshClient? client);

            try
            {
                if (forward.IsStarted)
                {
                    forward.Stop();
                }
                client?.RemoveForwardedPort(forward);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.closeTunnel: {e.Message}");
            }

            return null;
        });

        return ns.Build();
    }

    /// <summary>
    /// Extracts and validates the <see cref="SshClient"/> from a connection instance.
    /// </summary>
    private static SshClient GetClient(object? arg, string funcName)
    {
        if (arg is not StashInstance inst || inst.TypeName != "SshConnection")
        {
            throw new RuntimeError($"First argument to '{funcName}' must be an SshConnection.");
        }

        if (!_clients.TryGetValue(inst, out SshClient? client))
        {
            throw new RuntimeError($"{funcName}: connection is invalid or closed.");
        }

        return client;
    }
}
