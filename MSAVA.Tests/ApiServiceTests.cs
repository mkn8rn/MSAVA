using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_App.Services.Api;

namespace MSAVA_App.Tests;

public class ApiServiceTests
{
    [Test]
    public async Task SendForAsync_PropagatesCancellationDuringJsonDeserialization()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new CancelledJsonContent()
        });

        var act = async () => await api.SendForAsync<TestPayload>(HttpMethod.Get, "api/test");

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task SendMultipartForAsync_PropagatesCancellationDuringJsonDeserialization()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new CancelledJsonContent()
        });
        using var content = new MultipartFormDataContent();

        var act = async () => await api.SendMultipartForAsync<TestPayload>(HttpMethod.Post, "api/test", content);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task SendForAsync_ReturnsDefaultForInvalidJson()
    {
        var api = CreateApi(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        });

        var result = await api.SendForAsync<TestPayload>(HttpMethod.Get, "api/test");

        result.Should().BeNull();
    }

    private static ApiService CreateApi(HttpResponseMessage response)
    {
        return new ApiService(
            new StaticHttpClientFactory(new HttpClient(new StaticHttpMessageHandler(response))),
            NullLogger<ApiService>.Instance);
    }

    private sealed record TestPayload(string Value);

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
}
