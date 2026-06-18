namespace MSAVA_API.Diagnostics;

internal static class RequestLogValueSanitizer
{
    public const int MaximumValueLength = 256;

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        Span<char> buffer = value.Length <= MaximumValueLength
            ? stackalloc char[value.Length]
            : stackalloc char[MaximumValueLength];

        int written = 0;
        foreach (char c in value)
        {
            if (written >= MaximumValueLength)
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

        return new string(buffer[..written]);
    }
}
