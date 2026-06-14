using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IInviteCodeService
{
    Task<Guid> CreateNewInviteCode(int maxUses, DateTime expiresAt);
    int GetRemainingUses(Guid inviteCodeId);
    bool IsValidInviteCode(Guid inviteCodeId);
    List<InviteCodeDTO> GetAllInviteCodes();
    InviteCodeDTO GetInviteCodeById(Guid inviteCodeId);
}
