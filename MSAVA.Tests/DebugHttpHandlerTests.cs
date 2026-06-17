using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using MSAVA_App.Services.Endpoints;

namespace MSAVA_App.Tests;

public class DebugHttpHandlerTests
{
    [Test]
    public async Task SendAsync_LogsFailedRequestWithoutCredentialValues()
    {
        var logger = new CapturingLogger<DebugHttpHandler>();
        using var handler = new DebugHttpHandler(
            logger,
            new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.msava.test/api/auth/login")
        {
            Content = new StringContent(
                """{"username":"marko","password":"super-secret-password"}""",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "super-secret-token");
        request.Headers.Add("Cookie", "session=super-secret-cookie");
        request.Headers.Add("X-Api-Key", "super-secret-api-key");
        request.Headers.Add("X-Correlation-Id", "correlation-123");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string logText = string.Join(Environment.NewLine, logger.Messages.Select(message => message.Text));
        logText.Should().Contain("Unsuccessful API call POST https://api.msava.test/api/auth/login returned BadRequest");
        logText.Should().Contain("Authorization: [redacted]");
        logText.Should().Contain("Cookie: [redacted]");
        logText.Should().Contain("X-Api-Key: [redacted]");
        logText.Should().Contain("X-Correlation-Id: correlation-123");
        logText.Should().Contain("Request body redacted.");
        logText.Should().NotContain("super-secret-password");
        logText.Should().NotContain("super-secret-token");
        logText.Should().NotContain("super-secret-cookie");
        logText.Should().NotContain("super-secret-api-key");
    }

    [Test]
    public async Task SendAsync_RedactsCredentialQueryValuesFromFailedRequestUri()
    {
        var logger = new CapturingLogger<DebugHttpHandler>();
        using var handler = new DebugHttpHandler(
            logger,
            new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        using var invoker = new HttpMessageInvoker(handler);
        const string requestUri =
            "https://api.msava.test/api/files/retrieve/meta/all?filter=recent" +
            "&access_token=super-secret-token&api_key=super-secret-key&sig=super-secret-signature";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            requestUri);

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string logText = string.Join(Environment.NewLine, logger.Messages.Select(message => message.Text));
        logText.Should().Contain("filter=recent");
        logText.Should().Contain("access_token=[redacted]");
        logText.Should().Contain("api_key=[redacted]");
        logText.Should().Contain("sig=[redacted]");
        logText.Should().NotContain("super-secret-token");
        logText.Should().NotContain("super-secret-key");
        logText.Should().NotContain("super-secret-signature");
    }

    [Test]
    public async Task SendAsync_RedactsRequestUriFragmentFromFailedRequestUri()
    {
        var logger = new CapturingLogger<DebugHttpHandler>();
        using var handler = new DebugHttpHandler(
            logger,
            new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        using var invoker = new HttpMessageInvoker(handler);
        const string requestUri =
            "https://api.msava.test/api/auth/callback?state=visible" +
            "#access_token=super-secret-fragment-token&id_token=super-secret-id-token";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            requestUri);

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string logText = string.Join(Environment.NewLine, logger.Messages.Select(message => message.Text));
        logText.Should().Contain("https://api.msava.test/api/auth/callback?state=visible#[redacted]");
        logText.Should().NotContain("super-secret-fragment-token");
        logText.Should().NotContain("super-secret-id-token");
    }

    [Test]
    public async Task SendAsync_DoesNotLogSuccessfulRequest()
    {
        var logger = new CapturingLogger<DebugHttpHandler>();
        using var handler = new DebugHttpHandler(
            logger,
            new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.msava.test/api/users/session");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        logger.Messages.Should().BeEmpty();
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StaticResponseHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogMessage> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(new LogMessage(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogMessage(LogLevel Level, string Text);
}
