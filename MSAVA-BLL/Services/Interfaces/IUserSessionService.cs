using MSAVA_Shared.Models;
using MSAVA_INF.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IUserSessionService
{
    UserDTO GetUserById(Guid id);
    List<UserDTO> GetAllUsers();
    bool IsSessionUserAdmin();
    UserDTO GetSessionUser();
    Guid GetSessionUserId();
    UserDB GetSessionUserDB();
    SessionDTO GetSessionClaims();
}
