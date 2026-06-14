using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MSAVA_API.Authorization;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_API.Tests;

public class PublicFileAccessGuardTests
{
    [Test]
    public void CanServePublicFile_AllowsFileWithPublicDownloadMetadata()
    {
        string metadataDirectory = CreateTempDirectory();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(CreateMetadata(hash, publicDownload: true));
            var context = CreateContext(metadataStore);
            string physicalPath = Path.Combine(metadataDirectory, $"{Convert.ToHexString(hash).ToLowerInvariant()}.txt");

            bool result = PublicFileAccessGuard.CanServePublicFile(context, physicalPath);

            result.Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void CanServePublicFile_DeniesFileWithoutPublicDownloadMetadata()
    {
        string metadataDirectory = CreateTempDirectory();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(CreateMetadata(hash, publicDownload: false));
            var context = CreateContext(metadataStore);
            string physicalPath = Path.Combine(metadataDirectory, $"{Convert.ToHexString(hash).ToLowerInvariant()}.txt");

            bool result = PublicFileAccessGuard.CanServePublicFile(context, physicalPath);

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
        string metadataDirectory = CreateTempDirectory();

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var context = CreateContext(metadataStore);
            string physicalPath = Path.Combine(metadataDirectory, "not-a-hex-hash.txt");

            bool result = PublicFileAccessGuard.CanServePublicFile(context, physicalPath);

            result.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void CanServePublicFile_DeniesHexFileNameThatIsNotSha256Length()
    {
        string metadataDirectory = CreateTempDirectory();
        byte[] shortHash = Guid.NewGuid().ToByteArray();

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(CreateMetadata(shortHash, publicDownload: true));
            var context = CreateContext(metadataStore);
            string physicalPath = Path.Combine(metadataDirectory, $"{Convert.ToHexString(shortHash).ToLowerInvariant()}.txt");

            bool result = PublicFileAccessGuard.CanServePublicFile(context, physicalPath);

            result.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void CanServePublicFile_DeniesMissingMetadata()
    {
        string metadataDirectory = CreateTempDirectory();
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var context = CreateContext(metadataStore);
            string physicalPath = Path.Combine(metadataDirectory, $"{Convert.ToHexString(hash).ToLowerInvariant()}.txt");

            bool result = PublicFileAccessGuard.CanServePublicFile(context, physicalPath);

            result.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void CanServePublicFile_DeniesWhenMetadataStoreIsNotRegistered()
    {
        byte[] hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        string physicalPath = Path.Combine(
            Path.GetTempPath(),
            $"{Convert.ToHexString(hash).ToLowerInvariant()}.txt");

        bool result = PublicFileAccessGuard.CanServePublicFile(context, physicalPath);

        result.Should().BeFalse();
    }

    private static DefaultHttpContext CreateContext(MetadataStore metadataStore)
    {
        var services = new ServiceCollection()
            .AddSingleton(metadataStore)
            .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = services
        };
    }

    private static SavedFileMetaRecord CreateMetadata(byte[] hash, bool publicDownload)
    {
        return new SavedFileMetaRecord
        {
            RefId = Guid.NewGuid(),
            FileHash = hash,
            FileExtension = "txt",
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = publicDownload
        };
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
}
