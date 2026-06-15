using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_BLL.Utils.Metadata;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Files;

public class FilePersistenceService
{
    private readonly BaseDataContext _context;
    private readonly FileManager _fileManager;
    private readonly IRequestSessionAccessor _requestSessionAccessor;
    private readonly ServiceLogger _serviceLogger;
    private readonly ILogger<FilePersistenceService> _logger;

    public FilePersistenceService(
        BaseDataContext context,
        FileManager fileManager,
        IRequestSessionAccessor requestSessionAccessor,
        ServiceLogger serviceLogger,
        ILogger<FilePersistenceService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _requestSessionAccessor = requestSessionAccessor ?? throw new ArgumentNullException(nameof(requestSessionAccessor));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Guid> CreateFileFromStreamAsync(SaveFileFromStreamDTO dto, CancellationToken cancellationToken = default)
    {
        ValidateStreamDto(dto);

        Guid sessionUserId = await AuthorizeCreateInAccessGroupAsync(dto.AccessGroupId, cancellationToken);

        string tempFilePath = Path.GetTempFileName();

        try
        {
            var (fileHash, fileLength) = await CopyStreamToTempFileAndHashAsync(dto.Stream, tempFilePath, cancellationToken);
            var savedFileDb = MappingUtils.MapSavedFileReferenceDB(dto, fileHash);
            var metaRecord = MappingUtils.MapSavedFileMetaRecord(savedFileDb);
            string fileExtension = FileExtensionUtils.GetFileExtension(savedFileDb);
            JsonDocument metadata = ExtractMetadataFromTempFile(tempFilePath, fileExtension, fileLength);

            return await PersistFileRegistrationAsync(
                savedFileDb,
                metaRecord,
                () => MappingUtils.MapSavedFileDataDB(dto, savedFileDb, (ulong)fileLength, sessionUserId, sessionUserId, metadata),
                tempFilePath,
                sessionUserId,
                cancellationToken);
        }
        finally
        {
            TemporaryFileCleanup.DeleteIfPresent(tempFilePath, _logger);
        }
    }

    public async Task<Guid> CreateFileFromTempFileAsync(SaveFileFromFetchDTO dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        string tempFilePath = dto.TempFilePath;

        try
        {
            ValidateFetchDto(dto);

            Guid sessionUserId = await AuthorizeCreateInAccessGroupAsync(dto.AccessGroupId, cancellationToken);

            long fileLength = new FileInfo(tempFilePath).Length;
            byte[] fileHash = await ComputeFileHashAsync(tempFilePath, cancellationToken);
            var savedFileDb = MappingUtils.MapSavedFileReferenceDB(dto, fileHash);
            var metaRecord = MappingUtils.MapSavedFileMetaRecord(savedFileDb);

            return await PersistFileRegistrationAsync(
                savedFileDb,
                metaRecord,
                () => MappingUtils.MapSavedFileDataDB(dto, savedFileDb, (ulong)fileLength, sessionUserId, sessionUserId),
                tempFilePath,
                sessionUserId,
                cancellationToken);
        }
        finally
        {
            TemporaryFileCleanup.DeleteIfPresent(tempFilePath, _logger);
        }
    }

    public async Task<Guid> AuthorizeCreateInAccessGroupAsync(
        Guid accessGroupId,
        CancellationToken cancellationToken = default)
    {
        Guid sessionUserId = GetRequiredSessionUserId();
        await EnsureSessionUserCanCreateInAccessGroupAsync(sessionUserId, accessGroupId, cancellationToken);
        return sessionUserId;
    }

    private async Task<Guid> PersistFileRegistrationAsync(
        SavedFileReferenceDB savedFileDb,
        SavedFileMetaRecord metaRecord,
        Func<SavedFileDataDB> createFileData,
        string tempFilePath,
        Guid sessionUserId,
        CancellationToken cancellationToken)
    {
        SavedFileDataDB? savedFileDataDb = null;
        bool contentFileCreated = false;

        try
        {
            contentFileCreated = await _fileManager.SaveTempFileAsync(metaRecord, tempFilePath, cancellationToken);
            savedFileDataDb = createFileData();

            _context.FileRefs.Add(savedFileDb);
            _context.FileData.Add(savedFileDataDb);
            await _context.SaveChangesAsync(cancellationToken);

            string fileName = MappingUtils.GetFileName(savedFileDb);
            string fileExtension = FileExtensionUtils.GetFileExtension(savedFileDb);
            string fileNameWithExtension = $"{fileName}.{fileExtension}";

            await _serviceLogger.WriteLogAsync(AccessLogActions.NewFileCreated, $"File created: {fileNameWithExtension}", sessionUserId, fileNameWithExtension, savedFileDb.Id);

            return savedFileDb.Id;
        }
        catch
        {
            RollbackFileRegistration(metaRecord, contentFileCreated);
            DetachPendingFileEntities(savedFileDb, savedFileDataDb);
            throw;
        }
    }

    private static void ValidateStreamDto(SaveFileFromStreamDTO dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.FileName))
            throw new ArgumentException("FileName must be provided.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.FileExtension))
            throw new ArgumentException("FileExtension must be provided.", nameof(dto));
        if (dto.Stream is null)
            throw new ArgumentException("File content must be provided as a stream.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));
    }

    private static void ValidateFetchDto(SaveFileFromFetchDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FileName))
            throw new ArgumentException("FileName must be provided.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.FileExtension))
            throw new ArgumentException("FileExtension must be provided.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.TempFilePath))
            throw new ArgumentException("TempFilePath must be provided.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));
    }

    private static async Task<(byte[] FileHash, long FileLength)> CopyStreamToTempFileAndHashAsync(
        Stream source,
        string tempFilePath,
        CancellationToken cancellationToken)
    {
        long fileLength = 0;

        using var hashAlgorithm = SHA256.Create();
        await using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var cryptoStream = new CryptoStream(tempFileStream, hashAlgorithm, CryptoStreamMode.Write))
        {
            byte[] buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await cryptoStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                fileLength += bytesRead;
            }

            await cryptoStream.FlushAsync(cancellationToken);
        }

        return (hashAlgorithm.Hash ?? throw new InvalidOperationException("Hash computation failed."), fileLength);
    }

    private static async Task<byte[]> ComputeFileHashAsync(string tempFilePath, CancellationToken cancellationToken)
    {
        using var hashAlgorithm = SHA256.Create();

        await using (var tempFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var cryptoStream = new CryptoStream(tempFileStream, hashAlgorithm, CryptoStreamMode.Read))
        {
            byte[] buffer = new byte[81920];
            while (await cryptoStream.ReadAsync(buffer, cancellationToken) > 0) { }
        }

        return hashAlgorithm.Hash ?? throw new InvalidOperationException("Hash computation failed.");
    }

    private static JsonDocument ExtractMetadataFromTempFile(string tempFilePath, string fileExtension, long fileLength)
    {
        using var tempFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return MetadataExtractor.ExtractMetadata(tempFileStream, fileExtension, fileLength);
    }

    private async Task EnsureSessionUserCanCreateInAccessGroupAsync(
        Guid sessionUserId,
        Guid accessGroupId,
        CancellationToken cancellationToken)
    {
        if (accessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(accessGroupId));

        var sessionUser = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == sessionUserId)
            .Select(user => new { user.IsBanned, user.IsWhitelisted })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"User with id {sessionUserId} not found.");

        if (sessionUser.IsBanned)
            throw new UnauthorizedAccessException("Banned users cannot create files.");

        if (!sessionUser.IsWhitelisted)
            throw new UnauthorizedAccessException("Users must be whitelisted before creating files.");

        bool canCreate = await _context.AccessGroups
            .AsNoTracking()
            .AnyAsync(
                accessGroup => accessGroup.Id == accessGroupId &&
                    (accessGroup.OwnerId == sessionUserId || accessGroup.Users.Any(user => user.Id == sessionUserId)),
                cancellationToken);

        if (!canCreate)
            throw new UnauthorizedAccessException("User cannot create a file in the requested access group.");
    }

    private Guid GetRequiredSessionUserId()
    {
        return SessionGuard.RequireActive(
            _requestSessionAccessor.GetSession(),
            "User session not found.",
            "Banned users cannot create files.").UserId;
    }

    private void RollbackFileRegistration(SavedFileMetaRecord? metaRecord, bool contentFileCreated)
    {
        if (metaRecord is null)
            return;

        try
        {
            _fileManager.RollbackSavedFileRegistration(metaRecord, contentFileCreated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to roll back file registration {FileRefId}", metaRecord.RefId);
        }
    }

    private void DetachPendingFileEntities(SavedFileReferenceDB fileReference, SavedFileDataDB? fileData)
    {
        DetachIfTracked(fileReference);
        DetachIfTracked(fileData);
    }

    private void DetachIfTracked(object? entity)
    {
        if (entity is null)
            return;

        var entry = _context.Entry(entity);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }
}
