using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public class AccessLogDB : IIdentifiableDB
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public required Guid UserId { get; set; }
        public UserDB? User { get; set; }
        public required Guid FileRefId { get; set; }
        public SavedFileReferenceDB? FileRef { get; set; }
        public required AccessLogActions Action { get; set; }

    }
}
