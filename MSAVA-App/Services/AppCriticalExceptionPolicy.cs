namespace MSAVA_App.Services;

internal static class AppCriticalExceptionPolicy
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

    public static bool ContainsNonCancellationCriticalException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OutOfMemoryException
                or AccessViolationException)
                return true;
        }

        return false;
    }
}
