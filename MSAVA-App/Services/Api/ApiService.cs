using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MSAVA_App.Models;
using MSAVA_App.Services;

namespace MSAVA_App.Services.Api;
public class ApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApiService> _logger;
    private readonly Uri _baseAddress;

    // Cached access token for automatic auth header injection
    private string? _accessToken;

    public ApiService(
        IHttpClientFactory httpClientFactory,
        ApiClientOptions apiClientOptions,
        ILogger<ApiService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        ArgumentNullException.ThrowIfNull(apiClientOptions);
        _baseAddress = apiClientOptions.RequireBaseAddress();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static class Routes
    {
        public const string AuthLogin = "api/auth/login";
        public const string AuthRegister = "api/auth/register";
        public const string UsersMe = "api/users/me";
        public const string UsersSession = "api/users/session";
        public const string FilesRetrieveMetaAll = "api/files/retrieve/meta/all";
        public const string FilesStoreStream = "api/files/store/stream";
        public const string FilesStoreUrl = "api/files/store/url";
        public const string FilesStoreFormFile = "api/files/store/formfile";
    }

    public void SetAccessToken(string? token)
    {
        _accessToken = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public void ClearAccessToken() => _accessToken = null;

    public HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("MSAVA-Api");
        if (client.BaseAddress == null)
        {
            client.BaseAddress = _baseAddress;
        }
        // Default to JSON
        if (!client.DefaultRequestHeaders.Accept.Contains(new MediaTypeWithQualityHeaderValue("application/json")))
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        return client;
    }

    public HttpRequestMessage CreateJsonRequest(HttpMethod method, string relativeUrl, bool anonymous = false)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        AttachAuthorizationHeader(request, anonymous);
        return request;
    }

    public HttpRequestMessage CreateJsonRequest<TBody>(
        HttpMethod method,
        string relativeUrl,
        TBody body,
        JsonTypeInfo<TBody> bodyTypeInfo,
        bool anonymous = false)
    {
        ArgumentNullException.ThrowIfNull(bodyTypeInfo);

        var request = CreateJsonRequest(method, relativeUrl, anonymous);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, bodyTypeInfo),
            Encoding.UTF8,
            "application/json");

        return request;
    }

    public HttpRequestMessage CreateMultipartRequest(HttpMethod method, string relativeUrl, MultipartFormDataContent content, bool anonymous = false)
    {
        var request = new HttpRequestMessage(method, relativeUrl)
        {
            Content = content
        };
        AttachAuthorizationHeader(request, anonymous);
        return request;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public async Task<T?> SendForAsync<T>(
        HttpMethod method,
        string relativeUrl,
        JsonTypeInfo<T> responseTypeInfo,
        CancellationToken cancellationToken = default,
        bool anonymous = false)
    {
        using var msg = CreateJsonRequest(method, relativeUrl, anonymous);
        using var resp = await SendAsync(msg, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("API call {Method} {Url} failed with status {Status}", method, relativeUrl, resp.StatusCode);
            return default;
        }

        return await ReadJsonResponseAsync<T>(
            resp,
            relativeUrl,
            ApiResponseKind.Json,
            responseTypeInfo,
            cancellationToken);
    }

    public async Task<T?> SendMultipartForAsync<T>(
        HttpMethod method,
        string relativeUrl,
        MultipartFormDataContent content,
        JsonTypeInfo<T> responseTypeInfo,
        CancellationToken cancellationToken = default,
        bool anonymous = false)
    {
        using var msg = CreateMultipartRequest(method, relativeUrl, content, anonymous);
        using var resp = await SendAsync(msg, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("API multipart call {Method} {Url} failed with status {Status}", method, relativeUrl, resp.StatusCode);
            return default;
        }

        return await ReadJsonResponseAsync<T>(
            resp,
            relativeUrl,
            ApiResponseKind.Multipart,
            responseTypeInfo,
            cancellationToken);
    }

    private async Task<T?> ReadJsonResponseAsync<T>(
        HttpResponseMessage response,
        string relativeUrl,
        ApiResponseKind responseKind,
        JsonTypeInfo<T> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responseTypeInfo);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync(stream, responseTypeInfo, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableDeserializationFailure(ex))
        {
            LogDeserializationFailure(ex, responseKind, relativeUrl);
            return default;
        }
    }

    private void AttachAuthorizationHeader(HttpRequestMessage request, bool anonymous)
    {
        if (!anonymous && !string.IsNullOrWhiteSpace(_accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }

    private static bool IsRecoverableDeserializationFailure(Exception exception)
    {
        if (AppCriticalExceptionPolicy.ContainsCriticalException(exception))
            return false;

        return exception is JsonException
            or NotSupportedException
            or InvalidOperationException
            or IOException;
    }

    private void LogDeserializationFailure(Exception exception, ApiResponseKind responseKind, string relativeUrl)
    {
        if (responseKind == ApiResponseKind.Multipart)
        {
            _logger.LogError(exception, "Failed to deserialize multipart API response for {Url}", relativeUrl);
            return;
        }

        _logger.LogError(exception, "Failed to deserialize API response for {Url}", relativeUrl);
    }

    private enum ApiResponseKind
    {
        Json,
        Multipart
    }
}
