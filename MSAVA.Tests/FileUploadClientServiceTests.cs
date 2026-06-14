using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_App.Services.Api;
using MSAVA_App.Services.Files;

namespace MSAVA_App.Tests;

public class FileUploadClientServiceTests
{
    [Test]
    public async Task CreateFileFromFormFileAsync_ReturnsParsedGuidFromSuccessfulResponse()
    {
        var fileId = Guid.NewGuid();
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(fileId), Encoding.UTF8, "application/json")
        });

        var outcome = await CreateUploadAsync(service);

        outcome.Success.Should().BeTrue();
        outcome.StatusCode.Should().Be((int)HttpStatusCode.OK);
        outcome.Id.Should().Be(fileId.ToString());
        outcome.Error.Should().BeNull();
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_ReturnsRawBodyWhenSuccessfulResponseIsNotGuid()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("raw-upload-id", Encoding.UTF8, "text/plain")
        });

        var outcome = await CreateUploadAsync(service);

        outcome.Success.Should().BeTrue();
        outcome.StatusCode.Should().Be((int)HttpStatusCode.OK);
        outcome.Id.Should().Be("raw-upload-id");
        outcome.Error.Should().BeNull();
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_PropagatesCancellationWhileParsingSuccessfulResponse()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new FirstReadCancelledThenRawContent("raw-upload-id")
        });

        var act = async () => await CreateUploadAsync(service);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_PropagatesCancellationWhileReadingErrorResponse()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new CancelledContent()
        });

        var act = async () => await CreateUploadAsync(service);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_ReturnsErrorBodyFromFailedResponse()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new StringContent("upload rejected", Encoding.UTF8, "text/plain")
        });

        var outcome = await CreateUploadAsync(service);

        outcome.Success.Should().BeFalse();
        outcome.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        outcome.Id.Should().BeNull();
        outcome.Error.Should().Be("upload rejected");
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_ReturnsReasonPhraseWhenErrorBodyCannotBeRead()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new ThrowingContent()
        });

        var outcome = await CreateUploadAsync(service);

        outcome.Success.Should().BeFalse();
        outcome.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        outcome.Id.Should().BeNull();
        outcome.Error.Should().Be("Bad Request");
    }

    private static Task<UploadOutcome> CreateUploadAsync(FileUploadClientService service)
    {
        return service.CreateFileFromFormFileAsync(
            fileName: "sample",
            fileExtension: "txt",
            fileStream: new MemoryStream(Encoding.UTF8.GetBytes("content")),
            accessGroupId: Guid.NewGuid());
    }

    private static FileUploadClientService CreateService(HttpResponseMessage response)
    {
        var api = new ApiService(
            new StaticHttpClientFactory(new HttpClient(new StaticHttpMessageHandler(response))),
            NullLogger<ApiService>.Instance);

        return new FileUploadClientService(api, NullLogger<FileUploadClientService>.Instance);
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

    private sealed class CancelledContent : HttpContent
    {
        public CancelledContent()
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

    private sealed class ThrowingContent : HttpContent
    {
        public ThrowingContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new IOException("Unable to read content");
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            throw new IOException("Unable to read content");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class FirstReadCancelledThenRawContent : HttpContent
    {
        private readonly string _rawContent;
        private int _readAttempts;

        public FirstReadCancelledThenRawContent(string rawContent)
        {
            _rawContent = rawContent;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            if (Interlocked.Increment(ref _readAttempts) == 1)
                throw new OperationCanceledException();

            var bytes = Encoding.UTF8.GetBytes(_rawContent);
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _readAttempts) == 1)
                throw new OperationCanceledException(cancellationToken);

            var bytes = Encoding.UTF8.GetBytes(_rawContent);
            await stream.WriteAsync(bytes, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
