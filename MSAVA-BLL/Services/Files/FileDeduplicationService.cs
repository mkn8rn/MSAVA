using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MSAVA_BLL.Services.Files;

/// <summary>
/// Service for deduplication - checking file hashes and creating references to existing files.
/// </summary>
public partial class FileDeduplicationService : IFileDeduplicationService
{
    private readonly BaseDataContext _context;
    private readonly MetadataStore _metadataStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ServiceLogger _serviceLogger;
    private readonly ILogger<FileDeduplicationService> _logger;

    public FileDeduplicationService(
        BaseDataContext context,
        MetadataStore metadataStore,
        IHttpContextAccessor httpContextAccessor,
        ServiceLogger serviceLogger,
        ILogger<FileDeduplicationService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HashCheckResult> CheckAndGetReferenceAsync(
        HashCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate hash format
        if (string.IsNullOrWhiteSpace(request.ContentHashHex) ||
            !Sha256HexRegex().IsMatch(request.ContentHashHex))
        {
            return HashCheckResult.Failed(request.ContentHashHex ?? "", "Invalid hash format. Expected 64 hexadecimal characters.");
        }

        var sessionUserId = GetSessionUserId();
        if (sessionUserId == Guid.Empty)
        {
            return HashCheckResult.Failed(request.ContentHashHex, "User session not found.");
        }

        var hashHex = request.ContentHashHex.ToUpperInvariant();
        var extension = request.FileExtension.TrimStart('.').ToLowerInvariant();
        var fileHash = Convert.FromHexString(hashHex);

        // Get user's access groups
        var userAccessGroups = await GetUserAccessGroupsAsync(sessionUserId, cancellationToken);

        // Check if user already has a reference they can access
        var existingReference = await FindExistingAccessibleReferenceAsync(
            fileHash, extension, sessionUserId, userAccessGroups, cancellationToken);

        if (existingReference != null)
        {
            return HashCheckResult.ExistingAccess(hashHex, existingReference.Id);
        }

        // Check if file exists at all (any reference)
        var anyExistingReference = await FindAnyExistingReferenceAsync(fileHash, extension, cancellationToken);

        if (anyExistingReference == null)
        {
            // File doesn't exist on server - upload required
            return HashCheckResult.NotFound(hashHex);
        }

        // File exists but user has no access - create a new reference for them
        var newReference = await CreateNewReferenceAsync(
            request, fileHash, extension, anyExistingReference, sessionUserId, cancellationToken);

        _serviceLogger.WriteLog(
            AccessLogActions.NewReferenceAddedToExistingFile,
            $"Created reference to existing file: {hashHex}.{extension}",
            sessionUserId,
            $"{hashHex}.{extension}",
            newReference.Id);

        return HashCheckResult.NewReference(hashHex, newReference.Id);
    }

    public async Task<List<HashCheckResult>> CheckAndGetReferenceBatchAsync(
        List<HashCheckRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count > 100)
        {
            return [HashCheckResult.Failed("", "Maximum 100 hashes per batch request.")];
        }

        var results = new List<HashCheckResult>(requests.Count);

        foreach (var request in requests)
        {
            var result = await CheckAndGetReferenceAsync(request, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    private async Task<List<Guid>> GetUserAccessGroupsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.AccessGroups
            .Where(ag => ag.OwnerId == userId || ag.Users.Any(u => u.Id == userId))
            .Select(ag => ag.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<SavedFileReferenceDB?> FindExistingAccessibleReferenceAsync(
        byte[] fileHash,
        string extension,
        Guid userId,
        List<Guid> userAccessGroups,
        CancellationToken cancellationToken)
    {
        var extensionType = MappingUtils.ParseFileExtension(extension);

        // Find a reference that:
        // 1. Matches hash and extension
        // 2. User has access to (via access group membership or public download)
        return await _context.FileRefs
            .Where(fr => fr.FileHash == fileHash && fr.FileExtension == extensionType)
            .Where(fr => fr.PublicDownload || userAccessGroups.Contains(fr.AccessGroupId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SavedFileReferenceDB?> FindAnyExistingReferenceAsync(
        byte[] fileHash,
        string extension,
        CancellationToken cancellationToken)
    {
        var extensionType = MappingUtils.ParseFileExtension(extension);

        return await _context.FileRefs
            .Where(fr => fr.FileHash == fileHash && fr.FileExtension == extensionType)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SavedFileReferenceDB> CreateNewReferenceAsync(
        HashCheckRequest request,
        byte[] fileHash,
        string extension,
        SavedFileReferenceDB existingReference,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Get existing file data for metadata
        var existingData = await _context.FileData
            .Where(fd => fd.FileReferenceId == existingReference.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Determine access group - use provided or user's first access group
        var accessGroupId = request.AccessGroupId ?? await GetDefaultAccessGroupAsync(userId, cancellationToken);

        // Create new reference
        var newReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = existingReference.FileExtension,
            AccessGroupId = accessGroupId,
            PublicDownload = request.PublicDownload
        };

        // Create file data record
        var newData = new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReferenceId = newReference.Id,
            SizeInBytes = existingData?.SizeInBytes ?? 0,
            Checksum = Convert.ToHexString(fileHash),
            Name = request.FileName ?? existingData?.Name ?? "Unnamed",
            Description = request.Description ?? existingData?.Description ?? "",
            MimeType = existingData?.MimeType ?? MetadataUtils.GetContentType(extension),
            FileExtension = extension,
            Tags = request.Tags?.ToArray() ?? existingData?.Tags ?? [],
            Categories = request.Categories?.ToArray() ?? existingData?.Categories ?? [],
            Metadata = existingData?.Metadata ?? JsonDocument.Parse("{}"),
            PublicViewing = request.PublicViewing,
            OriginalCreator = userId,
            LastModifiedById = userId,
            SavedAt = DateTime.UtcNow,
            LastModifiedAt = DateTime.UtcNow
        };

        // Create metadata record for fast lookups
        var metaRecord = new SavedFileMetaRecord
        {
            RefId = newReference.Id,
            FileHash = fileHash,
            FileHashHex = Convert.ToHexString(fileHash),
            FileExtension = extension,
            AccessGroupId = accessGroupId,
            PublicDownload = request.PublicDownload
        };

        _context.FileRefs.Add(newReference);
        _context.FileData.Add(newData);

        bool metadataRecorded = false;
        try
        {
            _metadataStore.AddMetadata(metaRecord);
            metadataRecorded = true;

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            RollbackNewReference(newReference, newData, metaRecord, metadataRecorded);
            throw;
        }

        return newReference;
    }

    private void RollbackNewReference(
        SavedFileReferenceDB newReference,
        SavedFileDataDB newData,
        SavedFileMetaRecord metaRecord,
        bool metadataRecorded)
    {
        if (metadataRecorded)
            DeleteMetadataRecord(metaRecord);

        DetachIfTracked(newData);
        DetachIfTracked(newReference);
    }

    private void DeleteMetadataRecord(SavedFileMetaRecord metaRecord)
    {
        try
        {
            _metadataStore.Delete(metaRecord.RefId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to roll back deduplicated metadata record {FileRefId}", metaRecord.RefId);
        }
    }

    private void DetachIfTracked(object entity)
    {
        var entry = _context.Entry(entity);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }

    private async Task<Guid> GetDefaultAccessGroupAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Get user's first owned access group, or any they're a member of
        var accessGroup = await _context.AccessGroups
            .Where(ag => ag.OwnerId == userId)
            .Select(ag => ag.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (accessGroup != Guid.Empty)
            return accessGroup;

        // Fall back to any group user is member of
        accessGroup = await _context.AccessGroups
            .Where(ag => ag.Users.Any(u => u.Id == userId))
            .Select(ag => ag.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (accessGroup == Guid.Empty)
            throw new InvalidOperationException("User has no access groups.");

        return accessGroup;
    }

    private Guid GetSessionUserId()
    {
        if (_httpContextAccessor.HttpContext?.Items["SessionDTO"] is SessionDTO sessionDto)
            return sessionDto.UserId;
        return Guid.Empty;
    }

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.Compiled)]
    private static partial Regex Sha256HexRegex();
}

