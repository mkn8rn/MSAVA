namespace MSAVA_INF.Utils;

public static class FileStreamUtils
{
    public static FileStreamOptions GetDefaultFileStreamOptions()
    {
        return new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 81920,
            Options = FileOptions.Asynchronous
        };
    }
}
