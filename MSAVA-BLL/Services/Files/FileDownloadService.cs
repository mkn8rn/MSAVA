using MSAVA_Shared.Models;
using MSAVA_BLL.Utils;
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

    public StreamReturnFileDTO GetFileStreamById(Guid id)
    {
        var db = _context.FileRefs.AsNoTracking().SingleOrDefault(r => r.Id == id)
            ?? throw new KeyNotFoundException($"File with id {id} not found.");

        SessionDTO session = CanSessionUserAccessFile(db);

        FileStream fileStream = _fileManager.GetFileStream(db.FileHash, db.FileExtension.ToString());

        string fileName = MappingUtils.GetFileName(db);
        string extension = FileExtensionUtils.GetFileExtension(db);
        string fileNameWithExtension = $"{fileName}.{extension}";

        _serviceLogger.WriteLog(AccessLogActions.AccessViaFileStream, $"User accessed file stream for fileRefId: {db.Id}", session.UserId, fileNameWithExtension, db.Id);

        return MappingUtils.MapReturnFileDTO(db, fileStream: fileStream);
    }

    public PhysicalReturnFileDTO GetPhysicalFileReturnDataById(Guid id)
    {
        var db = _context.FileRefs.AsNoTracking().SingleOrDefault(r => r.Id == id)
            ?? throw new KeyNotFoundException($"File with id {id} not found.");

        SessionDTO session = CanSessionUserAccessFile(db);

        string fileName = MappingUtils.GetFileName(db);
        string extension = FileExtensionUtils.GetFileExtension(db);
        string contentType = MetadataUtils.GetContentType(extension);
        string fullPath = FileContentUtils.GetFullPathIfSafe(fileName, extension);
        string fileNameWithExtension = $"{fileName}.{extension}";

        _serviceLogger.WriteLog(AccessLogActions.AccessViaPhysicalFile, $"User accessed physical file for fileRefId: {db.Id}", session.UserId, fileNameWithExtension, db.Id);

        return new PhysicalReturnFileDTO
        {
            FilePath = fullPath,
            FileName = fileNameWithExtension,
            ContentType = contentType
        };
    }

    public PhysicalReturnFileDTO GetPhysicalFileReturnDataByPath(string fileNameWithExtension)
    {
        FilePathAccess access = CanSessionUserAccessFile(fileNameWithExtension);

        string fileName = Path.GetFileName(fileNameWithExtension);
        string extension = Path.GetExtension(fileName).TrimStart('.');
        string contentType = MetadataUtils.GetContentType(extension);
        string fullPath = FileContentUtils.GetFullPathIfSafe(fileNameWithExtension);

        _serviceLogger.WriteLog(AccessLogActions.AccessViaPhysicalFile, $"User accessed physical file by path: {fileNameWithExtension}", access.Session.UserId, fileNameWithExtension, access.RefId);

        return new PhysicalReturnFileDTO
        {
            FilePath = fullPath,
            FileName = fileName,
            ContentType = contentType
        };
    }

    public StreamReturnFileDTO GetFileStreamByPath(string fileNameWithExtension)
    {
        FilePathAccess access = CanSessionUserAccessFile(fileNameWithExtension);

        string fileName = Path.GetFileNameWithoutExtension(fileNameWithExtension);
        string extension = Path.GetExtension(fileNameWithExtension).TrimStart('.');
        FileStream fileStream = _fileManager.GetFileStream(fileNameWithExtension);

        _serviceLogger.WriteLog(AccessLogActions.AccessViaFileStream, $"User accessed file stream by path: {fileNameWithExtension}", access.Session.UserId, fileNameWithExtension, access.RefId);

        return new StreamReturnFileDTO
        {
            FileName = fileName,
            FileExtension = extension,
            FileStream = fileStream
        };
    }

    private FilePathAccess CanSessionUserAccessFile(string fileNameWithExtension)
    {
        SessionDTO claims = GetActiveSession();
        try
        {
            Guid refId = _fileManager.CheckFileAccessByPath(fileNameWithExtension, claims.AccessGroups);
            return new FilePathAccess(refId, claims);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("User does not have permission to access this file.");
        }
    }

    private SessionDTO CanSessionUserAccessFile(SavedFileReferenceDB fileReference)
    {
        SessionDTO claims = GetActiveSession();

        if (claims.IsAdmin)
            return claims;

        if (fileReference.PublicDownload)
            return claims;

        List<Guid> userAccessGroups = claims.AccessGroups ?? [];
        bool canAccess = userAccessGroups.Contains(fileReference.AccessGroupId);

        if (!canAccess)
            throw new UnauthorizedAccessException("User does not have permission to access this file.");

        return claims;
    }

    private SessionDTO GetActiveSession()
    {
        return SessionGuard.RequireActive(
            _userService.GetSessionClaims(),
            "Session user is required to download files.",
            "Banned users cannot download files.");
    }

    private readonly record struct FilePathAccess(Guid RefId, SessionDTO Session);
}
