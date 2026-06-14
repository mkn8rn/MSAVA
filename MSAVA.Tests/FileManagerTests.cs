using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;

namespace MSAVA_App.Tests;

public class FileManagerTests
{
    [Test]
    public async Task SaveTempFileAsync_AddsMetadataWithoutOverwritingExistingContent()
    {
        byte[] content = Encoding.UTF8.GetBytes($"existing-content-{Guid.NewGuid()}");
        byte[] hash = SHA256.HashData(content);
        string contentPath = FileContentUtils.GetFullPath(hash, "txt");
        string tempFilePath = Path.GetTempFileName();
        string metadataDirectory = CreateTempDirectory();
        var originalWriteTime = DateTime.UtcNow.AddHours(-2);

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            await File.WriteAllBytesAsync(contentPath, content);
            File.SetLastWriteTimeUtc(contentPath, originalWriteTime);
            await File.WriteAllBytesAsync(tempFilePath, content);

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var fileManager = new FileManager(metadataStore, NullLogger<FileManager>.Instance);
            var metadata = new SavedFileMetaRecord
            {
                RefId = Guid.NewGuid(),
                FileHash = hash,
                FileExtension = "txt",
                AccessGroupId = Guid.NewGuid(),
                PublicDownload = false
            };

            bool contentFileCreated = await fileManager.SaveTempFileAsync(metadata, tempFilePath);

            contentFileCreated.Should().BeFalse();
            File.Exists(tempFilePath).Should().BeFalse();
            File.Exists(contentPath).Should().BeTrue();
            File.GetLastWriteTimeUtc(contentPath).Should().Be(originalWriteTime);
            metadataStore.GetByRefId(metadata.RefId).Should().NotBeNull();
        }
        finally
        {
            DeleteFileIfPresent(tempFilePath);
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void CheckFileAccessByPath_DeniesHexFileNameThatIsNotSha256Length()
    {
        byte[] shortHash = Guid.NewGuid().ToByteArray();
        string metadataDirectory = CreateTempDirectory();

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = Guid.NewGuid(),
                FileHash = shortHash,
                FileExtension = "txt",
                AccessGroupId = Guid.NewGuid(),
                PublicDownload = true
            });
            var fileManager = new FileManager(metadataStore, NullLogger<FileManager>.Instance);
            string fileNameWithExtension = $"{Convert.ToHexString(shortHash).ToLowerInvariant()}.txt";

            Action act = () => fileManager.CheckFileAccessByPath(fileNameWithExtension, userAccessGroups: null);

            act.Should().Throw<UnauthorizedAccessException>()
                .WithMessage("Invalid file name format.");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "msava-tests", Guid.NewGuid().ToString("N"));
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
}
