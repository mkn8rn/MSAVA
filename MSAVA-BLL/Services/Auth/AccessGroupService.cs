using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
using MSAVA_Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace MSAVA_BLL.Services.Auth;

public class AccessGroupService
{
    private readonly BaseDataContext _context;
    private readonly IUserSessionService _userService;
    private readonly ServiceLogger _serviceLogger;

    public AccessGroupService(
        BaseDataContext context,
        IUserSessionService userService,
        ServiceLogger serviceLogger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
    }

    public List<AccessGroupDB> GetUserAccessGroups(Guid userId)
    {
        var user = _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .SingleOrDefault(u => u.Id == userId)
            ?? throw new KeyNotFoundException($"User with id {userId} not found.");

        return user.AccessGroups?.ToList() ?? [];
    }

    public Guid CreateAccessGroup(string name)
    {
        string accessGroupName = NormalizeAccessGroupName(name);
        SessionDTO session = GetActiveSession();

        var user = _context.Users.SingleOrDefault(u => u.Id == session.UserId)
            ?? throw new KeyNotFoundException($"User with id {session.UserId} not found.");

        var accessGroup = new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = user.Id,
            CreatedAt = DateTime.UtcNow,
            Name = accessGroupName,
            Users = [],
            SubGroups = []
        };

        _context.AccessGroups.Add(accessGroup);

        // Add user to the access group in the same transaction
        user.AccessGroups ??= [];
        user.AccessGroups.Add(accessGroup);

        _context.SaveChanges(); // Single save for both operations

        _serviceLogger.WriteLog(GroupLogActions.AccessGroupCreated, $"Access group '{accessGroupName}' created by user {user.Username}.", user.Id, accessGroup.Id);
        _serviceLogger.WriteLog(GroupLogActions.AccessGroupUserAdded, $"User {user.Username} added to access group '{accessGroupName}'.", user.Id, accessGroup.Id);

        return accessGroup.Id;
    }

    public async Task AddUserToAccessGroupAsync(Guid userId, Guid accessGroupId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id must be provided.", nameof(userId));

        if (accessGroupId == Guid.Empty)
            throw new ArgumentException("Access group id must be provided.", nameof(accessGroupId));

        SessionDTO session = GetActiveSession();

        var accessGroup = await _context.AccessGroups.SingleOrDefaultAsync(g => g.Id == accessGroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Access group with id {accessGroupId} not found.");

        if (!session.IsAdmin && accessGroup.OwnerId != session.UserId)
            throw new UnauthorizedAccessException("Only admins and access group owners can add users to an access group.");

        var user = await _context.Users
            .Include(u => u.AccessGroups)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with id {userId} not found.");

        user.AccessGroups ??= [];

        if (!user.AccessGroups.Any(g => g.Id == accessGroupId))
        {
            user.AccessGroups.Add(accessGroup);
            await _context.SaveChangesAsync(cancellationToken);

            _serviceLogger.WriteLog(GroupLogActions.AccessGroupUserAdded, $"User {user.Username} added to access group '{accessGroup.Name}'.", session.UserId, accessGroup.Id);
        }
    }

    private SessionDTO GetActiveSession()
    {
        SessionDTO session = _userService.GetSessionClaims();
        if (!session.LoggedIn || session.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("Session user is required to manage access groups.");

        if (session.IsBanned)
            throw new UnauthorizedAccessException("Banned users cannot manage access groups.");

        return session;
    }

    private static string NormalizeAccessGroupName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Access group name must be provided.", nameof(name));

        return name.Trim();
    }
}
