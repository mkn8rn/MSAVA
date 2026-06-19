using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MSAVA_App.Services.Api;
using MSAVA_Shared.Diagnostics;
using MSAVA_Shared.Models;

namespace MSAVA_App.Services.Files;

public class FileUploadClientService
{
    public const string InvalidSuccessResponseMessage = "Upload response did not contain a valid file id.";
    public const string FailedUploadMessage = "Upload failed. Check the server logs for details.";

    private readonly ApiService _api;
    private readonly ILogger<FileUploadClientService> _logger;

    public FileUploadClientService(ApiService api, ILogger<FileUploadClientService> logger)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Call POST api/files/store/formfile (multipart/form-data) and return detailed outcome
    public async Task<UploadOutcome> CreateFileFromFormFileAsync(
        string fileName,
        string fileExtension,
        Stream fileStream,
        Guid accessGroupId,
        IEnumerable<string>? tags = null,
        IEnumerable<string>? categories = null,
        string? description = null,
        bool publicViewing = false,
        bool publicDownload = false,
        CancellationToken ct = default)
    {
        if (fileStream is null) throw new ArgumentNullException(nameof(fileStream));
        if (accessGroupId == Guid.Empty) throw new ArgumentException("accessGroupId is required", nameof(accessGroupId));

        string normalizedFileName = FileMetadataPolicy.NormalizeFileName(fileName);
        string normalizedFileExtension = FileMetadataPolicy.NormalizeFileExtensionSyntax(fileExtension);
        string normalizedDescription = FileMetadataPolicy.NormalizeDescription(description);
        List<string> normalizedTags = FileMetadataPolicy.NormalizeMetadataValues(tags, FileUploadFormFields.Tags);
        List<string> normalizedCategories = FileMetadataPolicy.NormalizeMetadataValues(categories, FileUploadFormFields.Categories);

        var content = new MultipartFormDataContent();

        // Required simple fields
        content.Add(new StringContent(normalizedFileName), FileUploadFormFields.FileName);
        content.Add(new StringContent(normalizedFileExtension), FileUploadFormFields.FileExtension);
        content.Add(new StringContent(accessGroupId.ToString()), FileUploadFormFields.AccessGroupId);
        content.Add(new StringContent(normalizedDescription), FileUploadFormFields.Description);
        content.Add(new StringContent(publicViewing.ToString()), FileUploadFormFields.PublicViewing);
        content.Add(new StringContent(publicDownload.ToString()), FileUploadFormFields.PublicDownload);

        // Collections: send as repeated form keys: Tags=value
        foreach (var tag in normalizedTags)
        {
            content.Add(new StringContent(tag), FileUploadFormFields.Tags);
        }

        foreach (var category in normalizedCategories)
        {
            content.Add(new StringContent(category), FileUploadFormFields.Categories);
        }

        // File content as StreamContent named "FormFile"
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, FileUploadFormFields.FormFile, $"upload.{normalizedFileExtension}");

        using var msg = _api.CreateMultipartRequest(HttpMethod.Post, ApiService.Routes.FilesStoreFormFile, content);
        using var resp = await _api.SendAsync(msg, ct);
        var status = (int)resp.StatusCode;

        if (resp.IsSuccessStatusCode)
            return await ReadSuccessfulUploadOutcomeAsync(resp, status, ct);

        return CreateFailedUploadOutcome(status);
    }

    private async Task<UploadOutcome> ReadSuccessfulUploadOutcomeAsync(
        HttpResponseMessage response,
        int statusCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var id = await JsonSerializer.DeserializeAsync(
                responseStream,
                AppJsonSerializerContext.Default.Guid,
                cancellationToken);
            if (id == Guid.Empty)
            {
                _logger.LogError("Upload response contained an empty file id");
                return new UploadOutcome(false, statusCode, null, InvalidSuccessResponseMessage);
            }

            return new UploadOutcome(true, statusCode, id.ToString(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableResponseBodyFailure(ex))
        {
            _logger.LogError(ex, "Failed to parse GUID from upload response");
            return new UploadOutcome(false, statusCode, null, InvalidSuccessResponseMessage);
        }
    }

    private static UploadOutcome CreateFailedUploadOutcome(int statusCode)
    {
        return new UploadOutcome(false, statusCode, null, FailedUploadMessage);
    }

    private static bool IsRecoverableResponseBodyFailure(Exception exception)
    {
        if (CriticalExceptionPolicy.ContainsCriticalException(exception))
            return false;

        return exception is HttpRequestException
            or JsonException
            or NotSupportedException
            or InvalidOperationException
            or IOException;
    }
}
