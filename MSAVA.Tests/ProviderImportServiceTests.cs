using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Import;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class ProviderImportServiceTests
{
    [Test]
    public async Task GoogleDriveImportAsync_UsesInjectedHttpClientFactory()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("drive failure")
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new GoogleDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory);
            var dto = new FetchFileGoogleDriveDTO
            {
                FileUrl = "abcDEF12345",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Google Drive initial request failed 400: drive failure");

            httpClientFactory.WasCalled.Should().BeTrue();
            handler.Requests.Should().ContainSingle();
            handler.Requests[0].RequestUri!.Host.Should().Be("drive.google.com");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task OneDriveImportAsync_UsesInjectedHttpClientFactory()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("onedrive failure")
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "https://1drv.ms/u/s!abcDEF12345",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("OneDrive download failed 400: onedrive failure");

            httpClientFactory.WasCalled.Should().BeTrue();
            handler.Requests.Should().ContainSingle();
            handler.Requests[0].RequestUri!.Host.Should().Be("api.onedrive.com");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static FilePersistenceService CreatePersistenceService(BaseDataContext context, MetadataStore metadataStore)
    {
        return new FilePersistenceService(
            context,
            new FileManager(metadataStore),
            new NullHttpContextAccessor(),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
            NullLogger<FilePersistenceService>.Instance);
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
        private readonly HttpMessageHandler _handler;

        public RecordingHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public bool WasCalled { get; private set; }

        public HttpClient CreateClient(string name)
        {
            WasCalled = true;
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class NullHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
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
