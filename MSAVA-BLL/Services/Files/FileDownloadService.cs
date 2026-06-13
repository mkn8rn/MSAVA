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

        CanSessionUserAccessFile(db);

        FileStream fileStream = _fileManager.GetFileStream(db.FileHash, db.FileExtension.ToString());
        var claims = _userService.GetSessionClaims();

        string fileName = MappingUtils.GetFileName(db);
        string extension = FileExtensionUtils.GetFileExtension(db);
        string fileNameWithExtension = $"{fileName}.{extension}";

        _serviceLogger.WriteLog(AccessLogActions.AccessViaFileStream, $"User accessed file stream for fileRefId: {db.Id}", claims.UserId, fileNameWithExtension, db.Id);

        return MappingUtils.MapReturnFileDTO(db, fileStream: fileStream);
    }

    public PhysicalReturnFileDTO GetPhysicalFileReturnDataById(Guid id)
    {
        var db = _context.FileRefs.AsNoTracking().SingleOrDefault(r => r.Id == id)
            ?? throw new KeyNotFoundException($"File with id {id} not found.");

        CanSessionUserAccessFile(db);

        string fileName = MappingUtils.GetFileName(db);
        string extension = FileExtensionUtils.GetFileExtension(db);
        string contentType = MetadataUtils.GetContentType(extension);
        string fullPath = FileContentUtils.GetFullPathIfSafe(fileName, extension);
        string fileNameWithExtension = $"{fileName}.{extension}";

        var claims = _userService.GetSessionClaims();
        _serviceLogger.WriteLog(AccessLogActions.AccessViaPhysicalFile, $"User accessed physical file for fileRefId: {db.Id}", claims.UserId, fileNameWithExtension, db.Id);

        return new PhysicalReturnFileDTO
        {
            FilePath = fullPath,
            FileName = fileNameWithExtension,
            ContentType = contentType
        };
    }

    public PhysicalReturnFileDTO GetPhysicalFileReturnDataByPath(string fileNameWithExtension)
    {
        Guid refId = CanSessionUserAccessFile(fileNameWithExtension);

        string fileName = Path.GetFileName(fileNameWithExtension);
        string extension = Path.GetExtension(fileName).TrimStart('.');
        string contentType = MetadataUtils.GetContentType(extension);
        string fullPath = FileContentUtils.GetFullPathIfSafe(fileNameWithExtension);

        var claims = _userService.GetSessionClaims();
        _serviceLogger.WriteLog(AccessLogActions.AccessViaPhysicalFile, $"User accessed physical file by path: {fileNameWithExtension}", claims.UserId, fileNameWithExtension, refId);

        return new PhysicalReturnFileDTO
        {
            FilePath = fullPath,
            FileName = fileName,
            ContentType = contentType
        };
    }

    public StreamReturnFileDTO GetFileStreamByPath(string fileNameWithExtension)
    {
        Guid refId = CanSessionUserAccessFile(fileNameWithExtension);

        string fileName = Path.GetFileNameWithoutExtension(fileNameWithExtension);
        string extension = Path.GetExtension(fileNameWithExtension).TrimStart('.');
        FileStream fileStream = _fileManager.GetFileStream(fileNameWithExtension);

        var claims = _userService.GetSessionClaims();
        _serviceLogger.WriteLog(AccessLogActions.AccessViaFileStream, $"User accessed file stream by path: {fileNameWithExtension}", claims.UserId, fileNameWithExtension, refId);

        return new StreamReturnFileDTO
        {
            FileName = fileName,
            FileExtension = extension,
            FileStream = fileStream
        };
    }

    private Guid CanSessionUserAccessFile(string fileNameWithExtension)
    {
        SessionDTO claims = GetActiveSession();
        try
        {
            return _fileManager.CheckFileAccessByPath(fileNameWithExtension, claims.AccessGroups);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("User does not have permission to access this file.");
        }
    }

    private bool CanSessionUserAccessFile(SavedFileReferenceDB fileReference)
    {
        SessionDTO claims = GetActiveSession();

        if (claims.IsAdmin)
            return true;

        if (fileReference.PublicDownload)
            return true;

        List<Guid> userAccessGroups = claims.AccessGroups ?? [];
        bool canAccess = userAccessGroups.Contains(fileReference.AccessGroupId);

        if (!canAccess)
            throw new UnauthorizedAccessException("User does not have permission to access this file.");

        return canAccess;
    }

    private SessionDTO GetActiveSession()
    {
        SessionDTO session = _userService.GetSessionClaims();
        if (!session.LoggedIn || session.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("Session user is required to download files.");

        if (session.IsBanned)
            throw new UnauthorizedAccessException("Banned users cannot download files.");

        return session;
    }
}
