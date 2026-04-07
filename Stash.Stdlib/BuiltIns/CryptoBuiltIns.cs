namespace Stash.Stdlib.BuiltIns;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>Registers the <c>crypto</c> namespace providing hashing, HMAC, UUID, and random byte functions.</summary>
public static class CryptoBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("crypto");

        ns.Function("md5", [Param("data", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "crypto.md5");

            return StashValue.FromObj(HashToHex(MD5.HashData(Encoding.UTF8.GetBytes(s))));
        },
            returnType: "string",
            documentation: "Computes the MD5 hash of a string.\n@param data The string to hash\n@return The hash as a lowercase hexadecimal string");

        // crypto.sha1(input) — Returns the SHA-1 hash of the input string as a lowercase hex string.
        ns.Function("sha1", [Param("data", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                var s = SvArgs.String(args, 0, "crypto.sha1");

                return StashValue.FromObj(HashToHex(SHA1.HashData(Encoding.UTF8.GetBytes(s))));
            },
            returnType: "string",
            documentation: "Computes the SHA-1 hash of a string.\n@param data The string to hash\n@return The hash as a lowercase hexadecimal string"
        );

        // crypto.sha256(input) — Returns the SHA-256 hash of the input string as a lowercase hex string.
        ns.Function("sha256", [Param("data", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "crypto.sha256");

            return StashValue.FromObj(HashToHex(SHA256.HashData(Encoding.UTF8.GetBytes(s))));
        },
            returnType: "string",
            documentation: "Computes the SHA-256 hash of a string.\n@param data The string to hash\n@return The hash as a lowercase hexadecimal string");

        // crypto.sha512(input) — Returns the SHA-512 hash of the input string as a lowercase hex string.
        ns.Function("sha512", [Param("data", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "crypto.sha512");

            return StashValue.FromObj(HashToHex(SHA512.HashData(Encoding.UTF8.GetBytes(s))));
        },
            returnType: "string",
            documentation: "Computes the SHA-512 hash of a string.\n@param data The string to hash\n@return The hash as a lowercase hexadecimal string");

        // crypto.hmac(algo, key, data) — Computes the HMAC of 'data' using 'key' with the specified algorithm.
        //   'algo' must be one of: "md5", "sha1", "sha256", "sha512". Returns a lowercase hex string.
        ns.Function("hmac", [Param("algo", "string"), Param("key", "string"), Param("data", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var algo = SvArgs.String(args, 0, "crypto.hmac");
            var key = SvArgs.String(args, 1, "crypto.hmac");
            var data = SvArgs.String(args, 2, "crypto.hmac");

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

            return StashValue.FromObj(HashToHex(hash));
        },
            returnType: "string",
            documentation: "Computes an HMAC signature using the specified algorithm.\n@param algo The hash algorithm: \"md5\", \"sha1\", \"sha256\", or \"sha512\"\n@param key The secret key\n@param data The data to sign\n@return The HMAC as a lowercase hexadecimal string");

        // crypto.hashFile(path [, algo]) — Hashes the contents of a file using the specified algorithm (default: "sha256").
        //   Returns the hash as a lowercase hex string.
        ns.Function("hashFile", [Param("path", "string"), Param("algo", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2) throw new RuntimeError("'crypto.hashFile' expects 1 or 2 arguments.");
            var path = SvArgs.String(args, 0, "crypto.hashFile");

            var algo = "sha256";
            if (args.Length == 2)
            {
                algo = SvArgs.String(args, 1, "crypto.hashFile");
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

            return StashValue.FromObj(HashToHex(ComputeHash(algo, fileBytes)));
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Computes the hash of a file's contents.\n@param path The file path to hash\n@param algo Optional hash algorithm (default: \"sha256\"). One of \"md5\", \"sha1\", \"sha256\", \"sha512\"\n@return The hash as a lowercase hexadecimal string");

        // crypto.uuid() — Generates and returns a new random UUID (version 4) as a lowercase hyphenated string.
        ns.Function("uuid", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromObj(Guid.NewGuid().ToString());
        },
            returnType: "string",
            documentation: "Generates a random UUID v4 string.\n@return A UUID string in standard format (e.g., \"550e8400-e29b-41d4-a716-446655440000\")");

        // crypto.randomBytes(n) — Generates 'n' cryptographically secure random bytes and returns them as a lowercase hex string.
        ns.Function("randomBytes", [Param("n", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var n = SvArgs.Long(args, 0, "crypto.randomBytes");

            if (n <= 0)
            {
                throw new RuntimeError("Argument to 'crypto.randomBytes' must be greater than 0.");
            }

            if (n > int.MaxValue)
            {
                throw new RuntimeError("Argument to 'crypto.randomBytes' is too large.");
            }

            var bytes = RandomNumberGenerator.GetBytes((int)n);
            return StashValue.FromObj(HashToHex(bytes));
        },
            returnType: "string",
            documentation: "Generates cryptographically secure random bytes.\n@param n The number of random bytes to generate (must be > 0)\n@return The random bytes as a lowercase hexadecimal string");

        return ns.Build();
    }

    /// <summary>
    /// Converts a byte array to a lowercase hexadecimal string.
    /// </summary>
    /// <param name="hash">The byte array to convert.</param>
    /// <returns>A lowercase hex string representation of <paramref name="hash"/>.</returns>
    private static string HashToHex(byte[] hash) =>
        Convert.ToHexString(hash).ToLowerInvariant();

    /// <summary>
    /// Computes a hash of the given data using the specified algorithm name.
    /// </summary>
    /// <param name="algo">The algorithm name: <c>md5</c>, <c>sha1</c>, <c>sha256</c>, or <c>sha512</c>.</param>
    /// <param name="data">The raw bytes to hash.</param>
    /// <returns>The computed hash bytes.</returns>
    /// <exception cref="RuntimeError">Thrown when <paramref name="algo"/> is not a recognized algorithm.</exception>
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
