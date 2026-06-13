using MSAVA_BLL.Loggers;
using MSAVA_BLL.Utils;
using MSAVA_Shared.Models;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_INF.Managers;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using MSAVA_INF.Contexts;

namespace MSAVA_BLL.Services.Files;

public class FilePersistenceService
{
    private readonly BaseDataContext _context;
    private readonly FileManager _fileManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ServiceLogger _serviceLogger;

    public FilePersistenceService(
        BaseDataContext context,
        FileManager fileManager,
        IHttpContextAccessor httpContextAccessor,
        ServiceLogger serviceLogger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
    }

    public async Task<Guid> CreateFileFromStreamAsync(SaveFileFromStreamDTO dto, CancellationToken cancellationToken = default)
    {
        Guid sessionUserId = GetSessionUserId();

        string tempFilePath = Path.GetTempFileName();
        long fileLength = 0;
        byte[] fileHash;

        using (var hashAlgorithm = SHA256.Create())
        {
            await using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var cryptoStream = new CryptoStream(tempFileStream, hashAlgorithm, CryptoStreamMode.Write))
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await dto.Stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await cryptoStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    fileLength += bytesRead;
                }
                await cryptoStream.FlushAsync(cancellationToken);
            }
            fileHash = hashAlgorithm.Hash ?? throw new InvalidOperationException("Hash computation failed.");
        }

        SavedFileReferenceDB savedFileDb = MappingUtils.MapSavedFileReferenceDB(dto, fileHash, (ulong)fileLength);
        SavedFileMetaRecord metaRecord = MappingUtils.MapSavedFileMetaRecord(savedFileDb);

        await _fileManager.SaveTempFileAsync(metaRecord, tempFilePath, cancellationToken);

        SavedFileDataDB savedFileDataDb = MappingUtils.MapSavedFileDataDB(
            dto,
            savedFileDb,
            (ulong)fileLength,
            sessionUserId,
            sessionUserId
        );

        _context.FileRefs.Add(savedFileDb);
        _context.FileData.Add(savedFileDataDb);
        await _context.SaveChangesAsync(cancellationToken);

        string fileName = MappingUtils.GetFileName(savedFileDb);
        string fileExtension = FileExtensionUtils.GetFileExtension(savedFileDb);
        string fileNameWithExtension = $"{fileName}.{fileExtension}";

        _serviceLogger.WriteLog(AccessLogActions.NewFileCreated, $"File created: {fileNameWithExtension}", sessionUserId, fileNameWithExtension, savedFileDb.Id);

        return savedFileDb.Id;
    }

    public async Task<Guid> CreateFileFromTempFileAsync(SaveFileFromFetchDTO dto, CancellationToken cancellationToken = default)
    {
        Guid sessionUserId = GetSessionUserId();
        string tempFilePath = dto.TempFilePath;

        try
        {
            long fileLength = new FileInfo(tempFilePath).Length;
            byte[] fileHash;

            using (var hashAlgorithm = SHA256.Create())
            {
                await using (var tempFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var cryptoStream = new CryptoStream(tempFileStream, hashAlgorithm, CryptoStreamMode.Read))
                {
                    byte[] buffer = new byte[81920];
                    while (await cryptoStream.ReadAsync(buffer, cancellationToken) > 0) { }
                }
                fileHash = hashAlgorithm.Hash ?? throw new InvalidOperationException("Hash computation failed.");
            }

            SavedFileReferenceDB savedFileDb = MappingUtils.MapSavedFileReferenceDB(dto, fileHash, (ulong)fileLength);
            SavedFileMetaRecord metaRecord = MappingUtils.MapSavedFileMetaRecord(savedFileDb);

            await _fileManager.SaveTempFileAsync(metaRecord, tempFilePath, cancellationToken);

            SavedFileDataDB savedFileDataDb = MappingUtils.MapSavedFileDataDB(
                dto,
                savedFileDb,
                (ulong)fileLength,
                sessionUserId,
                sessionUserId
            );

            _context.FileRefs.Add(savedFileDb);
            _context.FileData.Add(savedFileDataDb);
            await _context.SaveChangesAsync(cancellationToken);

            string fileName = MappingUtils.GetFileName(savedFileDb);
            string fileExtension = FileExtensionUtils.GetFileExtension(savedFileDb);
            string fileNameWithExtension = $"{fileName}.{fileExtension}";

            _serviceLogger.WriteLog(AccessLogActions.NewFileCreated, $"File created: {fileNameWithExtension}", sessionUserId, fileNameWithExtension, savedFileDb.Id);

            return savedFileDb.Id;
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { /* ignore */ }
            }
        }
    }

    private Guid GetSessionUserId()
    {
        if (_httpContextAccessor.HttpContext?.Items["SessionDTO"] is SessionDTO sessionDto)
            return sessionDto.UserId;
        return Guid.Empty;
    }
}
