namespace MSAVA_Shared.Models;

public sealed class FileTooLargeException : Exception
{
    public FileTooLargeException(long fileSizeBytes, long maximumFileSizeBytes)
        : base($"File size {fileSizeBytes} bytes exceeds the maximum allowed size of {maximumFileSizeBytes} bytes.")
    {
        FileSizeBytes = fileSizeBytes;
        MaximumFileSizeBytes = maximumFileSizeBytes;
    }

    public long FileSizeBytes { get; }

    public long MaximumFileSizeBytes { get; }
}
