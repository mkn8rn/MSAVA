using MSAVA_Shared.Models;
using MSAVA_BLL.Utils;
using MSAVA_INF.Models;
using Microsoft.AspNetCore.Http;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;
using Microsoft.EntityFrameworkCore;

namespace MSAVA_BLL.Services.Auth;

public class UserSessionService : IUserSessionService
{
    private readonly BaseDataContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ServiceLogger _serviceLogger;

    public UserSessionService(BaseDataContext context, IHttpContextAccessor httpContextAccessor, ServiceLogger serviceLogger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
    }

    public UserDTO GetUserById(Guid id)
    {
        RequireCanReadUser(id);

        var userDb = _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .SingleOrDefault(u => u.Id == id)
            ?? throw new KeyNotFoundException($"User with id {id} not found.");

        return MappingUtils.MapUserDTOWithRelationships(userDb);
    }

    public Guid GetSessionUserId()
    {
        return RequireActiveSession(
            "Session user is required to access the current user.",
            "Banned users cannot access the current user.").UserId;
    }

    public SessionDTO GetSessionClaims()
    {
        SessionDTO tokenSession = GetTokenSessionDto();
        if (!tokenSession.LoggedIn || tokenSession.UserId == Guid.Empty)
            return tokenSession;

        UserDB userDb = GetSessionUserDb(tokenSession.UserId);
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

    public bool IsSessionUserAdmin()
    {
        return RequireActiveSession(
            "Session user is required to check admin status.",
            "Banned users cannot check admin status.").IsAdmin;
    }

    public UserDTO GetSessionUser() => MappingUtils.MapUserDTOWithRelationships(GetSessionUserDB());

    public UserDB GetSessionUserDB()
    {
        SessionDTO session = RequireActiveSession(
            "Session user is required to access the current user.",
            "Banned users cannot access the current user.");

        return GetSessionUserDb(session.UserId);
    }

    private UserDB GetSessionUserDb(Guid sessionUserId)
    {
        var userDb = _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .SingleOrDefault(u => u.Id == sessionUserId)
            ?? throw new KeyNotFoundException($"User with id {sessionUserId} not found.");

        return userDb;
    }

    public List<UserDTO> GetAllUsers()
    {
        RequireActiveAdminSession();

        var userDbs = _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .ToList();

        return userDbs.Select(MappingUtils.MapUserDTOWithRelationships).ToList();
    }

    private void RequireActiveAdminSession()
    {
        SessionDTO session = RequireActiveSession(
            "Session user is required to list users.",
            "Banned users cannot list users.");

        if (!session.IsAdmin)
            throw new UnauthorizedAccessException("Only admins can list users.");
    }

    private SessionDTO RequireCanReadUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id must be provided.", nameof(userId));

        SessionDTO session = RequireActiveSession(
            "Session user is required to access users.",
            "Banned users cannot access users.");

        if (!session.IsAdmin && session.UserId != userId)
            throw new UnauthorizedAccessException("Only admins can access other users.");

        return session;
    }

    private SessionDTO RequireActiveSession(string missingSessionMessage, string bannedSessionMessage)
    {
        return SessionGuard.RequireActive(
            GetSessionClaims(),
            missingSessionMessage,
            bannedSessionMessage);
    }

    private SessionDTO GetTokenSessionDto()
    {
        if (_httpContextAccessor.HttpContext?.Items["SessionDTO"] is SessionDTO sessionDto)
            return sessionDto;

        throw new UnauthorizedAccessException("Session not found in HttpContext.");
    }
}
