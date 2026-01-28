using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public class GroupLogDB : IIdentifiableDB
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public required Guid UserId { get; set; }
        public UserDB? User { get; set; }
        public required Guid GroupId { get; set; }
        public AccessGroupDB? Group { get; set; }
        public required GroupLogActions Action { get; set; }
    }
}
