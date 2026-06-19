namespace MSAVA_BLL.Utils;

internal static class HttpFailureMessage
{
    public const int MaximumReasonPhraseLength = 120;

    public static string FormatStatus(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        string reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? response.StatusCode.ToString()
            : SanitizeReasonPhrase(response.ReasonPhrase);

        return $"{(int)response.StatusCode} ({reason})";
    }

    private static string SanitizeReasonPhrase(string reasonPhrase)
    {
        Span<char> buffer = reasonPhrase.Length <= MaximumReasonPhraseLength
            ? stackalloc char[reasonPhrase.Length]
            : stackalloc char[MaximumReasonPhraseLength];

        int written = 0;
        foreach (char c in reasonPhrase)
        {
            if (written >= MaximumReasonPhraseLength)
                break;

            if (c is '\r' or '\n' or '\t')
            {
                buffer[written++] = ' ';
                continue;
            }

            if (char.IsControl(c))
                continue;

            buffer[written++] = c;
        }

        string sanitized = new(buffer[..written]);
        return string.IsNullOrWhiteSpace(sanitized)
            ? "Unknown"
            : sanitized;
    }
}
