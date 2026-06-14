namespace MSAVA_INF.Utils;

public readonly record struct StoredFileName(byte[] FileHash, string FileHashHex, string Extension)
{
    private const int Sha256HashByteLength = 32;
    private const int Sha256HashHexLength = Sha256HashByteLength * 2;

    public static bool TryParse(string? pathOrFileName, out StoredFileName storedFileName)
    {
        storedFileName = default;

        if (string.IsNullOrWhiteSpace(pathOrFileName))
            return false;

        string? fileName = Path.GetFileName(pathOrFileName);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        int lastDot = fileName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == fileName.Length - 1)
            return false;

        string hashHex = fileName[..lastDot];
        if (!IsSha256Hex(hashHex))
            return false;

        byte[] fileHash = Convert.FromHexString(hashHex);
        if (fileHash.Length != Sha256HashByteLength)
            return false;

        storedFileName = new StoredFileName(
            fileHash,
            hashHex.ToUpperInvariant(),
            fileName[(lastDot + 1)..].ToLowerInvariant());
        return true;
    }

    private static bool IsSha256Hex(string hashHex)
    {
        if (hashHex.Length != Sha256HashHexLength)
            return false;

        foreach (char c in hashHex)
        {
            if (!IsAsciiHexDigit(c))
                return false;
        }

        return true;
    }

    private static bool IsAsciiHexDigit(char c)
    {
        return c is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';
    }
}
