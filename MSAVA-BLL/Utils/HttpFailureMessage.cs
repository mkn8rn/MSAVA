namespace MSAVA_BLL.Utils;

internal static class HttpFailureMessage
{
    public static string FormatStatus(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        string reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? response.StatusCode.ToString()
            : response.ReasonPhrase;

        return $"{(int)response.StatusCode} ({reason})";
    }
}
