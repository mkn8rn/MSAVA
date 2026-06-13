using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
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
            new NullHttpContextAccessor(),
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
        public bool WasCalled { get; private set; }

        public HttpClient CreateClient(string name)
        {
            WasCalled = true;
            return new HttpClient();
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
