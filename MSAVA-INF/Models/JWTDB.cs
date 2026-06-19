using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MSAVA_Shared.Models;

namespace MSAVA_INF.Models
{
    public class JwtDB : IIdentifiableDB
    {
        public const int MaximumTokenStringLength = AuthenticationTokenPolicy.MaximumTokenStringLength;

        public Guid Id { get; set; }
        public required Guid UserId { get; set; }
        public UserDB? User { get; set; }
        public string Username { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsBanned { get; set; }
        public bool IsWhitelisted { get; set; }
        public Guid InviteCode { get; set; }
        public string TokenString { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
