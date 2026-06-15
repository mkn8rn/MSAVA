using MSAVA_Shared.Models;
using MSAVA_BLL.Utils;
using MSAVA_BLL.Utils.Metadata;
using MSAVA_INF.Models;
using MSAVA_INF.Managers;
using MSAVA_INF.Utils;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;
using Microsoft.EntityFrameworkCore;

namespace MSAVA_BLL.Services.Files;

public class FileDownloadService : IFileDownloadService
{
    private readonly BaseDataContext _context;
    private readonly FileManager _fileManager;
    private readonly IUserSessionService _userService;
    private readonly ServiceLogger _serviceLogger;

    public FileDownloadService(
        BaseDataContext context,
        IUserSessionService userService,
        FileManager fileManager,
        ServiceLogger serviceLogger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
    }

    public async Task<StreamReturnFileDTO> GetFileStreamByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var db = await _context.FileRefs
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"File with id {id} not found.");

        SessionDTO session = await CanSessionUserAccessFileAsync(db, cancellationToken);

        FileStream? fileStream = _fileManager.GetFileStream(db.FileHash, db.FileExtension.ToString());

        string fileName = MappingUtils.GetFileName(db);
        string extension = FileExtensionUtils.GetFileExtension(db);
        string fileNameWithExtension = $"{fileName}.{extension}";

        try
        {
            await IncrementDownloadCountAsync(db.Id, cancellationToken);
            await _serviceLogger.WriteLogAsync(AccessLogActions.AccessViaFileStream, $"User accessed file stream for fileRefId: {db.Id}", session.UserId, fileNameWithExtension, db.Id);

            var result = MappingUtils.MapReturnFileDTO(db, fileStream: fileStream);
            fileStream = null;
            return result;
        }
        finally
        {
            if (fileStream is not null)
                await fileStream.DisposeAsync();
        }
    }

    public async Task<PhysicalReturnFileDTO> GetPhysicalFileReturnDataByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var db = await _context.FileRefs
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"File with id {id} not found.");

        SessionDTO session = await CanSessionUserAccessFileAsync(db, cancellationToken);

        string fileName = MappingUtils.GetFileName(db);
        string extension = FileExtensionUtils.GetFileExtension(db);
        string contentType = MetadataExtractor.GetContentType(extension);
        string fullPath = FileContentUtils.GetFullPathIfSafe(fileName, extension);
        string fileNameWithExtension = $"{fileName}.{extension}";

        await IncrementDownloadCountAsync(db.Id, cancellationToken);
        await _serviceLogger.WriteLogAsync(AccessLogActions.AccessViaPhysicalFile, $"User accessed physical file for fileRefId: {db.Id}", session.UserId, fileNameWithExtension, db.Id);

        return new PhysicalReturnFileDTO
        {
            FilePath = fullPath,
            FileName = fileNameWithExtension,
            ContentType = contentType
        };
    }

    public async Task<PhysicalReturnFileDTO> GetPhysicalFileReturnDataByPathAsync(
        string fileNameWithExtension,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FilePathAccess access = await CanSessionUserAccessFileAsync(fileNameWithExtension, cancellationToken);

        string fileName = Path.GetFileName(fileNameWithExtension);
        string extension = Path.GetExtension(fileName).TrimStart('.');
        string contentType = MetadataExtractor.GetContentType(extension);
        string fullPath = FileContentUtils.GetFullPathIfSafe(fileNameWithExtension);

        await IncrementDownloadCountAsync(access.RefId, cancellationToken);
        await _serviceLogger.WriteLogAsync(AccessLogActions.AccessViaPhysicalFile, $"User accessed physical file by path: {fileNameWithExtension}", access.Session.UserId, fileNameWithExtension, access.RefId);

        return new PhysicalReturnFileDTO
        {
            FilePath = fullPath,
            FileName = fileName,
            ContentType = contentType
        };
    }

    public async Task<StreamReturnFileDTO> GetFileStreamByPathAsync(
        string fileNameWithExtension,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FilePathAccess access = await CanSessionUserAccessFileAsync(fileNameWithExtension, cancellationToken);

        string fileName = Path.GetFileNameWithoutExtension(fileNameWithExtension);
        string extension = Path.GetExtension(fileNameWithExtension).TrimStart('.');
        FileStream? fileStream = _fileManager.GetFileStream(fileNameWithExtension);

        try
        {
            await IncrementDownloadCountAsync(access.RefId, cancellationToken);
            await _serviceLogger.WriteLogAsync(AccessLogActions.AccessViaFileStream, $"User accessed file stream by path: {fileNameWithExtension}", access.Session.UserId, fileNameWithExtension, access.RefId);

            var result = new StreamReturnFileDTO
            {
                FileName = fileName,
                FileExtension = extension,
                FileStream = fileStream
            };
            fileStream = null;
            return result;
        }
        finally
        {
            if (fileStream is not null)
                await fileStream.DisposeAsync();
        }
    }

    private async Task<FilePathAccess> CanSessionUserAccessFileAsync(string fileNameWithExtension, CancellationToken cancellationToken)
    {
        SessionDTO session = await GetActiveSessionAsync(cancellationToken);
        SavedFileReferenceDB fileReference = await GetFileReferenceByPathAsync(fileNameWithExtension, session, cancellationToken);
        return new FilePathAccess(fileReference.Id, session);
    }

    private async Task<SessionDTO> CanSessionUserAccessFileAsync(SavedFileReferenceDB fileReference, CancellationToken cancellationToken)
    {
        SessionDTO claims = await GetActiveSessionAsync(cancellationToken);

        if (!CanSessionAccessFile(fileReference, claims))
            throw new UnauthorizedAccessException("User does not have permission to access this file.");

        return claims;
    }

    private async Task<SavedFileReferenceDB> GetFileReferenceByPathAsync(
        string fileNameWithExtension,
        SessionDTO claims,
        CancellationToken cancellationToken)
    {
        if (!FileContentUtils.TryGetSafeFullPath(fileNameWithExtension, out _))
            throw new UnauthorizedAccessException("User does not have permission to access this file.");

        if (!StoredFileName.TryParse(fileNameWithExtension, out var storedFileName))
            throw new UnauthorizedAccessException("User does not have permission to access this file.");

        FileExtensionType extensionType = MappingUtils.ParseFileExtension(storedFileName.Extension);
        var references = _context.FileRefs
            .AsNoTracking()
            .Where(fileReference =>
                fileReference.FileHash == storedFileName.FileHash &&
                fileReference.FileExtension == extensionType);

        var accessibleReference = await references
            .Where(fileReference =>
                claims.IsAdmin ||
                fileReference.PublicDownload ||
                (claims.AccessGroups != null && claims.AccessGroups.Contains(fileReference.AccessGroupId)))
            .OrderByDescending(fileReference => fileReference.PublicDownload)
            .ThenBy(fileReference => fileReference.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (accessibleReference is not null)
            return accessibleReference;

        bool exists = await references.AnyAsync(cancellationToken);
        if (!exists)
            throw new FileNotFoundException($"No file reference found for file: {fileNameWithExtension}");

        throw new UnauthorizedAccessException("User does not have permission to access this file.");
    }

    private static bool CanSessionAccessFile(SavedFileReferenceDB fileReference, SessionDTO claims)
    {
        if (claims.IsAdmin)
            return true;

        if (fileReference.PublicDownload)
            return true;

        List<Guid> userAccessGroups = claims.AccessGroups ?? [];
        return userAccessGroups.Contains(fileReference.AccessGroupId);
    }

    private async Task IncrementDownloadCountAsync(Guid fileReferenceId, CancellationToken cancellationToken)
    {
        var fileData = await _context.FileData
            .SingleOrDefaultAsync(fileData => fileData.FileReferenceId == fileReferenceId, cancellationToken)
            ?? throw new KeyNotFoundException($"File data for reference id {fileReferenceId} not found.");

        if (fileData.DownloadCount == uint.MaxValue)
            throw new OverflowException($"Download count for file reference id {fileReferenceId} has reached the maximum value.");

        fileData.DownloadCount++;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<SessionDTO> GetActiveSessionAsync(CancellationToken cancellationToken)
    {
        SessionDTO session = SessionGuard.RequireActive(
            await _userService.GetSessionClaimsAsync(cancellationToken),
            "Session user is required to download files.",
            "Banned users cannot download files.");

        if (!session.IsWhitelisted)
            throw new UnauthorizedAccessException("Users must be whitelisted before downloading files.");

        return session;
    }

    private readonly record struct FilePathAccess(Guid RefId, SessionDTO Session);
}
