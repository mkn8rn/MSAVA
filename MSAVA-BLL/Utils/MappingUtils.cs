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
        return TryParseSupportedFileExtension(extension, out var result, out _, out _)
            ? result
            : FileExtensionType.Unknown;
    }

    public static FileExtensionType ParseSupportedFileExtension(string extension)
    {
        if (TryParseSupportedFileExtension(extension, out var result, out _, out string error))
            return result;

        throw new ArgumentException(error, nameof(extension));
    }

    public static bool TryParseSupportedFileExtension(
        string? extension,
        out FileExtensionType result,
        out string normalizedExtension,
        out string error)
    {
        result = FileExtensionType.Unknown;
        normalizedExtension = string.Empty;
        error = string.Empty;

        if (!TryNormalizeFileExtension(extension, out normalizedExtension, out error))
            return false;

        Span<char> enumName = stackalloc char[normalizedExtension.Length + 1];
        enumName[0] = '_';

        for (int i = 0; i < normalizedExtension.Length; i++)
            enumName[i + 1] = char.ToUpperInvariant(normalizedExtension[i]);

        if (Enum.TryParse<FileExtensionType>(enumName.ToString(), out result) &&
            result != FileExtensionType.Unknown)
        {
            return true;
        }

        error = $"FileExtension '{normalizedExtension}' is not supported.";
        return false;
    }

    private static bool TryNormalizeFileExtension(
        string? extension,
        out string normalizedExtension,
        out string error)
    {
        normalizedExtension = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(extension))
        {
            error = "FileExtension must be provided.";
            return false;
        }

        var normalized = extension.AsSpan().Trim();

        while (normalized.Length > 0 && (normalized[0] == '.' || normalized[0] == '_'))
        {
            normalized = normalized[1..];
        }

        if (normalized.Length == 0 || normalized.IsWhiteSpace())
        {
            error = "FileExtension must be provided.";
            return false;
        }

        normalizedExtension = normalized.ToString().ToLowerInvariant();

        if (normalizedExtension.Contains('/') ||
            normalizedExtension.Contains('\\') ||
            normalizedExtension.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "FileExtension contains invalid characters.";
            return false;
        }

        return true;
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
            OwnerId = db.OwnerId,
            SubGroups = db.SubGroups?.Select(MapAccessGroupDTO).ToList() ?? []
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
        byte[] fileHash)
    {
        var extension = ParseSupportedFileExtension(dto.FileExtension);
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
        byte[] fileHash)
    {
        var extension = ParseSupportedFileExtension(dto.FileExtension);
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
        if (fileBytes is not null)
        {
            stream = new MemoryStream(fileBytes);
        }
        else if (fileStream is not null)
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
        Guid lastModifiedBy,
        JsonDocument metadata,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var checksum = BytesToHexString(savedFileDb.FileHash);
        var fileExtension = FileExtensionUtils.GetFileExtension(savedFileDb);
        var mimeType = MetadataExtractor.GetContentType(fileExtension);
        var tags = dto.Tags?.ToArray() ?? [];
        var categories = dto.Categories?.ToArray() ?? [];

        return new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReferenceId = savedFileDb.Id,
            SizeInBytes = sizeInBytes,
            SavedAt = utcNow,
            LastModifiedAt = utcNow,
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
        Guid lastModifiedBy,
        DateTime utcNow)
    {
        var checksum = BytesToHexString(savedFileDb.FileHash);
        var fileExtension = FileExtensionUtils.GetFileExtension(savedFileDb);
        var mimeType = MetadataExtractor.GetContentType(fileExtension);
        var tags = dto.Tags?.ToArray() ?? [];
        var categories = dto.Categories?.ToArray() ?? [];

        return new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReferenceId = savedFileDb.Id,
            SizeInBytes = sizeInBytes,
            SavedAt = utcNow,
            LastModifiedAt = utcNow,
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

    public static SavedFileMetaRecord MapSavedFileMetaRecord(SavedFileReferenceDB db, DateTime utcNow)
    {
        return new SavedFileMetaRecord
        {
            RefId = db.Id,
            FileHash = db.FileHash,
            FileExtension = db.FileExtension.ToString().TrimStart('_').ToLowerInvariant(),
            PublicDownload = db.PublicDownload,
            AccessGroupId = db.AccessGroupId,
            CreatedAt = utcNow
        };
    }
}
