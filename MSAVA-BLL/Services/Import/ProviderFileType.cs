using MSAVA_BLL.Utils;

namespace MSAVA_BLL.Services.Import;

internal static class ProviderFileType
{
    public static string RequireSupportedExtension(string providerName, string? inferredExtension)
    {
        if (MappingUtils.TryParseSupportedFileExtension(
                inferredExtension,
                out _,
                out string normalizedExtension,
                out string error))
        {
            return normalizedExtension;
        }

        if (string.IsNullOrWhiteSpace(inferredExtension))
            throw new InvalidOperationException($"{providerName} response did not include a supported file extension.");

        throw new InvalidOperationException($"{providerName} response file extension is not supported: {error}");
    }
}
