using System.Text.Json;

namespace MSAVA_BLL.Utils;

internal static class JsonDocumentUtils
{
    public static JsonDocument CreateEmpty()
    {
        return JsonDocument.Parse("{}");
    }

}
