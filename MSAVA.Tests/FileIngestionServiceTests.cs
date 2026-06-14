using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class FileIngestionServiceTests
{
    [Test]
    public async Task CreateFileFromUrlAsync_RejectsRelativeUrlBeforeCreatingHttpClient()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, httpClientFactory);
            var dto = CreateUrlDto("files/sample.txt");

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl must be an absolute HTTP or HTTPS URL.*");

            httpClientFactory.WasCalled.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_RejectsNonHttpUrlBeforeCreatingHttpClient()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, httpClientFactory);
            var dto = CreateUrlDto("file:///C:/temp/sample.txt");

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl must be an absolute HTTP or HTTPS URL.*");

            httpClientFactory.WasCalled.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [TestCase("http://localhost/file.txt")]
    [TestCase("http://api.localhost/file.txt")]
    [TestCase("http://127.0.0.1/file.txt")]
    [TestCase("http://10.0.0.5/file.txt")]
    [TestCase("http://172.16.1.5/file.txt")]
    [TestCase("http://192.168.1.10/file.txt")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://[::1]/file.txt")]
    [TestCase("http://[fd00::1]/file.txt")]
    [TestCase("https://metadata.google.internal/computeMetadata/v1/")]
    public async Task CreateFileFromUrlAsync_RejectsUnsafeHostBeforeCreatingHttpClient(string fileUrl)
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, httpClientFactory);
            var dto = CreateUrlDto(fileUrl);

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl host is not allowed for server-side ingestion.*");

            httpClientFactory.WasCalled.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_ThrowsHttpRequestExceptionWhenRemoteDownloadFails()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("missing file")
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, httpClientFactory);
            var dto = CreateUrlDto("https://example.com/files/missing.txt");

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            var exception = await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("File URL download failed 404: missing file");

            exception.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
            httpClientFactory.WasCalled.Should().BeTrue();
            handler.RequestUri.Should().Be(new Uri(dto.FileUrl));
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static SaveFileFromUrlDTO CreateUrlDto(string fileUrl)
    {
        return new SaveFileFromUrlDTO
        {
            FileUrl = fileUrl,
            FileName = "sample",
            FileExtension = "txt",
            AccessGroupId = Guid.NewGuid(),
            Tags = [],
            Categories = [],
            Description = string.Empty,
            PublicViewing = false,
            PublicDownload = false
        };
    }

    private static FileIngestionService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        IHttpClientFactory httpClientFactory)
    {
        var fileManager = new FileManager(metadataStore);
        var serviceLogger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var persistenceService = new FilePersistenceService(
            context,
            fileManager,
            new TestRequestSessionAccessor(),
            serviceLogger,
            NullLogger<FilePersistenceService>.Instance);

        return new FileIngestionService(persistenceService, httpClientFactory);
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "msava-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler? _handler;

        public RecordingHttpClientFactory(HttpMessageHandler? handler = null)
        {
            _handler = handler;
        }

        public bool WasCalled { get; private set; }

        public HttpClient CreateClient(string name)
        {
            WasCalled = true;
            return _handler is null
                ? new HttpClient()
                : new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _createResponse;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> createResponse)
        {
            _createResponse = createResponse;
        }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(_createResponse(request));
        }
    }

    private sealed class TestRequestSessionAccessor : IRequestSessionAccessor
    {
        public SessionDTO? GetSession() => null;
    }

    private sealed class TestDataContext : BaseDataContext
    {
        public TestDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }
}
