using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_API.Authorization;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;

namespace MSAVA_API.Tests;

public class PublicFileAccessGuardTests
{
    [Test]
    public async Task CanServePublicFile_AllowsFileWithPublicDownloadSqlReference()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var reference = CreateReference(hash, publicDownload: true);
        context.FileRefs.Add(reference);
        context.FileData.Add(CreateFileData(reference));
        await context.SaveChangesAsync();
        string physicalPath = CreatePhysicalPath(hash);

        bool result = CanServePublicFile(context, physicalPath);

        result.Should().BeTrue();
    }

    [Test]
    public async Task CanServePublicFile_DeniesPublicSqlReferenceWithoutFileData()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        context.FileRefs.Add(CreateReference(hash, publicDownload: true));
        await context.SaveChangesAsync();
        string physicalPath = CreatePhysicalPath(hash);

        bool result = CanServePublicFile(context, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public async Task CanServePublicFile_DeniesFileWithoutPublicDownloadSqlReference()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var reference = CreateReference(hash, publicDownload: false);
        context.FileRefs.Add(reference);
        context.FileData.Add(CreateFileData(reference));
        await context.SaveChangesAsync();
        string physicalPath = CreatePhysicalPath(hash);

        bool result = CanServePublicFile(context, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public async Task CanServePublicFile_DeniesPublicSqlReferenceOutsideDataDirectory()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var reference = CreateReference(hash, publicDownload: true);
        context.FileRefs.Add(reference);
        context.FileData.Add(CreateFileData(reference));
        await context.SaveChangesAsync();
        string physicalPath = Path.Combine(
            $"{FileContentUtils.FilesDirectory}-outside",
            $"{Convert.ToHexString(hash).ToLowerInvariant()}.txt");

        bool result = CanServePublicFile(context, physicalPath);

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
            context.FileData.Add(CreateFileData(privateReference));
            await context.SaveChangesAsync();

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = privateReference.Id,
                FileHash = hash,
                FileExtension = "txt",
                AccessGroupId = privateReference.AccessGroupId,
                PublicDownload = true,
                CreatedAt = DateTime.UnixEpoch
            });

            string physicalPath = CreatePhysicalPath(hash);

            bool result = CanServePublicFile(context, physicalPath);

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
        string physicalPath = Path.Combine(Path.GetTempPath(), "not-a-hex-hash.txt");

        bool result = CanServePublicFile(context, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public void CanServePublicFile_DeniesHexFileNameThatIsNotSha256Length()
    {
        using var context = CreateDataContext();
        byte[] shortHash = Guid.NewGuid().ToByteArray();
        string physicalPath = CreatePhysicalPath(shortHash);

        bool result = CanServePublicFile(context, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public async Task CanServePublicFile_DeniesUnsupportedExtensionEvenWhenUnknownSqlReferenceIsPublic()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var reference = CreateReference(hash, publicDownload: true);
        reference.FileExtension = FileExtensionType.Unknown;
        context.FileRefs.Add(reference);
        await context.SaveChangesAsync();
        string physicalPath = Path.Combine(
            FileContentUtils.FilesDirectory,
            $"{Convert.ToHexString(hash).ToLowerInvariant()}.exe");

        bool result = CanServePublicFile(context, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public void CanServePublicFile_DeniesMissingSqlReference()
    {
        using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        string physicalPath = CreatePhysicalPath(hash);

        bool result = CanServePublicFile(context, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public void CanServePublicFile_RejectsMissingDataContext()
    {
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        string physicalPath = CreatePhysicalPath(hash);

        Action act = () => PublicFileAccessGuard.CanServePublicFile(
            null!,
            NullLogger.Instance,
            physicalPath);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("dbContext");
    }

    [Test]
    public void CanServePublicFile_DeniesWhenDatabaseLookupFailsRecoverably()
    {
        var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        string physicalPath = CreatePhysicalPath(hash);
        context.Dispose();

        bool result = CanServePublicFile(context, physicalPath);

        result.Should().BeFalse();
    }

    [Test]
    public async Task PublicFileAccessMiddleware_DeniesExistingPrivatePublicFileWithoutCallingNext()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var reference = CreateReference(hash, publicDownload: false);
        context.FileRefs.Add(reference);
        context.FileData.Add(CreateFileData(reference));
        await context.SaveChangesAsync();
        string physicalPath = CreatePhysicalPath(hash);
        CreateFile(physicalPath);
        var httpContext = CreatePublicFileRequest(physicalPath);
        bool nextCalled = false;
        var middleware = new PublicFileAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            FileContentUtils.FilesDirectory,
            "/api/files/public");

        try
        {
            await middleware.InvokeAsync(
                httpContext,
                context,
                NullLogger<PublicFileAccessMiddleware>.Instance);

            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            nextCalled.Should().BeFalse();
        }
        finally
        {
            DeleteFileIfPresent(physicalPath);
        }
    }

    [Test]
    public async Task PublicFileAccessMiddleware_AllowsExistingPublicFileToReachStaticFileMiddleware()
    {
        await using var context = CreateDataContext();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var reference = CreateReference(hash, publicDownload: true);
        context.FileRefs.Add(reference);
        context.FileData.Add(CreateFileData(reference));
        await context.SaveChangesAsync();
        string physicalPath = CreatePhysicalPath(hash);
        CreateFile(physicalPath);
        var httpContext = CreatePublicFileRequest(physicalPath);
        bool nextCalled = false;
        var middleware = new PublicFileAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            FileContentUtils.FilesDirectory,
            "/api/files/public");

        try
        {
            await middleware.InvokeAsync(
                httpContext,
                context,
                NullLogger<PublicFileAccessMiddleware>.Instance);

            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            nextCalled.Should().BeTrue();
        }
        finally
        {
            DeleteFileIfPresent(physicalPath);
        }
    }

    [Test]
    public async Task PublicFileAccessMiddleware_IgnoresTraversalPathBeforeCheckingPhysicalFile()
    {
        await using var context = CreateDataContext();
        string outsideDirectory = CreateTempDirectory();
        string outsideFile = Path.Combine(outsideDirectory, "outside.txt");
        CreateFile(outsideFile);
        var httpContext = CreatePublicFileRequestFromRelativePath("../" + Path.GetFileName(outsideFile));
        bool nextCalled = false;
        var middleware = new PublicFileAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            FileContentUtils.FilesDirectory,
            "/api/files/public");

        try
        {
            await middleware.InvokeAsync(
                httpContext,
                context,
                NullLogger<PublicFileAccessMiddleware>.Instance);

            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            nextCalled.Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryIfPresent(outsideDirectory);
        }
    }

    [Test]
    public async Task PublicFileAccessMiddleware_IgnoresMalformedPathBeforeExceptionMiddleware()
    {
        await using var context = CreateDataContext();
        var httpContext = CreatePublicFileRequestFromRelativePath("bad\u0000name.txt");
        bool nextCalled = false;
        var middleware = new PublicFileAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            FileContentUtils.FilesDirectory,
            "/api/files/public");

        await middleware.InvokeAsync(
            httpContext,
            context,
            NullLogger<PublicFileAccessMiddleware>.Instance);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        nextCalled.Should().BeTrue();
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

    private static SavedFileDataDB CreateFileData(SavedFileReferenceDB reference)
    {
        return new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReference = reference,
            FileReferenceId = reference.Id,
            SizeInBytes = 1,
            Checksum = Convert.ToHexString(reference.FileHash),
            Name = "public-file",
            Description = "public file guard test",
            MimeType = "text/plain",
            FileExtension = "txt",
            Tags = [],
            Categories = [],
            Metadata = JsonDocument.Parse("{}"),
            PublicViewing = false,
            DownloadCount = 0,
            SavedAt = DateTime.UtcNow,
            OriginalCreator = Guid.NewGuid(),
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedById = Guid.NewGuid()
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
            FileContentUtils.FilesDirectory,
            $"{Convert.ToHexString(hash).ToLowerInvariant()}.txt");
    }

    private static bool CanServePublicFile(BaseDataContext context, string physicalPath)
    {
        return PublicFileAccessGuard.CanServePublicFile(
            context,
            NullLogger.Instance,
            physicalPath);
    }

    private static DefaultHttpContext CreatePublicFileRequest(string physicalPath)
    {
        return CreatePublicFileRequestFromRelativePath(Path.GetFileName(physicalPath));
    }

    private static DefaultHttpContext CreatePublicFileRequestFromRelativePath(string relativePath)
    {
        return new DefaultHttpContext
        {
            Request =
            {
                Method = HttpMethods.Get,
                Path = "/api/files/public/" + relativePath
            }
        };
    }

    private static void CreateFile(string physicalPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        File.WriteAllText(physicalPath, "public file test content");
    }

    private static void DeleteFileIfPresent(string physicalPath)
    {
        if (File.Exists(physicalPath))
            File.Delete(physicalPath);
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
