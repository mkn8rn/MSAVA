namespace MSAVA_BLL.Services.Import;

internal static class ProviderContentType
{
    public static string InferExtension(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        string normalizedContentType = contentType.Trim().ToLowerInvariant();

        return normalizedContentType switch
        {
            "video/mp4" => "mp4",
            "video/webm" => "webm",
            "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/ogg" => "ogg",
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "application/pdf" => "pdf",
            "application/zip" => "zip",
            "application/octet-stream" => "bin",
            "text/plain" => "txt",
            _ when normalizedContentType.Contains("mp4", StringComparison.Ordinal) => "mp4",
            _ when normalizedContentType.Contains("mpeg", StringComparison.Ordinal) => "mp3",
            _ when normalizedContentType.Contains("jpeg", StringComparison.Ordinal) ||
                normalizedContentType.Contains("jpg", StringComparison.Ordinal) => "jpg",
            _ when normalizedContentType.Contains("png", StringComparison.Ordinal) => "png",
            _ => string.Empty
        };
    }
}
