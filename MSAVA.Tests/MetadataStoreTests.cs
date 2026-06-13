using System.Security.Cryptography;
using System.Text;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_App.Tests;

public class MetadataStoreTests
{
    [Test]
    public void Delete_RemovesOnlyRequestedRecordWhenContentHashIsShared()
    {
        string metadataDirectory = CreateTempDirectory();
        byte[] sharedHash = SHA256.HashData(Encoding.UTF8.GetBytes($"shared-content-{Guid.NewGuid()}"));
        var firstRecord = CreateRecord(sharedHash);
        var secondRecord = CreateRecord(sharedHash);

        try
        {
            using var store = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            store.AddMetadata(firstRecord);
            store.AddMetadata(secondRecord);

            bool deleted = store.Delete(firstRecord.RefId);

            deleted.Should().BeTrue();
            store.GetByRefId(firstRecord.RefId).Should().BeNull();
            store.GetByRefId(secondRecord.RefId).Should().NotBeNull();
            store.Exists(sharedHash, "txt").Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static SavedFileMetaRecord CreateRecord(byte[] fileHash)
    {
        return new SavedFileMetaRecord
        {
            RefId = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = "txt",
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = false
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
