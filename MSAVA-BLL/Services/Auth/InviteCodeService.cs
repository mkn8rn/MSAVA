using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
using Microsoft.EntityFrameworkCore;

namespace MSAVA_BLL.Services.Auth;

public class InviteCodeService
{
    private readonly BaseDataContext _context;
    private readonly IUserSessionService _userService;
    private readonly ServiceLogger _serviceLogger;

    public InviteCodeService(
        BaseDataContext context,
        IUserSessionService userService,
        ServiceLogger serviceLogger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
    }

    public async Task<Guid> CreateNewInviteCode(int maxUses, DateTime expiresAt)
    {
        if (maxUses <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxUses), maxUses, "Invite code max uses must be greater than zero.");

        if (expiresAt <= DateTime.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, "Invite code expiration must be in the future.");

        UserDB user = _userService.GetSessionUserDB();

        var inviteCode = new InviteCodeDB
        {
            Id = Guid.NewGuid(),
            OwnerId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            MaxUses = maxUses
        };

        _context.InviteCodes.Add(inviteCode);
        await _context.SaveChangesAsync();

        _serviceLogger.WriteLog(InviteLogActions.InviteCodeCreated, $"Invite code created by user {user.Username}.", user.Id, inviteCode.Id);

        return inviteCode.Id;
    }

    public int GetHowManyUses(Guid inviteCode)
    {
        return _context.Users.AsNoTracking().Count(u => u.InviteCodeId == inviteCode);
    }

    /// <summary>
    /// Optimized: Single query to get both MaxUses and current usage count.
    /// </summary>
    public int GetRemainingUses(Guid inviteCodeId)
    {
        var result = _context.InviteCodes
            .AsNoTracking()
            .Where(ic => ic.Id == inviteCodeId)
            .Select(ic => new
            {
                ic.MaxUses,
                UsedCount = _context.Users.Count(u => u.InviteCodeId == inviteCodeId)
            })
            .SingleOrDefault()
            ?? throw new KeyNotFoundException($"Invite code with id {inviteCodeId} not found.");

        return result.MaxUses - result.UsedCount;
    }

    public bool IsValidInviteCode(Guid inviteCodeId)
    {
        // Optimized: Single query instead of calling GetRemainingUses
        var result = _context.InviteCodes
            .AsNoTracking()
            .Where(ic => ic.Id == inviteCodeId)
            .Select(ic => new
            {
                ic.MaxUses,
                ic.ExpiresAt,
                UsedCount = _context.Users.Count(u => u.InviteCodeId == inviteCodeId)
            })
            .SingleOrDefault();

        if (result is null)
            return false;

        // Check expiry and usage
        return result.ExpiresAt > DateTime.UtcNow && (result.MaxUses - result.UsedCount) > 0;
    }

    public List<InviteCodeDB> GetAllInviteCodes()
    {
        return _context.InviteCodes.AsNoTracking().ToList();
    }

    public InviteCodeDB GetInviteCodeById(Guid inviteCodeId)
    {
        return _context.InviteCodes.AsNoTracking().SingleOrDefault(i => i.Id == inviteCodeId)
            ?? throw new KeyNotFoundException($"Invite code with id {inviteCodeId} not found.");
    }
}
