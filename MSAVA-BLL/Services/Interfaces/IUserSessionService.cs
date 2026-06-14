using MSAVA_Shared.Models;
using MSAVA_INF.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IUserSessionService
{
    Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default);
    Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default);
    Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default);
    Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default);
    Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default);
    Task<SessionDTO> GetSessionClaimsAsync(CancellationToken cancellationToken = default);
}
