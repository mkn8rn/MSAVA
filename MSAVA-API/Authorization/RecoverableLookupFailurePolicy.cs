using System.Data.Common;

namespace MSAVA_API.Authorization;

internal static class RecoverableLookupFailurePolicy
{
    public static bool IsRecoverable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (ContainsCriticalException(exception))
            return false;

        return exception is DbException
            or IOException
            or InvalidOperationException
            or UnauthorizedAccessException;
    }

    private static bool ContainsCriticalException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException
                or OutOfMemoryException
                or AccessViolationException)
                return true;
        }

        return false;
    }
}
