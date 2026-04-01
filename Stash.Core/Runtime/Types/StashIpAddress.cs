namespace Stash.Runtime.Types;

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

public class StashIpAddress
{
    public IPAddress Address { get; }
    public int Version { get; }
    public int? PrefixLength { get; }

    public bool IsLoopback => IPAddress.IsLoopback(Address);
    public bool IsPrivate { get; }
    public bool IsLinkLocal { get; }

    public StashIpAddress(IPAddress address, int? prefixLength = null)
    {
        Address = address;
        Version = address.AddressFamily == AddressFamily.InterNetworkV6 ? 6 : 4;
        PrefixLength = prefixLength;
        IsPrivate = ComputeIsPrivate(address);
        IsLinkLocal = ComputeIsLinkLocal(address);
    }

    public StashIpAddress(byte[] bytes, int? prefixLength = null)
        : this(new IPAddress(bytes), prefixLength)
    {
    }

    public byte[] GetBytes() => Address.GetAddressBytes();

    public bool Contains(StashIpAddress other)
    {
        if (PrefixLength is null)
        {
            return Equals(other);
        }

        var networkBytes = Address.GetAddressBytes();
        var otherBytes = other.Address.GetAddressBytes();

        if (networkBytes.Length != otherBytes.Length)
        {
            return false;
        }

        int prefix = PrefixLength.Value;
        int fullBytes = prefix / 8;
        int remainingBits = prefix % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            if (networkBytes[i] != otherBytes[i])
            {
                return false;
            }
        }

        if (remainingBits > 0 && fullBytes < networkBytes.Length)
        {
            byte mask = (byte)(0xFF << (8 - remainingBits));
            if ((networkBytes[fullBytes] & mask) != (otherBytes[fullBytes] & mask))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryParse(string text, out StashIpAddress? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int slashIndex = text.IndexOf('/');
        if (slashIndex >= 0)
        {
            string ipPart = text[..slashIndex];
            string prefixPart = text[(slashIndex + 1)..];

            if (!IPAddress.TryParse(ipPart, out var addr))
            {
                return false;
            }

            if (!int.TryParse(prefixPart, out int prefix))
            {
                return false;
            }

            int maxPrefix = addr.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            if (prefix < 0 || prefix > maxPrefix)
            {
                return false;
            }

            result = new StashIpAddress(addr, prefix);
            return true;
        }

        if (!IPAddress.TryParse(text, out var ipAddress))
        {
            return false;
        }

        result = new StashIpAddress(ipAddress);
        return true;
    }

    /// Returns a specific error message if the address text is invalid, or null if valid.
    public static string? ValidateFormat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Empty IP address after '@'.";

        // Split off CIDR prefix and zone ID for separate validation
        string addressPart = text;
        string? cidrPart = null;
        string? zoneId = null;

        int slashIndex = text.IndexOf('/');
        if (slashIndex >= 0)
        {
            addressPart = text[..slashIndex];
            cidrPart = text[(slashIndex + 1)..];
        }

        int percentIndex = addressPart.IndexOf('%');
        if (percentIndex >= 0)
        {
            zoneId = addressPart[(percentIndex + 1)..];
            addressPart = addressPart[..percentIndex];
        }

        // Determine IPv4 vs IPv6 based on presence of ':'
        bool isIpv6 = addressPart.Contains(':');

        if (isIpv6)
            return ValidateIpv6(addressPart, cidrPart, zoneId);

        if (zoneId is not null)
            return "Zone IDs ('%...') are only valid for IPv6 addresses.";

        return ValidateIpv4(addressPart, cidrPart);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not StashIpAddress other)
        {
            return false;
        }

        return PrefixLength == other.PrefixLength
            && Address.GetAddressBytes().SequenceEqual(other.Address.GetAddressBytes());
    }

    public override int GetHashCode()
    {
        var bytes = Address.GetAddressBytes();
        var hashCode = new HashCode();
        foreach (byte b in bytes)
        {
            hashCode.Add(b);
        }
        hashCode.Add(PrefixLength);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(StashIpAddress? left, StashIpAddress? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(StashIpAddress? left, StashIpAddress? right) => !(left == right);

    public override string ToString()
    {
        string ip = Address.ToString();
        return PrefixLength.HasValue ? $"{ip}/{PrefixLength}" : ip;
    }

    /// Bitwise AND of two IP addresses (e.g., addr & mask → network address).
    public StashIpAddress BitwiseAnd(StashIpAddress other)
    {
        byte[] a = Address.GetAddressBytes();
        byte[] b = other.Address.GetAddressBytes();
        if (a.Length != b.Length)
            throw new InvalidOperationException("Cannot perform bitwise AND on IPv4 and IPv6 addresses.");
        byte[] result = new byte[a.Length];
        for (int i = 0; i < a.Length; i++)
            result[i] = (byte)(a[i] & b[i]);
        return new StashIpAddress(result);
    }

    /// Bitwise OR of two IP addresses (e.g., network | ~mask → broadcast).
    public StashIpAddress BitwiseOr(StashIpAddress other)
    {
        byte[] a = Address.GetAddressBytes();
        byte[] b = other.Address.GetAddressBytes();
        if (a.Length != b.Length)
            throw new InvalidOperationException("Cannot perform bitwise OR on IPv4 and IPv6 addresses.");
        byte[] result = new byte[a.Length];
        for (int i = 0; i < a.Length; i++)
            result[i] = (byte)(a[i] | b[i]);
        return new StashIpAddress(result);
    }

    /// Bitwise NOT / complement (e.g., ~mask → wildcard mask).
    public StashIpAddress BitwiseNot()
    {
        byte[] bytes = Address.GetAddressBytes();
        byte[] result = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            result[i] = (byte)~bytes[i];
        return new StashIpAddress(result);
    }

    /// Add an integer offset to an IP address (e.g., @10.0.0.0 + 42 → @10.0.0.42).
    public StashIpAddress Add(long offset)
    {
        byte[] bytes = Address.GetAddressBytes();
        long carry = offset;
        for (int i = bytes.Length - 1; i >= 0 && carry != 0; i--)
        {
            long sum = bytes[i] + carry;
            bytes[i] = (byte)(sum & 0xFF);
            carry = sum >> 8;
        }
        return new StashIpAddress(bytes);
    }

    /// Compute the numeric distance between two IP addresses.
    public long Subtract(StashIpAddress other)
    {
        byte[] a = Address.GetAddressBytes();
        byte[] b = other.Address.GetAddressBytes();
        if (a.Length != b.Length)
            throw new InvalidOperationException("Cannot subtract IPv4 and IPv6 addresses.");
        if (a.Length == 16)
            throw new InvalidOperationException("IP subtraction is not supported for IPv6 addresses.");
        long result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result = (result << 8) + (a[i] - b[i]);
        }
        return result;
    }

    /// Lexicographic byte comparison for ordering IP addresses.
    public int CompareTo(StashIpAddress other)
    {
        byte[] a = Address.GetAddressBytes();
        byte[] b = other.Address.GetAddressBytes();
        if (a.Length != b.Length)
            throw new InvalidOperationException("Cannot compare IPv4 and IPv6 addresses.");
        for (int i = 0; i < a.Length; i++)
        {
            int cmp = a[i].CompareTo(b[i]);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    private static string? ValidateIpv4(string address, string? cidrPart)
    {
        // Check for invalid characters
        foreach (char c in address)
        {
            if (!char.IsDigit(c) && c != '.')
                return $"Invalid character '{c}' in IPv4 address.";
        }

        // Split into octets
        string[] octets = address.Split('.');

        if (octets.Length != 4)
            return $"IPv4 address requires exactly 4 octets, but found {octets.Length}.";

        for (int i = 0; i < octets.Length; i++)
        {
            string octet = octets[i];

            if (octet.Length == 0)
                return $"Empty octet at position {i + 1} in IPv4 address.";

            if (!int.TryParse(octet, out int value))
                return $"IPv4 octet '{octet}' is not a valid number.";

            if (value < 0 || value > 255)
                return $"IPv4 octet '{octet}' is out of range (must be 0\u2013255).";

            if (octet.Length > 1 && octet[0] == '0')
                return $"IPv4 octet '{octet}' has a leading zero (ambiguous notation).";
        }

        // Validate CIDR prefix
        if (cidrPart is not null)
        {
            if (cidrPart.Length == 0)
                return "Missing CIDR prefix length after '/'.";

            if (!int.TryParse(cidrPart, out int prefix))
                return $"CIDR prefix '{cidrPart}' is not a valid number.";

            if (prefix < 0 || prefix > 32)
                return $"IPv4 CIDR prefix '{prefix}' is out of range (must be 0\u201332).";
        }

        return null; // Valid
    }

    private static string? ValidateIpv6(string address, string? cidrPart, string? zoneId)
    {
        // Check for ::: (triple colon) — always invalid
        if (address.Contains(":::"))
            return "IPv6 address contains ':::' (invalid \u2014 use '::' for zero compression).";

        // Check for multiple ::
        int doubleColonCount = 0;
        int idx = 0;
        while ((idx = address.IndexOf("::", idx, StringComparison.Ordinal)) >= 0)
        {
            doubleColonCount++;
            idx += 2;
        }
        if (doubleColonCount > 1)
            return "IPv6 address contains multiple '::' (only one allowed).";

        // Handle IPv4-mapped IPv6 (e.g., ::ffff:192.168.1.1)
        int lastColon = address.LastIndexOf(':');
        string? ipv4Suffix = null;
        string groupPart = address;

        if (lastColon >= 0 && address[(lastColon + 1)..].Contains('.'))
        {
            ipv4Suffix = address[(lastColon + 1)..];
            groupPart = address[..(lastColon + 1)]; // Include the trailing colon

            // Validate the IPv4 suffix
            string? ipv4Error = ValidateIpv4(ipv4Suffix, null);
            if (ipv4Error is not null)
                return $"IPv4-mapped suffix: {ipv4Error}";
        }

        // Split groups (handling :: expansion)
        bool hasDoubleColon = address.Contains("::");

        string[] parts;
        if (groupPart.Length > 0 && !groupPart.Contains('.'))
        {
            parts = groupPart.Split(':');
        }
        else
        {
            parts = groupPart.TrimEnd(':').Split(':');
        }

        // Validate individual hex groups (skip empty ones from ::)
        foreach (string part in parts)
        {
            if (part.Length == 0)
                continue; // Empty parts come from :: or leading/trailing :

            if (part.Length > 4)
                return $"IPv6 group '{part}' exceeds 4 hex digits.";

            foreach (char c in part)
            {
                if (!IsHexChar(c))
                    return $"Invalid character '{c}' in IPv6 group '{part}'.";
            }
        }

        // Count non-empty groups
        int groupCount = 0;
        foreach (string part in parts)
        {
            if (part.Length > 0)
                groupCount++;
        }

        int maxGroups = ipv4Suffix is not null ? 6 : 8; // IPv4-mapped uses 6 hex groups + IPv4 suffix

        if (!hasDoubleColon && groupCount != maxGroups)
            return $"IPv6 address requires {maxGroups} groups without '::' compression, but found {groupCount}.";

        if (hasDoubleColon && groupCount >= maxGroups)
            return $"IPv6 address with '::' has too many groups ({groupCount}).";

        // Validate CIDR prefix
        if (cidrPart is not null)
        {
            if (cidrPart.Length == 0)
                return "Missing CIDR prefix length after '/'.";

            if (!int.TryParse(cidrPart, out int prefix))
                return $"CIDR prefix '{cidrPart}' is not a valid number.";

            if (prefix < 0 || prefix > 128)
                return $"IPv6 CIDR prefix '{prefix}' is out of range (must be 0\u2013128).";
        }

        // Validate zone ID
        if (zoneId is not null && zoneId.Length == 0)
            return "Empty zone ID after '%'.";

        return null; // Valid
    }

    private static bool IsHexChar(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool ComputeIsPrivate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = address.GetAddressBytes();
            // fc00::/7 — ULA: first byte high 7 bits are 0xFC
            return (bytes[0] & 0xFE) == 0xFC;
        }

        byte[] ipBytes = address.GetAddressBytes();
        // 10.0.0.0/8
        if (ipBytes[0] == 10) return true;
        // 172.16.0.0/12
        if (ipBytes[0] == 172 && ipBytes[1] >= 16 && ipBytes[1] <= 31) return true;
        // 192.168.0.0/16
        if (ipBytes[0] == 192 && ipBytes[1] == 168) return true;

        return false;
    }

    private static bool ComputeIsLinkLocal(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal;
        }

        byte[] bytes = address.GetAddressBytes();
        // 169.254.0.0/16
        return bytes[0] == 169 && bytes[1] == 254;
    }
}
