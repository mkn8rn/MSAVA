using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MSAVA_Shared.Models;

namespace MSAVA_INF.Models
{
    public class UserDB : IIdentifiableDB
    {
        public const int MaximumUsernameLength = AuthenticationCredentialPolicy.MaximumUsernameLength;

        public Guid Id { get; set; }
        public required DateTime CreatedAt { get; set; }
        public required string Username { get; set; }
        public required byte[] PasswordHash { get; set; }
        public required byte[] PasswordSalt { get; set; }
        public required bool IsAdmin { get; set; }
        public required bool IsBanned { get; set; }
        public required bool IsWhitelisted { get; set; }
        public Guid? InviteCodeId { get; set; }
        public InviteCodeDB? InviteCode { get; set; }
        public ICollection<AccessGroupDB> AccessGroups { get; set; } = new List<AccessGroupDB>();
    }
}
