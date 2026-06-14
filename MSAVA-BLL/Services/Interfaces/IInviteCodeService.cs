using MSAVA_INF.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IInviteCodeService
{
    Task<Guid> CreateNewInviteCode(int maxUses, DateTime expiresAt);
    int GetRemainingUses(Guid inviteCodeId);
    bool IsValidInviteCode(Guid inviteCodeId);
    List<InviteCodeDB> GetAllInviteCodes();
    InviteCodeDB GetInviteCodeById(Guid inviteCodeId);
}
