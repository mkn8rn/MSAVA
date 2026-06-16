using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_BLL.Utils.Metadata;
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
    private readonly TimeProvider _timeProvider;

    public FileDeduplicationService(
        BaseDataContext context,
        MetadataStore metadataStore,
        IRequestSessionAccessor requestSessionAccessor,
        ServiceLogger serviceLogger,
        ILogger<FileDeduplicationService> logger,
        TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _requestSessionAccessor = requestSessionAccessor ?? throw new ArgumentNullException(nameof(requestSessionAccessor));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HashCheckResult> CheckAndGetReferenceAsync(
        HashCheckRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!HashCheckRequestPolicy.TryValidate(request, out var failureResult))
            return failureResult;

        if (!TryNormalizeHash(request.ContentHashHex, out string hashHex, out var hashValidationError))
        {
            return HashCheckResult.Failed(request.ContentHashHex ?? "", hashValidationError);
        }

        if (!TryNormalizeExtension(request.FileExtension, out string extension, out var extensionValidationError))
        {
            return HashCheckResult.Failed(hashHex, extensionValidationError);
        }

        if (!TryGetActiveSession(out SessionDTO session, out string? sessionError))
        {
            return HashCheckResult.Failed(hashHex, sessionError);
        }

        Guid sessionUserId = session.UserId;
        var fileHash = Convert.FromHexString(hashHex);

        CurrentUserFileAccess currentUserAccess;
        try
        {
            currentUserAccess = await GetCurrentUserFileAccessAsync(sessionUserId, cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            return HashCheckResult.Failed(hashHex, ex.Message);
        }

        // Check if user already has a reference they can access
        var existingReference = await FindExistingAccessibleReferenceAsync(
            fileHash,
            extension,
            currentUserAccess.AccessGroupIds,
            currentUserAccess.IsAdmin,
            cancellationToken);

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

        var accessGroupResolution = await ResolveReferenceAccessGroupAsync(
            request.AccessGroupId,
            sessionUserId,
            currentUserAccess.AccessGroupIds,
            cancellationToken);

        if (!accessGroupResolution.Succeeded)
        {
            return HashCheckResult.Failed(hashHex, accessGroupResolution.Error);
        }

        // File exists but user has no access - create a new reference for them
        SavedFileReferenceDB newReference;
        try
        {
            newReference = await CreateNewReferenceAsync(
                request,
                fileHash,
                extension,
                anyExistingReference,
                sessionUserId,
                accessGroupResolution.AccessGroupId,
                cancellationToken);
        }
        catch (FileMetadataValidationException ex)
        {
            return HashCheckResult.Failed(hashHex, ex.Message);
        }

        await _serviceLogger.WriteLogAsync(
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
        if (!HashCheckBatchPolicy.TryValidate(requests, out var failureResults))
            return failureResults;

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

        return MappingUtils.TryParseSupportedFileExtension(
            fileExtension,
            out _,
            out extension,
            out error);
    }

    private async Task<CurrentUserFileAccess> GetCurrentUserFileAccessAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.IsAdmin, user.IsBanned, user.IsWhitelisted })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"User with id {userId} not found.");

        if (user.IsBanned)
            throw new UnauthorizedAccessException("Banned users cannot check file hashes.");

        if (!user.IsWhitelisted)
            throw new UnauthorizedAccessException("Users must be whitelisted before checking file hashes.");

        var accessGroupIds = await _context.AccessGroups
            .Where(ag => ag.OwnerId == userId || ag.Users.Any(u => u.Id == userId))
            .Select(ag => ag.Id)
            .ToListAsync(cancellationToken);

        return new CurrentUserFileAccess(accessGroupIds, user.IsAdmin);
    }

    private async Task<SavedFileReferenceDB?> FindExistingAccessibleReferenceAsync(
        byte[] fileHash,
        string extension,
        List<Guid> userAccessGroups,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var extensionType = MappingUtils.ParseSupportedFileExtension(extension);

        var matchingReferences = _context.FileRefs
            .Where(fr => fr.FileHash == fileHash && fr.FileExtension == extensionType);

        if (isAdmin)
            return await FileReferenceSelectionPolicy.OrderForStableSelection(matchingReferences)
                .FirstOrDefaultAsync(cancellationToken);

        return await FileReferenceSelectionPolicy.OrderForStableSelection(matchingReferences
                .Where(fr => fr.PublicDownload || userAccessGroups.Contains(fr.AccessGroupId)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SavedFileReferenceDB?> FindAnyExistingReferenceAsync(
        byte[] fileHash,
        string extension,
        CancellationToken cancellationToken)
    {
        var extensionType = MappingUtils.ParseSupportedFileExtension(extension);

        return await FileReferenceSelectionPolicy.OrderForStableSelection(_context.FileRefs
                .Where(fr => fr.FileHash == fileHash && fr.FileExtension == extensionType))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SavedFileReferenceDB> CreateNewReferenceAsync(
        HashCheckRequest request,
        byte[] fileHash,
        string extension,
        SavedFileReferenceDB existingReference,
        Guid userId,
        Guid accessGroupId,
        CancellationToken cancellationToken)
    {
        // Get existing file data for metadata
        var existingData = await _context.FileData
            .Where(fd => fd.FileReferenceId == existingReference.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Create new reference
        var newReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = existingReference.FileExtension,
            AccessGroupId = accessGroupId,
            PublicDownload = request.PublicDownload
        };

        string fileName = FileMetadataPolicy.NormalizeFileName(request.FileName ?? existingData?.Name ?? "Unnamed");
        string description = FileMetadataPolicy.NormalizeDescription(request.Description ?? existingData?.Description);
        IEnumerable<string>? tagValues = request.Tags is not null ? request.Tags : existingData?.Tags;
        IEnumerable<string>? categoryValues = request.Categories is not null ? request.Categories : existingData?.Categories;
        var tags = FileMetadataPolicy.NormalizeMetadataValues(tagValues, nameof(request.Tags));
        var categories = FileMetadataPolicy.NormalizeMetadataValues(categoryValues, nameof(request.Categories));
        DateTime utcNow = GetUtcNow();

        // Create file data record
        var newData = new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReferenceId = newReference.Id,
            SizeInBytes = existingData?.SizeInBytes ?? 0,
            Checksum = Convert.ToHexString(fileHash),
            Name = fileName,
            Description = description,
            MimeType = existingData?.MimeType ?? MetadataExtractor.GetContentType(extension),
            FileExtension = extension,
            Tags = tags.ToArray(),
            Categories = categories.ToArray(),
            Metadata = existingData?.Metadata ?? JsonDocument.Parse("{}"),
            PublicViewing = request.PublicViewing,
            OriginalCreator = userId,
            LastModifiedById = userId,
            SavedAt = utcNow,
            LastModifiedAt = utcNow
        };

        // Create metadata record for fast lookups
        var metaRecord = new SavedFileMetaRecord
        {
            RefId = newReference.Id,
            FileHash = fileHash,
            FileHashHex = Convert.ToHexString(fileHash),
            FileExtension = extension,
            AccessGroupId = accessGroupId,
            PublicDownload = request.PublicDownload,
            CreatedAt = utcNow
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

    private async Task<ReferenceAccessGroupResolution> ResolveReferenceAccessGroupAsync(
        Guid? requestedAccessGroupId,
        Guid userId,
        List<Guid> userAccessGroups,
        CancellationToken cancellationToken)
    {
        if (requestedAccessGroupId is null)
        {
            var defaultAccessGroupId = await GetDefaultAccessGroupAsync(userId, cancellationToken);
            return defaultAccessGroupId is null
                ? ReferenceAccessGroupResolution.Failed("User has no access groups.")
                : ReferenceAccessGroupResolution.Success(defaultAccessGroupId.Value);
        }

        if (requestedAccessGroupId == Guid.Empty)
            return ReferenceAccessGroupResolution.Failed("Access group id must be provided.");

        if (userAccessGroups.Contains(requestedAccessGroupId.Value))
            return ReferenceAccessGroupResolution.Success(requestedAccessGroupId.Value);

        return ReferenceAccessGroupResolution.Failed("User cannot create a file reference in the requested access group.");
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

    private DateTime GetUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private async Task<Guid?> GetDefaultAccessGroupAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Get user's first owned access group, or any they're a member of
        var accessGroup = await OrderAccessGroupsForDefault(_context.AccessGroups
                .Where(ag => ag.OwnerId == userId))
            .Select(ag => ag.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (accessGroup != Guid.Empty)
            return accessGroup;

        // Fall back to any group user is member of
        accessGroup = await OrderAccessGroupsForDefault(_context.AccessGroups
                .Where(ag => ag.Users.Any(u => u.Id == userId)))
            .Select(ag => ag.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (accessGroup == Guid.Empty)
            return null;

        return accessGroup;
    }

    private static IOrderedQueryable<AccessGroupDB> OrderAccessGroupsForDefault(
        IQueryable<AccessGroupDB> accessGroups)
    {
        return accessGroups
            .OrderBy(accessGroup => accessGroup.CreatedAt)
            .ThenBy(accessGroup => accessGroup.Id);
    }

    private bool TryGetActiveSession(out SessionDTO session, out string error)
    {
        session = new SessionDTO();

        if (!SessionGuard.TryRequireActive(
                _requestSessionAccessor.GetSession(),
                out var activeSession,
                out error,
                "User session not found.",
                "Banned users cannot check file hashes."))
            return false;

        session = activeSession;
        return true;
    }

    private sealed record CurrentUserFileAccess(List<Guid> AccessGroupIds, bool IsAdmin);

    private sealed record ReferenceAccessGroupResolution(
        bool Succeeded,
        Guid AccessGroupId,
        string Error)
    {
        public static ReferenceAccessGroupResolution Success(Guid accessGroupId) =>
            new(true, accessGroupId, string.Empty);

        public static ReferenceAccessGroupResolution Failed(string error) =>
            new(false, Guid.Empty, error);
    }

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.Compiled)]
    private static partial Regex Sha256HexRegex();
}

