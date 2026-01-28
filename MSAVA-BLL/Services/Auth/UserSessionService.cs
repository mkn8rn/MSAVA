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
        var userDb = _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .SingleOrDefault(u => u.Id == id)
            ?? throw new KeyNotFoundException($"User with id {id} not found.");

        return MappingUtils.MapUserDTOWithRelationships(userDb);
    }

    public Guid GetSessionUserId() => GetSessionDto().UserId;

    public SessionDTO GetSessionClaims() => GetSessionDto();

    public bool IsSessionUserAdmin() => GetSessionDto().IsAdmin;

    public UserDTO GetSessionUser() => MappingUtils.MapUserDTOWithRelationships(GetSessionUserDB());

    public UserDB GetSessionUserDB()
    {
        Guid sessionUserId = GetSessionUserId();

        var userDb = _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .SingleOrDefault(u => u.Id == sessionUserId)
            ?? throw new KeyNotFoundException($"User with id {sessionUserId} not found.");

        return userDb;
    }

    public List<UserDTO> GetAllUsers()
    {
        var userDbs = _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .ToList();

        return userDbs.Select(MappingUtils.MapUserDTOWithRelationships).ToList();
    }

    public void DeleteUser(Guid id)
    {
        var user = _context.Users.SingleOrDefault(u => u.Id == id)
            ?? throw new KeyNotFoundException($"User with id {id} not found.");

        _context.Users.Remove(user);
        _context.SaveChanges();

        _serviceLogger.WriteLog(UserLogAction.AccountDeletion, $"User deleted: {id}", id, null);
    }

    private SessionDTO GetSessionDto()
    {
        if (_httpContextAccessor.HttpContext?.Items["SessionDTO"] is SessionDTO sessionDto)
            return sessionDto;

        throw new UnauthorizedAccessException("Session not found in HttpContext.");
    }
}
