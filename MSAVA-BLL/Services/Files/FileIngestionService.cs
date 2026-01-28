using MSAVA_Shared.Models;
using MSAVA_BLL.Services.Interfaces;

namespace MSAVA_BLL.Services.Files;

public class FileIngestionService : IFileIngestionService
{
    private readonly FilePersistenceService _persistenceService;
    private readonly IHttpClientFactory _httpClientFactory;

    public FileIngestionService(FilePersistenceService persistenceService, IHttpClientFactory httpClientFactory)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
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

        var httpClient = _httpClientFactory.CreateClient();
        using var response = await httpClient.GetAsync(dto.FileUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
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
