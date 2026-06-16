namespace MSAVA_Shared.Diagnostics;

public static class CriticalExceptionPolicy
{
    public static bool ContainsCriticalException(Exception exception)
    {
        return ContainsMatchingException(
            exception,
            current => current is OperationCanceledException || IsNonCancellationCriticalException(current));
    }

    public static bool ContainsNonCancellationCriticalException(Exception exception)
    {
        return ContainsMatchingException(exception, IsNonCancellationCriticalException);
    }

    private static bool ContainsMatchingException(Exception exception, Func<Exception, bool> isMatch)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(isMatch);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (isMatch(current))
                return true;
        }

        return false;
    }

    private static bool IsNonCancellationCriticalException(Exception exception)
    {
        return exception is OutOfMemoryException
            or AccessViolationException;
    }
}
