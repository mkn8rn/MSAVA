using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IInviteCodeService
{
    Task<Guid> CreateNewInviteCodeAsync(
        int maxUses,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);

    Task<int> GetRemainingUsesAsync(
        Guid inviteCodeId,
        CancellationToken cancellationToken = default);

    Task<bool> IsValidInviteCodeAsync(
        Guid inviteCodeId,
        CancellationToken cancellationToken = default);

    Task<List<InviteCodeDTO>> GetAllInviteCodesAsync(CancellationToken cancellationToken = default);

    Task<InviteCodeDTO> GetInviteCodeByIdAsync(
        Guid inviteCodeId,
        CancellationToken cancellationToken = default);
}
