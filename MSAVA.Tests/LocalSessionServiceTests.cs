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
    public async Task LoginAsync_ClearsPreviousSessionAndApiTokenWhenLoginFails()
    {
        var userId = Guid.NewGuid();
        const string token = "previous-token";
        var responses = new Queue<HttpResponseMessage>([
            CreateLoginResponse(token),
            CreateSessionResponse(userId, "database-alice", isAdmin: false, loggedIn: true),
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
        ]);
        var service = CreateService(
            new RecordingHttpMessageHandler((_, _) => Task.FromResult(responses.Dequeue())),
            out var api);
        await service.LoginAsync("alice", "password");

        var result = await service.LoginAsync("bob", "wrong-password");

        result.Should().BeNull();
        service.AccessToken.Should().BeNull();
        service.CurrentSession.Should().BeNull();
        service.IsLoggedIn.Should().BeFalse();
        using var request = api.CreateJsonRequest(HttpMethod.Get, ApiService.Routes.UsersSession);
        request.Headers.Authorization.Should().BeNull();
    }

    [Test]
    public async Task LoginAsync_StoresAccessTokenAndCurrentSessionFromApi()
    {
        var userId = Guid.NewGuid();
        const string token = "opaque-login-token";
        var requests = new List<(HttpMethod Method, string PathAndQuery, string? Authorization)>();
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            requests.Add(RecordRequest(request));

            return Task.FromResult(requests.Count switch
            {
                1 => CreateLoginResponse(token),
                2 => CreateSessionResponse(userId, "database-alice", isAdmin: false, loggedIn: true),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            });
        });
        var service = CreateService(handler);

        var result = await service.LoginAsync("alice", "password");

        requests.Should().HaveCount(2);
        requests[0].Method.Should().Be(HttpMethod.Post);
        requests[0].PathAndQuery.Should().Be("/api/auth/login");
        requests[0].Authorization.Should().BeNull();
        requests[1].Method.Should().Be(HttpMethod.Get);
        requests[1].PathAndQuery.Should().Be("/api/users/session");
        requests[1].Authorization.Should().Be($"Bearer {token}");
        result.Should().Be(token);
        service.AccessToken.Should().Be(token);
        service.IsLoggedIn.Should().BeTrue();
        service.CurrentSession.Should().NotBeNull();
        service.CurrentSession!.LoggedIn.Should().BeTrue();
        service.CurrentSession.UserId.Should().Be(userId);
        service.CurrentSession.Username.Should().Be("database-alice");
        service.CurrentSession.IsAdmin.Should().BeFalse("the app should use the API session response, not local token claims");
        service.CurrentSession.Claims["source"].Should().Equal("database");
    }

    [Test]
    public async Task LoginAsync_ReturnsNullWhenCurrentSessionIsInactive()
    {
        var userId = Guid.NewGuid();
        const string token = "opaque-inactive-token";
        var requests = new List<(HttpMethod Method, string PathAndQuery, string? Authorization)>();
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            requests.Add(RecordRequest(request));

            return Task.FromResult(requests.Count switch
            {
                1 => CreateLoginResponse(token),
                2 => CreateSessionResponse(userId, "database-alice", isAdmin: false, loggedIn: false),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            });
        });
        var service = CreateService(handler);

        var result = await service.LoginAsync("alice", "password");

        requests.Should().HaveCount(2);
        requests[1].Method.Should().Be(HttpMethod.Get);
        requests[1].PathAndQuery.Should().Be("/api/users/session");
        requests[1].Authorization.Should().Be($"Bearer {token}");
        result.Should().BeNull();
        service.AccessToken.Should().BeNull();
        service.CurrentSession.Should().BeNull();
        service.IsLoggedIn.Should().BeFalse();
    }

    private static LocalSessionService CreateService(HttpMessageHandler handler)
    {
        return CreateService(handler, out _);
    }

    private static LocalSessionService CreateService(
        HttpMessageHandler handler,
        out ApiService api)
    {
        var client = new HttpClient(handler);
        api = new ApiService(
            new StaticHttpClientFactory(client),
            new ApiClientOptions { Url = "https://api.msava.test/" },
            NullLogger<ApiService>.Instance);

        return new LocalSessionService(NullLogger<LocalSessionService>.Instance, api);
    }

    private static (HttpMethod Method, string PathAndQuery, string? Authorization) RecordRequest(
        HttpRequestMessage request)
    {
        return (
            request.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty,
            request.Headers.Authorization?.ToString());
    }

    private static HttpResponseMessage CreateLoginResponse(string token) =>
        CreateJsonResponse($$"""{ "token": "{{token}}" }""");

    private static HttpResponseMessage CreateSessionResponse(
        Guid userId,
        string username,
        bool isAdmin,
        bool loggedIn)
    {
        return CreateJsonResponse($$"""
            {
              "userId": "{{userId}}",
              "username": "{{username}}",
              "isAdmin": {{JsonBoolean(isAdmin)}},
              "isBanned": false,
              "isWhitelisted": true,
              "accessGroups": [],
              "roles": [],
              "claims": { "source": ["database"] },
              "issuedAt": "2026-06-16T10:00:00Z",
              "expiresAt": "2026-06-16T11:00:00Z",
              "loggedIn": {{JsonBoolean(loggedIn)}}
            }
            """);
    }

    private static HttpResponseMessage CreateJsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string JsonBoolean(bool value) => value ? "true" : "false";

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
