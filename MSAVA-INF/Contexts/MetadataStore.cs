using LiteDB;
using MSAVA_INF.Models;
using System.Collections.Concurrent;

namespace MSAVA_INF.Contexts;

/// <summary>
/// Fast file metadata store using LiteDB for sub-millisecond access checks.
/// Includes in-memory caching for frequently accessed records.
/// </summary>
public class MetadataStore : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<SavedFileMetaRecord> _files;
    private readonly ConcurrentDictionary<string, CachedAccessResult> _accessCache = new();
    private readonly Timer _cacheCleanupTimer;
    private bool _disposed;

    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CacheCleanupInterval = TimeSpan.FromMinutes(1);

    private record CachedAccessResult(Guid? RefId, DateTime ExpiresAt);

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

        _cacheCleanupTimer = new Timer(_ => CleanExpiredCache(), null, CacheCleanupInterval, CacheCleanupInterval);
    }

    private static string GetDefaultDatabasePath()
    {
        var baseDir = Path.GetDirectoryName(typeof(MetadataStore).Assembly.Location)!;
        return Path.Combine(baseDir, "Data", "file_metadata.db");
    }

    /// <summary>
    /// Adds metadata for a file.
    /// </summary>
    public void AddMetadata(SavedFileMetaRecord record)
    {
        _files.Insert(record);
        InvalidateCache(record.FileHashHex, record.FileExtension);
    }

    /// <summary>
    /// Gets a metadata record by its reference ID.
    /// </summary>
    public SavedFileMetaRecord? GetByRefId(Guid refId)
    {
        return _files.FindById(refId);
    }

    /// <summary>
    /// Gets all metadata records for a file hash and extension.
    /// </summary>
    public IEnumerable<SavedFileMetaRecord> GetByFileHash(byte[] fileHash, string fileExtension)
    {
        var hashHex = Convert.ToHexString(fileHash);
        return _files.Find(x => x.FileHashHex == hashHex && x.FileExtension == fileExtension);
    }

    /// <summary>
    /// Checks whether a file has public download metadata.
    /// </summary>
    public Guid? CheckPublicDownloadAccess(byte[] fileHash, string fileExtension)
    {
        var hashHex = Convert.ToHexString(fileHash);
        return CheckPublicDownloadAccess(hashHex, fileExtension);
    }

    private Guid? CheckPublicDownloadAccess(string hashHex, string fileExtension)
    {
        var cacheKey = $"public:{hashHex}:{fileExtension}";
        if (_accessCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.RefId;

        var result = CheckPublicDownloadAccessFromDb(hashHex, fileExtension);
        _accessCache[cacheKey] = new CachedAccessResult(result, DateTime.UtcNow.Add(CacheExpiry));
        return result;
    }

    private Guid? CheckPublicDownloadAccessFromDb(string hashHex, string fileExtension)
    {
        var records = _files.Find(x => x.FileHashHex == hashHex && x.FileExtension == fileExtension);

        foreach (var record in records)
        {
            if (record.PublicDownload)
                return record.RefId;
        }

        return null;
    }

    private Guid? CheckAuthorizedAccessFromDb(string hashHex, string fileExtension, List<Guid>? userAccessGroups, bool isAdmin)
    {
        var records = _files.Find(x => x.FileHashHex == hashHex && x.FileExtension == fileExtension);

        foreach (var record in records)
        {
            if (isAdmin)
                return record.RefId;

            if (record.PublicDownload)
                return record.RefId;

            if (userAccessGroups?.Contains(record.AccessGroupId) == true)
                return record.RefId;
        }

        return null;
    }

    /// <summary>
    /// Checks if a user has access to a file by filename (hash.extension format).
    /// Returns the RefId if allowed, throws if denied.
    /// </summary>
    public Guid CheckAccessOrThrow(string fileHashHex, string fileExtension, List<Guid>? userAccessGroups, bool isAdmin = false)
    {
        var normalizedHashHex = fileHashHex.ToUpperInvariant();
        var refId = CheckAuthorizedAccessFromDb(normalizedHashHex, fileExtension, userAccessGroups, isAdmin);

        if (refId is null)
        {
            // Check if file exists at all
            var exists = _files.Exists(x => x.FileHashHex == normalizedHashHex && x.FileExtension == fileExtension);
            if (!exists)
                throw new FileNotFoundException($"No metadata found for file: {fileHashHex}.{fileExtension}");

            throw new UnauthorizedAccessException("User does not have permission to access this file.");
        }

        return refId.Value;
    }

    /// <summary>
    /// Gets all metadata records for a specific access group.
    /// </summary>
    public IEnumerable<SavedFileMetaRecord> GetByAccessGroup(Guid accessGroupId)
    {
        return _files.Find(x => x.AccessGroupId == accessGroupId);
    }

    /// <summary>
    /// Deletes a metadata record by RefId.
    /// </summary>
    public bool Delete(Guid refId)
    {
        var record = _files.FindById(refId);
        if (record is null) return false;

        var result = _files.Delete(refId);
        if (result) InvalidateCache(record.FileHashHex, record.FileExtension);
        return result;
    }

    /// <summary>
    /// Checks if any metadata exists for a file hash and extension.
    /// </summary>
    public bool Exists(byte[] fileHash, string fileExtension)
    {
        var hashHex = Convert.ToHexString(fileHash);
        return _files.Exists(x => x.FileHashHex == hashHex && x.FileExtension == fileExtension);
    }

    private void InvalidateCache(string hashHex, string fileExtension)
    {
        var publicKey = $"public:{hashHex}:{fileExtension}";
        _accessCache.TryRemove(publicKey, out _);
    }

    private void CleanExpiredCache()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _accessCache
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
            _accessCache.TryRemove(key, out _);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cacheCleanupTimer.Dispose();
        _db.Dispose();
    }
}
