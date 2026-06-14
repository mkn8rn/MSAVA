using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
using Microsoft.EntityFrameworkCore;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Auth;

public class InviteCodeService
    : IInviteCodeService
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
        SessionDTO session = GetAuthorizedAdminSession();

        if (maxUses <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxUses), maxUses, "Invite code max uses must be greater than zero.");

        if (expiresAt <= DateTime.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, "Invite code expiration must be in the future.");

        var inviteCode = new InviteCodeDB
        {
            Id = Guid.NewGuid(),
            OwnerId = session.UserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            MaxUses = maxUses
        };

        _context.InviteCodes.Add(inviteCode);
        await _context.SaveChangesAsync();

        _serviceLogger.WriteLog(InviteLogActions.InviteCodeCreated, $"Invite code created by user {session.Username}.", session.UserId, inviteCode.Id);

        return inviteCode.Id;
    }

    /// <summary>
    /// Optimized: Single query to get both MaxUses and current usage count.
    /// </summary>
    public int GetRemainingUses(Guid inviteCodeId)
    {
        EnsureCurrentUserCanManageInviteCodes();

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

        return Math.Max(0, result.MaxUses - result.UsedCount);
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

    public List<InviteCodeDTO> GetAllInviteCodes()
    {
        EnsureCurrentUserCanManageInviteCodes();

        return _context.InviteCodes
            .AsNoTracking()
            .Select(inviteCode => new InviteCodeDTO
            {
                Id = inviteCode.Id,
                OwnerId = inviteCode.OwnerId,
                CreatedAt = inviteCode.CreatedAt,
                ExpiresAt = inviteCode.ExpiresAt,
                MaxUses = inviteCode.MaxUses
            })
            .ToList();
    }

    public InviteCodeDTO GetInviteCodeById(Guid inviteCodeId)
    {
        EnsureCurrentUserCanManageInviteCodes();

        var inviteCode = _context.InviteCodes.AsNoTracking().SingleOrDefault(i => i.Id == inviteCodeId)
            ?? throw new KeyNotFoundException($"Invite code with id {inviteCodeId} not found.");

        return MappingUtils.MapInviteCodeDTO(inviteCode);
    }

    private void EnsureCurrentUserCanManageInviteCodes()
    {
        _ = GetAuthorizedAdminSession();
    }

    private SessionDTO GetAuthorizedAdminSession()
    {
        SessionDTO session = SessionGuard.RequireActive(
            _userService.GetSessionClaims(),
            "Only active admins can manage invite codes.",
            "Only active admins can manage invite codes.");

        if (!session.IsAdmin)
            throw new UnauthorizedAccessException("Only active admins can manage invite codes.");

        return session;
    }
}
