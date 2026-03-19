namespace Stash.Interpreting.BuiltIns;

using System;
using System.Text;
using Stash.Interpreting.Types;

/// <summary>Registers the <c>encoding</c> namespace providing Base64, URL, and hex encoding/decoding.</summary>
public static class EncodingBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var encoding = new StashNamespace("encoding");

        encoding.Define("base64Encode", new BuiltInFunction("encoding.base64Encode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.base64Encode' must be a string.");
            }

            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        }));

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

        encoding.Define("urlEncode", new BuiltInFunction("encoding.urlEncode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.urlEncode' must be a string.");
            }

            return Uri.EscapeDataString(s);
        }));

        encoding.Define("urlDecode", new BuiltInFunction("encoding.urlDecode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.urlDecode' must be a string.");
            }

            return Uri.UnescapeDataString(s);
        }));

        encoding.Define("hexEncode", new BuiltInFunction("encoding.hexEncode", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'encoding.hexEncode' must be a string.");
            }

            return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(s)).ToLowerInvariant();
        }));

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
