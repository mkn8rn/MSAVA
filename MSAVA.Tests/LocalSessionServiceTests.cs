using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_App.Models;
using MSAVA_App.Services.Api;
using MSAVA_App.Services.Session;

namespace MSAVA_App.Tests;

public class LocalSessionServiceTests
{
    [Test]
    public async Task LoginAsync_PropagatesCallerCancellationWithoutChangingSessionState()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var service = CreateService(handler);

        var act = async () => await service.LoginAsync("alice", "password", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        service.AccessToken.Should().BeNull();
        service.CurrentSession.Should().BeNull();
        service.IsLoggedIn.Should().BeFalse();
    }

    [Test]
    public async Task LoginAsync_ReturnsNullForRecoverableHttpFailure()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Network unavailable.")));
        var service = CreateService(handler);

        var result = await service.LoginAsync("alice", "password");

        result.Should().BeNull();
        service.AccessToken.Should().BeNull();
        service.CurrentSession.Should().BeNull();
        service.IsLoggedIn.Should().BeFalse();
    }

    [Test]
    public async Task LoginAsync_PropagatesCriticalFailureWithoutChangingSessionState()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new OutOfMemoryException("Critical memory failure.")));
        var service = CreateService(handler);

        var act = async () => await service.LoginAsync("alice", "password");

        await act.Should().ThrowAsync<OutOfMemoryException>();
        service.AccessToken.Should().BeNull();
        service.CurrentSession.Should().BeNull();
        service.IsLoggedIn.Should().BeFalse();
    }

    [Test]
    public async Task LoginAsync_PropagatesWrappedCriticalFailureWithoutChangingSessionState()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException(
                "wrapped native failure",
                new AccessViolationException("native failure"))));
        var service = CreateService(handler);

        var act = async () => await service.LoginAsync("alice", "password");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("wrapped native failure");
        service.AccessToken.Should().BeNull();
        service.CurrentSession.Should().BeNull();
        service.IsLoggedIn.Should().BeFalse();
    }

    [Test]
    public async Task LoginAsync_ReturnsNullForHttpTimeoutWhenCallerDidNotCancel()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Request timed out.")));
        var service = CreateService(handler);

        var result = await service.LoginAsync("alice", "password");

        result.Should().BeNull();
        service.AccessToken.Should().BeNull();
        service.CurrentSession.Should().BeNull();
        service.IsLoggedIn.Should().BeFalse();
    }

    [Test]
    public async Task LoginAsync_StoresAccessTokenAndParsedSession()
    {
        var userId = Guid.NewGuid();
        string token = CreateToken($$"""
            {
              "sub": "{{userId}}",
              "unique_name": "alice",
              "role": "Admin"
            }
            """);
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{ "token": "{{token}}" }""", Encoding.UTF8, "application/json")
            }));
        var service = CreateService(handler);

        var result = await service.LoginAsync("alice", "password");

        result.Should().Be(token);
        service.AccessToken.Should().Be(token);
        service.IsLoggedIn.Should().BeTrue();
        service.CurrentSession.Should().NotBeNull();
        service.CurrentSession!.LoggedIn.Should().BeTrue();
        service.CurrentSession.UserId.Should().Be(userId);
        service.CurrentSession.Username.Should().Be("alice");
        service.CurrentSession.IsAdmin.Should().BeTrue();
    }

    [Test]
    public async Task LoginAsync_ReturnsNullWhenTokenDoesNotContainActiveSession()
    {
        string token = CreateToken("""
            {
              "unique_name": "alice",
              "role": "Admin"
            }
            """);
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{ "token": "{{token}}" }""", Encoding.UTF8, "application/json")
            }));
        var service = CreateService(handler);

        var result = await service.LoginAsync("alice", "password");

        result.Should().BeNull();
        service.AccessToken.Should().BeNull();
        service.CurrentSession.Should().BeNull();
        service.IsLoggedIn.Should().BeFalse();
    }

    private static LocalSessionService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var api = new ApiService(
            new StaticHttpClientFactory(client),
            new ApiClientOptions { Url = "https://api.msava.test/" },
            NullLogger<ApiService>.Instance);

        return new LocalSessionService(NullLogger<LocalSessionService>.Instance, api);
    }

    private static string CreateToken(string payloadJson)
    {
        return $"{Base64UrlEncode("{}")}.{Base64UrlEncode(payloadJson)}.signature";
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _responseFactory(request, cancellationToken);
        }
    }
}
