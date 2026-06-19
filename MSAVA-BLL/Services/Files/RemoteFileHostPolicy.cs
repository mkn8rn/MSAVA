using System.Net;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Files;

internal delegate Task<IPAddress[]> HostAddressResolver(string host, CancellationToken cancellationToken);

internal static class RemoteFileHostPolicy
{
    private const string HostDidNotResolveMessage = "FileUrl host did not resolve to an address.";
    private const int MaximumDnsHostLength = 253;
    private const int MaximumDnsLabelLength = 63;

    internal static Uri ParseHttpUri(string fileUrl, string parameterName)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("FileUrl must be an absolute HTTP or HTTPS URL.", parameterName);
        }

        FileUrlPolicy.EnsureNoEmbeddedCredentials(uri, parameterName);

        string host = GetNormalizedHost(uri, parameterName);

        if (IsUnsafeHost(host))
            throw new ArgumentException("FileUrl host is not allowed for server-side ingestion.", parameterName);

        EnsureDnsHostSyntaxIsAllowed(host, parameterName);

        return uri;
    }

    internal static async Task EnsureResolvedHostIsAllowedAsync(
        Uri uri,
        HostAddressResolver resolver,
        string parameterName,
        CancellationToken cancellationToken)
    {
        string host = GetNormalizedHost(uri, parameterName);

        if (IPAddress.TryParse(host, out var literalAddress))
        {
            if (IsPrivateOrReservedAddress(literalAddress))
            {
                throw new ArgumentException(
                    "FileUrl host is not allowed for server-side ingestion.",
                    parameterName);
            }

            return;
        }

        EnsureDnsHostSyntaxIsAllowed(host, parameterName);

        IPAddress[] addresses = await resolver(host, cancellationToken);
        EnsureResolvedAddressesAreAllowed(addresses, parameterName);
    }

    internal static async Task<IPAddress> ResolveConnectionAddressAsync(
        string host,
        HostAddressResolver resolver,
        string parameterName,
        CancellationToken cancellationToken)
    {
        string normalizedHost = NormalizeHost(host);

        if (IPAddress.TryParse(normalizedHost, out var literalAddress))
        {
            if (IsPrivateOrReservedAddress(literalAddress))
                throw new ArgumentException("FileUrl host is not allowed for server-side ingestion.", parameterName);

            return literalAddress;
        }

        if (IsUnsafeHost(normalizedHost))
            throw new ArgumentException("FileUrl host is not allowed for server-side ingestion.", parameterName);

        EnsureDnsHostSyntaxIsAllowed(normalizedHost, parameterName);

        IPAddress[] addresses = await resolver(normalizedHost, cancellationToken);
        EnsureResolvedAddressesAreAllowed(addresses, parameterName);

        return addresses[0];
    }

    internal static Task<IPAddress[]> ResolveHostAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        return Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    private static void EnsureResolvedAddressesAreAllowed(IPAddress[] addresses, string parameterName)
    {
        if (addresses.Length == 0)
            throw new HttpRequestException(HostDidNotResolveMessage);

        if (addresses.Any(IsPrivateOrReservedAddress))
        {
            throw new ArgumentException(
                "FileUrl host resolves to an address that is not allowed for server-side ingestion.",
                parameterName);
        }
    }

    private static bool IsUnsafeHost(string host)
    {
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
            return true;

        if (host == "metadata.google.internal")
            return true;

        return IPAddress.TryParse(host, out var address) && IsPrivateOrReservedAddress(address);
    }

    private static string GetNormalizedHost(Uri uri, string parameterName)
    {
        string host = NormalizeHost(uri.IdnHost);
        if (host.Length == 0)
            throw new ArgumentException("FileUrl host is not allowed for server-side ingestion.", parameterName);

        return host;
    }

    private static string NormalizeHost(string host)
    {
        return host.TrimEnd('.').ToLowerInvariant();
    }

    private static void EnsureDnsHostSyntaxIsAllowed(string host, string parameterName)
    {
        if (IPAddress.TryParse(host, out _))
            return;

        if (!IsValidDnsHostName(host))
            throw new ArgumentException("FileUrl host is not allowed for server-side ingestion.", parameterName);
    }

    private static bool IsValidDnsHostName(string host)
    {
        if (host.Length == 0 || host.Length > MaximumDnsHostLength)
            return false;

        foreach (var range in host.AsSpan().Split('.'))
        {
            ReadOnlySpan<char> label = host.AsSpan()[range];
            if (label.Length == 0 || label.Length > MaximumDnsLabelLength)
                return false;

            if (label[0] == '-' || label[^1] == '-')
                return false;

            foreach (char character in label)
            {
                if (!IsDnsLabelCharacter(character))
                    return false;
            }
        }

        return true;
    }

    private static bool IsDnsLabelCharacter(char character)
    {
        return character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
    }

    private static bool IsPrivateOrReservedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return IsPrivateOrReservedIPv4(bytes);

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return IsPrivateOrReservedIPv6(bytes);

        return true;
    }

    private static bool IsPrivateOrReservedIPv4(byte[] bytes)
    {
        return bytes[0] == 0 ||
               bytes[0] == 10 ||
               bytes[0] == 127 ||
               bytes[0] >= 224 ||
               (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) ||
               (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) ||
               (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)) ||
               (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
               (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }

    private static bool IsPrivateOrReservedIPv6(byte[] bytes)
    {
        bool unspecified = bytes.All(b => b == 0);
        bool uniqueLocal = (bytes[0] & 0xFE) == 0xFC;
        bool linkLocal = bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
        bool siteLocal = bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0;
        bool multicast = bytes[0] == 0xFF;
        bool documentation = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8;
        bool deprecatedIPv4Compatible = IsDeprecatedIPv4CompatibleAddress(bytes);
        bool nat64ToPrivateOrReservedIPv4 = IsWellKnownNat64Address(bytes) &&
            EmbeddedIPv4AddressIsPrivateOrReserved(bytes);

        return unspecified ||
            uniqueLocal ||
            linkLocal ||
            siteLocal ||
            multicast ||
            documentation ||
            deprecatedIPv4Compatible ||
            nat64ToPrivateOrReservedIPv4;
    }

    private static bool IsDeprecatedIPv4CompatibleAddress(byte[] bytes)
    {
        return PrefixIsZero(bytes, 12) && !SuffixIsZero(bytes, 12);
    }

    private static bool IsWellKnownNat64Address(byte[] bytes)
    {
        return bytes[0] == 0x00 &&
            bytes[1] == 0x64 &&
            bytes[2] == 0xFF &&
            bytes[3] == 0x9B &&
            RangeIsZero(bytes, start: 4, length: 8);
    }

    private static bool EmbeddedIPv4AddressIsPrivateOrReserved(byte[] bytes)
    {
        byte[] embeddedIPv4 =
        [
            bytes[12],
            bytes[13],
            bytes[14],
            bytes[15]
        ];

        return IsPrivateOrReservedIPv4(embeddedIPv4);
    }

    private static bool PrefixIsZero(byte[] bytes, int length)
    {
        return RangeIsZero(bytes, start: 0, length);
    }

    private static bool SuffixIsZero(byte[] bytes, int start)
    {
        return RangeIsZero(bytes, start, bytes.Length - start);
    }

    private static bool RangeIsZero(byte[] bytes, int start, int length)
    {
        for (int index = start; index < start + length; index++)
        {
            if (bytes[index] != 0)
                return false;
        }

        return true;
    }
}
