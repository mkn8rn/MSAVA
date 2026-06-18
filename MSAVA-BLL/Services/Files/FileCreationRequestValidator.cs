using MSAVA_BLL.Utils;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Files;

internal static class FileCreationRequestValidator
{
    public static void NormalizeAndValidate(SaveFileFromStreamDTO dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        ApplyMetadata(dto, NormalizeMetadata(
            dto.FileName,
            dto.FileExtension,
            dto.Tags,
            dto.Categories,
            dto.Description));

        if (dto.Stream is null)
            throw new ArgumentException("File content must be provided as a stream.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));
    }

    public static void NormalizeAndValidate(SaveFileFromFetchDTO dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        ApplyMetadata(dto, NormalizeMetadata(
            dto.FileName,
            dto.FileExtension,
            dto.Tags,
            dto.Categories,
            dto.Description));

        if (string.IsNullOrWhiteSpace(dto.TempFilePath))
            throw new ArgumentException("TempFilePath must be provided.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));
    }

    public static void NormalizeAndValidate(SaveFileFromUrlDTO dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.FileUrl))
            throw new ArgumentException("FileUrl must be provided.", nameof(dto));

        ApplyMetadata(dto, NormalizeMetadata(
            dto.FileName,
            dto.FileExtension,
            dto.Tags,
            dto.Categories,
            dto.Description));

        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));
    }

    public static void NormalizeAndValidate(SaveFileFromFormFileDTO dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        ApplyMetadata(dto, NormalizeMetadata(
            dto.FileName,
            dto.FileExtension,
            dto.Tags,
            dto.Categories,
            dto.Description));

        if (dto.FormFile is null)
            throw new ArgumentException("FormFile must be provided.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));
    }

    private static FileCreationMetadata NormalizeMetadata(
        string? fileName,
        string? fileExtension,
        IEnumerable<string>? tags,
        IEnumerable<string>? categories,
        string? description)
    {
        string normalizedFileName = FileMetadataPolicy.NormalizeFileName(fileName);
        string normalizedFileExtension = NormalizeSupportedFileExtension(fileExtension);
        string normalizedDescription = FileMetadataPolicy.NormalizeDescription(description);
        var normalizedTags = FileMetadataPolicy.NormalizeMetadataValues(
            tags,
            nameof(SaveFileFromStreamDTO.Tags));
        var normalizedCategories = FileMetadataPolicy.NormalizeMetadataValues(
            categories,
            nameof(SaveFileFromStreamDTO.Categories));

        return new FileCreationMetadata(
            normalizedFileName,
            normalizedFileExtension,
            normalizedTags,
            normalizedCategories,
            normalizedDescription);
    }

    private static string NormalizeSupportedFileExtension(string? fileExtension)
    {
        if (MappingUtils.TryParseSupportedFileExtension(
            fileExtension,
            out _,
            out string normalizedExtension,
            out string error))
        {
            return normalizedExtension;
        }

        throw new ArgumentException(error, nameof(fileExtension));
    }

    private static void ApplyMetadata(SaveFileFromStreamDTO dto, FileCreationMetadata metadata)
    {
        dto.FileName = metadata.FileName;
        dto.FileExtension = metadata.FileExtension;
        dto.Tags = metadata.Tags;
        dto.Categories = metadata.Categories;
        dto.Description = metadata.Description;
    }

    private static void ApplyMetadata(SaveFileFromFetchDTO dto, FileCreationMetadata metadata)
    {
        dto.FileName = metadata.FileName;
        dto.FileExtension = metadata.FileExtension;
        dto.Tags = metadata.Tags;
        dto.Categories = metadata.Categories;
        dto.Description = metadata.Description;
    }

    private static void ApplyMetadata(SaveFileFromUrlDTO dto, FileCreationMetadata metadata)
    {
        dto.FileName = metadata.FileName;
        dto.FileExtension = metadata.FileExtension;
        dto.Tags = metadata.Tags;
        dto.Categories = metadata.Categories;
        dto.Description = metadata.Description;
    }

    private static void ApplyMetadata(SaveFileFromFormFileDTO dto, FileCreationMetadata metadata)
    {
        dto.FileName = metadata.FileName;
        dto.FileExtension = metadata.FileExtension;
        dto.Tags = metadata.Tags;
        dto.Categories = metadata.Categories;
        dto.Description = metadata.Description;
    }

    private readonly record struct FileCreationMetadata(
        string FileName,
        string FileExtension,
        List<string> Tags,
        List<string> Categories,
        string Description);
}
