namespace MSAVA_App.Services.Endpoints;

internal class DebugHttpHandler : DelegatingHandler
{
    private const string RedactedValue = "[redacted]";

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key"
    };

    private static readonly HashSet<string> SensitiveQueryParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "access_token",
        "api_key",
        "apikey",
        "client_secret",
        "code",
        "id_token",
        "key",
        "password",
        "refresh_token",
        "secret",
        "sig",
        "signature",
        "token"
    };

    private readonly ILogger<DebugHttpHandler> _logger;

    public DebugHttpHandler(ILogger<DebugHttpHandler> logger, HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler())
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected async override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "Unsuccessful API call {Method} {Uri} returned {StatusCode}",
                request.Method,
                FormatRequestUri(request.RequestUri),
                response.StatusCode);

            LogHeaders(request.Headers);

            if (request.Content is not null)
            {
                LogHeaders(request.Content.Headers);
                _logger.LogDebug(
                    "Request body redacted. Content-Type={ContentType}; Content-Length={ContentLength}",
                    request.Content.Headers.ContentType?.ToString() ?? "unknown",
                    request.Content.Headers.ContentLength);
            }
        }

        return response;
    }

    private void LogHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        foreach (var header in headers)
        {
            _logger.LogDebug(
                "{HeaderName}: {HeaderValue}",
                header.Key,
                FormatHeaderValue(header.Key, header.Value));
        }
    }

    private static string FormatHeaderValue(string headerName, IEnumerable<string> headerValues)
    {
        if (SensitiveHeaderNames.Contains(headerName))
            return RedactedValue;

        return string.Join(", ", headerValues);
    }

    private static string FormatRequestUri(Uri? requestUri)
    {
        if (requestUri is null)
            return "unknown";

        var text = requestUri.IsAbsoluteUri ? FormatAbsoluteUri(requestUri) : requestUri.ToString();
        var fragmentStart = text.IndexOf('#', StringComparison.Ordinal);
        var fragment = fragmentStart < 0 ? string.Empty : $"#{RedactedValue}";
        var textWithoutFragment = fragmentStart < 0 ? text : text[..fragmentStart];

        var queryStart = textWithoutFragment.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return textWithoutFragment + fragment;

        var query = textWithoutFragment[(queryStart + 1)..];

        return textWithoutFragment[..(queryStart + 1)] + FormatQueryString(query) + fragment;
    }

    private static string FormatQueryString(string query)
    {
        if (string.IsNullOrEmpty(query))
            return query;

        var parameters = query.Split('&');
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (string.IsNullOrEmpty(parameter))
                continue;

            var separator = parameter.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? parameter : parameter[..separator];
            var decodedName = Uri.UnescapeDataString(name.Replace("+", " ", StringComparison.Ordinal));
            if (!SensitiveQueryParameterNames.Contains(decodedName))
                continue;

            parameters[i] = $"{name}={RedactedValue}";
        }

        return string.Join("&", parameters);
    }

    private static string FormatAbsoluteUri(Uri requestUri)
    {
        var text = requestUri.AbsoluteUri;
        if (string.IsNullOrEmpty(requestUri.UserInfo))
            return text;

        var authorityStart = text.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
            return text;

        authorityStart += 3;
        var authorityEnd = text.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
            authorityEnd = text.Length;

        var atSign = text.LastIndexOf('@', authorityEnd - 1, authorityEnd - authorityStart);
        return atSign < authorityStart
            ? text
            : text[..authorityStart] + RedactedValue + text[atSign..];
    }
}
