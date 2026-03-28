namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>encoding</c> namespace built-in functions for text encoding and decoding.
/// </summary>
public static class EncodingBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("encoding");

        // encoding.base64Encode(input) — Encodes the UTF-8 string 'input' to a Base64 string.
        ns.Function("base64Encode", [Param("s", "string")], (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.base64Encode' must be a string.");
            }

            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        },
            returnType: "string",
            documentation: "Encodes a string to Base64.\n@param s The string to encode\n@return The Base64-encoded string");

        // encoding.base64Decode(input) — Decodes a Base64 string to its original UTF-8 string. Throws on invalid input.
        ns.Function("base64Decode", [Param("s", "string")], (_, args) =>
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
        },
            returnType: "string",
            documentation: "Decodes a Base64 string back to its original string.\n@param s The Base64-encoded string\n@return The decoded string");

        // encoding.urlEncode(input) — Percent-encodes 'input' for safe inclusion in a URL query string or path segment.
        ns.Function("urlEncode", [Param("s", "string")], (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.urlEncode' must be a string.");
            }

            return Uri.EscapeDataString(s);
        },
            returnType: "string",
            documentation: "URL-encodes a string using RFC 3986 percent-encoding.\n@param s The string to encode\n@return The URL-encoded string");

        // encoding.urlDecode(input) — Decodes a percent-encoded URL string back to its original form.
        ns.Function("urlDecode", [Param("s", "string")], (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.urlDecode' must be a string.");
            }

            return Uri.UnescapeDataString(s);
        },
            returnType: "string",
            documentation: "Decodes a URL-encoded (percent-encoded) string.\n@param s The URL-encoded string\n@return The decoded string");

        // encoding.hexEncode(input) — Encodes the UTF-8 string 'input' to a lowercase hexadecimal string.
        ns.Function("hexEncode", [Param("s", "string")], (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.hexEncode' must be a string.");
            }

            return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(s)).ToLowerInvariant();
        },
            returnType: "string",
            documentation: "Encodes a string's UTF-8 bytes as a lowercase hexadecimal string.\n@param s The string to encode\n@return The hexadecimal string");

        // encoding.hexDecode(input) — Decodes a hexadecimal string back to its original UTF-8 string. Throws on invalid input.
        ns.Function("hexDecode", [Param("s", "string")], (_, args) =>
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
        },
            returnType: "string",
            documentation: "Decodes a hexadecimal string back to a UTF-8 string.\n@param s The hexadecimal string to decode\n@return The decoded string");

        return ns.Build();
    }
}
