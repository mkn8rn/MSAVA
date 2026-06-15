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
    public async Task CreateFileFromUrlAsync_RejectsHostResolvedToUnsafeAddressBeforeCreatingHttpClient()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();
        using var cancellationTokenSource = new CancellationTokenSource();
        string? resolvedHost = null;
        CancellationToken resolverCancellationToken = default;

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(
                context,
                metadataStore,
                httpClientFactory,
                (host, cancellationToken) =>
                {
                    resolvedHost = host;
                    resolverCancellationToken = cancellationToken;
                    return Task.FromResult(new[] { IPAddress.Parse("127.0.0.1") });
                });
            var dto = CreateUrlDto("https://files.example.test/sample.txt");

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto, cancellationTokenSource.Token);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl host resolves to an address that is not allowed for server-side ingestion.*");

            resolvedHost.Should().Be("files.example.test");
            resolverCancellationToken.Should().Be(cancellationTokenSource.Token);
            httpClientFactory.WasCalled.Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
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
            httpClientFactory.ClientName.Should().Be(FileIngestionService.RemoteFileHttpClientName);
            handler.RequestUri.Should().Be(new Uri(dto.FileUrl));
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_TreatsRedirectAsRemoteFailure()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("http://127.0.0.1/private.txt")
                }
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, httpClientFactory);
            var dto = CreateUrlDto("https://example.com/files/redirect.txt");

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            var exception = await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("File URL download failed 302 (Found).");

            exception.Which.StatusCode.Should().Be(HttpStatusCode.Redirect);
            httpClientFactory.ClientName.Should().Be(FileIngestionService.RemoteFileHttpClientName);
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
        IHttpClientFactory httpClientFactory,
        HostAddressResolver? hostAddressResolver = null)
    {
        var fileManager = new FileManager(metadataStore, NullLogger<FileManager>.Instance);
        var serviceLogger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var persistenceService = new FilePersistenceService(
            context,
            fileManager,
            new TestRequestSessionAccessor(),
            serviceLogger,
            NullLogger<FilePersistenceService>.Instance);

        hostAddressResolver ??= (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });

        return new FileIngestionService(persistenceService, httpClientFactory, hostAddressResolver);
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
        public string? ClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            WasCalled = true;
            ClientName = name;
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
