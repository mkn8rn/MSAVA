using System.Globalization;

namespace MSAVA_BLL.Utils.Signature;

/// <summary>
/// Represents an MSAVA file signature embedded in downloaded files.
/// Format: MSAVA:v1:{content_hash}:{file_id}:{timestamp}
/// </summary>
public sealed record MsavaSignature
{
    public const string Prefix = "MSAVA";
    public const int CurrentVersion = 1;
    public const string MetadataKey = "MSAVA-Signature";
    
    /// <summary>
    /// Signature version for forward compatibility.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;
    
    /// <summary>
    /// The SHA-256 hash of the original file content.
    /// </summary>
    public required string ContentHash { get; init; }
    
    /// <summary>
    /// The database ID of the file.
    /// </summary>
    public required long FileId { get; init; }
    
    /// <summary>
    /// Unix timestamp when the signature was created.
    /// </summary>
    public required long Timestamp { get; init; }

    /// <summary>
    /// Creates a new signature for a file.
    /// </summary>
    public static MsavaSignature Create(string contentHash, long fileId, DateTimeOffset createdAt)
    {
        return new MsavaSignature
        {
            ContentHash = contentHash,
            FileId = fileId,
            Timestamp = createdAt.ToUnixTimeSeconds()
        };
    }

    /// <summary>
    /// Serializes the signature to a string.
    /// </summary>
    public override string ToString()
    {
        return $"{Prefix}:v{Version}:{ContentHash}:{FileId}:{Timestamp}";
    }

    /// <summary>
    /// Tries to parse a signature string.
    /// </summary>
    public static bool TryParse(string? input, out MsavaSignature? signature)
    {
        signature = null;
        
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Find the MSAVA prefix in case there's surrounding content
        var startIndex = input.IndexOf($"{Prefix}:v", StringComparison.Ordinal);
        if (startIndex < 0)
            return false;

        var signaturePart = input[startIndex..];
        var endIndex = signaturePart.IndexOfAny(['\0', '\n', '\r', ' ']);
        if (endIndex > 0)
            signaturePart = signaturePart[..endIndex];

        var parts = signaturePart.Split(':');
        if (parts.Length < 5)
            return false;

        // Validate prefix
        if (parts[0] != Prefix)
            return false;

        // Parse version
        if (!parts[1].StartsWith('v') ||
            !TryParseUnsignedInt32(parts[1][1..], out var version) ||
            version == 0)
        {
            return false;
        }

        // Parse remaining fields
        var contentHash = parts[2];
        if (!IsSha256Hex(contentHash))
            return false;

        if (!TryParseUnsignedInt64(parts[3], out var fileId))
            return false;

        if (!TryParseUnsignedInt64(parts[4], out var timestamp))
            return false;

        signature = new MsavaSignature
        {
            Version = version,
            ContentHash = contentHash,
            FileId = fileId,
            Timestamp = timestamp
        };

        return true;
    }

    private static bool TryParseUnsignedInt32(string value, out int result)
    {
        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryParseUnsignedInt64(string value, out long result)
    {
        return long.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool IsSha256Hex(string contentHash)
    {
        if (contentHash.Length != 64)
            return false;

        foreach (char c in contentHash)
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

    /// <summary>
    /// Gets the age of the signature.
    /// </summary>
    public TimeSpan GetAge(DateTimeOffset utcNow)
    {
        return utcNow - DateTimeOffset.FromUnixTimeSeconds(Timestamp);
    }
}
