namespace MSAVA_Shared.Models;

public class ErrorLogDTO
{
    public Guid Id { get; set; }
    public required string Message { get; set; }
    public string? StackTrace { get; set; }
    public required DateTime Timestamp { get; set; }
    public required Guid? UserId { get; set; }
}
