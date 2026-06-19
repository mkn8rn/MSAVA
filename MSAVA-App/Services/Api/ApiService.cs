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
using MSAVA_Shared.Diagnostics;
using MSAVA_Shared.Models;

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

    public void SetAccessToken(string? token) =>
        _accessToken = NormalizeAccessToken(token, nameof(token));

    public void ClearAccessToken() => _accessToken = null;

    public HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("MSAVA-Api");
        if (client.BaseAddress != _baseAddress)
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
        var request = new HttpRequestMessage(method, CreateRelativeApiUri(relativeUrl));
        AttachAuthorizationHeader(request, anonymous);
        return request;
    }

    public HttpRequestMessage CreateJsonRequestWithAccessToken(
        HttpMethod method,
        string relativeUrl,
        string? accessToken)
    {
        string normalizedToken = NormalizeRequiredAccessToken(accessToken, nameof(accessToken));
        var request = new HttpRequestMessage(method, CreateRelativeApiUri(relativeUrl));
        AttachBearerToken(request, normalizedToken);
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
        var request = new HttpRequestMessage(method, CreateRelativeApiUri(relativeUrl))
        {
            Content = content
        };
        AttachAuthorizationHeader(request, anonymous);
        return request;
    }

    private static Uri CreateRelativeApiUri(string relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl))
            throw new ArgumentException("API route must be provided.", nameof(relativeUrl));

        if (!string.Equals(relativeUrl, relativeUrl.Trim(), StringComparison.Ordinal) ||
            Uri.TryCreate(relativeUrl, UriKind.Absolute, out _) ||
            relativeUrl.StartsWith("/", StringComparison.Ordinal) ||
            relativeUrl.StartsWith("?", StringComparison.Ordinal) ||
            relativeUrl.Contains('#', StringComparison.Ordinal) ||
            relativeUrl.Contains('\\', StringComparison.Ordinal) ||
            ContainsDotSegment(relativeUrl) ||
            !Uri.TryCreate(relativeUrl, UriKind.Relative, out var uri))
        {
            throw new InvalidOperationException("API routes must be relative paths.");
        }

        return uri;
    }

    private static bool ContainsDotSegment(string relativeUrl)
    {
        int suffixStart = relativeUrl.IndexOfAny(['?', '#']);
        ReadOnlySpan<char> path = suffixStart >= 0
            ? relativeUrl.AsSpan(0, suffixStart)
            : relativeUrl.AsSpan();

        foreach (var range in path.Split('/'))
        {
            ReadOnlySpan<char> segment = path[range];
            if (segment is "." or "..")
                return true;

            if (segment.IndexOf('%') < 0)
                continue;

            string decodedSegment = Uri.UnescapeDataString(segment.ToString());
            if (decodedSegment is "." or "..")
                return true;
        }

        return false;
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
            _logger.LogWarning(
                "API call {Method} {Url} failed with status {Status}",
                method,
                SanitizeRelativeUrlForLog(relativeUrl),
                resp.StatusCode);
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
            _logger.LogWarning(
                "API multipart call {Method} {Url} failed with status {Status}",
                method,
                SanitizeRelativeUrlForLog(relativeUrl),
                resp.StatusCode);
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
        if (!anonymous && _accessToken is not null)
        {
            AttachBearerToken(request, _accessToken);
        }
    }

    private static void AttachBearerToken(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    internal static string? NormalizeAccessToken(string? token, string parameterName = "token")
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        string normalizedToken = token.Trim();
        if (normalizedToken.Length > AuthenticationTokenPolicy.MaximumTokenStringLength)
        {
            throw new ArgumentException(
                $"Access token must be {AuthenticationTokenPolicy.MaximumTokenStringLength} characters or fewer.",
                parameterName);
        }

        if (ContainsInvalidTokenCharacter(normalizedToken))
            throw new ArgumentException("Access token contains invalid characters.", parameterName);

        return normalizedToken;
    }

    private static string NormalizeRequiredAccessToken(string? token, string parameterName)
    {
        string? normalizedToken = NormalizeAccessToken(token, parameterName);
        if (normalizedToken is null)
            throw new ArgumentException("Access token is required.", parameterName);

        return normalizedToken;
    }

    private static bool ContainsInvalidTokenCharacter(string value)
    {
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) ||
                char.IsControl(character) ||
                character == ',')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRecoverableDeserializationFailure(Exception exception)
    {
        if (CriticalExceptionPolicy.ContainsCriticalException(exception))
            return false;

        return exception is JsonException
            or NotSupportedException
            or InvalidOperationException
            or IOException;
    }

    private void LogDeserializationFailure(Exception exception, ApiResponseKind responseKind, string relativeUrl)
    {
        string logUrl = SanitizeRelativeUrlForLog(relativeUrl);

        if (responseKind == ApiResponseKind.Multipart)
        {
            _logger.LogError(exception, "Failed to deserialize multipart API response for {Url}", logUrl);
            return;
        }

        _logger.LogError(exception, "Failed to deserialize API response for {Url}", logUrl);
    }

    private static string SanitizeRelativeUrlForLog(string relativeUrl)
    {
        int queryStart = relativeUrl.IndexOf('?');
        ReadOnlySpan<char> path = queryStart >= 0
            ? relativeUrl.AsSpan(0, queryStart)
            : relativeUrl.AsSpan();

        string sanitizedPath = SanitizeLogValue(path);
        return queryStart >= 0
            ? sanitizedPath + "?[redacted]"
            : sanitizedPath;
    }

    private static string SanitizeLogValue(ReadOnlySpan<char> value)
    {
        const int maximumLogValueLength = 256;
        Span<char> buffer = value.Length <= maximumLogValueLength
            ? stackalloc char[value.Length]
            : stackalloc char[maximumLogValueLength];

        int written = 0;
        foreach (char character in value)
        {
            if (written >= maximumLogValueLength)
                break;

            if (character is '\r' or '\n' or '\t')
            {
                buffer[written++] = ' ';
                continue;
            }

            if (char.IsControl(character))
                continue;

            buffer[written++] = character;
        }

        return new string(buffer[..written]);
    }

    private enum ApiResponseKind
    {
        Json,
        Multipart
    }
}
