using MSAVA_Shared.Models;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_BLL.Utils.Metadata;
using System.Text.Json;

namespace MSAVA_BLL.Utils;

public static class MappingUtils
{
    public static FileExtensionType ParseFileExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return FileExtensionType.Unknown;

        var normalized = extension.AsSpan().Trim().TrimStart('.');
        Span<char> enumName = stackalloc char[normalized.Length + 1];
        enumName[0] = '_';
        
        for (int i = 0; i < normalized.Length; i++)
            enumName[i + 1] = char.ToUpperInvariant(normalized[i]);

        if (Enum.TryParse<FileExtensionType>(enumName.ToString(), out var result))
            return result;

        return FileExtensionType.Unknown;
    }

    public static UserDTO MapUserDTOWithRelationships(UserDB db)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(db.AccessGroups, nameof(db.AccessGroups));

        var accessGroups = new List<AccessGroupDTO>(db.AccessGroups.Count);
        foreach (var accessGroup in db.AccessGroups)
        {
            accessGroups.Add(MapAccessGroupDTO(accessGroup));
        }

        return new UserDTO
        {
            Id = db.Id,
            Username = db.Username,
            IsAdmin = db.IsAdmin,
            IsBanned = db.IsBanned,
            IsWhitelisted = db.IsWhitelisted,
            CreatedAt = db.CreatedAt,
            AccessGroups = accessGroups
        };
    }

    public static UserDTO MapUserDTO(UserDB db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return new UserDTO
        {
            Id = db.Id,
            Username = db.Username,
            IsAdmin = db.IsAdmin,
            IsBanned = db.IsBanned,
            IsWhitelisted = db.IsWhitelisted,
            CreatedAt = db.CreatedAt
        };
    }

    public static AccessGroupDTO MapAccessGroupDTO(AccessGroupDB db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return new AccessGroupDTO
        {
            Id = db.Id,
            Name = db.Name,
            CreatedAt = db.CreatedAt,
            OwnerId = db.OwnerId
        };
    }

    public static InviteCodeDTO MapInviteCodeDTO(InviteCodeDB db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return new InviteCodeDTO
        {
            Id = db.Id,
            OwnerId = db.OwnerId,
            CreatedAt = db.CreatedAt,
            ExpiresAt = db.ExpiresAt,
            MaxUses = db.MaxUses
        };
    }

    public static SavedFileReferenceDB MapSavedFileReferenceDB(
        SaveFileFromStreamDTO dto,
        byte[] fileHash,
        ulong fileLength)
    {
        var extension = ParseFileExtension(dto.FileExtension);
        return new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = extension,
            PublicDownload = dto.PublicDownload,
            AccessGroupId = dto.AccessGroupId
        };
    }

    public static SavedFileReferenceDB MapSavedFileReferenceDB(
        SaveFileFromFetchDTO dto,
        byte[] fileHash,
        ulong fileLength)
    {
        var extension = ParseFileExtension(dto.FileExtension);
        return new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = extension,
            PublicDownload = dto.PublicDownload,
            AccessGroupId = dto.AccessGroupId
        };
    }

    public static StreamReturnFileDTO MapReturnFileDTO(SavedFileReferenceDB db, byte[]? fileBytes = null, Stream? fileStream = null)
    {
        string fileName = GetFileName(db);
        string fileExtension = FileExtensionUtils.GetFileExtension(db);

        Stream stream;
        if (fileBytes is { Length: > 0 })
        {
            stream = new MemoryStream(fileBytes);
        }
        else if (fileStream is { Length: > 0 })
        {
            stream = fileStream;
            if (stream.CanSeek)
                stream.Position = 0;
        }
        else
        {
            throw new ArgumentException("Either fileBytes or fileStream must be provided.");
        }

        return new StreamReturnFileDTO
        {
            Id = db.Id,
            FileName = fileName,
            FileExtension = fileExtension,
            FileStream = stream,
        };
    }

    /// <summary>
    /// Converts file hash to lowercase hex string. Optimized to reduce allocations.
    /// </summary>
    public static string GetFileName(SavedFileReferenceDB db)
    {
        return Convert.ToHexString(db.FileHash).ToLowerInvariant();
    }

    /// <summary>
    /// Converts byte array to lowercase hex string. Optimized version.
    /// </summary>
    public static string BytesToHexString(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Cached empty JsonDocument to avoid repeated parsing
    private static readonly JsonDocument EmptyJsonDocument = JsonDocument.Parse("{}");

    public static SavedFileDataDB MapSavedFileDataDB(
        SaveFileFromStreamDTO dto,
        SavedFileReferenceDB savedFileDb,
        ulong sizeInBytes,
        Guid originalCreator,
        Guid lastModifiedBy)
    {
        var checksum = BytesToHexString(savedFileDb.FileHash);
        var fileExtension = FileExtensionUtils.GetFileExtension(savedFileDb);
        var mimeType = MetadataExtractor.GetContentType(fileExtension);
        var tags = dto.Tags?.ToArray() ?? [];
        var categories = dto.Categories?.ToArray() ?? [];
        var metadata = MetadataExtractor.ExtractMetadata(dto.Stream, fileExtension);
        var now = DateTime.UtcNow;

        return new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReferenceId = savedFileDb.Id,
            SizeInBytes = sizeInBytes,
            SavedAt = now,
            LastModifiedAt = now,
            LastModifiedById = lastModifiedBy,
            Checksum = checksum,
            Name = dto.FileName,
            Description = dto.Description ?? string.Empty,
            MimeType = mimeType,
            FileExtension = fileExtension,
            Tags = tags,
            Categories = categories,
            OriginalCreator = originalCreator,
            Metadata = metadata,
            PublicViewing = dto.PublicViewing,
            DownloadCount = 0,
        };
    }

    public static SavedFileDataDB MapSavedFileDataDB(
        SaveFileFromFetchDTO dto,
        SavedFileReferenceDB savedFileDb,
        ulong sizeInBytes,
        Guid originalCreator,
        Guid lastModifiedBy)
    {
        var checksum = BytesToHexString(savedFileDb.FileHash);
        var fileExtension = FileExtensionUtils.GetFileExtension(savedFileDb);
        var mimeType = MetadataExtractor.GetContentType(fileExtension);
        var tags = dto.Tags?.ToArray() ?? [];
        var categories = dto.Categories?.ToArray() ?? [];
        var now = DateTime.UtcNow;

        return new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReferenceId = savedFileDb.Id,
            SizeInBytes = sizeInBytes,
            SavedAt = now,
            LastModifiedAt = now,
            LastModifiedById = lastModifiedBy,
            Checksum = checksum,
            Name = dto.FileName,
            Description = dto.Description ?? string.Empty,
            MimeType = mimeType,
            FileExtension = fileExtension,
            Tags = tags,
            Categories = categories,
            OriginalCreator = originalCreator,
            Metadata = EmptyJsonDocument,
            PublicViewing = dto.PublicViewing,
            DownloadCount = 0,
        };
    }

    public static SearchFileDataDTO MapSearchFileDataDTO(SavedFileDataDB db)
    {
        ArgumentNullException.ThrowIfNull(db.FileReference, nameof(db.FileReference));

        var fileExtension = FileExtensionUtils.GetFileExtension(db.FileReference);
        var fileName = GetFileName(db.FileReference);

        return new SearchFileDataDTO
        {
            DataId = db.Id,
            RefId = db.FileReference.Id,
            FilePath = $"{fileName}.{fileExtension}",
            Name = db.Name,
            Description = db.Description,
            MimeType = db.MimeType,
            FileExtension = fileExtension,
            Tags = db.Tags,
            Categories = db.Categories,
            SizeInBytes = db.SizeInBytes,
            Checksum = db.Checksum,
            Metadata = db.Metadata,
            PublicViewing = db.PublicViewing,
            DownloadCount = db.DownloadCount,
            SavedAt = db.SavedAt,
            LastModifiedAt = db.LastModifiedAt,
            LastModifiedById = db.LastModifiedById
        };
    }

    public static SavedFileMetaRecord MapSavedFileMetaRecord(SavedFileReferenceDB db)
    {
        return new SavedFileMetaRecord
        {
            RefId = db.Id,
            FileHash = db.FileHash,
            FileExtension = db.FileExtension.ToString().TrimStart('_').ToLowerInvariant(),
            PublicDownload = db.PublicDownload,
            AccessGroupId = db.AccessGroupId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
