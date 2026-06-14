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

    public async Task<Guid> CreateNewInviteCodeAsync(
        int maxUses,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        SessionDTO session = await GetAuthorizedAdminSessionAsync(cancellationToken);

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
        await _context.SaveChangesAsync(cancellationToken);

        await _serviceLogger.WriteLogAsync(InviteLogActions.InviteCodeCreated, $"Invite code created by user {session.Username}.", session.UserId, inviteCode.Id);

        return inviteCode.Id;
    }

    /// <summary>
    /// Optimized: Single query to get both MaxUses and current usage count.
    /// </summary>
    public async Task<int> GetRemainingUsesAsync(Guid inviteCodeId, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentUserCanManageInviteCodesAsync(cancellationToken);

        var result = await _context.InviteCodes
            .AsNoTracking()
            .Where(ic => ic.Id == inviteCodeId)
            .Select(ic => new
            {
                ic.MaxUses,
                UsedCount = _context.Users.Count(u => u.InviteCodeId == inviteCodeId)
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Invite code with id {inviteCodeId} not found.");

        return Math.Max(0, result.MaxUses - result.UsedCount);
    }

    public async Task<bool> IsValidInviteCodeAsync(Guid inviteCodeId, CancellationToken cancellationToken = default)
    {
        // Optimized: Single query instead of calling GetRemainingUses
        var result = await _context.InviteCodes
            .AsNoTracking()
            .Where(ic => ic.Id == inviteCodeId)
            .Select(ic => new
            {
                ic.MaxUses,
                ic.ExpiresAt,
                UsedCount = _context.Users.Count(u => u.InviteCodeId == inviteCodeId)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
            return false;

        // Check expiry and usage
        return result.ExpiresAt > DateTime.UtcNow && (result.MaxUses - result.UsedCount) > 0;
    }

    public async Task<List<InviteCodeDTO>> GetAllInviteCodesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCurrentUserCanManageInviteCodesAsync(cancellationToken);

        return await _context.InviteCodes
            .AsNoTracking()
            .Select(inviteCode => new InviteCodeDTO
            {
                Id = inviteCode.Id,
                OwnerId = inviteCode.OwnerId,
                CreatedAt = inviteCode.CreatedAt,
                ExpiresAt = inviteCode.ExpiresAt,
                MaxUses = inviteCode.MaxUses
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<InviteCodeDTO> GetInviteCodeByIdAsync(Guid inviteCodeId, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentUserCanManageInviteCodesAsync(cancellationToken);

        var inviteCode = await _context.InviteCodes
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == inviteCodeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Invite code with id {inviteCodeId} not found.");

        return MappingUtils.MapInviteCodeDTO(inviteCode);
    }

    private async Task EnsureCurrentUserCanManageInviteCodesAsync(CancellationToken cancellationToken)
    {
        _ = await GetAuthorizedAdminSessionAsync(cancellationToken);
    }

    private async Task<SessionDTO> GetAuthorizedAdminSessionAsync(CancellationToken cancellationToken)
    {
        SessionDTO session = SessionGuard.RequireActive(
            await _userService.GetSessionClaimsAsync(cancellationToken),
            "Only active admins can manage invite codes.",
            "Only active admins can manage invite codes.");

        if (!session.IsAdmin)
            throw new UnauthorizedAccessException("Only active admins can manage invite codes.");

        return session;
    }
}
