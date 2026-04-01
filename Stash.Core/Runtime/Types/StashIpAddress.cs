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
