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

        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            Exception current = pending.Pop();
            if (!visited.Add(current))
                continue;

            if (isMatch(current))
                return true;

            if (current is AggregateException aggregateException)
            {
                foreach (Exception innerException in aggregateException.InnerExceptions)
                {
                    pending.Push(innerException);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }

        return false;
    }

    private static bool IsNonCancellationCriticalException(Exception exception)
    {
        return exception is OutOfMemoryException
            or AccessViolationException;
    }
}
