using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public class UserDB : IIdentifiableDB
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
