using System.Net;
using MSAVA_Shared.Models;
using MSAVA_BLL.Services.Interfaces;

namespace MSAVA_BLL.Services.Files;

public class FileIngestionService : IFileIngestionService
{
    public const string RemoteFileHttpClientName = "RemoteFileIngestion";

    private const int MaximumRemoteErrorBodyLength = 2048;

    private readonly FilePersistenceService _persistenceService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _hostAddressResolver;

    public FileIngestionService(FilePersistenceService persistenceService, IHttpClientFactory httpClientFactory)
        : this(persistenceService, httpClientFactory, ResolveHostAddressesAsync)
    {
    }

    internal FileIngestionService(
        FilePersistenceService persistenceService,
        IHttpClientFactory httpClientFactory,
        Func<string, CancellationToken, Task<IPAddress[]>> hostAddressResolver)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _hostAddressResolver = hostAddressResolver ?? throw new ArgumentNullException(nameof(hostAddressResolver));
    }

    public async Task<Guid> CreateFileFromStreamAsync(SaveFileFromStreamDTO dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.FileName))
            throw new ArgumentException("FileName must be provided.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.FileExtension))
            throw new ArgumentException("FileExtension must be provided.", nameof(dto));
        if (dto.Stream == null)
            throw new ArgumentException("File content must be provided as a stream.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));

        dto.Tags ??= [];
        dto.Categories ??= [];
        dto.Description ??= string.Empty;

        return await _persistenceService.CreateFileFromStreamAsync(dto, cancellationToken);
    }

    public async Task<Guid> CreateFileFromUrlAsync(SaveFileFromUrlDTO dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.FileUrl))
            throw new ArgumentException("FileUrl must be provided.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.FileName))
            throw new ArgumentException("FileName must be provided.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.FileExtension))
            throw new ArgumentException("FileExtension must be provided.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));

        Uri fileUri = ParseFileUrl(dto.FileUrl);
        await EnsureSafeResolvedHostAsync(fileUri, cancellationToken);

        var httpClient = _httpClientFactory.CreateClient(RemoteFileHttpClientName);
        using var response = await httpClient.GetAsync(fileUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessfulRemoteResponseAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var streamDto = new SaveFileFromStreamDTO
        {
            FileName = dto.FileName,
            FileExtension = dto.FileExtension,
            Stream = stream,
            AccessGroupId = dto.AccessGroupId,
            Tags = dto.Tags ?? [],
            Categories = dto.Categories ?? [],
            Description = dto.Description ?? string.Empty,
            PublicViewing = dto.PublicViewing,
            PublicDownload = dto.PublicDownload
        };

        return await _persistenceService.CreateFileFromStreamAsync(streamDto, cancellationToken);
    }

    private static async Task EnsureSuccessfulRemoteResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body = await ReadRemoteErrorBodyAsync(response, cancellationToken);
        string message = string.IsNullOrWhiteSpace(body)
            ? $"File URL download failed {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()})."
            : $"File URL download failed {(int)response.StatusCode}: {body}";

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<string> ReadRemoteErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
            return string.Empty;

        string body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

        if (body.Length <= MaximumRemoteErrorBodyLength)
            return body;

        return body[..MaximumRemoteErrorBodyLength];
    }

    private static Uri ParseFileUrl(string fileUrl)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("FileUrl must be an absolute HTTP or HTTPS URL.", nameof(fileUrl));
        }

        if (IsUnsafeFileHost(uri))
            throw new ArgumentException("FileUrl host is not allowed for server-side ingestion.", nameof(fileUrl));

        return uri;
    }

    private async Task EnsureSafeResolvedHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        string host = GetNormalizedHost(uri);

        if (IPAddress.TryParse(host, out _))
            return;

        IPAddress[] addresses = await _hostAddressResolver(host, cancellationToken);

        if (addresses.Length == 0)
            throw new HttpRequestException($"FileUrl host '{host}' did not resolve to an address.");

        if (addresses.Any(IsPrivateOrReservedAddress))
        {
            throw new ArgumentException(
                "FileUrl host resolves to an address that is not allowed for server-side ingestion.",
                "FileUrl");
        }
    }

    private static bool IsUnsafeFileHost(Uri uri)
    {
        string host = GetNormalizedHost(uri);

        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
            return true;

        if (host == "metadata.google.internal")
            return true;

        return IPAddress.TryParse(host, out var address) && IsPrivateOrReservedAddress(address);
    }

    private static Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken)
    {
        return Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    private static string GetNormalizedHost(Uri uri)
    {
        return uri.IdnHost.TrimEnd('.').ToLowerInvariant();
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
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19));
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

    public async Task<Guid> CreateFileFromFormFileAsync(SaveFileFromFormFileDTO dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.FileName))
            throw new ArgumentException("FileName must be provided.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.FileExtension))
            throw new ArgumentException("FileExtension must be provided.", nameof(dto));
        if (dto.FormFile == null)
            throw new ArgumentException("FormFile must be provided.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));

        await using var stream = dto.FormFile.OpenReadStream();

        var streamDto = new SaveFileFromStreamDTO
        {
            FileName = dto.FileName,
            FileExtension = dto.FileExtension,
            Stream = stream,
            AccessGroupId = dto.AccessGroupId,
            Tags = dto.Tags ?? [],
            Categories = dto.Categories ?? [],
            Description = dto.Description ?? string.Empty,
            PublicViewing = dto.PublicViewing,
            PublicDownload = dto.PublicDownload
        };

        return await _persistenceService.CreateFileFromStreamAsync(streamDto, cancellationToken);
    }
}
