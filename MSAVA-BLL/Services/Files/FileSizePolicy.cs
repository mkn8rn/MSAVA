namespace MSAVA_BLL.Services.Files;

public static class FileSizePolicy
{
    public const long MaximumFileSizeBytes = 100L * 1024L * 1024L;

    internal static long RequireValidMaximum(long maximumFileSizeBytes)
    {
        if (maximumFileSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFileSizeBytes), "Maximum file size must be greater than zero.");

        return maximumFileSizeBytes;
    }

    internal static void EnsureWithinMaximum(long fileSizeBytes, long maximumFileSizeBytes)
    {
        if (fileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes), "File size cannot be negative.");

        maximumFileSizeBytes = RequireValidMaximum(maximumFileSizeBytes);

        if (fileSizeBytes > maximumFileSizeBytes)
            throw CreateFileTooLargeException(fileSizeBytes, maximumFileSizeBytes);
    }

    internal static void EnsureChunkWithinMaximum(
        long currentFileSizeBytes,
        int nextChunkBytes,
        long maximumFileSizeBytes)
    {
        if (currentFileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(currentFileSizeBytes), "File size cannot be negative.");
        if (nextChunkBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(nextChunkBytes), "Read size cannot be negative.");

        maximumFileSizeBytes = RequireValidMaximum(maximumFileSizeBytes);
        long remainingBytes = maximumFileSizeBytes - currentFileSizeBytes;

        if (nextChunkBytes > remainingBytes)
            throw CreateFileTooLargeException(currentFileSizeBytes + nextChunkBytes, maximumFileSizeBytes);
    }

    internal static FileTooLargeException CreateFileTooLargeException(
        long fileSizeBytes,
        long maximumFileSizeBytes)
    {
        return new FileTooLargeException(fileSizeBytes, maximumFileSizeBytes);
    }
}
