namespace Stash.Interpreting.BuiltIns;

using System;
using System.Text;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the <c>encoding</c> namespace built-in functions for text encoding and decoding.
/// </summary>
/// <remarks>
/// <para>
/// Provides Base64 encoding and decoding (<c>encoding.base64Encode</c>, <c>encoding.base64Decode</c>),
/// URL percent-encoding (<c>encoding.urlEncode</c>, <c>encoding.urlDecode</c>), and hex encoding
/// (<c>encoding.hexEncode</c>, <c>encoding.hexDecode</c>).
/// </para>
/// <para>All string values are treated as UTF-8 when converting to or from bytes.</para>
/// </remarks>
public static class EncodingBuiltIns
{
    /// <summary>
    /// Registers all <c>encoding</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var encoding = new StashNamespace("encoding");

        // encoding.base64Encode(input) — Encodes the UTF-8 string 'input' to a Base64 string.
        encoding.Define("base64Encode", new BuiltInFunction("encoding.base64Encode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.base64Encode' must be a string.");
            }

            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        }));

        // encoding.base64Decode(input) — Decodes a Base64 string to its original UTF-8 string. Throws on invalid input.
        encoding.Define("base64Decode", new BuiltInFunction("encoding.base64Decode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.base64Decode' must be a string.");
            }

            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
            }
            catch (FormatException ex)
            {
                throw new RuntimeError($"Invalid Base64 string in 'encoding.base64Decode': {ex.Message}");
            }
        }));

        // encoding.urlEncode(input) — Percent-encodes 'input' for safe inclusion in a URL query string or path segment.
        encoding.Define("urlEncode", new BuiltInFunction("encoding.urlEncode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.urlEncode' must be a string.");
            }

            return Uri.EscapeDataString(s);
        }));

        // encoding.urlDecode(input) — Decodes a percent-encoded URL string back to its original form.
        encoding.Define("urlDecode", new BuiltInFunction("encoding.urlDecode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.urlDecode' must be a string.");
            }

            return Uri.UnescapeDataString(s);
        }));

        // encoding.hexEncode(input) — Encodes the UTF-8 string 'input' to a lowercase hexadecimal string.
        encoding.Define("hexEncode", new BuiltInFunction("encoding.hexEncode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.hexEncode' must be a string.");
            }

            return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(s)).ToLowerInvariant();
        }));

        // encoding.hexDecode(input) — Decodes a hexadecimal string back to its original UTF-8 string. Throws on invalid input.
        encoding.Define("hexDecode", new BuiltInFunction("encoding.hexDecode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.hexDecode' must be a string.");
            }

            try
            {
                var bytes = Convert.FromHexString(s);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException ex)
            {
                throw new RuntimeError($"Invalid hex string in 'encoding.hexDecode': {ex.Message}");
            }
        }));

        globals.Define("encoding", encoding);
    }
}
