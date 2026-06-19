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
    private const string InviteCodeNotFoundMessage = "Invite code was not found.";

    private readonly BaseDataContext _context;
    private readonly IUserSessionService _userService;
    private readonly ServiceLogger _serviceLogger;
    private readonly TimeProvider _timeProvider;

    public InviteCodeService(
        BaseDataContext context,
        IUserSessionService userService,
        ServiceLogger serviceLogger,
        TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Guid> CreateNewInviteCodeAsync(
        int maxUses,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        InviteCodePolicy.EnsureMaxUsesAllowed(maxUses);

        DateTime utcNow = GetUtcNow();
        InviteCodePolicy.EnsureExpirationAllowed(expiresAt, utcNow);

        SessionDTO session = await GetAuthorizedAdminSessionAsync(cancellationToken);

        var inviteCode = new InviteCodeDB
        {
            Id = Guid.NewGuid(),
            OwnerId = session.UserId,
            CreatedAt = utcNow,
            ExpiresAt = expiresAt,
            MaxUses = maxUses
        };

        _context.InviteCodes.Add(inviteCode);
        await _context.SaveChangesAsync(cancellationToken);

        await _serviceLogger.WriteLogAsync(
            InviteLogActions.InviteCodeCreated,
            $"Invite code created by user {session.Username}.",
            session.UserId,
            inviteCode.Id,
            cancellationToken);

        return inviteCode.Id;
    }

    /// <summary>
    /// Optimized: Single query to get both MaxUses and current usage count.
    /// </summary>
    public async Task<int> GetRemainingUsesAsync(Guid inviteCodeId, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentUserCanManageInviteCodesAsync(cancellationToken);

        var usage = await GetInviteCodeUsageAsync(inviteCodeId, cancellationToken)
            ?? throw new KeyNotFoundException(InviteCodeNotFoundMessage);

        return usage.GetRemainingUses(GetUtcNow());
    }

    public async Task<bool> IsValidInviteCodeAsync(Guid inviteCodeId, CancellationToken cancellationToken = default)
    {
        var usage = await GetInviteCodeUsageAsync(inviteCodeId, cancellationToken);
        return usage is not null && usage.HasRemainingUse(GetUtcNow());
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
            ?? throw new KeyNotFoundException(InviteCodeNotFoundMessage);

        return MappingUtils.MapInviteCodeDTO(inviteCode);
    }

    private async Task EnsureCurrentUserCanManageInviteCodesAsync(CancellationToken cancellationToken)
    {
        _ = await GetAuthorizedAdminSessionAsync(cancellationToken);
    }

    private async Task<SessionDTO> GetAuthorizedAdminSessionAsync(CancellationToken cancellationToken)
    {
        SessionDTO session = SessionGuard.RequireActiveWhitelisted(
            await _userService.GetCurrentSessionAsync(cancellationToken),
            "Only active admins can manage invite codes.",
            "Only active admins can manage invite codes.",
            "Users must be whitelisted before managing invite codes.");

        if (!session.IsAdmin)
            throw new UnauthorizedAccessException("Only active admins can manage invite codes.");

        return session;
    }

    private async Task<InviteCodeUsage?> GetInviteCodeUsageAsync(
        Guid inviteCodeId,
        CancellationToken cancellationToken)
    {
        return await _context.InviteCodes
            .AsNoTracking()
            .Where(inviteCode => inviteCode.Id == inviteCodeId)
            .Select(inviteCode => new InviteCodeUsage(
                inviteCode.MaxUses,
                inviteCode.ExpiresAt,
                _context.Users.Count(user => user.InviteCodeId == inviteCodeId)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private DateTime GetUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private sealed record InviteCodeUsage(
        int MaxUses,
        DateTime ExpiresAt,
        int UsedCount)
    {
        public int GetRemainingUses(DateTime utcNow)
        {
            if (ExpiresAt <= utcNow)
                return 0;

            return Math.Max(0, MaxUses - UsedCount);
        }

        public bool HasRemainingUse(DateTime utcNow)
        {
            return ExpiresAt > utcNow && UsedCount < MaxUses;
        }
    }
}
