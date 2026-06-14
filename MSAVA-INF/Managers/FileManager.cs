using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_INF.Contexts;

namespace MSAVA_INF.Managers;

public class FileManager
{
    private readonly MetadataStore _metadataStore;

    public FileManager(MetadataStore metadataStore)
    {
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
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
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { /* ignore */ }
            }
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

    public Guid CheckFileAccessByPath(string fileNameWithExtension, List<Guid>? userAccessGroups)
    {
        EnsureSafeFileName(fileNameWithExtension);

        var (hashHex, extension) = ParseFileName(fileNameWithExtension);
        return _metadataStore.CheckAccessOrThrow(hashHex, extension, userAccessGroups);
    }

    private static void EnsureSafeFileName(string fileNameWithExtension)
    {
        if (!FileContentUtils.IsSafeFileName(fileNameWithExtension))
            throw new UnauthorizedAccessException($"Unsafe file name: {fileNameWithExtension}");

        string fullPath = FileContentUtils.GetFullPath(fileNameWithExtension);
        if (!FileContentUtils.IsSafeFilePath(fullPath))
            throw new UnauthorizedAccessException($"Unsafe file path: {fullPath}");
    }

    private static (string HashHex, string Extension) ParseFileName(string fileNameWithExtension)
    {
        var lastDot = fileNameWithExtension.LastIndexOf('.');
        if (lastDot <= 0)
            throw new ArgumentException("Invalid file name format.", nameof(fileNameWithExtension));

        var hashHex = fileNameWithExtension[..lastDot].ToUpperInvariant();
        var extension = fileNameWithExtension[(lastDot + 1)..].ToLowerInvariant();

        return (hashHex, extension);
    }
}
