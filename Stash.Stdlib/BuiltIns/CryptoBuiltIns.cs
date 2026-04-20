namespace Stash.Stdlib.BuiltIns;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
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

        // crypto.randomBytes(n [, encoding]) — Generates 'n' cryptographically secure random bytes.
        //   When called with 1 argument, returns byte[] directly.
        //   When called with 2 arguments, 'encoding' can be "hex", "base64", or "raw" (Latin-1 byte string).
        ns.Function("randomBytes", [Param("n", "int"), Param("encoding", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'crypto.randomBytes' requires 1 or 2 arguments.");

            var n = SvArgs.Long(args, 0, "crypto.randomBytes");

            if (n <= 0)
                throw new RuntimeError("Argument to 'crypto.randomBytes' must be greater than 0.");

            if (n > int.MaxValue)
                throw new RuntimeError("Argument to 'crypto.randomBytes' is too large.");

            var bytes = RandomNumberGenerator.GetBytes((int)n);

            // No encoding param → return byte[] directly
            if (args.Length == 1)
                return StashValue.FromObj(new StashByteArray(bytes));

            // With encoding param → return encoded string (backward compat)
            string encoding = SvArgs.String(args, 1, "crypto.randomBytes");
            string result = encoding.ToLowerInvariant() switch
            {
                "hex"    => HashToHex(bytes),
                "base64" => Convert.ToBase64String(bytes),
                "raw"    => Encoding.Latin1.GetString(bytes),
                _        => throw new RuntimeError($"Unknown encoding '{encoding}' in 'crypto.randomBytes'. Expected \"hex\", \"base64\", or \"raw\".")
            };
            return StashValue.FromObj(result);
        },
            returnType: "byte[]",
            isVariadic: true,
            documentation: "Generates cryptographically secure random bytes. Returns byte[] when called with 1 argument, or an encoded string when called with 2.\n@param n The number of random bytes to generate (must be > 0)\n@param encoding Optional output encoding: \"hex\", \"base64\", or \"raw\" (Latin-1). If omitted, returns byte[]\n@return A byte[] or encoded string depending on arguments");

        ns.Function("md5Bytes", [Param("data", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "crypto.md5Bytes");
            return StashValue.FromObj(new StashByteArray(MD5.HashData(ba.AsSpan())));
        },
            returnType: "byte[]",
            documentation: "Computes the MD5 hash of a byte array.\n@param data The byte array to hash\n@return The 16-byte hash as a byte array");

        ns.Function("sha1Bytes", [Param("data", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "crypto.sha1Bytes");
            return StashValue.FromObj(new StashByteArray(SHA1.HashData(ba.AsSpan())));
        },
            returnType: "byte[]",
            documentation: "Computes the SHA-1 hash of a byte array.\n@param data The byte array to hash\n@return The 20-byte hash as a byte array");

        ns.Function("sha256Bytes", [Param("data", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "crypto.sha256Bytes");
            return StashValue.FromObj(new StashByteArray(SHA256.HashData(ba.AsSpan())));
        },
            returnType: "byte[]",
            documentation: "Computes the SHA-256 hash of a byte array.\n@param data The byte array to hash\n@return The 32-byte hash as a byte array");

        ns.Function("sha512Bytes", [Param("data", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "crypto.sha512Bytes");
            return StashValue.FromObj(new StashByteArray(SHA512.HashData(ba.AsSpan())));
        },
            returnType: "byte[]",
            documentation: "Computes the SHA-512 hash of a byte array.\n@param data The byte array to hash\n@return The 64-byte hash as a byte array");

        ns.Function("hmacBytes", [Param("algo", "string"), Param("key", "byte[]"), Param("data", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string algo = SvArgs.String(args, 0, "crypto.hmacBytes");
            StashByteArray key = SvArgs.ByteArray(args, 1, "crypto.hmacBytes");
            StashByteArray data = SvArgs.ByteArray(args, 2, "crypto.hmacBytes");
            byte[] keyArr = key.AsSpan().ToArray();
            byte[] dataArr = data.AsSpan().ToArray();
            using HMAC hmac = algo.ToLowerInvariant() switch
            {
                "md5"    => (HMAC)new HMACMD5(keyArr),
                "sha1"   => new HMACSHA1(keyArr),
                "sha256" => new HMACSHA256(keyArr),
                "sha512" => new HMACSHA512(keyArr),
                _        => throw new RuntimeError("crypto.hmacBytes: unknown algorithm '" + algo + "'. Expected 'md5', 'sha1', 'sha256', or 'sha512'.")
            };
            return StashValue.FromObj(new StashByteArray(hmac.ComputeHash(dataArr)));
        },
            returnType: "byte[]",
            documentation: "Computes an HMAC signature using byte arrays for key and data.\n@param algo The hash algorithm: \"md5\", \"sha1\", \"sha256\", or \"sha512\"\n@param key The secret key as byte[]\n@param data The data to sign as byte[]\n@return The HMAC as a byte array");

        // crypto.generateKey(bits?) — Generates a cryptographically secure random encryption key.
        //   'bits' defaults to 256. Accepts 128, 192, or 256. Returns a lowercase hex-encoded string.
        ns.Function("generateKey", [Param("bits", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            int bits = 256;
            if (args.Length == 1)
            {
                long rawBits = SvArgs.Long(args, 0, "crypto.generateKey");
                if (rawBits != 128 && rawBits != 192 && rawBits != 256)
                    throw new RuntimeError($"'crypto.generateKey' accepts 128, 192, or 256 bits, got {rawBits}.");
                bits = (int)rawBits;
            }
            return StashValue.FromObj(HashToHex(RandomNumberGenerator.GetBytes(bits / 8)));
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Generates a cryptographically secure random encryption key.\n@param bits Optional key size in bits: 128, 192, or 256 (default: 256)\n@return The key as a lowercase hexadecimal string");

        // crypto.encrypt(data, key, options?) — Encrypts data using AES-256-GCM.
        //   'data' can be a string or byte[]. 'key' must be a 32-byte hex string or byte[].
        //   Returns a dict: { ciphertext: "hex", iv: "hex", tag: "hex" }.
        ns.Function("encrypt", [Param("data", "string"), Param("key", "string"), Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'crypto.encrypt' expects 2 or 3 arguments.");

            byte[] plaintext;
            StashValue dataArg = args[0];
            if (dataArg.IsObj && dataArg.AsObj is string dataStr)
                plaintext = Encoding.UTF8.GetBytes(dataStr);
            else if (dataArg.IsObj && dataArg.AsObj is StashByteArray dataBa)
                plaintext = dataBa.AsSpan().ToArray();
            else
                throw new RuntimeError("1st argument to 'crypto.encrypt' must be a string or byte[].");

            byte[] keyBytes = ExtractKeyBytes(args[1], "crypto.encrypt");

            if (keyBytes.Length != 32)
                throw new RuntimeError($"'crypto.encrypt' requires a 32-byte (256-bit) key, got {keyBytes.Length} bytes.");

            byte[] iv = RandomNumberGenerator.GetBytes(12);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using var aes = new AesGcm(keyBytes, 16);
            aes.Encrypt(iv, plaintext, ciphertext, tag);

            var result = new StashDictionary();
            result.Set("ciphertext", StashValue.FromObj(HashToHex(ciphertext)));
            result.Set("iv", StashValue.FromObj(HashToHex(iv)));
            result.Set("tag", StashValue.FromObj(HashToHex(tag)));
            return StashValue.FromObj(result);
        },
            returnType: "dict",
            isVariadic: true,
            documentation: "Encrypts data using AES-256-GCM.\n@param data The plaintext to encrypt — string or byte[]\n@param key A 32-byte (256-bit) key as a hex string or byte[]\n@param options Reserved for future use (optional)\n@return A dictionary with fields: ciphertext (hex), iv (hex), tag (hex)");

        // crypto.decrypt(ciphertext, key, options?) — Decrypts AES-256-GCM encrypted data.
        //   'ciphertext' can be a dict { ciphertext, iv, tag } or a combined hex string (iv+tag+ciphertext).
        //   'key' must be the same 32-byte hex string or byte[] used for encryption.
        //   Returns the decrypted UTF-8 string. Throws if authentication fails.
        ns.Function("decrypt", [Param("ciphertext", "string"), Param("key", "string"), Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'crypto.decrypt' expects 2 or 3 arguments.");

            byte[] keyBytes = ExtractKeyBytes(args[1], "crypto.decrypt");

            if (keyBytes.Length != 32)
                throw new RuntimeError($"'crypto.decrypt' requires a 32-byte (256-bit) key, got {keyBytes.Length} bytes.");

            byte[] cipherBytes;
            byte[] ivBytes;
            byte[] tagBytes;

            StashValue ctArg = args[0];
            if (ctArg.IsObj && ctArg.AsObj is StashDictionary ctDict)
            {
                StashValue ctVal = ctDict.Get("ciphertext");
                StashValue ivVal = ctDict.Get("iv");
                StashValue tagVal = ctDict.Get("tag");
                if (ctVal.IsNull || ivVal.IsNull || tagVal.IsNull)
                    throw new RuntimeError("'crypto.decrypt' dict must contain 'ciphertext', 'iv', and 'tag' fields.");
                if (ctVal.AsObj is not string ctHexField)
                    throw new RuntimeError("'ciphertext' field in 'crypto.decrypt' dict must be a hex string.");
                if (ivVal.AsObj is not string ivHexField)
                    throw new RuntimeError("'iv' field in 'crypto.decrypt' dict must be a hex string.");
                if (tagVal.AsObj is not string tagHexField)
                    throw new RuntimeError("'tag' field in 'crypto.decrypt' dict must be a hex string.");
                cipherBytes = HexToBytes(ctHexField, "crypto.decrypt");
                ivBytes = HexToBytes(ivHexField, "crypto.decrypt");
                tagBytes = HexToBytes(tagHexField, "crypto.decrypt");
            }
            else if (ctArg.IsObj && ctArg.AsObj is string combinedHex)
            {
                // Combined format: iv (24 hex chars) + tag (32 hex chars) + ciphertext (rest)
                if (combinedHex.Length < 56)
                    throw new RuntimeError("'crypto.decrypt' combined hex string is too short (must encode at least iv + tag).");
                ivBytes = HexToBytes(combinedHex[..24], "crypto.decrypt");
                tagBytes = HexToBytes(combinedHex[24..56], "crypto.decrypt");
                cipherBytes = HexToBytes(combinedHex[56..], "crypto.decrypt");
            }
            else
            {
                throw new RuntimeError("1st argument to 'crypto.decrypt' must be a dictionary { ciphertext, iv, tag } or a combined hex string.");
            }

            byte[] plaintext = new byte[cipherBytes.Length];
            try
            {
                using var aes = new AesGcm(keyBytes, 16);
                aes.Decrypt(ivBytes, cipherBytes, tagBytes, plaintext);
            }
            catch (CryptographicException)
            {
                throw new RuntimeError("'crypto.decrypt' failed: authentication tag verification failed.");
            }

            return StashValue.FromObj(Encoding.UTF8.GetString(plaintext));
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Decrypts data encrypted with AES-256-GCM.\n@param ciphertext A dictionary { ciphertext, iv, tag } with hex fields, or a combined hex string (iv+tag+ciphertext)\n@param key The 32-byte (256-bit) decryption key as a hex string or byte[]\n@param options Reserved for future use (optional)\n@return The decrypted plaintext string");

        return ns.Build();
    }

    /// <summary>
    /// Extracts key bytes from either a hex string or a <see cref="StashByteArray"/>.
    /// </summary>
    private static byte[] ExtractKeyBytes(StashValue keyArg, string funcName)
    {
        if (keyArg.IsObj && keyArg.AsObj is string keyHex)
            return HexToBytes(keyHex, funcName);
        if (keyArg.IsObj && keyArg.AsObj is StashByteArray keyBa)
            return keyBa.AsSpan().ToArray();
        throw new RuntimeError($"2nd argument to '{funcName}' must be a hex string or byte[].");
    }

    /// <summary>
    /// Converts a lowercase or uppercase hexadecimal string to a byte array.
    /// </summary>
    /// <param name="hex">The hex string to convert.</param>
    /// <param name="funcName">The calling function name used in error messages.</param>
    /// <exception cref="RuntimeError">Thrown when <paramref name="hex"/> has odd length or invalid characters.</exception>
    private static byte[] HexToBytes(string hex, string funcName)
    {
        if (hex.Length % 2 != 0)
            throw new RuntimeError($"Invalid hex string in '{funcName}': length must be even.");
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            throw new RuntimeError($"Invalid hex string in '{funcName}': contains non-hexadecimal characters.");
        }
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
