using LiteDB;

namespace MSAVA_INF.Models;

/// <summary>
/// File metadata record stored in LiteDB for fast access checks.
/// Complex queries (tags, categories, search) are handled by PostgreSQL.
/// </summary>
public class SavedFileMetaRecord
{
    [BsonId]
    public Guid RefId { get; set; }

    /// <summary>
    /// File hash stored as hex string for efficient indexing.
    /// </summary>
    public string FileHashHex { get; set; } = string.Empty;

    public string FileExtension { get; set; } = string.Empty;

    public Guid AccessGroupId { get; set; }

    public bool PublicDownload { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Helper property for hash conversion (not stored in DB).
    /// </summary>
    [BsonIgnore]
    public byte[] FileHash
    {
        get => Convert.FromHexString(FileHashHex);
        set => FileHashHex = Convert.ToHexString(value);
    }
}
