using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MSAVA_App.Services;
using MSAVA_App.Services.Api;
using MSAVA_Shared.Models;

namespace MSAVA_App.Services.Files;

public class FileUploadClientService
{
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
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName is required", nameof(fileName));
        if (string.IsNullOrWhiteSpace(fileExtension)) throw new ArgumentException("fileExtension is required", nameof(fileExtension));
        if (fileStream is null) throw new ArgumentNullException(nameof(fileStream));
        if (accessGroupId == Guid.Empty) throw new ArgumentException("accessGroupId is required", nameof(accessGroupId));

        var content = new MultipartFormDataContent();

        // Required simple fields
        content.Add(new StringContent(fileName), FileUploadFormFields.FileName);
        content.Add(new StringContent(fileExtension), FileUploadFormFields.FileExtension);
        content.Add(new StringContent(accessGroupId.ToString()), FileUploadFormFields.AccessGroupId);
        content.Add(new StringContent((description ?? string.Empty)), FileUploadFormFields.Description);
        content.Add(new StringContent(publicViewing.ToString()), FileUploadFormFields.PublicViewing);
        content.Add(new StringContent(publicDownload.ToString()), FileUploadFormFields.PublicDownload);

        // Collections: send as repeated form keys: Tags=value
        if (tags != null)
        {
            foreach (var t in tags)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    content.Add(new StringContent(t), FileUploadFormFields.Tags);
                }
            }
        }
        if (categories != null)
        {
            foreach (var c in categories)
            {
                if (!string.IsNullOrWhiteSpace(c))
                {
                    content.Add(new StringContent(c), FileUploadFormFields.Categories);
                }
            }
        }

        // File content as StreamContent named "FormFile"
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, FileUploadFormFields.FormFile, fileName + "." + fileExtension.TrimStart('.'));

        using var msg = _api.CreateMultipartRequest(HttpMethod.Post, ApiService.Routes.FilesStoreFormFile, content);
        using var resp = await _api.SendAsync(msg, ct);
        var status = (int)resp.StatusCode;

        return resp.IsSuccessStatusCode
            ? await ReadSuccessfulUploadOutcomeAsync(resp, status, ct)
            : await ReadFailedUploadOutcomeAsync(resp, status, ct);
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
            return new UploadOutcome(true, statusCode, id.ToString(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableResponseBodyFailure(ex))
        {
            _logger.LogError(ex, "Failed to parse GUID from upload response");
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return new UploadOutcome(true, statusCode, raw, null);
        }
    }

    private async Task<UploadOutcome> ReadFailedUploadOutcomeAsync(
        HttpResponseMessage response,
        int statusCode,
        CancellationToken cancellationToken)
    {
        var error = await ReadFailedUploadErrorAsync(response, cancellationToken);
        return new UploadOutcome(false, statusCode, null, error);
    }

    private async Task<string> ReadFailedUploadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableResponseBodyFailure(ex))
        {
            _logger.LogWarning(ex, "Failed to read upload error response body");
            return response.ReasonPhrase ?? "Unknown error";
        }
    }

    private static bool IsRecoverableResponseBodyFailure(Exception exception)
    {
        if (AppCriticalExceptionPolicy.ContainsCriticalException(exception))
            return false;

        return exception is HttpRequestException
            or JsonException
            or NotSupportedException
            or InvalidOperationException
            or IOException;
    }
}
