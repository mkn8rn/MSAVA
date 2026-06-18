namespace MSAVA_BLL.Services.Import;

internal static class ProviderContentType
{
    private static readonly Dictionary<string, string> ExtensionByMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = "mp4",
        ["video/webm"] = "webm",
        ["audio/mpeg"] = "mp3",
        ["audio/mp3"] = "mp3",
        ["audio/ogg"] = "ogg",
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/pjpeg"] = "jpg",
        ["image/jpg"] = "jpg",
        ["application/pdf"] = "pdf",
        ["application/zip"] = "zip",
        ["application/octet-stream"] = "bin",
        ["text/plain"] = "txt"
    };

    public static string InferExtension(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        string normalizedContentType = contentType.Split(';', 2)[0].Trim();
        return ExtensionByMediaType.GetValueOrDefault(normalizedContentType, string.Empty);
    }
}
