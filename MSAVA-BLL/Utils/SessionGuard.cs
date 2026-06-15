using System.Diagnostics.CodeAnalysis;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Utils;

public static class SessionGuard
{
    public static SessionDTO RequireActive(
        SessionDTO? session,
        string missingSessionMessage,
        string bannedSessionMessage)
    {
        if (!TryRequireActive(session, out var activeSession, out var error, missingSessionMessage, bannedSessionMessage))
            throw new UnauthorizedAccessException(error);

        return activeSession;
    }

    public static SessionDTO RequireActiveWhitelisted(
        SessionDTO? session,
        string missingSessionMessage,
        string bannedSessionMessage,
        string nonWhitelistedSessionMessage)
    {
        SessionDTO activeSession = RequireActive(session, missingSessionMessage, bannedSessionMessage);

        if (!activeSession.IsWhitelisted)
            throw new UnauthorizedAccessException(nonWhitelistedSessionMessage);

        return activeSession;
    }

    public static bool TryRequireActive(
        SessionDTO? session,
        [NotNullWhen(true)] out SessionDTO? activeSession,
        out string error,
        string missingSessionMessage,
        string bannedSessionMessage)
    {
        activeSession = null;
        error = string.Empty;

        if (session is null || !session.LoggedIn || session.UserId == Guid.Empty)
        {
            error = missingSessionMessage;
            return false;
        }

        if (session.IsBanned)
        {
            error = bannedSessionMessage;
            return false;
        }

        activeSession = session;
        return true;
    }
}
