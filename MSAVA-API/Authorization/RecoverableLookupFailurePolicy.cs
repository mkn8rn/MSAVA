using System.Data.Common;
using MSAVA_BLL.Utils;

namespace MSAVA_API.Authorization;

internal static class RecoverableLookupFailurePolicy
{
    public static bool IsRecoverable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (CriticalExceptionPolicy.ContainsCriticalException(exception))
            return false;

        return exception is DbException
            or IOException
            or InvalidOperationException
            or UnauthorizedAccessException;
    }
}
