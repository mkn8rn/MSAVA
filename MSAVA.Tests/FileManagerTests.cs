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
    public void FileManager_DoesNotExposeUnsafeFullPathStreamOverload()
    {
        var unsafeOverload = typeof(FileManager)
            .GetMethods()
            .SingleOrDefault(method =>
                method.Name == nameof(FileManager.GetFileStream) &&
                method.GetParameters() is
                [
                    { ParameterType: var firstParameter },
                    { ParameterType: var secondParameter }
                ] &&
                firstParameter == typeof(string) &&
                secondParameter == typeof(FileStreamOptions));

        unsafeOverload.Should().BeNull();
    }

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
                PublicDownload = false,
                CreatedAt = DateTime.UnixEpoch
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
    public async Task SaveTempFileAsync_RejectsMissingTempFileWithoutEchoingPath()
    {
        byte[] content = Encoding.UTF8.GetBytes($"missing-temp-{Guid.NewGuid()}");
        byte[] hash = SHA256.HashData(content);
        string tempFilePath = Path.Combine(Path.GetTempPath(), $"msava-missing-{Guid.NewGuid():N}.tmp");
        string metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(tempFilePath);

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var fileManager = new FileManager(metadataStore, NullLogger<FileManager>.Instance);
            var metadata = new SavedFileMetaRecord
            {
                RefId = Guid.NewGuid(),
                FileHash = hash,
                FileExtension = "txt",
                AccessGroupId = Guid.NewGuid(),
                PublicDownload = false,
                CreatedAt = DateTime.UnixEpoch
            };

            Func<Task> act = () => fileManager.SaveTempFileAsync(metadata, tempFilePath);

            var exception = await act.Should().ThrowAsync<FileNotFoundException>()
                .WithMessage("Temporary file not found.");
            exception.Which.Message.Should().NotContain(tempFilePath);
            exception.Which.FileName.Should().BeNull();
        }
        finally
        {
            DeleteFileIfPresent(tempFilePath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void GetFileStream_RejectsMissingContentWithoutEchoingPath()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"missing-content-{Guid.NewGuid()}"));
        string contentPath = FileContentUtils.GetFullPath(hash, "txt");
        string metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var fileManager = new FileManager(metadataStore, NullLogger<FileManager>.Instance);

            Action act = () => fileManager.GetFileStream(hash, "txt");

            var exception = act.Should().Throw<FileNotFoundException>()
                .WithMessage("Stored file content was not found.");
            exception.Which.Message.Should().NotContain(contentPath);
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
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
