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
        await RequireCanReadUserAsync(id, cancellationToken);

        var userDb = await _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"User with id {id} not found.");

        return MappingUtils.MapUserDTOWithRelationships(userDb);
    }

    public async Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default)
    {
        return (await RequireActiveSessionAsync(
            "Session user is required to access the current user.",
            "Banned users cannot access the current user.",
            cancellationToken)).UserId;
    }

    public async Task<SessionDTO> GetSessionClaimsAsync(CancellationToken cancellationToken = default)
    {
        SessionDTO tokenSession = GetTokenSessionDto();
        if (!tokenSession.LoggedIn || tokenSession.UserId == Guid.Empty)
            return tokenSession;

        UserDB userDb = await GetSessionUserDbAsync(tokenSession.UserId, cancellationToken);
        var roles = new List<string>(3);

        if (userDb.IsAdmin)
            roles.Add("Admin");
        if (userDb.IsBanned)
            roles.Add("Banned");
        if (userDb.IsWhitelisted)
            roles.Add("Whitelisted");

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
        return (await RequireActiveSessionAsync(
            "Session user is required to check admin status.",
            "Banned users cannot check admin status.",
            cancellationToken)).IsAdmin;
    }

    public async Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) =>
        MappingUtils.MapUserDTOWithRelationships(await GetSessionUserDBAsync(cancellationToken));

    public async Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default)
    {
        SessionDTO session = await RequireActiveSessionAsync(
            "Session user is required to access the current user.",
            "Banned users cannot access the current user.",
            cancellationToken);

        return await GetSessionUserDbAsync(session.UserId, cancellationToken);
    }

    private async Task<UserDB> GetSessionUserDbAsync(Guid sessionUserId, CancellationToken cancellationToken)
    {
        var userDb = await _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .SingleOrDefaultAsync(u => u.Id == sessionUserId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with id {sessionUserId} not found.");

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
        SessionDTO session = await RequireActiveSessionAsync(
            "Session user is required to list users.",
            "Banned users cannot list users.",
            cancellationToken);

        if (!session.IsAdmin)
            throw new UnauthorizedAccessException("Only admins can list users.");
    }

    private async Task<SessionDTO> RequireCanReadUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id must be provided.", nameof(userId));

        SessionDTO session = await RequireActiveSessionAsync(
            "Session user is required to access users.",
            "Banned users cannot access users.",
            cancellationToken);

        if (!session.IsAdmin && session.UserId != userId)
            throw new UnauthorizedAccessException("Only admins can access other users.");

        return session;
    }

    private async Task<SessionDTO> RequireActiveSessionAsync(
        string missingSessionMessage,
        string bannedSessionMessage,
        CancellationToken cancellationToken)
    {
        return SessionGuard.RequireActive(
            await GetSessionClaimsAsync(cancellationToken),
            missingSessionMessage,
            bannedSessionMessage);
    }

    private SessionDTO GetTokenSessionDto()
    {
        if (_requestSessionAccessor.GetSession() is SessionDTO sessionDto)
            return sessionDto;

        throw new UnauthorizedAccessException("Session not found in HttpContext.");
    }
}
