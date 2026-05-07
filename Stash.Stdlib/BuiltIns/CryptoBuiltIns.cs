namespace Stash.Stdlib.BuiltIns;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>Registers the <c>crypto</c> namespace providing hashing, HMAC, UUID, and random byte functions.</summary>
[StashNamespace]
public static partial class CryptoBuiltIns
{
    /// <summary>Computes the MD5 hash of a string.</summary>
    /// <param name="data">The string to hash</param>
    /// <returns>The hash as a lowercase hexadecimal string</returns>
    [StashFn]
    private static string Md5(string data)
    {
        return HashToHex(MD5.HashData(Encoding.UTF8.GetBytes(data)));
    }

    /// <summary>Computes the SHA-1 hash of a string.</summary>
    /// <param name="data">The string to hash</param>
    /// <returns>The hash as a lowercase hexadecimal string</returns>
    [StashFn]
    private static string Sha1(string data)
    {
        return HashToHex(SHA1.HashData(Encoding.UTF8.GetBytes(data)));
    }

    /// <summary>Computes the SHA-256 hash of a string.</summary>
    /// <param name="data">The string to hash</param>
    /// <returns>The hash as a lowercase hexadecimal string</returns>
    [StashFn]
    private static string Sha256(string data)
    {
        return HashToHex(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }

    /// <summary>Computes the SHA-512 hash of a string.</summary>
    /// <param name="data">The string to hash</param>
    /// <returns>The hash as a lowercase hexadecimal string</returns>
    [StashFn]
    private static string Sha512(string data)
    {
        return HashToHex(SHA512.HashData(Encoding.UTF8.GetBytes(data)));
    }

    /// <summary>Computes an HMAC signature using the specified algorithm.</summary>
    /// <param name="algo">The hash algorithm: "md5", "sha1", "sha256", or "sha512"</param>
    /// <param name="key">The secret key</param>
    /// <param name="data">The data to sign</param>
    /// <returns>The HMAC as a lowercase hexadecimal string</returns>
    [StashFn]
    private static string Hmac(string algo, string key, string data)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] dataBytes = Encoding.UTF8.GetBytes(data);

        using HMAC hmac = algo.ToLowerInvariant() switch
        {
            "md5"    => (HMAC)new HMACMD5(keyBytes),
            "sha1"   => new HMACSHA1(keyBytes),
            "sha256" => new HMACSHA256(keyBytes),
            "sha512" => new HMACSHA512(keyBytes),
            _        => throw new RuntimeError("crypto.hmac: unknown algorithm '" + algo + "'. Expected 'md5', 'sha1', 'sha256', or 'sha512'.", errorType: StashErrorTypes.ValueError)
        };

        return HashToHex(hmac.ComputeHash(dataBytes));
    }

    /// <summary>Computes the hash of a file's contents.</summary>
    /// <param name="path">The file path to hash</param>
    /// <param name="algo">Optional hash algorithm (default: "sha256"). One of "md5", "sha1", "sha256", "sha512"</param>
    /// <returns>The hash as a lowercase hexadecimal string</returns>
    [StashFn]
    private static string HashFile(string path, [StashParam(Name = "algo")] params StashValue[] rest)
    {
        if (rest.Length > 1) throw new RuntimeError("'crypto.hashFile' expects 1 or 2 arguments.");

        string algo = "sha256";
        if (rest.Length == 1)
            algo = SvArgs.String(rest, 0, "crypto.hashFile");

        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            throw new RuntimeError($"File not found: '{path}'.", errorType: StashErrorTypes.IOError);
        }
        catch (IOException ex)
        {
            throw new RuntimeError($"Error reading file '{path}': {ex.Message}", errorType: StashErrorTypes.IOError);
        }

        return HashToHex(ComputeHash(algo, fileBytes));
    }

    /// <summary>Generates a random UUID v4 string.</summary>
    /// <returns>A UUID string in standard format (e.g., "550e8400-e29b-41d4-a716-446655440000")</returns>
    [StashFn]
    private static string Uuid()
    {
        return Guid.NewGuid().ToString();
    }

    /// <summary>Generates cryptographically secure random bytes.</summary>
    /// <param name="n">The number of random bytes to generate (must be > 0)</param>
    /// <param name="encoding">Optional output encoding: "hex", "base64", or "raw". If omitted, returns byte[]</param>
    /// <returns>A byte[] or encoded string depending on arguments</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue RandomBytes(long n, [StashParam(Name = "encoding")] params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'crypto.randomBytes' requires 1 or 2 arguments.");

        if (n <= 0)
            throw new RuntimeError("Argument to 'crypto.randomBytes' must be greater than 0.", errorType: StashErrorTypes.ValueError);

        if (n > int.MaxValue)
            throw new RuntimeError("Argument to 'crypto.randomBytes' is too large.", errorType: StashErrorTypes.ValueError);

        byte[] bytes = RandomNumberGenerator.GetBytes((int)n);

        if (rest.Length == 0)
            return StashValue.FromObj(new StashByteArray(bytes));

        string encoding = SvArgs.String(rest, 0, "crypto.randomBytes");
        string result = encoding.ToLowerInvariant() switch
        {
            "hex"    => HashToHex(bytes),
            "base64" => Convert.ToBase64String(bytes),
            "raw"    => Encoding.Latin1.GetString(bytes),
            _        => throw new RuntimeError($"Unknown encoding '{encoding}' in 'crypto.randomBytes'. Expected \"hex\", \"base64\", or \"raw\".", errorType: StashErrorTypes.ValueError)
        };
        return StashValue.FromObj(result);
    }

    /// <summary>Computes the MD5 hash of a byte array.</summary>
    /// <param name="data">The byte array to hash</param>
    /// <returns>The 16-byte hash as a byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Md5Bytes(byte[] data)
    {
        return StashValue.FromObj(new StashByteArray(MD5.HashData(data)));
    }

    /// <summary>Computes the SHA-1 hash of a byte array.</summary>
    /// <param name="data">The byte array to hash</param>
    /// <returns>The 20-byte hash as a byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Sha1Bytes(byte[] data)
    {
        return StashValue.FromObj(new StashByteArray(SHA1.HashData(data)));
    }

    /// <summary>Computes the SHA-256 hash of a byte array.</summary>
    /// <param name="data">The byte array to hash</param>
    /// <returns>The 32-byte hash as a byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Sha256Bytes(byte[] data)
    {
        return StashValue.FromObj(new StashByteArray(SHA256.HashData(data)));
    }

    /// <summary>Computes the SHA-512 hash of a byte array.</summary>
    /// <param name="data">The byte array to hash</param>
    /// <returns>The 64-byte hash as a byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Sha512Bytes(byte[] data)
    {
        return StashValue.FromObj(new StashByteArray(SHA512.HashData(data)));
    }

    /// <summary>Computes an HMAC signature using byte arrays for key and data.</summary>
    /// <param name="algo">The hash algorithm: "md5", "sha1", "sha256", or "sha512"</param>
    /// <param name="key">The secret key as byte[]</param>
    /// <param name="data">The data to sign as byte[]</param>
    /// <returns>The HMAC as a byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue HmacBytes(string algo, byte[] key, byte[] data)
    {
        using HMAC hmac = algo.ToLowerInvariant() switch
        {
            "md5"    => (HMAC)new HMACMD5(key),
            "sha1"   => new HMACSHA1(key),
            "sha256" => new HMACSHA256(key),
            "sha512" => new HMACSHA512(key),
            _        => throw new RuntimeError("crypto.hmacBytes: unknown algorithm '" + algo + "'. Expected 'md5', 'sha1', 'sha256', or 'sha512'.", errorType: StashErrorTypes.ValueError)
        };
        return StashValue.FromObj(new StashByteArray(hmac.ComputeHash(data)));
    }

    /// <summary>Generates a cryptographically secure random encryption key.</summary>
    /// <param name="bits">Optional key size in bits: 128, 192, or 256 (default: 256)</param>
    /// <returns>The key as a lowercase hexadecimal string</returns>
    [StashFn]
    private static string GenerateKey([StashParam(Name = "bits")] params StashValue[] args)
    {
        int bits = 256;
        if (args.Length == 1)
        {
            long rawBits = SvArgs.Long(args, 0, "crypto.generateKey");
            if (rawBits != 128 && rawBits != 192 && rawBits != 256)
                throw new RuntimeError($"'crypto.generateKey' accepts 128, 192, or 256 bits, got {rawBits}.", errorType: StashErrorTypes.ValueError);
            bits = (int)rawBits;
        }
        return HashToHex(RandomNumberGenerator.GetBytes(bits / 8));
    }

    /// <summary>Encrypts data using AES-256-GCM.</summary>
    /// <param name="data">The plaintext to encrypt -- string or byte[]</param>
    /// <param name="key">A 32-byte (256-bit) key as a hex string or byte[]</param>
    /// <param name="options">Reserved for future use (optional)</param>
    /// <returns>A dictionary with fields: ciphertext (hex), iv (hex), tag (hex)</returns>
    [StashFn(ReturnType = "dict")]
    private static StashValue Encrypt(StashValue data, StashValue key, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'crypto.encrypt' expects 2 or 3 arguments.");

        byte[] plaintext;
        if (data.IsObj && data.AsObj is string dataStr)
            plaintext = Encoding.UTF8.GetBytes(dataStr);
        else if (data.IsObj && data.AsObj is StashByteArray dataBa)
            plaintext = dataBa.AsSpan().ToArray();
        else
            throw new RuntimeError("1st argument to 'crypto.encrypt' must be a string or byte[].", errorType: StashErrorTypes.TypeError);

        byte[] keyBytes = ExtractKeyBytes(key, "crypto.encrypt");

        if (keyBytes.Length != 32)
            throw new RuntimeError($"'crypto.encrypt' requires a 32-byte (256-bit) key, got {keyBytes.Length} bytes.", errorType: StashErrorTypes.ValueError);

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
    }

    /// <summary>Decrypts data encrypted with AES-256-GCM.</summary>
    /// <param name="ciphertext">A dictionary { ciphertext, iv, tag } with hex fields, or a combined hex string</param>
    /// <param name="key">The 32-byte (256-bit) decryption key as a hex string or byte[]</param>
    /// <param name="options">Reserved for future use (optional)</param>
    /// <returns>The decrypted plaintext string</returns>
    [StashFn(ReturnType = "string")]
    private static string Decrypt(StashValue ciphertext, StashValue key, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'crypto.decrypt' expects 2 or 3 arguments.");

        byte[] keyBytes = ExtractKeyBytes(key, "crypto.decrypt");

        if (keyBytes.Length != 32)
            throw new RuntimeError($"'crypto.decrypt' requires a 32-byte (256-bit) key, got {keyBytes.Length} bytes.", errorType: StashErrorTypes.ValueError);

        byte[] cipherBytes;
        byte[] ivBytes;
        byte[] tagBytes;

        if (ciphertext.IsObj && ciphertext.AsObj is StashDictionary ctDict)
        {
            StashValue ctVal = ctDict.Get("ciphertext");
            StashValue ivVal = ctDict.Get("iv");
            StashValue tagVal = ctDict.Get("tag");
            if (ctVal.IsNull || ivVal.IsNull || tagVal.IsNull)
                throw new RuntimeError("'crypto.decrypt' dict must contain 'ciphertext', 'iv', and 'tag' fields.", errorType: StashErrorTypes.TypeError);
            if (ctVal.AsObj is not string ctHexField)
                throw new RuntimeError("'ciphertext' field in 'crypto.decrypt' dict must be a hex string.", errorType: StashErrorTypes.TypeError);
            if (ivVal.AsObj is not string ivHexField)
                throw new RuntimeError("'iv' field in 'crypto.decrypt' dict must be a hex string.", errorType: StashErrorTypes.TypeError);
            if (tagVal.AsObj is not string tagHexField)
                throw new RuntimeError("'tag' field in 'crypto.decrypt' dict must be a hex string.", errorType: StashErrorTypes.TypeError);
            cipherBytes = HexToBytes(ctHexField, "crypto.decrypt");
            ivBytes = HexToBytes(ivHexField, "crypto.decrypt");
            tagBytes = HexToBytes(tagHexField, "crypto.decrypt");
        }
        else if (ciphertext.IsObj && ciphertext.AsObj is string combinedHex)
        {
            if (combinedHex.Length < 56)
                throw new RuntimeError("'crypto.decrypt' combined hex string is too short (must encode at least iv + tag).", errorType: StashErrorTypes.ValueError);
            ivBytes = HexToBytes(combinedHex[..24], "crypto.decrypt");
            tagBytes = HexToBytes(combinedHex[24..56], "crypto.decrypt");
            cipherBytes = HexToBytes(combinedHex[56..], "crypto.decrypt");
        }
        else
        {
            throw new RuntimeError("1st argument to 'crypto.decrypt' must be a dictionary { ciphertext, iv, tag } or a combined hex string.", errorType: StashErrorTypes.TypeError);
        }

        byte[] plaintext = new byte[cipherBytes.Length];
        try
        {
            using var aes = new AesGcm(keyBytes, 16);
            aes.Decrypt(ivBytes, cipherBytes, tagBytes, plaintext);
        }
        catch (CryptographicException)
        {
            throw new RuntimeError("'crypto.decrypt' failed: authentication tag verification failed.", errorType: StashErrorTypes.ValueError);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] ExtractKeyBytes(StashValue keyArg, string funcName)
    {
        if (keyArg.IsObj && keyArg.AsObj is string keyHex)
            return HexToBytes(keyHex, funcName);
        if (keyArg.IsObj && keyArg.AsObj is StashByteArray keyBa)
            return keyBa.AsSpan().ToArray();
        throw new RuntimeError($"2nd argument to '{funcName}' must be a hex string or byte[].", errorType: StashErrorTypes.TypeError);
    }

    private static byte[] HexToBytes(string hex, string funcName)
    {
        if (hex.Length % 2 != 0)
            throw new RuntimeError($"Invalid hex string in '{funcName}': length must be even.", errorType: StashErrorTypes.ParseError);
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            throw new RuntimeError($"Invalid hex string in '{funcName}': contains non-hexadecimal characters.", errorType: StashErrorTypes.ParseError);
        }
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
            _        => throw new RuntimeError($"Unknown hash algorithm '{algo}'. Supported: md5, sha1, sha256, sha512.", errorType: StashErrorTypes.ValueError)
        };
}
