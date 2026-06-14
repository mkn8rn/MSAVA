using Microsoft.Extensions.Logging;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_INF.Contexts;

namespace MSAVA_INF.Managers;

public class FileManager
{
    private readonly MetadataStore _metadataStore;
    private readonly ILogger<FileManager> _logger;

    public FileManager(MetadataStore metadataStore, ILogger<FileManager> logger)
    {
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> SaveTempFileAsync(
        SavedFileMetaRecord fileMeta,
        string tempFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempFilePath);

        if (!File.Exists(tempFilePath))
            throw new FileNotFoundException("Temporary file not found.", tempFilePath);

        string path = FileContentUtils.GetFullPath(fileMeta.FileHash, fileMeta.FileExtension);
        bool fileExists = File.Exists(path);
        bool contentFileCreated = false;

        try
        {
            await using (var tempFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!FileContentUtils.ValidateFileContent(tempFileStream, fileMeta.FileExtension))
                    throw new ArgumentException("File content does not match the provided extension.");
            }

            if (!fileExists)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                File.Move(tempFilePath, path);
                contentFileCreated = true;
            }

            try
            {
                _metadataStore.AddMetadata(fileMeta);
            }
            catch
            {
                RollbackSavedFileRegistration(fileMeta, contentFileCreated);
                throw;
            }

            return contentFileCreated;
        }
        finally
        {
            DeleteTempFileIfPresent(tempFilePath);
        }
    }

    public void RollbackSavedFileRegistration(SavedFileMetaRecord fileMeta, bool deleteContentIfUnreferenced)
    {
        ArgumentNullException.ThrowIfNull(fileMeta);

        _metadataStore.Delete(fileMeta.RefId);

        if (!deleteContentIfUnreferenced)
            return;

        if (_metadataStore.Exists(fileMeta.FileHash, fileMeta.FileExtension))
            return;

        string path = FileContentUtils.GetFullPath(fileMeta.FileHash, fileMeta.FileExtension);
        if (File.Exists(path))
            File.Delete(path);
    }

    public FileStream GetFileStream(string fileNameWithExtension)
    {
        string fullPath = FileContentUtils.GetFullPathIfSafe(fileNameWithExtension);
        var options = FileStreamUtils.GetDefaultFileStreamOptions();
        return GetFileStream(fullPath, options);
    }

    public FileStream GetFileStream(byte[] fileHash, string fileExtension)
    {
        string fullPath = FileContentUtils.GetFullPath(fileHash, fileExtension);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {fullPath}");

        var options = FileStreamUtils.GetDefaultFileStreamOptions();
        return GetFileStream(fullPath, options);
    }

    public FileStream GetFileStream(string fullPath, FileStreamOptions options)
    {
        return new FileStream(fullPath, options);
    }

    public Guid CheckFileAccessByPath(string fileNameWithExtension, List<Guid>? userAccessGroups, bool isAdmin = false)
    {
        EnsureSafeFileName(fileNameWithExtension);

        var storedFileName = ParseStoredFileName(fileNameWithExtension);
        return _metadataStore.CheckAccessOrThrow(
            storedFileName.FileHashHex,
            storedFileName.Extension,
            userAccessGroups,
            isAdmin);
    }

    private static void EnsureSafeFileName(string fileNameWithExtension)
    {
        if (!FileContentUtils.IsSafeFileName(fileNameWithExtension))
            throw new UnauthorizedAccessException($"Unsafe file name: {fileNameWithExtension}");

        string fullPath = FileContentUtils.GetFullPath(fileNameWithExtension);
        if (!FileContentUtils.IsSafeFilePath(fullPath))
            throw new UnauthorizedAccessException($"Unsafe file path: {fullPath}");
    }

    private static StoredFileName ParseStoredFileName(string fileNameWithExtension)
    {
        if (!StoredFileName.TryParse(fileNameWithExtension, out var storedFileName))
            throw new UnauthorizedAccessException("Invalid file name format.");

        return storedFileName;
    }

    private void DeleteTempFileIfPresent(string tempFilePath)
    {
        if (!File.Exists(tempFilePath))
            return;

        try
        {
            File.Delete(tempFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary file {TempFilePath}", tempFilePath);
        }
    }
}
