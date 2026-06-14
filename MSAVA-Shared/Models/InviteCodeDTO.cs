namespace MSAVA_Shared.Models;

public class InviteCodeDTO
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int MaxUses { get; set; }
}
