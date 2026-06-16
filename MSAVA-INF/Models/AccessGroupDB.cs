using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public class AccessGroupDB : IIdentifiableDB
    {
        public const int MaximumNameLength = 128;

        public Guid Id { get; set; }
        public required Guid OwnerId { get; set; }
        public UserDB? Owner { get; set; }
        public required DateTime CreatedAt { get; set; }
        public required string Name { get; set; }
        public ICollection<UserDB> Users { get; set; } = new List<UserDB>();
        public IEnumerable<AccessGroupDB>? SubGroups { get; set; }
    }
}
