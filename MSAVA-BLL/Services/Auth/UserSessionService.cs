using MSAVA_Shared.Models;
using MSAVA_BLL.Utils;
using MSAVA_INF.Models;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using Microsoft.EntityFrameworkCore;

namespace MSAVA_BLL.Services.Auth;

public class UserSessionService : IUserSessionService
{
    private readonly BaseDataContext _context;
    private readonly IRequestSessionAccessor _requestSessionAccessor;

    public UserSessionService(BaseDataContext context, IRequestSessionAccessor requestSessionAccessor)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _requestSessionAccessor = requestSessionAccessor ?? throw new ArgumentNullException(nameof(requestSessionAccessor));
    }

    public async Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ActiveSessionUser currentUser = await RequireCanReadUserAsync(id, cancellationToken);

        if (currentUser.Session.UserId == id)
            return MappingUtils.MapUserDTOWithRelationships(currentUser.User);

        var userDb = await GetUserDbAsync(id, cancellationToken);

        return MappingUtils.MapUserDTOWithRelationships(userDb);
    }

    public async Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default)
    {
        return (await RequireCurrentUserAccessSessionAsync(
            "Session user is required to access the current user.",
            "Banned users cannot access the current user.",
            "Users must be whitelisted before accessing the current user.",
            cancellationToken)).UserId;
    }

    public async Task<SessionDTO> GetSessionClaimsAsync(CancellationToken cancellationToken = default)
    {
        return (await GetCurrentSessionAsync(cancellationToken)).Session;
    }

    private async Task<CurrentSession> GetCurrentSessionAsync(CancellationToken cancellationToken)
    {
        SessionDTO tokenSession = GetTokenSessionDto();
        if (!tokenSession.LoggedIn || tokenSession.UserId == Guid.Empty)
            return new CurrentSession(tokenSession, null);

        UserDB userDb = await GetUserDbAsync(tokenSession.UserId, cancellationToken);
        return new CurrentSession(BuildSessionDto(tokenSession, userDb), userDb);
    }

    private static SessionDTO BuildSessionDto(SessionDTO tokenSession, UserDB userDb)
    {
        var roles = new List<string>(3);

        if (userDb.IsAdmin)
            roles.Add(SessionRoles.Admin);
        if (userDb.IsBanned)
            roles.Add(SessionRoles.Banned);
        if (userDb.IsWhitelisted)
            roles.Add(SessionRoles.Whitelisted);

        return new SessionDTO
        {
            LoggedIn = true,
            UserId = userDb.Id,
            Username = userDb.Username,
            IsAdmin = userDb.IsAdmin,
            IsBanned = userDb.IsBanned,
            IsWhitelisted = userDb.IsWhitelisted,
            Roles = roles,
            Claims = tokenSession.Claims,
            AccessGroups = userDb.AccessGroups.Select(group => group.Id).ToList(),
            IssuedAt = tokenSession.IssuedAt,
            ExpiresAt = tokenSession.ExpiresAt
        };
    }

    public async Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default)
    {
        return (await RequireCurrentUserAccessSessionAsync(
            "Session user is required to check admin status.",
            "Banned users cannot check admin status.",
            "Users must be whitelisted before checking admin status.",
            cancellationToken)).IsAdmin;
    }

    public async Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) =>
        MappingUtils.MapUserDTOWithRelationships(await GetSessionUserDBAsync(cancellationToken));

    public async Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default)
    {
        return (await RequireCurrentUserAccessSessionUserAsync(
            "Session user is required to access the current user.",
            "Banned users cannot access the current user.",
            "Users must be whitelisted before accessing the current user.",
            cancellationToken)).User;
    }

    private async Task<UserDB> GetUserDbAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userDb = await _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with id {userId} not found.");

        return userDb;
    }

    public async Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        await RequireActiveAdminSessionAsync(cancellationToken);

        var userDbs = await _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .ToListAsync(cancellationToken);

        return userDbs.Select(MappingUtils.MapUserDTOWithRelationships).ToList();
    }

    private async Task RequireActiveAdminSessionAsync(CancellationToken cancellationToken)
    {
        SessionDTO session = await RequireCurrentUserAccessSessionAsync(
            "Session user is required to list users.",
            "Banned users cannot list users.",
            "Users must be whitelisted before listing users.",
            cancellationToken);

        if (!session.IsAdmin)
            throw new UnauthorizedAccessException("Only admins can list users.");
    }

    private async Task<ActiveSessionUser> RequireCanReadUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id must be provided.", nameof(userId));

        ActiveSessionUser currentUser = await RequireCurrentUserAccessSessionUserAsync(
            "Session user is required to access users.",
            "Banned users cannot access users.",
            "Users must be whitelisted before accessing users.",
            cancellationToken);

        if (!currentUser.Session.IsAdmin && currentUser.Session.UserId != userId)
            throw new UnauthorizedAccessException("Only admins can access other users.");

        return currentUser;
    }

    private async Task<SessionDTO> RequireCurrentUserAccessSessionAsync(
        string missingSessionMessage,
        string bannedSessionMessage,
        string nonWhitelistedSessionMessage,
        CancellationToken cancellationToken)
    {
        SessionDTO session = SessionGuard.RequireActive(
            (await GetCurrentSessionAsync(cancellationToken)).Session,
            missingSessionMessage,
            bannedSessionMessage);

        if (!session.IsWhitelisted)
            throw new UnauthorizedAccessException(nonWhitelistedSessionMessage);

        return session;
    }

    private async Task<ActiveSessionUser> RequireCurrentUserAccessSessionUserAsync(
        string missingSessionMessage,
        string bannedSessionMessage,
        string nonWhitelistedSessionMessage,
        CancellationToken cancellationToken)
    {
        CurrentSession currentSession = await GetCurrentSessionAsync(cancellationToken);
        SessionDTO activeSession = SessionGuard.RequireActive(
            currentSession.Session,
            missingSessionMessage,
            bannedSessionMessage);

        if (!activeSession.IsWhitelisted)
            throw new UnauthorizedAccessException(nonWhitelistedSessionMessage);

        return new ActiveSessionUser(
            activeSession,
            currentSession.User ?? throw new InvalidOperationException("Active session did not include a loaded user."));
    }

    private SessionDTO GetTokenSessionDto()
    {
        if (_requestSessionAccessor.GetSession() is SessionDTO sessionDto)
            return sessionDto;

        throw new UnauthorizedAccessException("Session not found in HttpContext.");
    }

    private sealed record CurrentSession(SessionDTO Session, UserDB? User);

    private sealed record ActiveSessionUser(SessionDTO Session, UserDB User);
}
