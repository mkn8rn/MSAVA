using System.Text.Json;

namespace MSAVA_BLL.Utils;

internal static class JsonDocumentUtils
{
    public static JsonDocument CreateEmpty()
    {
        return JsonDocument.Parse("{}");
    }

    public static JsonDocument Clone(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return JsonDocument.Parse(document.RootElement.GetRawText());
    }
}
