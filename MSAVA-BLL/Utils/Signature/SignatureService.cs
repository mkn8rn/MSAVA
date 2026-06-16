namespace MSAVA_BLL.Utils.Signature;

/// <summary>
/// High-level service for MSAVA signature operations.
/// Use this in controllers/services rather than calling Embedder/Detector directly.
/// </summary>
public sealed class SignatureService
{
    private readonly TimeProvider _timeProvider;

    public SignatureService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Prepares a file for download by embedding an MSAVA signature.
    /// </summary>
    /// <param name="fileStream">The original file stream.</param>
    /// <param name="extension">File extension (with or without dot).</param>
    /// <param name="contentHash">The SHA-256 hash of the file content.</param>
    /// <param name="fileId">The database ID of the file.</param>
    /// <returns>A stream with the signature embedded, or original if embedding not supported.</returns>
    public Stream PrepareForDownload(Stream fileStream, string extension, string contentHash, long fileId)
    {
        if (!SignatureEmbedder.IsSupported(extension))
            return fileStream;

        var signature = MsavaSignature.Create(contentHash, fileId, _timeProvider.GetUtcNow());
        return SignatureEmbedder.TryEmbed(fileStream, extension, signature);
    }

    /// <summary>
    /// Checks an uploaded file for an MSAVA signature.
    /// </summary>
    /// <param name="fileStream">The uploaded file stream.</param>
    /// <param name="extension">File extension (with or without dot).</param>
    /// <returns>Detection result with signature details if found.</returns>
    public SignatureDetector.DetectionResult CheckUpload(Stream fileStream, string extension)
    {
        return SignatureDetector.TryDetect(fileStream, extension);
    }

    /// <summary>
    /// Quick check if a file might be from MSAVA (checks first few KB for signature pattern).
    /// This is a fast pre-check before full detection.
    /// </summary>
    public bool QuickCheck(Stream fileStream)
    {
        if (!fileStream.CanSeek)
            return false;

        var originalPosition = fileStream.Position;
        
        try
        {
            // Read first 64KB to look for signature pattern
            var buffer = new byte[Math.Min(65536, fileStream.Length)];
            var bytesRead = fileStream.Read(buffer, 0, buffer.Length);
            
            // Convert to string and look for pattern
            // This is a rough check - the pattern might be in binary metadata
            var content = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead);
            return content.Contains(MsavaSignature.Prefix + ":v");
        }
        finally
        {
            fileStream.Position = originalPosition;
        }
    }

    /// <summary>
    /// Gets the list of extensions that support signature embedding.
    /// </summary>
    public static IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Images (ImageSharp)
        "png", "jpg", "jpeg", "webp", "gif", "bmp", "tiff", "tga", "pbm", "pgm", "ppm", "dib",
        
        // Vector
        "svg", "svgz",
        
        // Audio (TagLib)
        "mp3", "flac", "m4a", "ogg", "wav", "aac", "wma", "aiff", "opus", "ape", "mpc", "wv", "dsf", "au",
        
        // Video (TagLib)
        "mp4", "mkv", "avi", "mov", "webm", "wmv", "flv", "mpg", "mpeg", "3gp", "3g2", "ogv", "asf", "m4v", "f4v",
        
        // Documents
        "docx", "xlsx", "pptx", "odt", "ods", "odp", "pdf"
    };
}

/// <summary>
/// Extension methods for signature operations.
/// </summary>
public static class SignatureExtensions
{
    /// <summary>
    /// Checks if this extension supports MSAVA signatures.
    /// </summary>
    public static bool SupportsSignature(this string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return SignatureService.SupportedExtensions.Contains(ext);
    }
}
