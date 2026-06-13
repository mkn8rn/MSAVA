using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class FilePersistenceServiceTests
{
    [Test]
    public async Task CreateFileFromStreamAsync_RemovesNewMetadataAndContentWhenDatabaseSaveFails()
    {
        var content = Encoding.UTF8.GetBytes($"rollback-new-content-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateThrowingContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore);

            var dto = CreateStreamDto(content);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Simulated database failure.");

            File.Exists(contentPath).Should().BeFalse();
            metadataStore.Exists(hash, "txt").Should().BeFalse();
            context.ChangeTracker.Entries<SavedFileReferenceDB>().Should().BeEmpty();
            context.ChangeTracker.Entries<SavedFileDataDB>().Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromStreamAsync_KeepsExistingContentAndMetadataWhenDatabaseSaveFails()
    {
        var content = Encoding.UTF8.GetBytes($"rollback-existing-content-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();
        var existingRecord = new SavedFileMetaRecord
        {
            RefId = Guid.NewGuid(),
            FileHash = hash,
            FileExtension = "txt",
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = false,
            CreatedAt = DateTime.UtcNow
        };

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            await File.WriteAllBytesAsync(contentPath, content);

            using var context = CreateThrowingContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(existingRecord);
            var service = CreateService(context, metadataStore);

            var dto = CreateStreamDto(content);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Simulated database failure.");

            File.Exists(contentPath).Should().BeTrue();
            metadataStore.GetByFileHash(hash, "txt")
                .Should()
                .ContainSingle(record => record.RefId == existingRecord.RefId);
            context.ChangeTracker.Entries<SavedFileReferenceDB>().Should().BeEmpty();
            context.ChangeTracker.Entries<SavedFileDataDB>().Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static SaveFileFromStreamDTO CreateStreamDto(byte[] content)
    {
        return new SaveFileFromStreamDTO
        {
            FileName = "rollback-test",
            FileExtension = "txt",
            Stream = new MemoryStream(content),
            AccessGroupId = Guid.NewGuid(),
            Tags = [],
            Categories = [],
            Description = string.Empty,
            PublicViewing = false,
            PublicDownload = false
        };
    }

    private static FilePersistenceService CreateService(BaseDataContext context, MetadataStore metadataStore)
    {
        var fileManager = new FileManager(metadataStore);
        var serviceLogger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);

        return new FilePersistenceService(
            context,
            fileManager,
            new NullHttpContextAccessor(),
            serviceLogger,
            NullLogger<FilePersistenceService>.Instance);
    }

    private static BaseDataContext CreateThrowingContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ThrowingDataContext(options);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "msava-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class NullHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class ThrowingDataContext : BaseDataContext
    {
        public ThrowingDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<int>(new InvalidOperationException("Simulated database failure."));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }
}
