using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_App.Models;
using MSAVA_App.Services.Api;
using MSAVA_App.Services.Files;
using MSAVA_Shared.Models;

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
    public async Task CreateFileFromFormFileAsync_ReturnsFailureWhenSuccessfulResponseIsNotGuid()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("raw-upload-id", Encoding.UTF8, "text/plain")
        });

        var outcome = await CreateUploadAsync(service);

        outcome.Success.Should().BeFalse();
        outcome.StatusCode.Should().Be((int)HttpStatusCode.OK);
        outcome.Id.Should().BeNull();
        outcome.Error.Should().Be(FileUploadClientService.InvalidSuccessResponseMessage);
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_ReturnsFailureWhenSuccessfulResponseIsEmptyGuid()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(Guid.Empty), Encoding.UTF8, "application/json")
        });

        var outcome = await CreateUploadAsync(service);

        outcome.Success.Should().BeFalse();
        outcome.StatusCode.Should().Be((int)HttpStatusCode.OK);
        outcome.Id.Should().BeNull();
        outcome.Error.Should().Be(FileUploadClientService.InvalidSuccessResponseMessage);
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
    public async Task CreateFileFromFormFileAsync_PropagatesCriticalFailureWhileParsingSuccessfulResponse()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent(new OutOfMemoryException("Critical memory failure."))
        });

        var act = async () => await CreateUploadAsync(service);

        await act.Should().ThrowAsync<OutOfMemoryException>();
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_PropagatesWrappedCriticalFailureWhileParsingSuccessfulResponse()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent(new InvalidOperationException(
                "wrapped native failure",
                new AccessViolationException("native failure")))
        });

        var act = async () => await CreateUploadAsync(service);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("wrapped native failure");
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_ReturnsGenericErrorForFailedResponse()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new StringContent(
                "upload rejected because internal token abc123 was unavailable",
                Encoding.UTF8,
                "text/plain")
        });

        var outcome = await CreateUploadAsync(service);

        outcome.Success.Should().BeFalse();
        outcome.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        outcome.Id.Should().BeNull();
        outcome.Error.Should().Be(FileUploadClientService.FailedUploadMessage);
        outcome.Error.Should().NotContain("internal token");
        outcome.Error.Should().NotContain("upload rejected");
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_DoesNotReadFailedResponseBody()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Internal Server Error",
            Content = new ThrowingContent(new InvalidOperationException("Body should not be read."))
        });

        var outcome = await CreateUploadAsync(service);

        outcome.Success.Should().BeFalse();
        outcome.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        outcome.Id.Should().BeNull();
        outcome.Error.Should().Be(FileUploadClientService.FailedUploadMessage);
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_NormalizesMultipartMetadataBeforeSending()
    {
        var fileId = Guid.NewGuid();
        MultipartRequestSnapshot? snapshot = null;
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(fileId), Encoding.UTF8, "application/json")
            },
            async (request, cancellationToken) =>
            {
                snapshot = await ReadMultipartRequestAsync(request, cancellationToken);
            });

        var outcome = await service.CreateFileFromFormFileAsync(
            fileName: "  quarterly report  ",
            fileExtension: " .TXT ",
            fileStream: new MemoryStream(Encoding.UTF8.GetBytes("content")),
            accessGroupId: Guid.Parse("f1e4c22b-0dd8-4aa4-b9a2-640b6e241552"),
            tags: ["  alpha  ", "beta  "],
            categories: [" finance "],
            description: "  first line\nsecond line\t  ",
            publicViewing: true,
            publicDownload: true);

        outcome.Success.Should().BeTrue();
        snapshot.Should().NotBeNull();
        snapshot!.PathAndQuery.Should().Be("/api/files/store/formfile");
        snapshot.Fields[FileUploadFormFields.FileName].Should().Equal("quarterly report");
        snapshot.Fields[FileUploadFormFields.FileExtension].Should().Equal("txt");
        snapshot.Fields[FileUploadFormFields.AccessGroupId].Should().Equal("f1e4c22b-0dd8-4aa4-b9a2-640b6e241552");
        snapshot.Fields[FileUploadFormFields.Description].Should().Equal("first line\nsecond line");
        snapshot.Fields[FileUploadFormFields.PublicViewing].Should().Equal("True");
        snapshot.Fields[FileUploadFormFields.PublicDownload].Should().Equal("True");
        snapshot.Fields[FileUploadFormFields.Tags].Should().Equal("alpha", "beta");
        snapshot.Fields[FileUploadFormFields.Categories].Should().Equal("finance");
        snapshot.FilePartFileName.Should().Be("upload.txt");
        snapshot.FilePartContentType.Should().Be("application/octet-stream");
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_RejectsPathLikeFileNameBeforeSending()
    {
        bool requestWasSent = false;
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK),
            (_, _) =>
            {
                requestWasSent = true;
                return Task.CompletedTask;
            });

        Func<Task> act = () => service.CreateFileFromFormFileAsync(
            fileName: "quarterly/report",
            fileExtension: "txt",
            fileStream: new MemoryStream(Encoding.UTF8.GetBytes("content")),
            accessGroupId: Guid.NewGuid());

        await act.Should().ThrowAsync<FileMetadataValidationException>()
            .WithMessage("FileName contains invalid characters.");
        requestWasSent.Should().BeFalse();
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_RejectsPathLikeExtensionBeforeSending()
    {
        bool requestWasSent = false;
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK),
            (_, _) =>
            {
                requestWasSent = true;
                return Task.CompletedTask;
            });

        Func<Task> act = () => service.CreateFileFromFormFileAsync(
            fileName: "sample",
            fileExtension: "../txt",
            fileStream: new MemoryStream(Encoding.UTF8.GetBytes("content")),
            accessGroupId: Guid.NewGuid());

        await act.Should().ThrowAsync<FileMetadataValidationException>()
            .WithMessage("FileExtension contains invalid characters.");
        requestWasSent.Should().BeFalse();
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_RejectsBlankMetadataValueBeforeSending()
    {
        bool requestWasSent = false;
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK),
            (_, _) =>
            {
                requestWasSent = true;
                return Task.CompletedTask;
            });

        Func<Task> act = () => service.CreateFileFromFormFileAsync(
            fileName: "sample",
            fileExtension: "txt",
            fileStream: new MemoryStream(Encoding.UTF8.GetBytes("content")),
            accessGroupId: Guid.NewGuid(),
            tags: ["valid", " "]);

        await act.Should().ThrowAsync<FileMetadataValidationException>()
            .WithMessage("Tags values must be provided.");
        requestWasSent.Should().BeFalse();
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
        return CreateService(response, inspectRequest: null);
    }

    private static FileUploadClientService CreateService(
        HttpResponseMessage response,
        Func<HttpRequestMessage, CancellationToken, Task>? inspectRequest)
    {
        var api = new ApiService(
            new StaticHttpClientFactory(new HttpClient(new StaticHttpMessageHandler(response, inspectRequest))),
            new ApiClientOptions { Url = "https://api.msava.test/" },
            NullLogger<ApiService>.Instance);

        return new FileUploadClientService(api, NullLogger<FileUploadClientService>.Instance);
    }

    private static async Task<MultipartRequestSnapshot> ReadMultipartRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Content.Should().BeOfType<MultipartFormDataContent>();
        var content = (MultipartFormDataContent)request.Content!;
        var fields = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? filePartFileName = null;
        string? filePartContentType = null;

        foreach (var part in content)
        {
            string name = Unquote(part.Headers.ContentDisposition?.Name) ?? string.Empty;
            if (name == FileUploadFormFields.FormFile)
            {
                filePartFileName = Unquote(
                    part.Headers.ContentDisposition?.FileNameStar ??
                    part.Headers.ContentDisposition?.FileName);
                filePartContentType = part.Headers.ContentType?.MediaType;
                continue;
            }

            if (!fields.TryGetValue(name, out var values))
            {
                values = [];
                fields.Add(name, values);
            }

            values.Add(await part.ReadAsStringAsync(cancellationToken));
        }

        return new MultipartRequestSnapshot(
            request.RequestUri?.PathAndQuery ?? string.Empty,
            fields,
            filePartFileName,
            filePartContentType);
    }

    private static string? Unquote(string? value)
    {
        return value?.Trim('"');
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
        private readonly Func<HttpRequestMessage, CancellationToken, Task>? _inspectRequest;

        public StaticHttpMessageHandler(
            HttpResponseMessage response,
            Func<HttpRequestMessage, CancellationToken, Task>? inspectRequest)
        {
            _response = response;
            _inspectRequest = inspectRequest;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_inspectRequest is not null)
                await _inspectRequest(request, cancellationToken);

            return _response;
        }
    }

    private sealed record MultipartRequestSnapshot(
        string PathAndQuery,
        Dictionary<string, List<string>> Fields,
        string? FilePartFileName,
        string? FilePartContentType);

    private sealed class ThrowingContent : HttpContent
    {
        private readonly Exception _exception;

        public ThrowingContent()
            : this(new IOException("Unable to read content"))
        {
        }

        public ThrowingContent(Exception exception)
        {
            Headers.ContentType = new MediaTypeHeaderValue("text/plain");
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
