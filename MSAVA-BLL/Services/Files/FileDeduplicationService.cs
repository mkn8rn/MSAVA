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
    private readonly IRequestSessionAccessor _requestSessionAccessor;
    private readonly ServiceLogger _serviceLogger;
    private readonly ILogger<FileDeduplicationService> _logger;

    public FileDeduplicationService(
        BaseDataContext context,
        MetadataStore metadataStore,
        IRequestSessionAccessor requestSessionAccessor,
        ServiceLogger serviceLogger,
        ILogger<FileDeduplicationService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _requestSessionAccessor = requestSessionAccessor ?? throw new ArgumentNullException(nameof(requestSessionAccessor));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HashCheckResult> CheckAndGetReferenceAsync(
        HashCheckRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return HashCheckResult.Failed("", "Hash check request is required.");

        if (!TryNormalizeHash(request.ContentHashHex, out string hashHex, out var hashValidationError))
        {
            return HashCheckResult.Failed(request.ContentHashHex ?? "", hashValidationError);
        }

        if (!TryNormalizeExtension(request.FileExtension, out string extension, out var extensionValidationError))
        {
            return HashCheckResult.Failed(hashHex, extensionValidationError);
        }

        if (!TryGetActiveSessionUserId(out Guid sessionUserId, out string? sessionError))
        {
            return HashCheckResult.Failed(hashHex, sessionError);
        }

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
            request, fileHash, extension, anyExistingReference, sessionUserId, userAccessGroups, cancellationToken);

        _serviceLogger.WriteLog(
            AccessLogActions.NewReferenceAddedToExistingFile,
            $"Created reference to existing file: {hashHex}.{extension}",
            sessionUserId,
            $"{hashHex}.{extension}",
            newReference.Id);

        return HashCheckResult.NewReference(hashHex, newReference.Id);
    }

    public async Task<List<HashCheckResult>> CheckAndGetReferenceBatchAsync(
        List<HashCheckRequest>? requests,
        CancellationToken cancellationToken = default)
    {
        if (requests is null)
        {
            return [HashCheckResult.Failed("", "Hash check batch request is required.")];
        }

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

    private static bool TryNormalizeHash(
        string? contentHashHex,
        out string hashHex,
        out string error)
    {
        hashHex = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(contentHashHex) || !Sha256HexRegex().IsMatch(contentHashHex))
        {
            error = "Invalid hash format. Expected 64 hexadecimal characters.";
            return false;
        }

        hashHex = contentHashHex.ToUpperInvariant();
        return true;
    }

    private static bool TryNormalizeExtension(
        string? fileExtension,
        out string extension,
        out string error)
    {
        extension = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(fileExtension))
        {
            error = "FileExtension must be provided.";
            return false;
        }

        extension = fileExtension.Trim().TrimStart('.').ToLowerInvariant();
        if (extension.Length == 0)
        {
            error = "FileExtension must be provided.";
            return false;
        }

        return true;
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
        List<Guid> userAccessGroups,
        CancellationToken cancellationToken)
    {
        // Get existing file data for metadata
        var existingData = await _context.FileData
            .Where(fd => fd.FileReferenceId == existingReference.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var accessGroupId = await ResolveReferenceAccessGroupAsync(
            request.AccessGroupId,
            userId,
            userAccessGroups,
            cancellationToken);

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

    private async Task<Guid> ResolveReferenceAccessGroupAsync(
        Guid? requestedAccessGroupId,
        Guid userId,
        List<Guid> userAccessGroups,
        CancellationToken cancellationToken)
    {
        if (requestedAccessGroupId is null)
            return await GetDefaultAccessGroupAsync(userId, cancellationToken);

        if (requestedAccessGroupId == Guid.Empty)
            throw new ArgumentException("Access group id must be provided.", nameof(requestedAccessGroupId));

        if (userAccessGroups.Contains(requestedAccessGroupId.Value))
            return requestedAccessGroupId.Value;

        throw new UnauthorizedAccessException("User cannot create a file reference in the requested access group.");
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

    private bool TryGetActiveSessionUserId(out Guid userId, out string error)
    {
        userId = Guid.Empty;

        if (!SessionGuard.TryRequireActive(
                _requestSessionAccessor.GetSession(),
                out var activeSession,
                out error,
                "User session not found.",
                "Banned users cannot check file hashes."))
            return false;

        userId = activeSession.UserId;
        return true;
    }

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.Compiled)]
    private static partial Regex Sha256HexRegex();
}

