using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IAuthenticationService
{
    Task<LoginResponseDTO> LoginAsync(LoginRequestDTO request, CancellationToken cancellationToken = default);
    Task<Guid> RegisterAsync(RegisterRequestDTO request, CancellationToken cancellationToken = default);
}
