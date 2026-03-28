namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>sftp</c> namespace built-in functions for SFTP file transfer operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides functions for transferring files and managing remote file systems via SFTP:
/// <c>sftp.connect</c>, <c>sftp.upload</c>, <c>sftp.download</c>, <c>sftp.readFile</c>,
/// <c>sftp.writeFile</c>, <c>sftp.list</c>, <c>sftp.delete</c>, <c>sftp.mkdir</c>,
/// <c>sftp.rmdir</c>, <c>sftp.exists</c>, <c>sftp.stat</c>, <c>sftp.chmod</c>,
/// <c>sftp.rename</c>, <c>sftp.close</c>, and <c>sftp.isConnected</c>.
/// </para>
/// <para>
/// Connection functions return an <c>SftpConnection</c> struct instance with <c>host</c>,
/// <c>port</c>, and <c>username</c> fields. This namespace is only registered when the
/// <see cref="StashCapabilities.Network"/> capability is enabled.
/// </para>
/// </remarks>
public static class SftpBuiltIns
{
    /// <summary>Maps SftpConnection instances to their underlying SftpClient. Uses weak references to allow GC.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StashInstance, SftpClient> _clients = new();

    /// <summary>
    /// Registers all <c>sftp</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("sftp");
        ns.RequiresCapability(StashCapabilities.Network);

        // sftp.connect(options) — Connects to a remote host via SFTP.
        // Options dict: host (string, required), port (int, default 22), username (string, required),
        // password (string), privateKey (string path), passphrase (string).
        // Returns an SftpConnection struct.
        ns.Function("connect", [Param("options", "dict")], (ctx, args) =>
        {
            var options = Args.Dict(args, 0, "sftp.connect");

            var host = options.Get("host") as string
                ?? throw new RuntimeError("sftp.connect: 'host' is required and must be a string.");
            var username = options.Get("username") as string
                ?? throw new RuntimeError("sftp.connect: 'username' is required and must be a string.");

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
                throw new RuntimeError("sftp.connect: must provide 'password' or 'privateKey'.");
            }

            try
            {
                SftpClient client;
                if (privateKeyPath is not null)
                {
                    privateKeyPath = ctx.ExpandTilde(privateKeyPath);
                    PrivateKeyFile keyFile = passphrase is not null
                        ? new PrivateKeyFile(privateKeyPath, passphrase)
                        : new PrivateKeyFile(privateKeyPath);
                    client = new SftpClient(host, port, username, keyFile);
                }
                else
                {
                    client = new SftpClient(host, port, username, password!);
                }

                client.Connect();

                var instance = new StashInstance("SftpConnection", new Dictionary<string, object?>
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
                throw new RuntimeError($"sftp.connect: authentication failed — {e.Message}");
            }
            catch (SshConnectionException e)
            {
                throw new RuntimeError($"sftp.connect: connection failed — {e.Message}");
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.connect: {e.Message}");
            }
            catch (IOException e)
            {
                throw new RuntimeError($"sftp.connect: {e.Message}");
            }
            catch (System.Net.Sockets.SocketException e)
            {
                throw new RuntimeError($"sftp.connect: {e.Message}");
            }
        });

        // sftp.upload(conn, localPath, remotePath) — Uploads a local file to the remote host.
        ns.Function("upload", [Param("conn", "SftpConnection"), Param("localPath", "string"), Param("remotePath", "string")], (ctx, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.upload");
            var localPath = Args.String(args, 1, "sftp.upload");
            var remotePath = Args.String(args, 2, "sftp.upload");

            localPath = ctx.ExpandTilde(localPath);

            try
            {
                using FileStream fileStream = File.OpenRead(localPath);
                client.UploadFile(fileStream, remotePath);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.upload: {e.Message}");
            }
            catch (IOException e)
            {
                throw new RuntimeError($"sftp.upload: {e.Message}");
            }

            return null;
        });

        // sftp.download(conn, remotePath, localPath) — Downloads a remote file to the local host.
        ns.Function("download", [Param("conn", "SftpConnection"), Param("remotePath", "string"), Param("localPath", "string")], (ctx, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.download");
            var remotePath = Args.String(args, 1, "sftp.download");
            var localPath = Args.String(args, 2, "sftp.download");

            localPath = ctx.ExpandTilde(localPath);

            try
            {
                using FileStream fileStream = File.Create(localPath);
                client.DownloadFile(remotePath, fileStream);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.download: {e.Message}");
            }
            catch (IOException e)
            {
                throw new RuntimeError($"sftp.download: {e.Message}");
            }

            return null;
        });

        // sftp.readFile(conn, remotePath) — Reads a remote file and returns its contents as a string.
        ns.Function("readFile", [Param("conn", "SftpConnection"), Param("remotePath", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.readFile");
            var remotePath = Args.String(args, 1, "sftp.readFile");

            try
            {
                using var memStream = new MemoryStream();
                client.DownloadFile(remotePath, memStream);
                return Encoding.UTF8.GetString(memStream.ToArray());
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.readFile: {e.Message}");
            }
        });

        // sftp.writeFile(conn, remotePath, content) — Writes a string to a remote file.
        ns.Function("writeFile", [Param("conn", "SftpConnection"), Param("remotePath", "string"), Param("content", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.writeFile");
            var remotePath = Args.String(args, 1, "sftp.writeFile");
            var content = Args.String(args, 2, "sftp.writeFile");

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                using var memStream = new MemoryStream(bytes);
                client.UploadFile(memStream, remotePath);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.writeFile: {e.Message}");
            }

            return null;
        });

        // sftp.list(conn, remotePath) — Lists entries in a remote directory.
        // Returns an array of dicts with name, size, isDir, and modified fields.
        ns.Function("list", [Param("conn", "SftpConnection"), Param("remotePath", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.list");
            var remotePath = Args.String(args, 1, "sftp.list");

            try
            {
                IEnumerable<ISftpFile> entries = client.ListDirectory(remotePath);
                var results = new List<object?>();

                foreach (ISftpFile entry in entries)
                {
                    if (entry.Name == "." || entry.Name == "..")
                    {
                        continue;
                    }

                    var dict = new StashDictionary();
                    dict.Set("name", entry.Name);
                    dict.Set("size", entry.Length);
                    dict.Set("isDir", entry.IsDirectory);
                    dict.Set("modified", entry.LastWriteTime.ToString("o"));
                    results.Add(dict);
                }

                return results;
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.list: {e.Message}");
            }
        });

        // sftp.delete(conn, remotePath) — Deletes a remote file.
        ns.Function("delete", [Param("conn", "SftpConnection"), Param("remotePath", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.delete");
            var remotePath = Args.String(args, 1, "sftp.delete");

            try
            {
                client.Delete(remotePath);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.delete: {e.Message}");
            }

            return null;
        });

        // sftp.mkdir(conn, remotePath) — Creates a remote directory.
        ns.Function("mkdir", [Param("conn", "SftpConnection"), Param("remotePath", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.mkdir");
            var remotePath = Args.String(args, 1, "sftp.mkdir");

            try
            {
                client.CreateDirectory(remotePath);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.mkdir: {e.Message}");
            }

            return null;
        });

        // sftp.rmdir(conn, remotePath) — Removes a remote directory.
        ns.Function("rmdir", [Param("conn", "SftpConnection"), Param("remotePath", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.rmdir");
            var remotePath = Args.String(args, 1, "sftp.rmdir");

            try
            {
                client.DeleteDirectory(remotePath);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.rmdir: {e.Message}");
            }

            return null;
        });

        // sftp.exists(conn, remotePath) — Checks if a remote path exists.
        ns.Function("exists", [Param("conn", "SftpConnection"), Param("remotePath", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.exists");
            var remotePath = Args.String(args, 1, "sftp.exists");

            try
            {
                return client.Exists(remotePath);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.exists: {e.Message}");
            }
        });

        // sftp.stat(conn, remotePath) — Gets file attributes for a remote path.
        // Returns a dict with size, isDir, modified, and permissions fields.
        ns.Function("stat", [Param("conn", "SftpConnection"), Param("remotePath", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.stat");
            var remotePath = Args.String(args, 1, "sftp.stat");

            try
            {
                SftpFileAttributes attrs = client.GetAttributes(remotePath);
                var dict = new StashDictionary();
                dict.Set("size", attrs.Size);
                dict.Set("isDir", attrs.IsDirectory);
                dict.Set("modified", attrs.LastWriteTime.ToString("o"));

                // Format permissions as octal string (e.g., "755")
                int mode = 0;
                if (attrs.OwnerCanRead)
                {
                    mode += 400;
                }

                if (attrs.OwnerCanWrite)
                {
                    mode += 200;
                }

                if (attrs.OwnerCanExecute)
                {
                    mode += 100;
                }

                if (attrs.GroupCanRead)
                {
                    mode += 40;
                }

                if (attrs.GroupCanWrite)
                {
                    mode += 20;
                }

                if (attrs.GroupCanExecute)
                {
                    mode += 10;
                }

                if (attrs.OthersCanRead)
                {
                    mode += 4;
                }

                if (attrs.OthersCanWrite)
                {
                    mode += 2;
                }

                if (attrs.OthersCanExecute)
                {
                    mode += 1;
                }

                dict.Set("permissions", mode.ToString());

                return dict;
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.stat: {e.Message}");
            }
        });

        // sftp.chmod(conn, remotePath, mode) — Changes file permissions on a remote path.
        // Mode is an integer representing octal permissions (e.g., 755).
        ns.Function("chmod", [Param("conn", "SftpConnection"), Param("remotePath", "string"), Param("mode", "int")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.chmod");
            var remotePath = Args.String(args, 1, "sftp.chmod");
            var modeDecimal = Args.Long(args, 2, "sftp.chmod");

            // Convert decimal representation of octal (e.g., 755) to actual octal value
            short octalMode = ParseOctalMode(modeDecimal, "sftp.chmod");

            try
            {
                client.ChangePermissions(remotePath, octalMode);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.chmod: {e.Message}");
            }

            return null;
        });

        // sftp.rename(conn, oldPath, newPath) — Renames or moves a remote file.
        ns.Function("rename", [Param("conn", "SftpConnection"), Param("oldPath", "string"), Param("newPath", "string")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.rename");
            var oldPath = Args.String(args, 1, "sftp.rename");
            var newPath = Args.String(args, 2, "sftp.rename");

            try
            {
                client.RenameFile(oldPath, newPath);
            }
            catch (SshException e)
            {
                throw new RuntimeError($"sftp.rename: {e.Message}");
            }

            return null;
        });

        // sftp.close(conn) — Disconnects and disposes the SFTP connection.
        ns.Function("close", [Param("conn", "SftpConnection")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.close");

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
                throw new RuntimeError($"sftp.close: {e.Message}");
            }

            return null;
        });

        // sftp.isConnected(conn) — Returns true if the SFTP connection is still active.
        ns.Function("isConnected", [Param("conn", "SftpConnection")], (_, args) =>
        {
            SftpClient client = GetClient(args[0], "sftp.isConnected");
            return client.IsConnected;
        });

        ns.Struct("SftpConnection", [
            new BuiltInField("host", "string"),
            new BuiltInField("port", "int"),
            new BuiltInField("username", "string"),
        ]);

        return ns.Build();
    }

    /// <summary>
    /// Extracts and validates the <see cref="SftpClient"/> from a connection instance.
    /// </summary>
    private static SftpClient GetClient(object? arg, string funcName)
    {
        if (arg is not StashInstance inst || inst.TypeName != "SftpConnection")
        {
            throw new RuntimeError($"First argument to '{funcName}' must be an SftpConnection.");
        }

        if (!_clients.TryGetValue(inst, out SftpClient? client))
        {
            throw new RuntimeError($"{funcName}: connection is invalid or closed.");
        }

        return client;
    }

    /// <summary>
    /// Parses a decimal representation of an octal permission mode (e.g., 755) into the actual octal value.
    /// </summary>
    private static short ParseOctalMode(long modeDecimal, string funcName)
    {
        string modeStr = modeDecimal.ToString();
        foreach (char c in modeStr)
        {
            if (c < '0' || c > '7')
            {
                throw new RuntimeError($"{funcName}: invalid permission mode '{modeDecimal}'. Each digit must be 0-7.");
            }
        }

        try
        {
            return Convert.ToInt16(modeStr, 8);
        }
        catch (FormatException)
        {
            throw new RuntimeError($"{funcName}: invalid permission mode '{modeDecimal}'.");
        }
    }
}
