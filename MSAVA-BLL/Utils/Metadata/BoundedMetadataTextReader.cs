using System.Text;

namespace MSAVA_BLL.Utils.Metadata;

internal static class BoundedMetadataTextReader
{
    internal const int MaximumAnalyzedCharacters = 1024 * 1024;

    internal static BoundedMetadataText Read(
        Stream stream,
        Encoding? encoding = null,
        bool detectEncodingFromByteOrderMarks = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(
            stream,
            encoding ?? Encoding.UTF8,
            detectEncodingFromByteOrderMarks,
            bufferSize: 4096,
            leaveOpen: true);

        var buffer = new char[4096];
        var content = new StringBuilder(buffer.Length);
        int remainingCharacters = MaximumAnalyzedCharacters;

        while (remainingCharacters > 0)
        {
            int charactersRead = reader.ReadBlock(
                buffer,
                0,
                Math.Min(buffer.Length, remainingCharacters));

            if (charactersRead == 0)
                break;

            content.Append(buffer, 0, charactersRead);
            remainingCharacters -= charactersRead;
        }

        bool truncated = remainingCharacters == 0 && reader.Peek() != -1;

        return new BoundedMetadataText(content.ToString(), reader.CurrentEncoding, truncated);
    }
}

internal sealed record BoundedMetadataText(
    string Content,
    Encoding Encoding,
    bool Truncated);
