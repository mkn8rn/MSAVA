using MSAVA_Shared.Models;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;

namespace MSAVA_BLL.Services.Files;

public class FileIngestionService : IFileIngestionService
{
    public const string RemoteFileHttpClientName = "RemoteFileIngestion";

    private const int MaximumRemoteErrorBodyLength = 2048;

    private readonly FilePersistenceService _persistenceService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HostAddressResolver _hostAddressResolver;

    public FileIngestionService(FilePersistenceService persistenceService, IHttpClientFactory httpClientFactory)
        : this(persistenceService, httpClientFactory, RemoteFileHostPolicy.ResolveHostAddressesAsync)
    {
    }

    internal FileIngestionService(
        FilePersistenceService persistenceService,
        IHttpClientFactory httpClientFactory,
        HostAddressResolver hostAddressResolver)
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

        Uri fileUri = RemoteFileHostPolicy.ParseHttpUri(dto.FileUrl, nameof(dto.FileUrl));
        await _persistenceService.AuthorizeCreateInAccessGroupAsync(dto.AccessGroupId, cancellationToken);
        await EnsureSafeResolvedHostAsync(fileUri, cancellationToken);

        var httpClient = _httpClientFactory.CreateClient(RemoteFileHttpClientName);
        using var response = await httpClient.GetAsync(fileUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessfulRemoteResponseAsync(response, cancellationToken);
        EnsureRemoteContentIsWithinMaximum(response);
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

        string body = await HttpErrorBodyReader.ReadTrimmedBodyAsync(
            response.Content,
            MaximumRemoteErrorBodyLength,
            cancellationToken);
        string message = string.IsNullOrWhiteSpace(body)
            ? $"File URL download failed {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()})."
            : $"File URL download failed {(int)response.StatusCode}: {body}";

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private async Task EnsureSafeResolvedHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        await RemoteFileHostPolicy.EnsureResolvedHostIsAllowedAsync(
            uri,
            _hostAddressResolver,
            nameof(SaveFileFromUrlDTO.FileUrl),
            cancellationToken);
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

        FileSizePolicy.EnsureWithinMaximum(dto.FormFile.Length, _persistenceService.MaximumFileSizeBytes);
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

    private void EnsureRemoteContentIsWithinMaximum(HttpResponseMessage response)
    {
        if (response.Content is null)
            throw new HttpRequestException("File URL response did not include content.");

        long? declaredContentLength = response.Content.Headers.ContentLength;

        if (declaredContentLength is not null)
            FileSizePolicy.EnsureWithinMaximum(declaredContentLength.Value, _persistenceService.MaximumFileSizeBytes);
    }
}
