namespace MSAVA_Shared.Models;

public static class FileQueryPagePolicy
{
    public const int DefaultPageSize = 100;
    public const int MaximumPageSize = 100;
    public const int MaximumSkip = 100_000;

    public static FileQueryPage Normalize(int skip, int take)
    {
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip), $"File query skip must be between 0 and {MaximumSkip}.");

        if (skip > MaximumSkip)
            throw new ArgumentOutOfRangeException(nameof(skip), $"File query skip must be between 0 and {MaximumSkip}.");

        if (take <= 0)
            throw new ArgumentOutOfRangeException(nameof(take), $"File query take must be between 1 and {MaximumPageSize}.");

        if (take > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(take), $"File query take must be between 1 and {MaximumPageSize}.");

        return new FileQueryPage(skip, take);
    }
}

public readonly record struct FileQueryPage(int Skip, int Take);
