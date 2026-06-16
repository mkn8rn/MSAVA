using LiteDB;
using MSAVA_INF.Models;

namespace MSAVA_INF.Contexts;

/// <summary>
/// File metadata store using LiteDB for metadata lookup and rollback support.
/// </summary>
public class MetadataStore : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<SavedFileMetaRecord> _files;
    private bool _disposed;

    public MetadataStore(string? databasePath = null)
    {
        databasePath ??= GetDefaultDatabasePath();

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // Configure LiteDB for better performance
        var connectionString = new ConnectionString(databasePath)
        {
            Connection = ConnectionType.Shared, // Allow concurrent reads
        };

        _db = new LiteDatabase(connectionString);
        _files = _db.GetCollection<SavedFileMetaRecord>("file_metadata");

        // Create composite index for the most common query pattern
        _files.EnsureIndex(x => x.FileHashHex);
        _files.EnsureIndex(x => x.FileExtension);
        _files.EnsureIndex(x => x.AccessGroupId);
        _files.EnsureIndex("FileHash_Extension", BsonExpression.Create("{ FileHashHex: $.FileHashHex, FileExtension: $.FileExtension }"));
    }

    private static string GetDefaultDatabasePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Data", "file_metadata.db");
    }

    /// <summary>
    /// Adds metadata for a file.
    /// </summary>
    public void AddMetadata(SavedFileMetaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.CreatedAt = NormalizeUtc(record.CreatedAt);
        _files.Insert(record);
    }

    /// <summary>
    /// Gets a metadata record by its reference ID.
    /// </summary>
    public SavedFileMetaRecord? GetByRefId(Guid refId)
    {
        var record = _files.FindById(refId);
        return record is null ? null : NormalizeRecord(record);
    }

    /// <summary>
    /// Gets all metadata records for a file hash and extension.
    /// </summary>
    public IEnumerable<SavedFileMetaRecord> GetByFileHash(byte[] fileHash, string fileExtension)
    {
        var hashHex = Convert.ToHexString(fileHash);
        return _files
            .Find(x => x.FileHashHex == hashHex && x.FileExtension == fileExtension)
            .Select(NormalizeRecord);
    }

    /// <summary>
    /// Gets all metadata records for a specific access group.
    /// </summary>
    public IEnumerable<SavedFileMetaRecord> GetByAccessGroup(Guid accessGroupId)
    {
        return _files
            .Find(x => x.AccessGroupId == accessGroupId)
            .Select(NormalizeRecord);
    }

    /// <summary>
    /// Deletes a metadata record by RefId.
    /// </summary>
    public bool Delete(Guid refId)
    {
        var record = _files.FindById(refId);
        if (record is null) return false;

        return _files.Delete(refId);
    }

    /// <summary>
    /// Checks if any metadata exists for a file hash and extension.
    /// </summary>
    public bool Exists(byte[] fileHash, string fileExtension)
    {
        var hashHex = Convert.ToHexString(fileHash);
        return _files.Exists(x => x.FileHashHex == hashHex && x.FileExtension == fileExtension);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _db.Dispose();
    }

    private static SavedFileMetaRecord NormalizeRecord(SavedFileMetaRecord record)
    {
        record.CreatedAt = NormalizeUtc(record.CreatedAt);
        return record;
    }

    private static DateTime NormalizeUtc(DateTime timestamp)
    {
        return timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };
    }
}
