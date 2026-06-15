namespace MSAVA_Shared.Models;

public static class SessionClaimNames
{
    public const string Subject = "sub";
    public const string UniqueName = "unique_name";
    public const string Role = "role";
    public const string RoleUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    public const string InviteCode = "inviteCode";
    public const string AccessGroups = "accessGroups";
    public const string IssuedAt = "iat";
    public const string NotBefore = "nbf";
    public const string ExpiresAt = "exp";
}
