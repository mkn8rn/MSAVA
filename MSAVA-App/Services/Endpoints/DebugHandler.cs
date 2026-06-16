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
                request.RequestUri,
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
}
