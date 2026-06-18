namespace MSAVA_BLL.Services.Import;

internal interface IProviderImportTempFileFactory
{
    string CreateEmptyTempFilePath();

    string CreateRandomTempFilePath();
}

internal sealed class FileSystemProviderImportTempFileFactory : IProviderImportTempFileFactory
{
    internal static readonly FileSystemProviderImportTempFileFactory Instance = new();

    private FileSystemProviderImportTempFileFactory()
    {
    }

    public string CreateEmptyTempFilePath() => Path.GetTempFileName();

    public string CreateRandomTempFilePath() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
}
