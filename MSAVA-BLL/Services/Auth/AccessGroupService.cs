using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
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
        Guid userId = _userService.GetSessionUserId();

        var user = _context.Users.SingleOrDefault(u => u.Id == userId)
            ?? throw new KeyNotFoundException($"User with id {userId} not found.");

        var accessGroup = new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = user.Id,
            CreatedAt = DateTime.UtcNow,
            Name = name,
            Users = [],
            SubGroups = []
        };

        _context.AccessGroups.Add(accessGroup);

        // Add user to the access group in the same transaction
        user.AccessGroups ??= [];
        user.AccessGroups.Add(accessGroup);

        _context.SaveChanges(); // Single save for both operations

        _serviceLogger.WriteLog(GroupLogActions.AccessGroupCreated, $"Access group '{name}' created by user {user.Username}.", user.Id, accessGroup.Id);
        _serviceLogger.WriteLog(GroupLogActions.AccessGroupUserAdded, $"User {user.Username} added to access group '{name}'.", user.Id, accessGroup.Id);

        return accessGroup.Id;
    }

    public async Task AddAccessGroupToUserAsync(Guid accessGroupId, Guid userId)
    {
        var accessGroup = await _context.AccessGroups.SingleOrDefaultAsync(g => g.Id == accessGroupId)
            ?? throw new KeyNotFoundException($"Access group with id {accessGroupId} not found.");

        var user = await _context.Users
            .Include(u => u.AccessGroups)
            .SingleOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException($"User with id {userId} not found.");

        user.AccessGroups ??= [];

        if (!user.AccessGroups.Any(g => g.Id == accessGroupId))
        {
            user.AccessGroups.Add(accessGroup);
            await _context.SaveChangesAsync();

            _serviceLogger.WriteLog(GroupLogActions.AccessGroupUserAdded, $"User {user.Username} added to access group '{accessGroup.Name}'.", user.Id, accessGroup.Id);
        }
    }
}
