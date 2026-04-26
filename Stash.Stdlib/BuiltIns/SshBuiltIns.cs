namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
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
        ns.Function("connect", [Param("options", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var options = SvArgs.Dict(args, 0, "ssh.connect");

            var host = options.Get("host").ToObject() as string
                ?? throw new RuntimeError("ssh.connect: 'host' is required and must be a string.", errorType: StashErrorTypes.TypeError);
            var username = options.Get("username").ToObject() as string
                ?? throw new RuntimeError("ssh.connect: 'username' is required and must be a string.", errorType: StashErrorTypes.TypeError);

            int port = 22;
            var portVal = options.Get("port").ToObject();
            if (portVal is long p)
            {
                port = (int)p;
            }

            var password = options.Get("password").ToObject() as string;
            var privateKeyPath = options.Get("privateKey").ToObject() as string;
            var passphrase = options.Get("passphrase").ToObject() as string;

            if (password is null && privateKeyPath is null)
            {
                throw new RuntimeError("ssh.connect: must provide 'password' or 'privateKey'.", errorType: StashErrorTypes.ValueError);
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

                var instance = new StashInstance("SshConnection", new Dictionary<string, StashValue>
                {
                    ["host"] = StashValue.FromObj(host),
                    ["port"] = StashValue.FromInt((long)port),
                    ["username"] = StashValue.FromObj(username)
                });
                _clients.Add(instance, client);
                return StashValue.FromObj(instance);
            }
            catch (SshAuthenticationException e)
            {
                throw new RuntimeError($"ssh.connect: authentication failed — {e.Message}", errorType: StashErrorTypes.IOError);
            }
            catch (SshConnectionException e)
            {
                throw new RuntimeError($"ssh.connect: connection failed — {e.Message}", errorType: StashErrorTypes.IOError);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.connect: {e.Message}", errorType: StashErrorTypes.IOError);
            }
            catch (IOException e)
            {
                throw new RuntimeError($"ssh.connect: {e.Message}", errorType: StashErrorTypes.IOError);
            }
            catch (System.Net.Sockets.SocketException e)
            {
                throw new RuntimeError($"ssh.connect: {e.Message}", errorType: StashErrorTypes.IOError);
            }
        },
            returnType: "SshConnection",
            documentation: "Connects to an SSH server and returns an SshConnection instance.\n@param options Options dict with host, port (default 22), username, and password or privateKey\n@return An SshConnection struct with host, port, and username fields");

        // ssh.exec(conn, command) — Executes a command on the remote host.
        // Returns a CommandResult struct with stdout, stderr, and exitCode.
        ns.Function("exec", [Param("conn", "SshConnection"), Param("command", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            SshClient client = GetClient(args[0].ToObject(), "ssh.exec");
            var command = SvArgs.String(args, 1, "ssh.exec");

            try
            {
                SshCommand cmd = client.RunCommand(command);
                return StashValue.FromObj(RuntimeValues.CreateCommandResult(cmd.Result ?? "", cmd.Error ?? "", (long)(cmd.ExitStatus ?? -1)));
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.exec: {e.Message}", errorType: StashErrorTypes.IOError);
            }
        },
            returnType: "CommandResult",
            documentation: "Executes a single command on the remote server and returns the result.\n@param conn The SSH connection\n@param command The command to execute\n@return A CommandResult struct with stdout, stderr, and exitCode");

        // ssh.execAll(conn, commands) — Executes an array of commands sequentially.
        // Returns an array of CommandResult structs.
        ns.Function("execAll", [Param("conn", "SshConnection"), Param("commands", "array")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            SshClient client = GetClient(args[0].ToObject(), "ssh.execAll");
            var commands = SvArgs.StashList(args, 1, "ssh.execAll");

            try
            {
                var results = new List<StashValue>();
                foreach (StashValue item in commands)
                {
                    if (item.ToObject() is not string cmd)
                    {
                        throw new RuntimeError("ssh.execAll: all commands must be strings.", errorType: StashErrorTypes.TypeError);
                    }

                    SshCommand sshCmd = client.RunCommand(cmd);
                    results.Add(StashValue.FromObj(RuntimeValues.CreateCommandResult(sshCmd.Result ?? "", sshCmd.Error ?? "", (long)(sshCmd.ExitStatus ?? -1))));
                }
                return StashValue.FromObj(results);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.execAll: {e.Message}", errorType: StashErrorTypes.IOError);
            }
        },
            returnType: "array",
            documentation: "Executes multiple commands sequentially on the remote server and returns an array of results.\n@param conn The SSH connection\n@param commands An array of command strings to execute\n@return An array of CommandResult structs with stdout, stderr, and exitCode");

        // ssh.shell(conn, commands) — Runs commands through an interactive shell stream.
        // Useful for commands requiring a TTY or stateful sessions (sudo, etc.).
        // Returns the combined shell output as a string.
        ns.Function("shell", [Param("conn", "SshConnection"), Param("commands", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            SshClient client = GetClient(args[0].ToObject(), "ssh.shell");
            var commands = SvArgs.StashList(args, 1, "ssh.shell");

            try
            {
                using ShellStream stream = client.CreateShellStream("xterm", 80, 24, 800, 600, 4096);

                foreach (StashValue item in commands)
                {
                    if (item.ToObject() is not string cmd)
                    {
                        throw new RuntimeError("ssh.shell: all commands must be strings.", errorType: StashErrorTypes.TypeError);
                    }

                    stream.WriteLine(cmd);
                }

                // Allow time for commands to execute and output to arrive
                if (ctx.CancellationToken.CanBeCanceled)
                {
                    ctx.CancellationToken.WaitHandle.WaitOne(500);
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                }
                else
                {
                    Thread.Sleep(500);
                }
                return StashValue.FromObj(stream.Read());
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.shell: {e.Message}", errorType: StashErrorTypes.IOError);
            }
        },
            returnType: "string",
            documentation: "Opens an interactive shell session and executes commands in sequence, returning the combined output.\n@param conn The SSH connection\n@param commands An array of command strings to run in the shell\n@return The combined shell output as a string");

        // ssh.close(conn) — Disconnects and disposes the SSH connection.
        ns.Function("close", [Param("conn", "SshConnection")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            SshClient client = GetClient(args[0].ToObject(), "ssh.close");

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
                throw new RuntimeError($"ssh.close: {e.Message}", errorType: StashErrorTypes.IOError);
            }

            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Closes the SSH connection and releases all resources.\n@param conn The SSH connection to close\n@return null");

        // ssh.isConnected(conn) — Returns true if the SSH connection is still active.
        ns.Function("isConnected", [Param("conn", "SshConnection")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            SshClient client = GetClient(args[0].ToObject(), "ssh.isConnected");
            return StashValue.FromBool(client.IsConnected);
        },
            returnType: "bool",
            documentation: "Returns true if the SSH connection is still active.\n@param conn The SSH connection to check\n@return true if connected, false otherwise");

        // ssh.tunnel(conn, options) — Creates a local port forward (SSH tunnel).
        // Options dict: localPort (int, default 0 for auto), remoteHost (string, required), remotePort (int, required).
        // Returns an SshTunnel struct with localPort, remoteHost, and remotePort.
        ns.Function("tunnel", [Param("conn", "SshConnection"), Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            SshClient client = GetClient(args[0].ToObject(), "ssh.tunnel");
            var options = SvArgs.Dict(args, 1, "ssh.tunnel");

            var remoteHost = options.Get("remoteHost").ToObject() as string
                ?? throw new RuntimeError("ssh.tunnel: 'remoteHost' is required and must be a string.", errorType: StashErrorTypes.TypeError);

            var remotePortVal = options.Get("remotePort").ToObject();
            if (remotePortVal is not long remotePort)
            {
                throw new RuntimeError("ssh.tunnel: 'remotePort' is required and must be an integer.", errorType: StashErrorTypes.TypeError);
            }

            uint localPort = 0;
            var localPortVal = options.Get("localPort").ToObject();
            if (localPortVal is long lp)
            {
                localPort = (uint)lp;
            }

            try
            {
                var forward = new ForwardedPortLocal("127.0.0.1", localPort, remoteHost, (uint)remotePort);
                client.AddForwardedPort(forward);
                forward.Start();

                var instance = new StashInstance("SshTunnel", new Dictionary<string, StashValue>
                {
                    ["localPort"] = StashValue.FromInt((long)forward.BoundPort),
                    ["remoteHost"] = StashValue.FromObj(remoteHost),
                    ["remotePort"] = StashValue.FromInt(remotePort)
                });
                _tunnels.Add(instance, forward);
                _tunnelClients.Add(instance, client);
                return StashValue.FromObj(instance);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"ssh.tunnel: {e.Message}", errorType: StashErrorTypes.IOError);
            }
        },
            returnType: "SshTunnel",
            documentation: "Creates an SSH port-forwarding tunnel and returns an SshTunnel instance.\n@param conn The SSH connection\n@param options Options dict with localPort (default 0 for auto), remoteHost, and remotePort\n@return An SshTunnel struct with localPort, remoteHost, and remotePort fields");

        // ssh.closeTunnel(tunnel) — Closes an SSH tunnel (port forward).
        ns.Function("closeTunnel", [Param("tunnel", "SshTunnel")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var inst = SvArgs.Instance(args, 0, "SshTunnel", "ssh.closeTunnel");

            if (!_tunnels.TryGetValue(inst, out ForwardedPortLocal? forward))
            {
                throw new RuntimeError("ssh.closeTunnel: tunnel is invalid.", errorType: StashErrorTypes.TypeError);
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
                throw new RuntimeError($"ssh.closeTunnel: {e.Message}", errorType: StashErrorTypes.IOError);
            }

            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Closes an SSH port-forwarding tunnel.\n@param tunnel The SshTunnel instance to close\n@return null");

        ns.Struct("SshConnection", [
            new BuiltInField("host", "string"),
            new BuiltInField("port", "int"),
            new BuiltInField("username", "string"),
        ]);
        ns.Struct("SshTunnel", [
            new BuiltInField("localPort", "int"),
            new BuiltInField("remoteHost", "string"),
            new BuiltInField("remotePort", "int"),
        ]);

        return ns.Build();
    }

    /// <summary>
    /// Extracts and validates the <see cref="SshClient"/> from a connection instance.
    /// </summary>
    private static SshClient GetClient(object? arg, string funcName)
    {
        if (arg is not StashInstance inst || inst.TypeName != "SshConnection")
        {
            throw new RuntimeError($"First argument to '{funcName}' must be an SshConnection.", errorType: StashErrorTypes.TypeError);
        }

        if (!_clients.TryGetValue(inst, out SshClient? client))
        {
            throw new RuntimeError($"{funcName}: connection is invalid or closed.", errorType: StashErrorTypes.IOError);
        }

        return client;
    }
}
