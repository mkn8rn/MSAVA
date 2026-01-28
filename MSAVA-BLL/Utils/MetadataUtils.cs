using System.Text.Json;
using MSAVA_BLL.Utils.Metadata;

namespace MSAVA_BLL.Utils;

/// <summary>
/// Facade for metadata extraction. Delegates to specialized extractors in MSAVA_BLL.Utils.Metadata.
/// This class is kept for backward compatibility.
/// </summary>
public static class MetadataUtils
{
    /// <summary>
    /// Extracts metadata from a file stream based on its extension.
    /// </summary>
    public static JsonDocument ExtractMetadataFromFileStream(Stream fileStream, string extension, long size = -1)
    {
        return MetadataExtractor.ExtractMetadata(fileStream, extension, size);
    }

    /// <summary>
    /// Gets the content type for a file extension.
    /// </summary>
    public static string GetContentType(string extension)
    {
        return MetadataExtractor.GetContentType(extension);
    }
}
