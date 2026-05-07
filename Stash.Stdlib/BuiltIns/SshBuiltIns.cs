namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;

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
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class SshBuiltIns
{
    /// <summary>Maps SshConnection instances to their underlying SshClient. Uses weak references to allow GC.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StashInstance, SshClient> _clients = new();

    /// <summary>Maps SshTunnel instances to their forwarded port.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StashInstance, ForwardedPortLocal> _tunnels = new();

    /// <summary>Maps SshTunnel instances to the SshClient that owns the tunnel.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StashInstance, SshClient> _tunnelClients = new();

    // ── Struct declarations ───────────────────────────────────────────────────

    /// <summary>An active SSH connection to a remote host.</summary>
    [StashStruct]
    public sealed record SshConnection(string Host, long Port, string Username);

    /// <summary>An active SSH port-forwarding tunnel.</summary>
    [StashStruct]
    public sealed record SshTunnel(long LocalPort, string RemoteHost, long RemotePort);

    // ── Functions ─────────────────────────────────────────────────────────────

    /// <summary>Connects to an SSH server and returns an SshConnection instance.</summary>
    /// <param name="options">Options dict with host, port (default 22), username, and password or privateKey</param>
    /// <returns>An SshConnection struct with host, port, and username fields</returns>
    [StashFn(Raw = true, ReturnType = "SshConnection")]
    private static StashValue Connect(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Executes a single command on the remote server and returns the result.</summary>
    /// <param name="conn">The SSH connection</param>
    /// <param name="command">The command to execute</param>
    /// <returns>A CommandResult struct with stdout, stderr, and exitCode</returns>
    [StashFn(Raw = true, ReturnType = "CommandResult")]
    private static StashValue Exec(IInterpreterContext _, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Executes multiple commands sequentially on the remote server and returns an array of results.</summary>
    /// <param name="conn">The SSH connection</param>
    /// <param name="commands">An array of command strings to execute</param>
    /// <returns>An array of CommandResult structs with stdout, stderr, and exitCode</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue ExecAll(IInterpreterContext _, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Opens an interactive shell session and executes commands in sequence, returning the combined output.</summary>
    /// <param name="conn">The SSH connection</param>
    /// <param name="commands">An array of command strings to run in the shell</param>
    /// <returns>The combined shell output as a string</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Shell(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Closes the SSH connection and releases all resources.</summary>
    /// <param name="conn">The SSH connection to close</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Close(IInterpreterContext _, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Returns true if the SSH connection is still active.</summary>
    /// <param name="conn">The SSH connection to check</param>
    /// <returns>true if connected, false otherwise</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue IsConnected(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        SshClient client = GetClient(args[0].ToObject(), "ssh.isConnected");
        return StashValue.FromBool(client.IsConnected);
    }

    /// <summary>Creates an SSH port-forwarding tunnel and returns an SshTunnel instance.</summary>
    /// <param name="conn">The SSH connection</param>
    /// <param name="options">Options dict with localPort (default 0 for auto), remoteHost, and remotePort</param>
    /// <returns>An SshTunnel struct with localPort, remoteHost, and remotePort fields</returns>
    [StashFn(Raw = true, ReturnType = "SshTunnel")]
    private static StashValue Tunnel(IInterpreterContext _, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Closes an SSH port-forwarding tunnel.</summary>
    /// <param name="tunnel">The SshTunnel instance to close</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue CloseTunnel(IInterpreterContext _, ReadOnlySpan<StashValue> args)
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
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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
