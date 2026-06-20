namespace MSAVA_Shared.Models;

public class UserDTO
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsBanned { get; set; }
    public bool IsWhitelisted { get; set; }
    public Guid InviteCode { get; set; }
    public List<AccessGroupDTO> AccessGroups { get; set; } = new List<AccessGroupDTO>();
}
