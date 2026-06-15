using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSAVA_API.Authorization;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_API.Tests;

public class PublicFileAccessGuardTests
{
    [Test]
    public async Task CanServePublicFile_AllowsFileWithPublicDownloadSqlReference()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        context.FileRefs.Add(CreateReference(hash, publicDownload: true));
        await context.SaveChangesAsync();
        var httpContext = CreateHttpContext(context);
        string physicalPath = CreatePhysicalPath(hash);

        bool result = PublicFileAccessGuard.CanServePublicFile(httpContext, physicalPath);

        result.Should().BeTrue();
    }

    [Test]
    public async Task CanServePublicFile_DeniesFileWithoutPublicDownloadSqlReference()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        context.FileRefs.Add(CreateReference(hash, publicDownload: false));
        await context.SaveChangesAsync();
        var httpContext = CreateHttpContext(context);
        string physicalPath = CreatePhysicalPath(hash);

        bool result = PublicFileAccessGuard.CanServePublicFile(httpContext, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public async Task CanServePublicFile_DeniesWhenMetadataIsPublicButSqlReferenceIsPrivate()
    {
        await using var context = CreateDataContext();
        string metadataDirectory = CreateTempDirectory();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var privateReference = CreateReference(hash, publicDownload: false);

        try
        {
            context.FileRefs.Add(privateReference);
            await context.SaveChangesAsync();

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = privateReference.Id,
                FileHash = hash,
                FileExtension = "txt",
                AccessGroupId = privateReference.AccessGroupId,
                PublicDownload = true
            });

            var httpContext = CreateHttpContext(context, metadataStore);
            string physicalPath = CreatePhysicalPath(hash);

            bool result = PublicFileAccessGuard.CanServePublicFile(httpContext, physicalPath);

            result.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void CanServePublicFile_DeniesMalformedFileNameWithoutThrowing()
    {
        using var context = CreateDataContext();
        var httpContext = CreateHttpContext(context);
        string physicalPath = Path.Combine(Path.GetTempPath(), "not-a-hex-hash.txt");

        bool result = PublicFileAccessGuard.CanServePublicFile(httpContext, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public void CanServePublicFile_DeniesHexFileNameThatIsNotSha256Length()
    {
        using var context = CreateDataContext();
        byte[] shortHash = Guid.NewGuid().ToByteArray();
        var httpContext = CreateHttpContext(context);
        string physicalPath = CreatePhysicalPath(shortHash);

        bool result = PublicFileAccessGuard.CanServePublicFile(httpContext, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public void CanServePublicFile_DeniesMissingSqlReference()
    {
        using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var httpContext = CreateHttpContext(context);
        string physicalPath = CreatePhysicalPath(hash);

        bool result = PublicFileAccessGuard.CanServePublicFile(httpContext, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public void CanServePublicFile_DeniesWhenDataContextIsNotRegistered()
    {
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var services = new ServiceCollection().BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };
        string physicalPath = CreatePhysicalPath(hash);

        bool result = PublicFileAccessGuard.CanServePublicFile(httpContext, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public void CanServePublicFile_DeniesWhenDatabaseLookupFailsRecoverably()
    {
        var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var httpContext = CreateHttpContext(context);
        string physicalPath = CreatePhysicalPath(hash);
        context.Dispose();

        bool result = PublicFileAccessGuard.CanServePublicFile(httpContext, physicalPath);

        result.Should().BeFalse();
    }

    private static DefaultHttpContext CreateHttpContext(
        BaseDataContext context,
        MetadataStore? metadataStore = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(context);

        if (metadataStore is not null)
            services.AddSingleton(metadataStore);

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
    }

    private static SavedFileReferenceDB CreateReference(byte[] hash, bool publicDownload)
    {
        return new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = hash,
            FileExtension = FileExtensionType._TXT,
            PublicDownload = publicDownload,
            AccessGroupId = Guid.NewGuid()
        };
    }

    private static BaseDataContext CreateDataContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static string CreatePhysicalPath(byte[] hash)
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"{Convert.ToHexString(hash).ToLowerInvariant()}.txt");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "msava-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
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
