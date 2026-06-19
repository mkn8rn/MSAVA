using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_App.Models;
using MSAVA_App.Services.Api;

namespace MSAVA_App.Tests;

public class ApiServiceTests
{
    [Test]
    public void Constructor_RejectsMissingApiBaseUrl()
    {
        Action act = () => _ = new ApiService(
            new StaticHttpClientFactory(new HttpClient()),
            new ApiClientOptions(),
            NullLogger<ApiService>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("ApiClient:Url must be an absolute HTTP or HTTPS URL.");
    }

    [Test]
    public void Constructor_RejectsNonHttpApiBaseUrl()
    {
        Action act = () => _ = new ApiService(
            new StaticHttpClientFactory(new HttpClient()),
            new ApiClientOptions { Url = "file:///tmp/msava" },
            NullLogger<ApiService>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("ApiClient:Url must be an absolute HTTP or HTTPS URL.");
    }

    [TestCase("https://user:pass@api.msava.test/")]
    [TestCase("https://api.msava.test/?tenant=alpha")]
    [TestCase("https://api.msava.test/#fragment")]
    public void Constructor_RejectsApiBaseUrlWithUnsafeComponents(string url)
    {
        Action act = () => _ = new ApiService(
            new StaticHttpClientFactory(new HttpClient()),
            new ApiClientOptions { Url = url },
            NullLogger<ApiService>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("ApiClient:Url must not contain user info, query, or fragment components.");
    }

    [Test]
    public void CreateClient_AppliesConfiguredBaseAddressWhenFactoryClientHasNone()
    {
        var api = new ApiService(
            new StaticHttpClientFactory(new HttpClient()),
            new ApiClientOptions { Url = "https://api.msava.test/" },
            NullLogger<ApiService>.Instance);

        var client = api.CreateClient();

        client.BaseAddress.Should().Be(new Uri("https://api.msava.test/"));
    }

    [Test]
    public void CreateClient_CanonicalizesConfiguredPathBaseWithTrailingSlash()
    {
        var api = new ApiService(
            new StaticHttpClientFactory(new HttpClient()),
            new ApiClientOptions { Url = "https://api.msava.test/msava" },
            NullLogger<ApiService>.Instance);

        var client = api.CreateClient();

        client.BaseAddress.Should().Be(new Uri("https://api.msava.test/msava/"));
    }

    [Test]
    public void CreateClient_OverridesFactoryBaseAddressWithConfiguredBaseAddress()
    {
        using var factoryClient = new HttpClient
        {
            BaseAddress = new Uri("https://wrong-api.msava.test/")
        };
        var api = new ApiService(
            new StaticHttpClientFactory(factoryClient),
            new ApiClientOptions { Url = "https://api.msava.test/" },
            NullLogger<ApiService>.Instance);

        var client = api.CreateClient();

        client.BaseAddress.Should().Be(new Uri("https://api.msava.test/"));
    }

    [Test]
    public async Task CreateJsonRequest_SerializesBodyWithProvidedJsonMetadata()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));

        using var request = api.CreateJsonRequest(
            HttpMethod.Post,
            "api/test",
            new ApiServiceTestPayload("alpha"),
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload,
            anonymous: true);

        string body = await request.Content!.ReadAsStringAsync();

        body.Should().Be("""{"value":"alpha"}""");
    }

    [Test]
    public void CreateJsonRequest_AttachesTrimmedBearerToken()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));
        api.SetAccessToken("  sensitive-token  ");

        using var request = api.CreateJsonRequest(HttpMethod.Get, "api/test");

        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("sensitive-token");
    }

    [Test]
    public void CreateJsonRequestWithAccessToken_AttachesTrimmedExplicitBearerToken()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));
        api.SetAccessToken("cached-token");

        using var request = api.CreateJsonRequestWithAccessToken(
            HttpMethod.Get,
            "api/users/session",
            "  login-token  ");

        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("login-token");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void CreateJsonRequestWithAccessToken_RejectsMissingExplicitBearerToken(string? token)
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));

        Action act = () =>
        {
            using var _ = api.CreateJsonRequestWithAccessToken(HttpMethod.Get, "api/users/session", token);
        };

        act.Should().Throw<ArgumentException>()
            .WithMessage("Access token is required.*");
    }

    [TestCase("bad\ntoken")]
    [TestCase("bad\u0000token")]
    [TestCase("bad token")]
    [TestCase("bad,token")]
    public void CreateJsonRequestWithAccessToken_RejectsInvalidTokenCharacters(string token)
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));

        Action act = () =>
        {
            using var _ = api.CreateJsonRequestWithAccessToken(HttpMethod.Get, "api/users/session", token);
        };

        act.Should().Throw<ArgumentException>()
            .WithMessage("Access token contains invalid characters.*");
    }

    [Test]
    public void CreateJsonRequest_DoesNotAttachBearerTokenForAnonymousRequest()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));
        api.SetAccessToken("sensitive-token");

        using var request = api.CreateJsonRequest(HttpMethod.Get, "api/auth/login", anonymous: true);

        request.Headers.Authorization.Should().BeNull();
    }

    [Test]
    public void ClearAccessToken_RemovesBearerTokenFromLaterRequests()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));
        api.SetAccessToken("sensitive-token");

        api.ClearAccessToken();

        using var request = api.CreateJsonRequest(HttpMethod.Get, "api/test");
        request.Headers.Authorization.Should().BeNull();
    }

    [TestCase("bad\ntoken")]
    [TestCase("bad\u0000token")]
    [TestCase("bad token")]
    [TestCase("bad,token")]
    public void SetAccessToken_RejectsInvalidTokenCharacters(string token)
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));

        Action act = () => api.SetAccessToken(token);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Access token contains invalid characters.*");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void SetAccessToken_ClearsMissingToken(string? token)
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));
        api.SetAccessToken("sensitive-token");

        api.SetAccessToken(token);

        using var request = api.CreateJsonRequest(HttpMethod.Get, "api/test");
        request.Headers.Authorization.Should().BeNull();
    }

    [TestCase("https://evil.example/api")]
    [TestCase("http://evil.example/api")]
    [TestCase("//evil.example/api")]
    [TestCase(@"\\evil.example\api")]
    [TestCase("/api/test")]
    [TestCase("../api/test")]
    [TestCase("api/../test")]
    [TestCase("api/%2e%2e/test")]
    [TestCase(@"api\test")]
    [TestCase(" api/test")]
    [TestCase("api/test ")]
    [TestCase("?take=10")]
    [TestCase("api/test#fragment")]
    public void CreateJsonRequest_RejectsNonRelativeApiRoutes(string route)
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));
        api.SetAccessToken("sensitive-token");

        Action act = () =>
        {
            using var _ = api.CreateJsonRequest(HttpMethod.Get, route);
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("API routes must be relative paths.");
    }

    [Test]
    public async Task SendForAsync_SendsRelativeRouteUnderConfiguredPathBase()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":"alpha"}""", Encoding.UTF8, "application/json")
        };
        var handler = new RecordingHttpMessageHandler(response);
        var api = new ApiService(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new ApiClientOptions { Url = "https://api.msava.test/msava" },
            NullLogger<ApiService>.Instance);

        var result = await api.SendForAsync(
            HttpMethod.Get,
            "api/test",
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        result.Should().Be(new ApiServiceTestPayload("alpha"));
        handler.RequestUri.Should().Be(new Uri("https://api.msava.test/msava/api/test"));
    }

    [Test]
    public async Task SendForAsync_RedactsQueryStringFromFailureLog()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var handler = new RecordingHttpMessageHandler(response);
        var logger = new CapturingLogger<ApiService>();
        var api = new ApiService(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new ApiClientOptions { Url = "https://api.msava.test/" },
            logger);
        const string route = "api/test?filter=recent&access_token=super-secret-token";

        var result = await api.SendForAsync(
            HttpMethod.Get,
            route,
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        result.Should().BeNull();
        handler.RequestUri.Should().Be(new Uri("https://api.msava.test/" + route));
        logger.Messages.Should().ContainSingle();
        string logText = logger.Messages[0].Text;
        logText.Should().Contain("api/test?[redacted]");
        logText.Should().NotContain("super-secret-token");
        logText.Should().NotContain("filter=recent");
    }

    [TestCase("")]
    [TestCase(" ")]
    public void CreateJsonRequest_RejectsMissingApiRoute(string route)
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));

        Action act = () =>
        {
            using var _ = api.CreateJsonRequest(HttpMethod.Get, route);
        };

        act.Should().Throw<ArgumentException>()
            .WithMessage("API route must be provided.*");
    }

    [Test]
    public void CreateMultipartRequest_RejectsNonRelativeApiRoutes()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK));
        api.SetAccessToken("sensitive-token");
        using var content = new MultipartFormDataContent();

        Action act = () =>
        {
            using var _ = api.CreateMultipartRequest(
                HttpMethod.Post,
                "https://evil.example/api",
                content);
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("API routes must be relative paths.");
    }

    [Test]
    public async Task SendForAsync_DeserializesResponseWithProvidedJsonMetadata()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":"alpha"}""", Encoding.UTF8, "application/json")
        });

        var result = await api.SendForAsync(
            HttpMethod.Get,
            "api/test",
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        result.Should().Be(new ApiServiceTestPayload("alpha"));
    }

    [Test]
    public async Task SendForAsync_PropagatesCancellationDuringJsonDeserialization()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new CancelledJsonContent()
        });

        var act = async () => await api.SendForAsync(
            HttpMethod.Get,
            "api/test",
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task SendForAsync_PropagatesWrappedCancellationDuringJsonDeserialization()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingJsonContent(new InvalidOperationException(
                "wrapped cancellation",
                new OperationCanceledException("cancelled")))
        });

        var act = async () => await api.SendForAsync(
            HttpMethod.Get,
            "api/test",
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("wrapped cancellation");
    }

    [Test]
    public async Task SendMultipartForAsync_PropagatesCancellationDuringJsonDeserialization()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new CancelledJsonContent()
        });
        using var content = new MultipartFormDataContent();

        var act = async () => await api.SendMultipartForAsync(
            HttpMethod.Post,
            "api/test",
            content,
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task SendForAsync_PropagatesCriticalDeserializationFailure()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingJsonContent(new OutOfMemoryException("Critical memory failure."))
        });

        var act = async () => await api.SendForAsync(
            HttpMethod.Get,
            "api/test",
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        await act.Should().ThrowAsync<OutOfMemoryException>();
    }

    [Test]
    public async Task SendForAsync_PropagatesWrappedCriticalDeserializationFailure()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingJsonContent(new InvalidOperationException(
                "wrapped native failure",
                new AccessViolationException("native failure")))
        });

        var act = async () => await api.SendForAsync(
            HttpMethod.Get,
            "api/test",
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("wrapped native failure");
    }

    [Test]
    public async Task SendMultipartForAsync_PropagatesCriticalDeserializationFailure()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingJsonContent(new OutOfMemoryException("Critical memory failure."))
        });
        using var content = new MultipartFormDataContent();

        var act = async () => await api.SendMultipartForAsync(
            HttpMethod.Post,
            "api/test",
            content,
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        await act.Should().ThrowAsync<OutOfMemoryException>();
    }

    [Test]
    public async Task SendForAsync_ReturnsDefaultForInvalidJson()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        });

        var result = await api.SendForAsync(
            HttpMethod.Get,
            "api/test",
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        result.Should().BeNull();
    }

    [Test]
    public async Task SendForAsync_RedactsQueryStringFromDeserializationFailureLog()
    {
        var logger = new CapturingLogger<ApiService>();
        var api = new ApiService(
            new StaticHttpClientFactory(new HttpClient(new StaticHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
                }))),
            new ApiClientOptions { Url = "https://api.msava.test/" },
            logger);

        var result = await api.SendForAsync(
            HttpMethod.Get,
            "api/test?downloadToken=super-secret-download-token",
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        result.Should().BeNull();
        logger.Messages.Should().ContainSingle();
        string logText = logger.Messages[0].Text;
        logText.Should().Contain("api/test?[redacted]");
        logText.Should().NotContain("super-secret-download-token");
    }

    [Test]
    public async Task SendMultipartForAsync_ReturnsDefaultForInvalidJson()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        });
        using var content = new MultipartFormDataContent();

        var result = await api.SendMultipartForAsync(
            HttpMethod.Post,
            "api/test",
            content,
            ApiServiceTestJsonSerializerContext.Default.ApiServiceTestPayload);

        result.Should().BeNull();
    }

    private static ApiService CreateApi(HttpResponseMessage response)
    {
        return new ApiService(
            new StaticHttpClientFactory(new HttpClient(new StaticHttpMessageHandler(response))),
            new ApiClientOptions { Url = "https://api.msava.test/" },
            NullLogger<ApiService>.Instance);
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

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StaticHttpMessageHandler(HttpResponseMessage response)
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

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
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
            Messages.Add(new LogMessage(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogMessage(LogLevel Level, Exception? Exception, string Text);

    private sealed class CancelledJsonContent : HttpContent
    {
        public CancelledJsonContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new OperationCanceledException();
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ThrowingJsonContent : HttpContent
    {
        private readonly Exception _exception;

        public ThrowingJsonContent(Exception exception)
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
            _exception = exception;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw _exception;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

internal sealed record ApiServiceTestPayload(string Value);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ApiServiceTestPayload))]
internal sealed partial class ApiServiceTestJsonSerializerContext : JsonSerializerContext;
