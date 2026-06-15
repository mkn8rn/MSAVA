using System.Text;

namespace MSAVA_BLL.Utils;

internal static class HttpErrorBodyReader
{
    public static async Task<string> ReadTrimmedBodyAsync(
        HttpContent? content,
        int maximumBodyLength,
        CancellationToken cancellationToken)
    {
        if (content is null)
            return string.Empty;
        if (maximumBodyLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBodyLength), "Maximum body length must be greater than zero.");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(
            stream,
            ResolveEncoding(content),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: Math.Min(1024, maximumBodyLength + 1),
            leaveOpen: false);

        var buffer = new char[maximumBodyLength + 1];
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken);

            if (read == 0)
                break;

            totalRead += read;
        }

        int responseLength = Math.Min(totalRead, maximumBodyLength);
        return new string(buffer, 0, responseLength).Trim();
    }

    private static Encoding ResolveEncoding(HttpContent content)
    {
        string? charset = content.Headers.ContentType?.CharSet?.Trim('"');

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
            }
        }

        return Encoding.UTF8;
    }
}
