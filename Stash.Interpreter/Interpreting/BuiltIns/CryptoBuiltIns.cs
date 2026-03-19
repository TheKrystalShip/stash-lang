namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Stash.Interpreting.Types;

/// <summary>Registers the <c>crypto</c> namespace providing hashing, HMAC, UUID, and random byte functions.</summary>
public static class CryptoBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var crypto = new StashNamespace("crypto");

        crypto.Define("md5", new BuiltInFunction("crypto.md5", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'crypto.md5' must be a string.");
            }

            return HashToHex(MD5.HashData(Encoding.UTF8.GetBytes(s)));
        }));

        crypto.Define("sha1", new BuiltInFunction("crypto.sha1", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'crypto.sha1' must be a string.");
            }

            return HashToHex(SHA1.HashData(Encoding.UTF8.GetBytes(s)));
        }));

        crypto.Define("sha256", new BuiltInFunction("crypto.sha256", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'crypto.sha256' must be a string.");
            }

            return HashToHex(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
        }));

        crypto.Define("sha512", new BuiltInFunction("crypto.sha512", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'crypto.sha512' must be a string.");
            }

            return HashToHex(SHA512.HashData(Encoding.UTF8.GetBytes(s)));
        }));

        crypto.Define("hmac", new BuiltInFunction("crypto.hmac", 3, (_, args) =>
        {
            if (args[0] is not string algo)
            {
                throw new RuntimeError("First argument to 'crypto.hmac' must be a string (algorithm).");
            }

            if (args[1] is not string key)
            {
                throw new RuntimeError("Second argument to 'crypto.hmac' must be a string (key).");
            }

            if (args[2] is not string data)
            {
                throw new RuntimeError("Third argument to 'crypto.hmac' must be a string (data).");
            }

            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);

            using HMAC hmac = algo.ToLowerInvariant() switch
            {
                "md5"    => (HMAC)new HMACMD5(keyBytes),
                "sha1"   => new HMACSHA1(keyBytes),
                "sha256" => new HMACSHA256(keyBytes),
                "sha512" => new HMACSHA512(keyBytes),
                _        => throw new RuntimeError("crypto.hmac: unknown algorithm '" + algo + "'. Expected 'md5', 'sha1', 'sha256', or 'sha512'.")
            };
            byte[] hash = hmac.ComputeHash(dataBytes);

            return HashToHex(hash);
        }));

        crypto.Define("hashFile", new BuiltInFunction("crypto.hashFile", -1, (_, args) =>
        {
            if (args.Count < 1 || args.Count > 2)
            {
                throw new RuntimeError("'crypto.hashFile' expects 1 or 2 arguments: path [, algo].");
            }

            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'crypto.hashFile' must be a string (file path).");
            }

            var algo = "sha256";
            if (args.Count == 2)
            {
                if (args[1] is not string a)
                {
                    throw new RuntimeError("Second argument to 'crypto.hashFile' must be a string (algorithm).");
                }

                algo = a;
            }

            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(path);
            }
            catch (FileNotFoundException)
            {
                throw new RuntimeError($"File not found: '{path}'.");
            }
            catch (IOException ex)
            {
                throw new RuntimeError($"Error reading file '{path}': {ex.Message}");
            }

            return HashToHex(ComputeHash(algo, fileBytes));
        }));

        crypto.Define("uuid", new BuiltInFunction("crypto.uuid", 0, (_, _) =>
        {
            return Guid.NewGuid().ToString();
        }));

        crypto.Define("randomBytes", new BuiltInFunction("crypto.randomBytes", 1, (_, args) =>
        {
            if (args[0] is not long n)
            {
                throw new RuntimeError("First argument to 'crypto.randomBytes' must be an integer.");
            }

            if (n <= 0)
            {
                throw new RuntimeError("Argument to 'crypto.randomBytes' must be greater than 0.");
            }

            if (n > int.MaxValue)
            {
                throw new RuntimeError("Argument to 'crypto.randomBytes' is too large.");
            }

            var bytes = RandomNumberGenerator.GetBytes((int)n);
            return HashToHex(bytes);
        }));

        globals.Define("crypto", crypto);
    }

    private static string HashToHex(byte[] hash) =>
        Convert.ToHexString(hash).ToLowerInvariant();

    private static byte[] ComputeHash(string algo, byte[] data) =>
        algo.ToLowerInvariant() switch
        {
            "md5"    => MD5.HashData(data),
            "sha1"   => SHA1.HashData(data),
            "sha256" => SHA256.HashData(data),
            "sha512" => SHA512.HashData(data),
            _        => throw new RuntimeError($"Unknown hash algorithm '{algo}'. Supported: md5, sha1, sha256, sha512.")
        };
}
