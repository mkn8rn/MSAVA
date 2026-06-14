using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace MSAVA_BLL.Services.Import;

public class GoogleDriveImportService : IFileImportService<FetchFileGoogleDriveDTO>
{
    private readonly FilePersistenceService _persistenceService;
    private readonly ServiceLogger _serviceLogger;
    private readonly IHttpClientFactory _httpClientFactory;

    public GoogleDriveImportService(
        FilePersistenceService persistenceService,
        ServiceLogger serviceLogger,
        IHttpClientFactory httpClientFactory)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<Guid> ImportAsync(FetchFileGoogleDriveDTO dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.FileUrl))
            throw new ArgumentException("FileUrl must be provided.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));

        string? fileId = ExtractDriveFileId(dto.FileUrl)
            ?? throw new ArgumentException("Could not extract Google Drive file id from the provided FileUrl.", nameof(dto.FileUrl));

        string baseDownloadUrl = $"https://drive.google.com/uc?export=download&id={WebUtility.UrlEncode(fileId)}";

        var http = _httpClientFactory.CreateClient();
        using var initialResp = await http.GetAsync(baseDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!initialResp.IsSuccessStatusCode)
        {
            await ProviderHttpFailure.ThrowAsync("Google Drive initial request", initialResp, cancellationToken);
        }

        var contentType = initialResp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        string? downloadUrl = baseDownloadUrl;

        if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            var html = await initialResp.Content.ReadAsStringAsync(cancellationToken);

            if (html.Contains("docs.google.com"))
                throw new InvalidOperationException("The file is a native Google Docs/Sheets/Slides type and cannot be downloaded.");

            string? confirmToken = null;
            if (initialResp.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var m = Regex.Match(cookie, @"download_warning_[^=]+=([^;]+)");
                    if (m.Success)
                    {
                        confirmToken = WebUtility.UrlEncode(m.Groups[1].Value);
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(confirmToken))
            {
                var m2 = Regex.Match(html, @"confirm=([0-9A-Za-z_\-]+)");
                if (m2.Success) confirmToken = WebUtility.UrlEncode(m2.Groups[1].Value);
            }

            downloadUrl = !string.IsNullOrEmpty(confirmToken)
                ? $"{baseDownloadUrl}&confirm={confirmToken}"
                : $"{baseDownloadUrl}&confirm=t";
        }

        var tempFilePath = Path.GetTempFileName();
        _serviceLogger.LogInformation($"Downloading Google Drive file {fileId} to temp path {tempFilePath}");

        try
        {
            string finalExtension;
            using (var downloadResp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                if (!downloadResp.IsSuccessStatusCode)
                    await ProviderHttpFailure.ThrowAsync("Google Drive download", downloadResp, cancellationToken);

                var respMediaType = downloadResp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (respMediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Google Drive returned HTML instead of file content.");

                await using var ms = await downloadResp.Content.ReadAsStreamAsync(cancellationToken);
                await using var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await ms.CopyToAsync(fs, cancellationToken);

                var inferredExtension = GetExtensionFromContentType(downloadResp.Content.Headers.ContentType?.MediaType ?? string.Empty);
                finalExtension = string.IsNullOrWhiteSpace(inferredExtension) ? "bin" : inferredExtension;
            }

            var fetchDto = new SaveFileFromFetchDTO
            {
                FileName = "Google Drive File",
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
            DeleteTempFileIfPresent(tempFilePath);
        }
    }

    private static string? ExtractDriveFileId(string urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
            return null;

        if (Regex.IsMatch(urlOrId, @"^[A-Za-z0-9_\-]{10,100}$"))
            return urlOrId;

        try
        {
            var uri = new Uri(urlOrId);
            var s = uri.AbsoluteUri;

            var m = Regex.Match(s, @"/d/([A-Za-z0-9_\-]+)");
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(s, @"[?&]id=([A-Za-z0-9_\-]+)");
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(s, @"/uc\?id=([A-Za-z0-9_\-]+)");
            if (m.Success) return m.Groups[1].Value;
        }
        catch { }

        return null;
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
            _ when contentType.Contains("png") => "png",
            _ => string.Empty
        };
    }

    private static void DeleteTempFileIfPresent(string tempFilePath)
    {
        if (!File.Exists(tempFilePath))
            return;

        try { File.Delete(tempFilePath); } catch { /* best-effort temp cleanup */ }
    }
}
