namespace MSAVA_BLL.Utils;

internal static class CriticalExceptionPolicy
{
    public static bool ContainsCriticalException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

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
