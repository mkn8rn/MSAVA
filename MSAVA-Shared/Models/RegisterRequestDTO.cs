namespace MSAVA_Shared.Models;

public class RegisterRequestDTO
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public required Guid InviteCode { get; set; }
}
