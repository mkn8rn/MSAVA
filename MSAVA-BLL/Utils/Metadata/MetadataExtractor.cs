using System.Text.Json;

namespace MSAVA_BLL.Utils.Metadata;

/// <summary>
/// Main entry point for metadata extraction. Delegates to specialized extractors.
/// Unsupported formats return InvalidMetadata values for easy detection.
/// </summary>
public static class MetadataExtractor
{
    private static readonly Dictionary<string, (Func<Stream, long, JsonDocument> Extractor, string ContentType)> ExtensionMap =
        new(StringComparer.OrdinalIgnoreCase);

    static MetadataExtractor()
    {
        // Register all extractors - only supported formats
        ImageMetadataExtractor.Register(ExtensionMap);
        VectorMetadataExtractor.Register(ExtensionMap);
        AudioMetadataExtractor.Register(ExtensionMap);
        VideoMetadataExtractor.Register(ExtensionMap);
        DocumentMetadataExtractor.Register(ExtensionMap);
        TextMetadataExtractor.Register(ExtensionMap);
    }

    /// <summary>
    /// Extracts metadata from a file stream based on its extension.
    /// Returns InvalidMetadata values for unsupported formats.
    /// </summary>
    public static JsonDocument ExtractMetadata(Stream fileStream, string extension, long size = -1)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        
        if (ExtensionMap.TryGetValue(ext, out var entry))
        {
            try
            {
                if (fileStream.CanSeek)
                    fileStream.Position = 0;
                    
                return entry.Extractor(fileStream, size);
            }
            catch (Exception ex) when (IsRecoverableExtractionFailure(ex))
            {
                return CreateInvalidMetadata(size, "Extraction failed");
            }
        }
        
        return CreateInvalidMetadata(size, "Unsupported format");
    }

    private static bool IsRecoverableExtractionFailure(Exception exception)
    {
        return exception is not OperationCanceledException
            and not OutOfMemoryException;
    }

    /// <summary>
    /// Gets the content type for a file extension.
    /// </summary>
    public static string GetContentType(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        
        if (ExtensionMap.TryGetValue(ext, out var entry))
            return entry.ContentType;
            
        return "application/octet-stream";
    }

    /// <summary>
    /// Checks if an extension is supported for metadata extraction.
    /// </summary>
    public static bool IsSupported(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ExtensionMap.ContainsKey(ext);
    }

    /// <summary>
    /// Creates metadata for unsupported/invalid formats with sentinel values.
    /// </summary>
    internal static JsonDocument CreateInvalidMetadata(long size, string reason)
    {
        var result = new
        {
            Type = InvalidMetadata.String,
            Valid = false,
            Reason = reason,
            Size = size
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(result));
    }

    /// <summary>
    /// Helper to serialize metadata to JsonDocument.
    /// </summary>
    internal static JsonDocument ToJsonDocument<T>(T metadata)
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(metadata));
    }
}
