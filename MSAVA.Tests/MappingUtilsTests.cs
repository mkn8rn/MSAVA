using System.Security.Cryptography;
using System.Text;
using MSAVA_BLL.Utils;
using MSAVA_BLL.Utils.Metadata;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class MappingUtilsTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 16, 12, 30, 0, DateTimeKind.Utc);

    [Test]
    public void MapSavedFileReferenceDB_FromStream_MapsReferenceFields()
    {
        var fileHash = SHA256.HashData(Encoding.UTF8.GetBytes("stream-reference"));
        using var stream = new MemoryStream([]);
        var dto = new SaveFileFromStreamDTO
        {
            FileName = "reference",
            FileExtension = " .TXT ",
            Stream = stream,
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = true
        };

        var reference = MappingUtils.MapSavedFileReferenceDB(dto, fileHash);

        reference.Id.Should().NotBeEmpty();
        reference.FileHash.Should().Equal(fileHash);
        reference.FileExtension.Should().Be(FileExtensionType._TXT);
        reference.AccessGroupId.Should().Be(dto.AccessGroupId);
        reference.PublicDownload.Should().BeTrue();
    }

    [Test]
    public void MapSavedFileReferenceDB_FromFetch_MapsReferenceFields()
    {
        var fileHash = SHA256.HashData(Encoding.UTF8.GetBytes("fetch-reference"));
        var dto = new SaveFileFromFetchDTO
        {
            FileName = "reference",
            FileExtension = " .PDF ",
            TempFilePath = "download.tmp",
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = false
        };

        var reference = MappingUtils.MapSavedFileReferenceDB(dto, fileHash);

        reference.Id.Should().NotBeEmpty();
        reference.FileHash.Should().Equal(fileHash);
        reference.FileExtension.Should().Be(FileExtensionType._PDF);
        reference.AccessGroupId.Should().Be(dto.AccessGroupId);
        reference.PublicDownload.Should().BeFalse();
    }

    [Test]
    public void MapSavedFileReferenceDB_FromStream_RejectsUnsupportedExtension()
    {
        var fileHash = SHA256.HashData(Encoding.UTF8.GetBytes("unsupported-stream-reference"));
        using var stream = new MemoryStream([]);
        var dto = new SaveFileFromStreamDTO
        {
            FileName = "reference",
            FileExtension = "exe",
            Stream = stream,
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = true
        };

        Action act = () => MappingUtils.MapSavedFileReferenceDB(dto, fileHash);

        act.Should().Throw<ArgumentException>()
            .WithMessage("FileExtension 'exe' is not supported.*");
    }

    [Test]
    public void TryParseSupportedFileExtension_RejectsPathLikeExtension()
    {
        bool parsed = MappingUtils.TryParseSupportedFileExtension(
            "folder/txt",
            out var result,
            out string normalizedExtension,
            out string error);

        parsed.Should().BeFalse();
        result.Should().Be(FileExtensionType.Unknown);
        normalizedExtension.Should().Be("folder/txt");
        error.Should().Be("FileExtension contains invalid characters.");
    }

    [Test]
    public void TryParseSupportedFileExtension_NormalizesPrefixAndCasing()
    {
        bool parsed = MappingUtils.TryParseSupportedFileExtension(
            " _TXT ",
            out var result,
            out string normalizedExtension,
            out string error);

        parsed.Should().BeTrue();
        result.Should().Be(FileExtensionType._TXT);
        normalizedExtension.Should().Be("txt");
        error.Should().BeEmpty();
    }

    [Test]
    public void MapReturnFileDTO_AcceptsEmptyByteArray()
    {
        var fileReference = CreateFileReference(SHA256.HashData([]));

        var result = MappingUtils.MapReturnFileDTO(fileReference, fileBytes: []);

        result.Id.Should().Be(fileReference.Id);
        result.FileName.Should().Be(MappingUtils.GetFileName(fileReference));
        result.FileExtension.Should().Be("txt");
        result.FileStream.Should().BeOfType<MemoryStream>();
        result.FileStream.Length.Should().Be(0);
    }

    [Test]
    public void MapReturnFileDTO_AcceptsEmptyStream()
    {
        var fileReference = CreateFileReference(SHA256.HashData([]));
        using var stream = new MemoryStream();

        var result = MappingUtils.MapReturnFileDTO(fileReference, fileStream: stream);

        result.FileStream.Should().BeSameAs(stream);
        result.FileStream.Length.Should().Be(0);
        result.FileStream.Position.Should().Be(0);
    }

    [Test]
    public void MapAccessGroupDTO_MapsSubGroupsAsCollection()
    {
        var child = CreateAccessGroup("Child");
        var parent = CreateAccessGroup("Parent");
        parent.SubGroups = [child];

        var result = MappingUtils.MapAccessGroupDTO(parent);

        result.Id.Should().Be(parent.Id);
        result.Name.Should().Be("Parent");
        result.SubGroups.Should().ContainSingle();
        result.SubGroups[0].Id.Should().Be(child.Id);
        result.SubGroups[0].Name.Should().Be("Child");
        result.SubGroups[0].SubGroups.Should().BeEmpty();
    }

    [Test]
    public void MapAccessGroupDTO_UsesEmptySubGroupsWhenSourceHasNone()
    {
        var accessGroup = CreateAccessGroup("No children");

        var result = MappingUtils.MapAccessGroupDTO(accessGroup);

        result.SubGroups.Should().BeEmpty();
    }

    [Test]
    public void MapSavedFileDataDB_FromStream_UsesNormalizedReferenceExtension()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        using var stream = new MemoryStream(content);
        var dto = new SaveFileFromStreamDTO
        {
            FileName = "notes",
            FileExtension = " .TXT ",
            Stream = stream,
            AccessGroupId = Guid.NewGuid(),
            Tags = [],
            Categories = [],
            Description = "normalized extension test"
        };
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = SHA256.HashData(content),
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = dto.AccessGroupId,
            PublicDownload = false
        };

        var data = MappingUtils.MapSavedFileDataDB(
            dto,
            fileReference,
            (ulong)content.Length,
            Guid.NewGuid(),
            Guid.NewGuid(),
            MetadataExtractor.ExtractMetadata(stream, "txt", content.Length),
            FixedNow);

        data.FileExtension.Should().Be("txt");
        data.MimeType.Should().Be("text/plain");
        data.SavedAt.Should().Be(FixedNow);
        data.LastModifiedAt.Should().Be(FixedNow);
        data.Metadata.RootElement.GetProperty("Valid").GetBoolean().Should().BeTrue();
        data.Metadata.RootElement.GetProperty("Type").GetString().Should().Be("Text");
    }

    [Test]
    public void MapSavedFileDataDB_FromFetch_UsesNormalizedReferenceExtension()
    {
        var dto = new SaveFileFromFetchDTO
        {
            FileName = "downloaded",
            FileExtension = " .TXT ",
            TempFilePath = Path.GetTempFileName(),
            AccessGroupId = Guid.NewGuid(),
            Tags = [],
            Categories = [],
            Description = "normalized extension test"
        };
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = SHA256.HashData(Encoding.UTF8.GetBytes("hello world")),
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = dto.AccessGroupId,
            PublicDownload = false
        };

        try
        {
            var data = MappingUtils.MapSavedFileDataDB(
                dto,
                fileReference,
                11,
                Guid.NewGuid(),
                Guid.NewGuid(),
                FixedNow);

            data.FileExtension.Should().Be("txt");
            data.MimeType.Should().Be("text/plain");
            data.SavedAt.Should().Be(FixedNow);
            data.LastModifiedAt.Should().Be(FixedNow);
        }
        finally
        {
            if (File.Exists(dto.TempFilePath))
                File.Delete(dto.TempFilePath);
        }
    }

    [Test]
    public void MapSavedFileMetaRecord_UsesProvidedTimestamp()
    {
        var fileReference = CreateFileReference(SHA256.HashData(Encoding.UTF8.GetBytes("metadata-record")));

        var metaRecord = MappingUtils.MapSavedFileMetaRecord(fileReference, FixedNow);

        metaRecord.RefId.Should().Be(fileReference.Id);
        metaRecord.FileHash.Should().Equal(fileReference.FileHash);
        metaRecord.FileExtension.Should().Be("txt");
        metaRecord.AccessGroupId.Should().Be(fileReference.AccessGroupId);
        metaRecord.PublicDownload.Should().Be(fileReference.PublicDownload);
        metaRecord.CreatedAt.Should().Be(FixedNow);
    }

    private static SavedFileReferenceDB CreateFileReference(byte[] hash)
    {
        return new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = hash,
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = false
        };
    }

    private static AccessGroupDB CreateAccessGroup(string name)
    {
        return new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Name = name
        };
    }
}
