using System.Net;

namespace MSAVA_BLL.Services.Files;

internal delegate Task<IPAddress[]> HostAddressResolver(string host, CancellationToken cancellationToken);

internal static class RemoteFileHostPolicy
{
    internal static Uri ParseHttpUri(string fileUrl, string parameterName)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("FileUrl must be an absolute HTTP or HTTPS URL.", parameterName);
        }

        string host = GetNormalizedHost(uri);

        if (IsUnsafeHost(host))
            throw new ArgumentException("FileUrl host is not allowed for server-side ingestion.", parameterName);

        return uri;
    }

    internal static async Task EnsureResolvedHostIsAllowedAsync(
        Uri uri,
        HostAddressResolver resolver,
        string parameterName,
        CancellationToken cancellationToken)
    {
        string host = GetNormalizedHost(uri);

        if (IPAddress.TryParse(host, out _))
            return;

        IPAddress[] addresses = await resolver(host, cancellationToken);
        EnsureResolvedAddressesAreAllowed(host, addresses, parameterName);
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

        IPAddress[] addresses = await resolver(normalizedHost, cancellationToken);
        EnsureResolvedAddressesAreAllowed(normalizedHost, addresses, parameterName);

        return addresses[0];
    }

    internal static Task<IPAddress[]> ResolveHostAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        return Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    private static void EnsureResolvedAddressesAreAllowed(
        string host,
        IPAddress[] addresses,
        string parameterName)
    {
        if (addresses.Length == 0)
            throw new HttpRequestException($"FileUrl host '{host}' did not resolve to an address.");

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

    private static string GetNormalizedHost(Uri uri)
    {
        return NormalizeHost(uri.IdnHost);
    }

    private static string NormalizeHost(string host)
    {
        return host.TrimEnd('.').ToLowerInvariant();
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

        return unspecified || uniqueLocal || linkLocal || siteLocal || multicast || documentation;
    }
}
