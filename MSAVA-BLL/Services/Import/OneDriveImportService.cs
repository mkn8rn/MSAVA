using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_Shared.Models;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MSAVA_BLL.Services.Import;

public class OneDriveImportService : IFileImportService<FetchFileFromOneDriveDTO>
{
    private readonly FilePersistenceService _persistenceService;
    private readonly ServiceLogger _serviceLogger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OneDriveImportService> _logger;

    public OneDriveImportService(
        FilePersistenceService persistenceService,
        ServiceLogger serviceLogger,
        IHttpClientFactory httpClientFactory,
        ILogger<OneDriveImportService> logger)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Guid> ImportAsync(FetchFileFromOneDriveDTO dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.FileUrl))
            throw new ArgumentException("FileUrl must be provided.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));

        Uri fileUri = ParseFileUrl(dto.FileUrl);
        await _persistenceService.AuthorizeCreateInAccessGroupAsync(dto.AccessGroupId, cancellationToken);

        var http = _httpClientFactory.CreateClient(FileIngestionService.RemoteFileHttpClientName);
        var shareId = "u!" + Base64UrlEncode(fileUri.AbsoluteUri);
        var downloadUrl = $"https://api.onedrive.com/v1.0/shares/{shareId}/root/content";

        _serviceLogger.LogInformation($"Starting OneDrive download. URL: {downloadUrl}");

        using var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            await ProviderHttpFailure.ThrowAsync("OneDrive download", resp, cancellationToken);
        }

        var respMediaType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (respMediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OneDrive returned HTML instead of file content.");

        string finalFileName = "OneDrive File";
        string inferredExtension = GetExtensionFromResponse(resp);

        if (resp.Content.Headers.ContentDisposition != null)
        {
            var cd = resp.Content.Headers.ContentDisposition;
            var fileName = cd.FileNameStar ?? cd.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                fileName = fileName.Trim('"');
                finalFileName = Path.GetFileNameWithoutExtension(fileName);
                var fileExt = Path.GetExtension(fileName).TrimStart('.');
                if (!string.IsNullOrWhiteSpace(fileExt))
                    inferredExtension = fileExt;
            }
        }

        string finalExtension = ProviderFileType.RequireSupportedExtension("OneDrive", inferredExtension);
        EnsureDeclaredContentLengthWithinMaximum(resp.Content);

        var tempFilePath = Path.GetTempFileName();
        _serviceLogger.LogInformation($"Downloading OneDrive content to temp path {tempFilePath}");

        try
        {
            await using (var contentStream = await resp.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var boundedFileStream = new FileSizeLimitedWriteStream(fs, _persistenceService.MaximumFileSizeBytes);
                await contentStream.CopyToAsync(boundedFileStream, cancellationToken);
            }

            var fetchDto = new SaveFileFromFetchDTO
            {
                FileName = finalFileName,
                FileExtension = finalExtension,
                TempFilePath = tempFilePath,
                AccessGroupId = dto.AccessGroupId,
                Tags = dto.Tags ?? [],
                Categories = dto.Categories ?? [],
                Description = dto.Description ?? string.Empty,
                PublicViewing = dto.PublicViewing,
                PublicDownload = dto.PublicDownload
            };

            return await _persistenceService.CreateFileFromTempFileAsync(fetchDto, cancellationToken);
        }
        finally
        {
            TemporaryFileCleanup.DeleteIfPresent(tempFilePath, _logger);
        }
    }

    private static Uri ParseFileUrl(string fileUrl)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("FileUrl must be an absolute HTTP or HTTPS URL.", nameof(fileUrl));
        }

        return uri;
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var base64 = Convert.ToBase64String(bytes);
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GetExtensionFromResponse(HttpResponseMessage resp)
    {
        var mediaType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var ext = GetExtensionFromContentType(mediaType);

        if (!string.IsNullOrWhiteSpace(ext))
            return ext;

        if (resp.Content.Headers.ContentDisposition != null)
        {
            var cd = resp.Content.Headers.ContentDisposition;
            var fileName = cd.FileNameStar ?? cd.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var fileExt = Path.GetExtension(fileName).TrimStart('.');
                if (!string.IsNullOrWhiteSpace(fileExt))
                    return fileExt;
            }
        }

        return string.Empty;
    }

    private static string GetExtensionFromContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return string.Empty;
        contentType = contentType.ToLowerInvariant();

        return contentType switch
        {
            "video/mp4" => "mp4",
            "video/webm" => "webm",
            "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/ogg" => "ogg",
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "application/pdf" => "pdf",
            "application/zip" => "zip",
            "application/octet-stream" => "bin",
            "text/plain" => "txt",
            _ when contentType.Contains("mp4") => "mp4",
            _ when contentType.Contains("mpeg") => "mp3",
            _ when contentType.Contains("jpeg") || contentType.Contains("jpg") => "jpg",
            _ when contentType.Contains("png") => "png",
            _ => string.Empty
        };
    }

    private void EnsureDeclaredContentLengthWithinMaximum(HttpContent content)
    {
        if (content.Headers.ContentLength is long contentLength)
            FileSizePolicy.EnsureWithinMaximum(contentLength, _persistenceService.MaximumFileSizeBytes);
    }
}
