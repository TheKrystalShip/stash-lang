namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>Registers the <c>encoding</c> namespace with base64, URL, and hex encoding/decoding functions.</summary>
[StashNamespace]
public static partial class EncodingBuiltIns
{
    /// <summary>Encodes a string to base64.</summary>
    /// <param name="s">The string to encode (UTF-8)</param>
    /// <param name="urlSafe">Optional boolean; when true, uses URL-safe base64 (no padding, '-' and '_')</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The base64-encoded string</returns>
    [StashFn]
    private static string Base64Encode(string s, [StashParam(Name = "urlSafe")] params StashValue[] rest)
    {
        bool urlSafe = rest.Length > 0 && SvArgs.Bool(rest, 0, "encoding.base64Encode");
        string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        return urlSafe ? encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_') : encoded;
    }

    /// <summary>Decodes a base64 string to a plain string.</summary>
    /// <param name="s">The base64-encoded string to decode</param>
    /// <param name="urlSafe">Optional boolean; when true, accepts URL-safe base64 format</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The decoded string (interpreted as UTF-8)</returns>
    [StashFn]
    private static string Base64Decode(string s, [StashParam(Name = "urlSafe")] params StashValue[] rest)
    {
        bool urlSafe = rest.Length > 0 && SvArgs.Bool(rest, 0, "encoding.base64Decode");
        if (urlSafe)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            int pad = s.Length % 4;
            if (pad == 2) s += "==";
            else if (pad == 3) s += "=";
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    /// <summary>Percent-encodes a string for use in URLs.</summary>
    /// <param name="s">The string to encode</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The URL-encoded string</returns>
    [StashFn]
    private static string UrlEncode(string s)
    {
        return Uri.EscapeDataString(s);
    }

    /// <summary>Decodes a percent-encoded URL string.</summary>
    /// <param name="s">The URL-encoded string to decode</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The decoded string</returns>
    [StashFn]
    private static string UrlDecode(string s)
    {
        return Uri.UnescapeDataString(s);
    }

    /// <summary>Encodes a string as a lowercase hexadecimal string (UTF-8 bytes).</summary>
    /// <param name="s">The string to encode</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The hex-encoded string</returns>
    [StashFn]
    private static string HexEncode(string s)
    {
        return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(s)).ToLowerInvariant();
    }

    /// <summary>Decodes a hexadecimal-encoded string.</summary>
    /// <param name="s">The hex string to decode (must be valid hex)</param>
    /// <exception cref="ValueError">if the input is not a valid hexadecimal string</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The decoded string (interpreted as UTF-8)</returns>
    [StashFn]
    private static string HexDecode(string s)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromHexString(s));
        }
        catch (FormatException)
        {
            throw new ValueError($"'encoding.hexDecode' invalid hex string: {s}");
        }
    }

    /// <summary>Decodes a base64 string to a byte array.</summary>
    /// <param name="s">The base64-encoded string to decode</param>
    /// <param name="urlSafe">Optional boolean; when true, accepts URL-safe base64 format</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The decoded data as a byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Base64DecodeBytes(string s, [StashParam(Name = "urlSafe")] params StashValue[] rest)
    {
        bool urlSafe = rest.Length > 0 && SvArgs.Bool(rest, 0, "encoding.base64DecodeBytes");
        if (urlSafe)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            int pad = s.Length % 4;
            if (pad == 2) s += "==";
            else if (pad == 3) s += "=";
        }
        return StashValue.FromObj(new StashByteArray(Convert.FromBase64String(s)));
    }

    /// <summary>Decodes a hexadecimal string to a byte array.</summary>
    /// <param name="s">The hex string to decode (must be valid hex)</param>
    /// <exception cref="ValueError">if the input is not a valid hexadecimal string</exception>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The decoded data as a byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue HexDecodeBytes(string s)
    {
        try
        {
            return StashValue.FromObj(new StashByteArray(Convert.FromHexString(s)));
        }
        catch (FormatException)
        {
            throw new ValueError($"'encoding.hexDecodeBytes' invalid hex string: {s}");
        }
    }
}
